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
    print(f"=== Creating Epic MF-03 & Homepage Revamp Tasks on Jira ===")

    # 2. Create Epic-3_MF-03
    epic_summary = "Epic-3_MF-03: P2P Purchase, Escrow Holding & Ownership Transfer"
    epic_desc = (
        "Xây dựng hoàn chỉnh luồng nghiệp vụ MF-03: Giữ chỗ vé 10 phút (TRANSACTING), sinh mã Dynamic VietQR, "
        "tiếp nhận Webhook thanh toán an toàn (Idempotent), tự động khóa tiền ký quỹ EscrowTransaction (LOCKED), "
        "gọi gRPC sang MockOrganizer hủy vé cũ và cấp vé mới chính chủ cho Buyer. Khóa hai đầu bảo vệ an toàn, "
        "tích hợp toàn bộ các dịch vụ ngoại vi (VietQR Hub, SMTP Mailer, Ngrok Tunnel, STK Seller) "
        "và tái thiết kế trải nghiệm Trang chủ (Homepage / Marketplace) với Slider sự kiện HOT động và đa dạng danh mục."
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

    # 3. Comprehensive Stories and Subtasks
    stories_data = [
        {
            "summary": "US-3.0_External Services Setup & Peripheral Integrations (VietQR, SMTP & Tunnel)",
            "desc": "Thiết lập toàn bộ tài khoản dịch vụ ngoại vi, tích hợp cổng thanh toán VietQR, cấu hình máy chủ gửi mail SMTP và kênh Webhook Tunnel phục vụ môi trường phát triển.",
            "subtasks": [
                (
                    "EXT-3.0.1_VietQR Hub account registration & API credentials setup",
                    "Đăng ký tài khoản cổng VietQR/SePAY/Casso, lấy Client ID/API Key và cấu hình Webhook URL nhận thông báo biến động số dư."
                ),
                (
                    "EXT-3.0.2_SMTP Mail server setup & environment credentials configuration",
                    "Cấu hình tài khoản SMTP (SendGrid/Gmail App Password), khai báo biến môi trường Host, Port, Username, Password vào appsettings và .env."
                ),
                (
                    "EXT-3.0.3_Local Webhook tunneling setup with Ngrok or Cloudflare Tunnel",
                    "Cấu hình kịch bản đường hầm tunneling để hứng webhook chuyển khoản ngân hàng thực tế từ cổng VietQR về localhost API."
                ),
                (
                    "EXT-3.0.4_Design HTML transaction email templates for Buyer & Seller",
                    "Thiết kế 2 mẫu email HTML: 1) Cấp vé mới chính chủ cho Buyer kèm mã vé/QR; 2) Thông báo cho Seller tiền đã vào quỹ ký quỹ Escrow LOCKED."
                )
            ]
        },
        {
            "summary": "US-3.1_Ticket Hold, Seller Bank Account & Dynamic VietQR Generation (10-Minute Lock)",
            "desc": "Khóa giữ chỗ vé trong 10 phút (TRANSACTING), sinh mã Dynamic VietQR chuẩn NAPAS 247 và quản lý tài khoản ngân hàng thụ hưởng của Seller.",
            "subtasks": [
                (
                    "BE-CORE-3.1.1_Implement HoldListingForPurchaseCommand & unique payment reference",
                    "Tạo Command chuyển Listing sang TRANSACTING 10 phút, sinh chuỗi transfer_content độc nhất để đối soát ngân hàng."
                ),
                (
                    "BE-CORE-3.1.2_Build Dynamic VietQR QuickLink generation service",
                    "Service sinh URL hình ảnh mã QR VietQR chuẩn NAPAS 247 chứa BankCode, AccountNumber, Amount, TransferContent."
                ),
                (
                    "BE-CORE-3.1.3_Implement Auto-Release Expired Hold background worker",
                    "Background Service tự động quét và nhả vé từ TRANSACTING về VERIFIED nếu sau 10 phút Buyer không thanh toán (BR-E01)."
                ),
                (
                    "BE-CORE-3.1.4_Implement Seller Bank Account API (UserBankAccount)",
                    "Endpoint POST & GET /api/v1/user-bank-accounts để Seller liên kết STK ngân hàng thụ hưởng chính chủ phục vụ giải ngân Payout sau này."
                ),
                (
                    "FE-3.1.1_Implement Checkout Modal with 10-Minute Countdown & Dynamic VietQR",
                    "Modal thanh toán hiển thị mã QR VietQR, đồng hồ đếm ngược 10 phút, thông tin tài khoản và nút Copy nhanh số tiền/nội dung."
                ),
                (
                    "FE-3.1.2_Build Seller Bank Account management form in Profile/Settings",
                    "Màn hình/Modal cho phép Seller chọn mã ngân hàng và nhập STK thụ hưởng chính chủ."
                )
            ]
        },
        {
            "summary": "US-3.2_Idempotent Payment Webhook, ACID Escrow Lock & Dual-Hold Enforcement",
            "desc": "Tiếp nhận Webhook VietQR xử lý bất biến (Idempotent), tạo bản ghi EscrowTransaction (LOCKED), tính hạn giải ngân UnlockAt và kích hoạt khóa hai đầu.",
            "subtasks": [
                (
                    "BE-CORE-3.2.1_Implement POST /api/v1/payments/vietqr-webhook with idempotency check",
                    "Endpoint tiếp nhận webhook ngân hàng, xác thực signature và kiểm tra transaction_reference chống xử lý trùng (BR-E02)."
                ),
                (
                    "BE-CORE-3.2.2_Atomic DB Transaction for Escrow creation and Dual-Hold flags",
                    "Trong 1 DB Transaction: Cập nhật Listing sang SOLD, tạo Escrow LOCKED, tính UnlockAt = Min(Now + 24h, EventStart - 2h), gắn cờ in_settlement_buffer = true (BR-G03, BR-G04)."
                ),
                (
                    "BE-CORE-3.2.3_Dispatch transactional confirmation emails to Buyer and Seller",
                    "Gửi email bất đồng bộ thông báo mua vé thành công cho Buyer và thông báo tiền ký quỹ cho Seller qua SMTP."
                ),
                (
                    "FE-3.2.1_Implement Payment status polling & auto-redirect on Checkout Modal",
                    "Polling/Realtime listener lắng nghe trạng thái thanh toán thành công để tự động chuyển hướng Buyer sang trang thông báo thành công."
                )
            ]
        },
        {
            "summary": "US-3.3_gRPC Ownership Transfer & Ticket Reissuance on MockOrganizer",
            "desc": "Điều phối gRPC sang Ban tổ chức (MockOrganizer) để vô hiệu hóa vé cũ của Seller và cấp phát mã vé mới chính chủ đứng tên Buyer.",
            "subtasks": [
                (
                    "CONTRACT-3.3.1_Update organizer_resale.proto with TransferOwnership RPC",
                    "Bổ sung RPC TransferOwnership, TransferOwnershipRequest, TransferOwnershipResponse và compile gRPC stubs."
                ),
                (
                    "BE-MOCK-3.3.1_Implement TransferOwnership RPC in MockOrganizer.API",
                    "Chuyển trạng thái vé cũ sang TRANSFERRED (vô hiệu hóa mã QR cũ) và tạo mã vé mới mang thông tin định danh Buyer trạng thái VALID."
                ),
                (
                    "BE-MOCK-3.3.2_Implement Buyer Ticket Query API (GET /api/v1/mock-tickets/my-tickets)",
                    "Endpoint giả lập app BTC cho phép Buyer tra cứu danh sách vé mới được cấp."
                ),
                (
                    "BE-CORE-3.3.1_gRPC Client integration with 100% rollback protection",
                    "Core gọi sang MockOrganizer; nếu gRPC đứt kết nối hoặc lỗi, tự động rollback toàn bộ DB transaction không để treo tiền."
                )
            ]
        },
        {
            "summary": "US-3.4_Buyer Purchased Tickets & Seller Escrow Status UI",
            "desc": "Giao diện quản lý vé đã mua cho Buyer, hiển thị trạng thái tiền ký quỹ cho Seller và hỗ trợ chọn nhanh vé để bán lại.",
            "subtasks": [
                (
                    "FE-3.4.1_Build Buyer My Tickets page with dynamic QR entry pass",
                    "Trang danh sách vé đã mua thành công, hiển thị chi tiết sự kiện, thông tin chỗ ngồi, mã vé mới và mã QR vào cổng."
                ),
                (
                    "FE-3.4.2_Update Seller Listing status badge with Escrow LOCKED notice",
                    "Cập nhật danh sách vé của Seller: đổi badge sang SOLD kèm dòng thông báo: 'Tiền đang được bảo vệ trong quỹ Escrow LOCKED - Sẽ giải ngân sau 24h'."
                ),
                (
                    "FE-3.4.3_Implement SellTicketPage dropdown from purchased eligible tickets",
                    "Dropdown 'Chọn từ vé của tôi' trên trang bán vé tự động hiển thị các vé đã giải ngân xong (hết cờ in_settlement_buffer) để bán lại nhanh (BR-G05)."
                )
            ]
        },
        {
            "summary": "US-3.5_Revamp Homepage & Marketplace with Dynamic Trending Events Slider & Rich Categories",
            "desc": "Nâng cấp giao diện Trang chủ & Marketplace: Tích hợp Slider Carousel sự kiện HOT fetch dữ liệu động từ API (không hardcode), dải Marquee Ticker, danh mục thể loại, lưới vé Fair-Price và Bento Grid bảo chứng niềm tin.",
            "subtasks": [
                (
                    "BE-CORE-3.5.1_Implement GET /api/v1/events/trending API endpoint",
                    "Viết Query trong Core trả về danh sách các sự kiện đang HOT nhất (nhiều vé niêm yết, sắp diễn ra) kèm poster banner, địa điểm và giá vé sàn thấp nhất."
                ),
                (
                    "FE-3.5.1_Build Dynamic Hero & 3D Trending Events Carousel",
                    "Hero 2 cột: Cột trái Smart Search & Quick Filter, Cột phải 3D Carousel trượt sự kiện HOT động với nút 'Săn vé ngay'."
                ),
                (
                    "FE-3.5.2_Build Live Ticket Verification Marquee Ticker",
                    "Thanh chạy thông báo ngang không ngắt quãng (Infinite Marquee) hiển thị ẩn danh các giao dịch cấp vé mới và bảo hiểm tiền thành công theo thời gian thực."
                ),
                (
                    "FE-3.5.3_Build Category Filter Pills & Discovery Tabs",
                    "Dải thẻ phân loại thể loại (Concert, EDM, Thể thao, Kịch) và các tab khám phá: 'Sắp diễn ra trong 48h', 'Vé chiết khấu tốt nhất', 'Mới lên sàn hôm nay'."
                ),
                (
                    "FE-3.5.4_Build Resale Ticket Grid with Fair-Price Meter & Holding State",
                    "Lưới thẻ vé chi tiết chỗ ngồi, Fair-Price Meter so sánh giá bán lại vs giá gốc BTC, và badge làm mờ đếm ngược 10 phút khi có người giữ chỗ (BR-E01)."
                ),
                (
                    "FE-3.5.5_Build Bento Grid Trust Pillars & Live Platform Stats",
                    "Khối Bento Grid 3 trụ cột: Cấp vé mới 100% từ BTC, Bảo hiểm tiền 24h, Người thật - Vé thật kèm bộ đếm số vé đã sang tên an toàn."
                ),
                (
                    "FE-3.5.6_Build Dual-Persona Switch & Seller Call-To-Action Banner",
                    "Header toggle Buyer/Seller và Banner kính mờ mời khán giả có vé không dùng đăng bán lại chính chủ an toàn."
                )
            ]
        },
        {
            "summary": "US-3.6_MF03 Quality Assurance, Security & Integration Testing",
            "desc": "Bộ kiểm thử tự động toàn diện cho luồng giữ chỗ, đối soát thanh toán trùng, bảo mật webhook và phục hồi lỗi gRPC.",
            "subtasks": [
                (
                    "TEST-3.6.1_Unit tests for Hold timeout, VietQR generation and auto-release",
                    "Test logic đếm ngược 10 phút, kiểm tra vé khóa TRANSACTING và tự động mở lại VERIFIED khi hết hạn."
                ),
                (
                    "TEST-3.6.2_Integration tests for Idempotent Webhook and ACID Escrow creation",
                    "Bắn 2 request webhook trùng mã giao dịch, xác minh hệ thống chỉ tạo duy nhất 1 Escrow và 1 vé mới."
                ),
                (
                    "TEST-3.6.3_Rollback test on gRPC MockOrganizer failure",
                    "Giả lập MockOrganizer trả lỗi khi sang tên, xác minh Listing và Escrow được Rollback an toàn 100%."
                ),
                (
                    "TEST-3.6.4_Webhook Security test with invalid signatures and fake payloads",
                    "Kiểm thử bảo mật: từ chối các request webhook không hợp lệ hoặc thiếu chữ ký bảo mật."
                )
            ]
        }
    ]

    for s_idx, story in enumerate(stories_data, 1):
        print(f"\n📂 [{s_idx}/{len(stories_data)}] Creating Story: {story['summary']}")
        s_code, s_resp = h._request("/rest/api/3/issue", method="POST", data={
            "fields": {
                "project": {"key": project_key},
                "summary": story["summary"],
                "issuetype": {"name": "Story"},
                "parent": {"key": epic_key},
                "description": adf_doc(story["desc"]),
                "assignee": {"accountId": account_id}
            }
        })

        if s_code != 201:
            print(f"❌ Failed to create Story '{story['summary']}': {s_resp}")
            continue

        story_key = s_resp["key"]
        print(f"   └── ✅ Created Story: [{story_key}]")

        for sub_summary, sub_desc in story["subtasks"]:
            sub_code, sub_resp = h._request("/rest/api/3/issue", method="POST", data={
                "fields": {
                    "project": {"key": project_key},
                    "summary": sub_summary,
                    "issuetype": {"name": "Subtask"},
                    "parent": {"key": story_key},
                    "description": adf_doc(sub_desc),
                    "assignee": {"accountId": account_id}
                }
            })

            if sub_code == 201:
                print(f"       ├── 🔨 Subtask: [{sub_resp['key']}] {sub_summary}")
            else:
                print(f"       ├── ❌ Failed Subtask '{sub_summary}': {sub_resp}")

    print("\n🎉 === ALL MF-03 & HOMEPAGE REVAMP TASKS SUCCESSFULLY CREATED ON JIRA! ===")

if __name__ == "__main__":
    main()
