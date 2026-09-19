import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent.parent))

from scripts.jira_helper import JiraHelper

def adf_doc(text):
    return {
        "type": "doc",
        "version": 1,
        "content": [
            {
                "type": "paragraph",
                "content": [{"type": "text", "text": text}]
            }
        ]
    }

def main():
    h = JiraHelper()
    project_key = h.project_key

    # 1. Fetch user accountId for assignment
    status, user_info = h._request("/rest/api/3/myself")
    if status != 200:
        print(f"❌ Failed to fetch current user info: {user_info}")
        return

    account_id = user_info.get("accountId")
    display_name = user_info.get("displayName")
    print(f"👤 Assigning all tasks to: {display_name} (Account ID: {account_id})")
    print(f"=== Creating Architecture Migration & Event-Driven Tasks on Jira ===")

    # 2. Create Epic-ARCH
    epic_summary = "Epic-ARCH_Microservices Migration & Event-Driven Infrastructure"
    epic_desc = (
        "Tái cấu trúc kiến trúc phân rã monolith: Tách cơ sở dữ liệu độc lập trading_db và settlement_db "
        "triệt tiêu anti-pattern shared database, tích hợp Message Broker RabbitMQ qua MassTransit, "
        "dựng daemon ngầm SettlementWorker đếm ngược T+24h và API Pre-flight Sync Check chống Race Condition."
    )

    code, epic_resp = h._request("/rest/api/3/issue", method="POST", data={
        "fields": {
            "project": {"key": project_key},
            "summary": epic_summary,
            "issuetype": {"name": "Epic"},
            "description": adf_doc(epic_desc),
            "assignee": {"accountId": account_id}
        }
    })

    if code != 201:
        print(f"❌ Failed to create Epic: {epic_resp}")
        return

    epic_key = epic_resp["key"]
    print(f"✅ Created Epic: [{epic_key}] {epic_summary}")

    # 3. Stories and Subtasks
    stories_data = [
        {
            "summary": "US-ARCH.1_Event-driven messaging infrastructure with RabbitMQ and MassTransit",
            "desc": "Cấu hình container RabbitMQ và tích hợp MassTransit vào TradingCore và Contracts để phát tán sự kiện IOwnershipTransferredEvent bất đồng bộ.",
            "subtasks": [
                (
                    "INFRA-1.1_Add RabbitMQ 3 Management container to docker-compose",
                    "Khai báo service rabbitmq với image rabbitmq:3-management, cổng 5672 và 15672 trong docker-compose.yml."
                ),
                (
                    "CONTRACT-1.1_Define IOwnershipTransferredEvent with UnlockAt in Contracts",
                    "Định nghĩa interface sự kiện RabbitMQ mang trường UnlockAt phục vụ worker đếm ngược giải ngân."
                ),
                (
                    "CORE-1.1_Configure MassTransit RabbitMQ publisher in TradingCore",
                    "Đăng ký DI MassTransit trong Program.cs và cấu hình IPublishEndpoint để phát tán event khi sang tên thành công."
                )
            ]
        },
        {
            "summary": "US-ARCH.2_Database decoupling and SettlementWorker service scaffold",
            "desc": "Tách biệt kết nối CSDL giữa trading_db và settlement_db loại bỏ shared database; khởi tạo dự án TicketShield.SettlementWorker độc lập.",
            "subtasks": [
                (
                    "INFRA-2.1_Segregate trading_db and settlement_db configurations",
                    "Phân tách chuỗi kết nối và DbContext độc lập giữa TradingCore và SettlementWorker."
                ),
                (
                    "WORKER-2.1_Scaffold TicketShield.SettlementWorker project with MassTransit Consumer",
                    "Tạo project .NET 8 Worker Service, cấu hình MassTransit Consumer lắng nghe IOwnershipTransferredEvent từ queue."
                ),
                (
                    "WORKER-2.2_Implement settlement countdown timer and claim-payout trigger",
                    "Hiện thực logic hẹn giờ đếm ngược đến UnlockAt (hỗ trợ dev mode 10s) và kích hoạt lệnh claim-payout sang Core."
                )
            ]
        },
        {
            "summary": "US-ARCH.3_Atomic pre-flight sync check API with PostgreSQL row-level lock",
            "desc": "Xây dựng endpoint nội bộ claim-payout trong Core với khóa dòng PostgreSQL chống race condition lúc 23:59:59 giữa khiếu nại và giải ngân.",
            "subtasks": [
                (
                    "CORE-3.1_Implement POST /api/v1/internal/escrows/{id}/claim-payout with row-level lock",
                    "Thực thi câu lệnh UPDATE ... WHERE status = 'LOCKED' AND has_dispute = false chuyển atomic sang PAYING_OUT."
                ),
                (
                    "CORE-3.2_Implement PUT /api/v1/internal/escrows/{id}/release endpoint",
                    "Endpoint nội bộ cập nhật trạng thái Escrow sang RELEASED sau khi Worker chuyển khoản NAPAS 247 thành công."
                ),
                (
                    "TEST-3.1_Integration test end-to-end event publish and pre-flight claim",
                    "Kiểm thử tự động luồng bắn event qua RabbitMQ, worker nhận và gọi claim-payout thành công."
                )
            ]
        }
    ]

    created_stories = []
    for s in stories_data:
        s_code, s_resp = h._request("/rest/api/3/issue", method="POST", data={
            "fields": {
                "project": {"key": project_key},
                "summary": s["summary"],
                "issuetype": {"name": "Story"},
                "parent": {"key": epic_key},
                "description": adf_doc(s["desc"]),
                "assignee": {"accountId": account_id}
            }
        })
        if s_code != 201:
            print(f"❌ Failed to create Story {s['summary']}: {s_resp}")
            continue

        s_key = s_resp["key"]
        created_stories.append(s_key)
        print(f"\n📂 Created Story: [{s_key}] {s['summary']}")

        for sub_sum, sub_desc in s["subtasks"]:
            sub_code, sub_resp = h._request("/rest/api/3/issue", method="POST", data={
                "fields": {
                    "project": {"key": project_key},
                    "summary": sub_sum,
                    "issuetype": {"name": "Subtask"},
                    "parent": {"key": s_key},
                    "description": adf_doc(sub_desc),
                    "assignee": {"accountId": account_id}
                }
            })
            if sub_code == 201:
                sub_key = sub_resp["key"]
                print(f"   └── Subtask: [{sub_key}] {sub_sum} (Assigned to {display_name})")
            else:
                print(f"   ❌ Failed subtask {sub_sum}: {sub_resp}")

    # 4. Move created stories into SCRUM Sprint 1 (Sprint ID: 3)
    sprint_id = 3
    print(f"\n--- Adding Stories {created_stories} to SCRUM Sprint 1 (ID: {sprint_id}) ---")
    m_code, m_resp = h._request(f"/rest/agile/1.0/sprint/{sprint_id}/issue", method="POST", data={
        "issues": created_stories
    })

    if m_code in (200, 204):
        print(f"✅ Successfully moved all Architecture Stories into SCRUM Sprint 1!")
    else:
        print(f"❌ Failed to move issues into sprint: {m_resp}")

if __name__ == "__main__":
    main()
