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

    # 1. Fetch current user info
    status, user_info = h._request("/rest/api/3/myself")
    if status != 200:
        print(f"❌ Failed to fetch user info: {user_info}")
        return
    account_id = user_info.get("accountId")
    display_name = user_info.get("displayName")
    print(f"👤 Connected as: {display_name} (Account ID: {account_id})")

    # 2. Transition completed Microservices tasks to 'Done'
    completed_keys = [
        "SCRUM-53", "SCRUM-57",
        "SCRUM-59", "SCRUM-60", "SCRUM-61", "SCRUM-62", "SCRUM-63",
        "SCRUM-64", "SCRUM-65", "SCRUM-66", "SCRUM-67",
        "SCRUM-68", "SCRUM-69", "SCRUM-70", "SCRUM-71"
    ]
    print(f"\n--- 1. Transitioning Completed Microservices Tasks to 'Done' ---")
    for key in completed_keys:
        h.transition_issue(key, "Done")

    # 3. Create NEW Subtasks
    new_subtasks = [
        # Frontend Auth Tasks
        ("SCRUM-46", "FE-AUTH-01_Integrate Google Identity Services OAuth 2.0 SDK", 
         "Nhúng thư viện chính thức @react-oauth/google vào LoginPage.tsx & RegisterPage.tsx, thay thế mock token bằng credential thật từ Google."),
        ("SCRUM-40", "FE-AUTH-02_Implement Axios Silent Refresh Token Interceptor", 
         "Xây dựng Fetch/Axios Interceptor tự động bắt HTTP 401 khi access token 15m hết hạn, gọi ngầm /api/v1/auth/refresh-token làm mới phiên trong suốt."),
        ("SCRUM-48", "FE-AUTH-03_Build Forgot Password & Reset Password Pages", 
         "Dựng giao diện ForgotPasswordModal.tsx (gửi OTP qua email) và ResetPasswordPage.tsx (nhập OTP + đổi mật khẩu mới)."),

        # DevOps Tasks
        ("SCRUM-60", "DOCKER-01_Multi-Container Microservices docker-compose configuration", 
         "Cập nhật docker-compose.yml khởi chạy trọn bộ 5 cụm: Gateway (:5000), MockOrganizer (:5001), Identity (:5002), Core (:5003), RabbitMQ (:5672) và Postgres (:5432)."),
        ("SCRUM-60", "SCRIPT-01_One-Click Launch Script for Windows PowerShell & Batch", 
         "Tạo scripts/start-all.ps1 và start-dev.bat tự động bật toàn bộ hệ thống microservices chỉ với 1 câu lệnh duy nhất."),

        # MF-03 Edge Cases & Tests
        ("SCRUM-78", "BE-CORE-3.1.5_Implement Cancel Hold API (Release Hold Early)", 
         "Endpoint POST /api/v1/resale-listings/{id}/release-hold cho phép Buyer hủy giữ chỗ chủ động khi đóng modal, nhả vé về VERIFIED ngay lập tức."),
        ("SCRUM-78", "BE-CORE-3.1.6_Enforce Anti-Self-Purchase validation rule", 
         "Chặn Seller tự mua vé của chính mình trong HoldListingForPurchaseCommand (báo lỗi 400 Bad Request: Bạn không thể tự mua vé của chính mình)."),
        ("SCRUM-85", "BE-CORE-3.2.4_Late Payment Handling and Refund Queue on Expired Hold", 
         "Xử lý trường hợp tiền vào sau khi 10 phút giữ vé đã hết hạn: tự động đưa giao dịch vào REFUND_QUEUE và gửi thông báo cho Buyer."),
        ("SCRUM-90", "TEST-3.3.1_Automated Integration Test Suite for MF-03 End-to-End Flows", 
         "Bộ kiểm thử tự động xUnit cho Idempotent Webhook, gRPC Rollback và Background Worker nhả vé quá hạn 10 phút.")
    ]

    print(f"\n--- 2. Creating New Tasks on Jira ---")
    created_count = 0
    for parent_key, summary, desc in new_subtasks:
        code, resp = h._request("/rest/api/3/issue", method="POST", data={
            "fields": {
                "project": {"key": project_key},
                "summary": summary,
                "issuetype": {"name": "Subtask"},
                "parent": {"key": parent_key},
                "description": adf_doc(desc),
                "assignee": {"accountId": account_id}
            }
        })
        if code == 201:
            created_count += 1
            print(f"✅ Created [{resp['key']}] {summary} (Parent: {parent_key})")
        else:
            print(f"❌ Failed to create {summary}: {resp}")

    print(f"\n🎉 ALL DONE! Transitioned {len(completed_keys)} tasks to Done, created {created_count} new tasks on Jira!")

if __name__ == "__main__":
    main()
