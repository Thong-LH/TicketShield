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
    print("=== Updating Architecture Tasks on Jira to Focus on Service Separation (Identity vs TradingCore) ===")

    updates = [
        # Epic-ARCH
        ("SCRUM-59", {
            "summary": "Epic-ARCH_Microservices Architecture: Identity and TradingCore Separation",
            "description": adf_doc(
                "Tái cấu trúc kiến trúc phân rã Monolith: Tách riêng TicketShield.Identity (Port 5002 / identity_db) "
                "và TicketShield.TradingCore (Port 5000 / trading_db), áp dụng cơ chế JWT Stateless triệt tiêu gọi chéo database, "
                "tích hợp RabbitMQ MassTransit làm Event Bus."
            )
        }),

        # Story 1: RabbitMQ & Contracts (Giữ nguyên mục tiêu, làm rõ mô tả)
        ("SCRUM-60", {
            "summary": "US-ARCH.1_Event-driven messaging infrastructure with RabbitMQ and MassTransit",
            "description": adf_doc(
                "Cấu hình container RabbitMQ và tích hợp MassTransit vào TradingCore và Contracts để sẵn sàng phát tán sự kiện bất đồng bộ."
            )
        }),
        ("SCRUM-61", {
            "summary": "INFRA-1.1_Add RabbitMQ 3 Management container to docker-compose",
            "description": adf_doc("Khai báo service rabbitmq (image rabbitmq:3-management, cổng 5672 và 15672) trong docker-compose.yml.")
        }),
        ("SCRUM-62", {
            "summary": "CONTRACT-1.1_Define shared event interfaces in TicketShield.Contracts",
            "description": adf_doc("Tạo thư mục Events trong TicketShield.Contracts chứa các interface sự kiện dùng chung.")
        }),
        ("SCRUM-63", {
            "summary": "CORE-1.1_Configure MassTransit RabbitMQ publisher in TradingCore",
            "description": adf_doc("Đăng ký DI MassTransit trong TradingCore Program.cs sẵn sàng publish message lên RabbitMQ.")
        }),

        # Story 2: Chuyển từ SettlementWorker sang Bóc tách Identity Service & Database
        ("SCRUM-64", {
            "summary": "US-ARCH.2_Extract TicketShield.Identity service and database segregation",
            "description": adf_doc(
                "Tách toàn bộ phân hệ Auth/User thành dịch vụ độc lập TicketShield.Identity (:5002) sở hữu identity_db; "
                "TradingCore (:5000) sở hữu trading_db, triệt tiêu hoàn toàn Shared Database."
            )
        }),
        ("SCRUM-65", {
            "summary": "INFRA-2.1_Setup identity_db and trading_db segregated connections",
            "description": adf_doc("Cấu hình chuỗi kết nối và DbContext riêng biệt cho identity_db (Users, UserBankAccounts) và trading_db.")
        }),
        ("SCRUM-66", {
            "summary": "IDENTITY-2.1_Scaffold TicketShield.Identity service with JWT Bearer auth",
            "description": adf_doc("Khởi tạo project TicketShield.Identity (:5002), di chuyển AuthController, Register, Login, Google OAuth, Password Reset.")
        }),
        ("SCRUM-67", {
            "summary": "CORE-2.1_Clean Auth from TradingCore and configure stateless JWT validation",
            "description": adf_doc("Dọn dẹp code Auth khỏi TradingCore, cấu hình JWT Bearer xác thực chữ ký độc lập qua Secret Key chung.")
        }),

        # Story 3: Chuyển sang Tích hợp liên thông Client & Soft-reference User
        ("SCRUM-68", {
            "summary": "US-ARCH.3_Service communication, soft references and client integration",
            "description": adf_doc(
                "Cập nhật TradingCore sử dụng User ID mềm (GUID soft reference từ token claims), cập nhật Frontend API client định tuyến đúng giữa 2 service."
            )
        }),
        ("SCRUM-69", {
            "summary": "CORE-3.1_Convert hard User foreign keys to soft reference GUIDs in trading_db",
            "description": adf_doc("Loại bỏ navigation property Users khỏi ResaleListing và EscrowTransaction, chuyển thành SellerId và BuyerId GUID độc lập.")
        }),
        ("SCRUM-70", {
            "summary": "FE-3.1_Update frontend API client routes for Identity (:5002) and TradingCore (:5000)",
            "description": adf_doc("Cấu hình @ticketshield/api-client tách base URL: Auth gọi port 5002, Resale/Marketplace gọi port 5000.")
        }),
        ("SCRUM-71", {
            "summary": "TEST-3.1_End-to-end integration test for login, token exchange and trading",
            "description": adf_doc("Kiểm thử liên thông: Đăng nhập tại Identity (:5002) -> Nhận JWT -> Dùng JWT gọi TradingCore (:5000) niêm yết vé thành công.")
        }),
    ]

    for key, fields in updates:
        code, resp = h._request(f"/rest/api/3/issue/{key}", method="PUT", data={"fields": fields})
        if code in (200, 204):
            print(f"✅ Updated [{key}]: {fields['summary']}")
        else:
            print(f"❌ Failed to update [{key}]: {resp}")

    print("\n🎉 All Architecture tasks on Jira successfully adjusted to focus on Identity & TradingCore Separation!")

if __name__ == "__main__":
    main()
