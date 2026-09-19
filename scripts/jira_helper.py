import json
import base64
import urllib.request
import urllib.error
import sys
from pathlib import Path

# Force UTF-8 on Windows terminal
if sys.stdout.encoding != 'utf-8':
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass

CONFIG_PATH = Path(__file__).parent / "jira_config.json"

class JiraHelper:
    def __init__(self, config_file=CONFIG_PATH):
        if not config_file.exists():
            raise FileNotFoundError(f"Config file not found: {config_file}")
        
        with open(config_file, "r", encoding="utf-8") as f:
            self.config = json.load(f)
            
        self.base_url = self.config["jira_url"].rstrip("/")
        self.email = self.config["email"]
        self.api_token = self.config["api_token"]
        self.project_key = self.config["project_key"]
        
        # Build Basic Auth header
        credentials = f"{self.email}:{self.api_token}"
        encoded_credentials = base64.b64encode(credentials.encode("utf-8")).decode("utf-8")
        self.headers = {
            "Authorization": f"Basic {encoded_credentials}",
            "Accept": "application/json",
            "Content-Type": "application/json"
        }

    def _request(self, endpoint, method="GET", data=None):
        url = f"{self.base_url}{endpoint}"
        req_data = json.dumps(data).encode("utf-8") if data else None
        req = urllib.request.Request(url, data=req_data, headers=self.headers, method=method)
        
        try:
            with urllib.request.urlopen(req) as resp:
                status_code = resp.status
                content = resp.read().decode("utf-8")
                return status_code, json.loads(content) if content else {}
        except urllib.error.HTTPError as e:
            error_msg = e.read().decode("utf-8")
            return e.code, {"error": error_msg}
        except Exception as e:
            return 500, {"error": str(e)}

    def test_connection(self):
        print(f"--- 1. Testing Jira Connection to: {self.base_url} ---")
        status, user_info = self._request("/rest/api/3/myself")
        if status == 200:
            print(f"✅ Connection SUCCESSFUL!")
            print(f"   - Display Name : {user_info.get('displayName')}")
            print(f"   - Email        : {user_info.get('emailAddress')}")
            print(f"   - Account ID   : {user_info.get('accountId')}")
            print(f"   - Active       : {user_info.get('active')}")
        else:
            print(f"❌ Connection FAILED (Status {status}): {user_info}")
            return False

        print(f"\n--- 2. Checking Project: '{self.project_key}' ---")
        status, project_info = self._request(f"/rest/api/3/project/{self.project_key}")
        if status == 200:
            print(f"✅ Project Found!")
            print(f"   - Name        : {project_info.get('name')}")
            print(f"   - Key         : {project_info.get('key')}")
            print(f"   - Project Type: {project_info.get('projectTypeKey')}")
            
            # Print available issue types
            issue_types = project_info.get("issueTypes", [])
            print(f"   - Issue Types : {', '.join([it.get('name') for it in issue_types])}")
            return True
    def transition_issue(self, issue_key, target_status):
        """Transition an issue to a target status (e.g. 'In Progress', 'Done', 'In Review')"""
        status_code, resp = self._request(f"/rest/api/3/issue/{issue_key}/transitions")
        if status_code != 200:
            print(f"❌ Failed to fetch transitions for {issue_key}: {resp}")
            return False
            
        transitions = resp.get("transitions", [])
        matched = None
        for t in transitions:
            if t["name"].lower() == target_status.lower() or t["to"]["name"].lower() == target_status.lower():
                matched = t
                break
                
        if not matched:
            avail = [f"'{t['name']}' (to: {t['to']['name']})" for t in transitions]
            print(f"❌ Cannot transition {issue_key} to '{target_status}'. Available transitions: {', '.join(avail)}")
            return False
            
        code, post_resp = self._request(
            f"/rest/api/3/issue/{issue_key}/transitions",
            method="POST",
            data={"transition": {"id": matched["id"]}}
        )
        if code == 204:
            print(f"✅ Issue {issue_key} transitioned to '{matched['to']['name']}' successfully!")
            return True
        else:
            print(f"❌ Transition error for {issue_key}: {post_resp}")
            return False

if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser(description="Jira Helper CLI")
    parser.add_argument("--test", action="store_true", help="Test Jira connection")
    parser.add_argument("--transition", nargs=2, metavar=("ISSUE_KEY", "STATUS"), help="Transition issue status (e.g. SCRUM-15 Done)")
    args = parser.parse_args()

    try:
        helper = JiraHelper()
        if args.transition:
            helper.transition_issue(args.transition[0], args.transition[1])
        else:
            helper.test_connection()
    except Exception as ex:
        print(f"Error: {ex}")
        sys.exit(1)
