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
    print(f"=== Adding Review Feedback Refinements to Epic-2 (SCRUM-8) on Jira ===")

    # 1. Create US-2.6 Story under SCRUM-8
    story_summary = "US-2.6_Seller Flow Refinements based on Review 1 Feedback"
    story_desc = (
        "Cập nhật phản biện Thầy Đức và Cô giáo cho luồng Người bán: "
        "bổ sung biên độ giá trần linh hoạt Delta % (BR-G01) và áp dụng "
        "mức phí ưu đãi 1-2% cùng thông điệp bảo vệ giao dịch P2P cho Private Resale (BR-L03)."
    )
    
    code, story_resp = h._request("/rest/api/3/issue", method="POST", data={
        "fields": {
            "project": {"key": project_key},
            "summary": story_summary,
            "issuetype": {"name": "Story"},
            "parent": {"key": "SCRUM-8"},
            "description": adf_doc(story_desc)
        }
    })
    
    if code != 201:
        print(f"❌ Failed to create Story US-2.6: {story_resp}")
        return
        
    story_key = story_resp["key"]
    print(f"✅ Created Story: [{story_key}] {story_summary}")

    # 2. Sub-tasks under US-2.6
    subtasks = [
        (
            "BE-CORE-2.6.1_Configurable markup percentage on Event and ResaleListing",
            "Bổ sung trường MaxResaleMarkupPercentage vào Event và cập nhật kiểm tra trần giá trong domain ResaleListing và FluentValidator."
        ),
        (
            "FE-2.6.1_Price slider ceiling warning with dynamic markup rate",
            "Cập nhật UI form đăng bán hiển thị ngưỡng giá trần tối đa theo biên độ sự kiện."
        ),
        (
            "BE-CORE-2.6.2_Discounted platform fee calculation for private resale",
            "Cập nhật logic tính phí sàn ưu đãi 1-2% khi is_private = true trong EscrowTransaction."
        ),
        (
            "FE-2.6.2_Private resale protection benefits modal and badge",
            "Thêm badge bảo vệ P2P và modal giải thích lý do nên mua bán private qua sàn bảo hiểm."
        ),
        (
            "TEST-2.6.1_Unit test configurable markup price ceiling",
            "Unit test kiểm thử giá trong ngưỡng delta và vượt ngưỡng delta cho rule BR-G01."
        )
    ]

    created_subtasks = []
    for sub_sum, sub_desc in subtasks:
        sub_code, sub_resp = h._request("/rest/api/3/issue", method="POST", data={
            "fields": {
                "project": {"key": project_key},
                "summary": sub_sum,
                "issuetype": {"name": "Subtask"},
                "parent": {"key": story_key},
                "description": adf_doc(sub_desc)
            }
        })
        if sub_code == 201:
            k = sub_resp["key"]
            created_subtasks.append(k)
            print(f"   └── Subtask: [{k}] {sub_sum}")
        else:
            print(f"   ❌ Failed subtask {sub_sum}: {sub_resp}")

    # 3. Add all Epic MF-02 Stories to Sprint 1 (Sprint ID: 3)
    sprint_id = 3
    mf02_stories = ["SCRUM-9", "SCRUM-16", "SCRUM-21", "SCRUM-28", "SCRUM-35", story_key]
    print(f"\n--- Adding MF-02 Stories {mf02_stories} to SCRUM Sprint 1 (ID: {sprint_id}) ---")
    
    code, sprint_resp = h._request(f"/rest/agile/1.0/sprint/{sprint_id}/issue", method="POST", data={
        "issues": mf02_stories
    })
    
    if code in (200, 204):
        print(f"✅ Successfully moved all MF-02 Stories into SCRUM Sprint 1!")
    else:
        print(f"❌ Failed to move issues into sprint: {sprint_resp}")

if __name__ == "__main__":
    main()
