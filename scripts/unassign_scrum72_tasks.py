import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent.parent))

from scripts.jira_helper import JiraHelper

def main():
    h = JiraHelper()
    
    # 1. Fetch all issues in the project
    next_token = None
    all_issues = []
    while True:
        payload = {
            "jql": f"project = {h.project_key} ORDER BY key ASC",
            "maxResults": 100,
            "fields": ["summary", "issuetype", "assignee", "parent"]
        }
        if next_token:
            payload["nextPageToken"] = next_token
        status, res = h._request("/rest/api/3/search/jql", method="POST", data=payload)
        if status != 200:
            print("Search error:", res)
            break
        issues = res.get("issues", [])
        all_issues.extend(issues)
        next_token = res.get("nextPageToken")
        if not next_token or not issues:
            break

    print(f"Total issues in project: {len(all_issues)}")

    # 2. Identify US-3.5 issues (to KEEP assigned to user)
    # Story: US-3.5 (SCRUM-99) and its subtasks (SCRUM-100..106)
    keep_keys = set()
    for issue in all_issues:
        key = issue["key"]
        summary = issue["fields"]["summary"]
        parent_key = issue["fields"].get("parent", {}).get("key")
        if "US-3.5" in summary or key == "SCRUM-99" or parent_key == "SCRUM-99":
            keep_keys.add(key)

    print(f"Preserving US-3.5 assignments for: {sorted(list(keep_keys))}")

    # 3. Identify all issues in SCRUM-72 epic tree
    scrum_72_tree = set(["SCRUM-72"])
    
    # Add direct children of SCRUM-72 (Stories)
    for issue in all_issues:
        parent_key = issue["fields"].get("parent", {}).get("key")
        if parent_key == "SCRUM-72":
            scrum_72_tree.add(issue["key"])

    # Add children of those stories (Subtasks)
    for issue in all_issues:
        parent_key = issue["fields"].get("parent", {}).get("key")
        if parent_key in scrum_72_tree:
            scrum_72_tree.add(issue["key"])

    print(f"Total issues in SCRUM-72 epic tree: {len(scrum_72_tree)}")

    # 4. Target issues to unassign: issues in SCRUM-72 tree EXCEPT keep_keys
    to_unassign = [k for k in sorted(list(scrum_72_tree)) if k not in keep_keys]
    print(f"\nUnassigning {len(to_unassign)} issues in SCRUM-72:")

    unassigned_count = 0
    for key in to_unassign:
        # Find summary
        issue_obj = next((i for i in all_issues if i["key"] == key), None)
        summary = issue_obj["fields"]["summary"] if issue_obj else ""
        current_assignee = issue_obj["fields"].get("assignee") if issue_obj else None
        
        status, res = h._request(f"/rest/api/3/issue/{key}/assignee", method="PUT", data={"accountId": None})
        if status in (200, 204):
            print(f"  ✅ [{key}] Unassigned -> {summary[:60]}")
            unassigned_count += 1
        else:
            print(f"  ❌ [{key}] Failed to unassign: {res}")

    print(f"\nDone! Successfully unassigned {unassigned_count}/{len(to_unassign)} issues.")

if __name__ == "__main__":
    main()
