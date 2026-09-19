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

def push_auth_epic():
    h = JiraHelper()
    project_key = h.project_key
    
    # 1. Get current user's accountId
    status, user_info = h._request("/rest/api/3/myself")
    if status != 200:
        print(f"❌ Failed to fetch user info: {user_info}")
        return
        
    account_id = user_info.get("accountId")
    display_name = user_info.get("displayName")
    print(f"👤 Assigning all tickets to: {display_name} (Account ID: {account_id})")
    print(f"=== Starting Jira Backlog Push for Epic-1: Auth & Identity (Project: {project_key}) ===")

    # 2. Create Epic
    epic_summary = "Epic-1_Authentication & Identity Management"
    epic_desc = (
        "Quản lý đăng ký tài khoản, đăng nhập JWT Bearer Token, "
        "tích hợp Google OAuth 2.0, quên mật khẩu và đặt lại mật khẩu qua mã OTP gửi về Email."
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

    # Backlog Hierarchy
    stories = [
        {
            "summary": "US-1.1_User Registration, Login and Session Management with JWT",
            "desc": "Người dùng đăng ký tài khoản mới bằng Email/Mật khẩu hoặc đăng nhập để nhận JWT Token phục vụ việc xác thực khi đăng bán và giao dịch vé.",
            "subtasks": [
                ("BE-1.1.1_Setup JWT Bearer authentication & BCrypt password hasher", "Cấu hình JwtBearerHandler trong Program.cs, triển khai IJwtTokenGenerator và IPasswordHasher (BCrypt)."),
                ("BE-1.1.2_Register new user account API", "Endpoint POST /api/v1/auth/register (validate email, check duplicate email, hash password, cấp token)."),
                ("BE-1.1.3_User login with email and password API", "Endpoint POST /api/v1/auth/login (đối soát BCrypt, cấp JWT token)."),
                ("BE-1.1.4_Get current user profile API", "Endpoint GET /api/v1/auth/me với [Authorize], trích xuất UserId từ Claims."),
                ("BE-1.1.5_Integrate CurrentUserService into Resale Listings flow", "Trích xuất UserId thật từ JWT claims khi tạo tin bán vé (SCRUM-25) thay vì fallback user mẫu.")
            ]
        },
        {
            "summary": "US-1.2_Social Authentication via Google OAuth 2.0",
            "desc": "Người dùng đăng nhập nhanh bằng tài khoản Google, hệ thống tự động đồng bộ tài khoản và cấp JWT Token của TicketShield.",
            "subtasks": [
                ("BE-1.2.1_Google OAuth ID token verification and exchange API", "Endpoint POST /api/v1/auth/google (xác thực Google IdToken qua Google.Apis.Auth, tự động tạo user nếu mới, cấp JWT token).")
            ]
        },
        {
            "summary": "US-1.3_Password Recovery and OTP Email Verification",
            "desc": "Người dùng quên mật khẩu yêu cầu gửi mã OTP 6 số về email và đặt lại mật khẩu mới an toàn.",
            "subtasks": [
                ("BE-1.3.1_Mock email service interface & forgot password OTP API", "Endpoint POST /api/v1/auth/forgot-password (sinh OTP 6 số hạn 10 phút, gọi IEmailService mock in ra console chờ merge SMTP của Thịnh)."),
                ("BE-1.3.2_Reset password with OTP verification API", "Endpoint POST /api/v1/auth/reset-password (kiểm tra OTP hợp lệ và cập nhật mật khẩu mới bằng BCrypt).")
            ]
        },
        {
            "summary": "US-1.4_Testing & Security Validation for Authentication",
            "desc": "Kiểm thử tự động xUnit cho toàn bộ các ca đăng ký, đăng nhập đúng/sai, token hết hạn, và luồng OTP đặt lại mật khẩu.",
            "subtasks": [
                ("TEST-1.1.1_Unit & Integration tests for Auth flows", "Viết test kiểm tra đăng ký, đăng nhập thành công/thất bại, hash mật khẩu và OTP đặt lại mật khẩu.")
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
                "description": adf_doc(story_data["desc"]),
                "assignee": {"accountId": account_id}
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
                    "description": adf_doc(sub_desc),
                    "assignee": {"accountId": account_id}
                }
            })
            
            if code == 201:
                total_subtasks += 1
                print(f"     ↳ 🔨 [{sub_resp['key']}] {sub_summary}")
            else:
                print(f"     ↳ ❌ Failed subtask {sub_summary}: {sub_resp}")

    print(f"\n🎉 ALL DONE! Created 1 Epic, {len(stories)} Stories, and {total_subtasks} Subtasks on Jira!")
    print(f"👉 Board URL: https://ticketshield-task.atlassian.net/jira/software/projects/{project_key}/boards/1/backlog")

if __name__ == "__main__":
    push_auth_epic()
