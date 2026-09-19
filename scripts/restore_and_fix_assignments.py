import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent.parent))

from scripts.jira_helper import JiraHelper

def main():
    h = JiraHelper()

    # 1. Fetch users
    status, users = h._request(f"/rest/api/3/user/assignable/search?project={h.project_key}")
    print("=== Assignable Users ===")
    for u in users:
        print(f" • {u.get('displayName')} -> {u.get('accountId')}")

    # 2. Check changelog for SCRUM-22, 23, 24, etc. to restore previous assignees
    checked_keys = ["SCRUM-22", "SCRUM-23", "SCRUM-24", "SCRUM-8", "SCRUM-72"]
    for k in checked_keys:
        st, iss = h._request(f"/rest/api/3/issue/{k}?expand=changelog")
        if st == 200:
            histories = iss.get("changelog", {}).get("histories", [])
            for h_item in reversed(histories):
                for item in h_item.get("items", []):
                    if item.get("field") == "assignee":
                        from_user = item.get("fromString")
                        from_id = item.get("from")
                        to_user = item.get("toString")
                        print(f"[{k}] Assignee change: from '{from_user}' ({from_id}) to '{to_user}'")

if __name__ == "__main__":
    main()
