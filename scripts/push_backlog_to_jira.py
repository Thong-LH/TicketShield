import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent.parent))

from scripts.jira_helper import JiraHelper

# Force UTF-8 on Windows terminal
if sys.stdout.encoding != 'utf-8':
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass

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

def push_mf02():
    h = JiraHelper()
    project_key = h.project_key
    print(f"=== Starting Jira Backlog Push for MF-02 (Project: {project_key}) ===")

    # 1. Create Epic
    epic_summary = "Epic-2_MF-02: Ticket Verification & P2P Resale Listing"
    epic_desc = (
        "Xác thực vé chính chủ từ Ban tổ chức (MockOrganizer) qua OTP, "
        "kiểm soát trần giá bán lại (resale <= original), hỗ trợ bán công khai (Public) "
        "hoặc bán riêng tư (Private Sale) cho bạn bè và quản lý vé đã niêm yết."
    )
    
    code, epic_resp = h._request("/rest/api/3/issue", method="POST", data={
        "fields": {
            "project": {"key": project_key},
            "summary": epic_summary,
            "issuetype": {"name": "Epic"},
            "description": adf_doc(epic_desc)
        }
    })
    
    if code != 201:
        print(f"❌ Failed to create Epic: {epic_resp}")
        return
        
    epic_key = epic_resp["key"]
    print(f"✅ Created Epic: [{epic_key}] {epic_summary}")

    # Backlog Hierarchy
    stories = [
        {
            "summary": "US-2.1_Seller requests ticket ownership verification OTP from Organizer",
            "desc": "Người bán nhập mã vé gốc và email F0 để Ban tổ chức gửi mã OTP 6 số qua email xác nhận quyền sở hữu hợp pháp.",
            "subtasks": [
                ("FE-2.1.1_Ticket code & email verification input component", "Ô nhập mã vé gốc + Email F0, format uppercase cho mã vé, validate email."),
                ("FE-2.1.2_OTP countdown timer & resend button component", "Nút gửi OTP kèm đồng hồ đếm ngược 60s cooldown chống spam."),
                ("BE-MOCK-2.1.1_Send ticket verification OTP api", "Kiểm tra vé tồn tại & email khớp trong organizer_db, sinh OTP 6 số, lưu hash hạn 5 phút."),
                ("BE-MOCK-2.1.2_SMTP email service for OTP delivery", "Cấu hình Mail Service gửi email HTML chứa mã OTP và tên Concert đến email chủ vé."),
                ("BE-CORE-2.1.1_Request OTP proxy api with resilient client", "MediatR RequestOtpCommand, dùng Polly HttpClient gọi sang MockOrganizer."),
                ("DB-2.1.1_Setup mock_tickets and mock_otps schema", "Script tạo bảng trong organizer_db kèm chỉ mục tìm kiếm theo mã vé.")
            ]
        },
        {
            "summary": "US-2.2_Seller verifies OTP and sets price within ceiling",
            "desc": "Người bán nhập OTP và cài đặt giá bán lại tuân thủ nghiêm ngặt Quy tắc trần giá (ResalePrice <= OriginalPrice).",
            "subtasks": [
                ("FE-2.2.1_OTP 6-digit input modal component", "Modal 6 ô vuông tự nhảy focus khi gõ số OTP, xử lý paste chuỗi từ clipboard."),
                ("FE-2.2.2_Price ceiling slider & alert component", "Thanh trượt/input giá bán: hiện giá gốc tham chiếu, báo đỏ nếu nhập quá giá gốc."),
                ("BE-MOCK-2.2.1_Verify OTP and lock ticket for resale api", "Đối soát OTP còn hạn hay không. Nếu đúng, đổi trạng thái vé gốc sang LOCKED_FOR_RESALE."),
                ("BE-CORE-2.2.1_Validate price ceiling domain rule", "Domain Rule ném BusinessRuleViolationException nếu ResalePrice > OriginalPrice.")
            ]
        },
        {
            "summary": "US-2.3_Seller selects listing mode (Public vs Private) and publishes ticket",
            "desc": "Người bán chọn đăng bán Công khai lên sàn hoặc Bán riêng tư cho bạn bè kèm Link mã hóa chứa token bí mật.",
            "subtasks": [
                ("FE-2.3.1_Listing mode selector component (Public vs Private)", "Radio button chọn giữa 'Đăng lên chợ công khai' và 'Bán riêng cho bạn bè'."),
                ("FE-2.3.2_Private share link modal & QR generator", "Modal thành công, hiển thị link riêng tư kèm nút copy và mã QR gửi bạn bè."),
                ("FE-2.3.3_Private listing preview page", "Màn hình người bạn mở link riêng tư để xem thông tin vé (chỉ xem được khi có đúng token)."),
                ("BE-CORE-2.3.1_Create resale listing with private token api", "Xử lý sinh GUID token bí mật nếu is_private = true, lưu vào resale_listings."),
                ("BE-CORE-2.3.2_Validate private listing access token api", "Endpoint xác thực token khi truy cập link private (thiếu/sai token -> 403 Forbidden)."),
                ("DB-CORE-2.3.1_Setup resale_listings table with partial unique index", "Cấu hình EF Core Fluent API và Partial Unique Index chống bán trùng.")
            ]
        },
        {
            "summary": "US-2.4_Seller manages listed tickets and cancels listing",
            "desc": "Người bán xem danh sách vé đã đăng, lấy lại link bán riêng hoặc hủy niêm yết để hoàn vé gốc bên BTC.",
            "subtasks": [
                ("FE-2.4.1_Seller listed tickets table & status badges component", "Danh sách vé của tôi kèm huy hiệu Public/Private/Verified/Sold."),
                ("FE-2.4.2_Cancel listing confirmation modal & copy link button", "Nút lấy lại link private và nút hủy đăng bán."),
                ("FE-2.4.3_Seller ticket management page", "Trang tổng hợp quản lý vé của Seller."),
                ("BE-CORE-2.4.1_Get seller listings api", "Query lấy danh sách vé theo CurrentUserService.UserId."),
                ("BE-CORE-2.4.2_Cancel resale listing api", "Kiểm tra vé chưa bị mua, đổi sang CANCELLED, gọi MockOrganizer mở khóa vé."),
                ("BE-MOCK-2.4.1_Unlock original ticket api", "Mở khóa vé gốc từ LOCKED_FOR_RESALE về VALID bên hệ thống BTC.")
            ]
        },
        {
            "summary": "US-2.5_Quality Assurance & Security Testing for MF-02",
            "desc": "Kiểm thử tự động cho toàn bộ luồng MF-02: logic trần giá, vòng đời OTP và bảo mật link riêng tư.",
            "subtasks": [
                ("TEST-2.5.1_Unit test price ceiling domain rule", "Viết unit test C# kiểm tra: ResalePrice > OriginalPrice phải ném lỗi; <= phải pass."),
                ("TEST-2.5.2_Integration test OTP verification & expiry", "Test API luồng OTP: đúng OTP, sai OTP quá 3 lần, OTP hết hạn sau 5 phút."),
                ("TEST-2.5.3_Security test private token protection", "Test kiểm tra vé private không bao giờ truy cập được nếu thiếu token bí mật.")
            ]
        }
    ]

    total_subtasks = 0

    for s_idx, story_data in enumerate(stories, 1):
        # Create Story linked to Epic
        code, story_resp = h._request("/rest/api/3/issue", method="POST", data={
            "fields": {
                "project": {"key": project_key},
                "summary": story_data["summary"],
                "issuetype": {"name": "Story"},
                "parent": {"key": epic_key},
                "description": adf_doc(story_data["desc"])
            }
        })
        
        if code != 201:
            print(f"❌ Failed to create Story {story_data['summary']}: {story_resp}")
            continue
            
        story_key = story_resp["key"]
        print(f"\n  📖 [{story_key}] {story_data['summary']}")

        # Create Subtasks under this Story
        for sub_summary, sub_desc in story_data["subtasks"]:
            code, sub_resp = h._request("/rest/api/3/issue", method="POST", data={
                "fields": {
                    "project": {"key": project_key},
                    "summary": sub_summary,
                    "issuetype": {"name": "Subtask"},
                    "parent": {"key": story_key},
                    "description": adf_doc(sub_desc)
                }
            })
            
            if code == 201:
                total_subtasks += 1
                print(f"     ↳ 🔨 [{sub_resp['key']}] {sub_summary}")
            else:
                print(f"     ↳ ❌ Failed subtask {sub_summary}: {sub_resp}")

    print(f"\n🎉 ALL DONE! Created 1 Epic, {len(stories)} Stories, and {total_subtasks} Subtasks on Jira!")

if __name__ == "__main__":
    push_mf02()
