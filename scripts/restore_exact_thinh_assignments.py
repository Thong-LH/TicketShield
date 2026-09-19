import json
import requests
import time
import sys

if sys.stdout.encoding != 'utf-8':
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass

with open('scripts/jira_config.json') as f:
    cfg = json.load(f)

base_url = cfg['jira_url'].rstrip('/')
auth = (cfg['email'], cfg['api_token'])
session = requests.Session()
session.auth = auth

headers = {
    "Accept": "application/json",
    "Content-Type": "application/json"
}

def get_issue(key):
    url = f"{base_url}/rest/api/3/issue/{key}?expand=changelog"
    for _ in range(3):
        try:
            res = session.get(url, headers=headers, timeout=10)
            if res.status_code == 200:
                return res.json()
            elif res.status_code == 404:
                return None
        except Exception:
            time.sleep(1)
    return None

def set_assignee(key, account_id):
    url = f"{base_url}/rest/api/3/issue/{key}/assignee"
    res = session.put(url, headers=headers, json={"accountId": account_id}, timeout=10)
    return res.status_code in [200, 204]

THONG_ID = "712020:5af6d284-798d-4bca-8a9b-173809cb77bc"

print("=== 🔄 RESTORING EXACT TEAM ASSIGNMENTS MADE BY THỊNH / TEAMMATES ===")

restored_count = 0
for i in range(1, 125):
    key = f"SCRUM-{i}"
    data = get_issue(key)
    if not data:
        continue
    
    fields = data.get('fields', {})
    summary = fields.get('summary', '')
    curr = fields.get('assignee', {})
    curr_id = curr.get('accountId') if curr else None
    curr_name = curr.get('displayName') if curr else 'Unassigned'
    
    histories = data.get('changelog', {}).get('histories', [])
    
    # Find the latest assignment made by Nguyen Hung Thinh (or other teammate)
    target_account_id = None
    target_name = None
    
    for h in histories:
        author = h.get('author', {}).get('displayName', '')
        if "Thinh" in author or "Tùng" in author or "Linh" in author:
            for item in h.get('items', []):
                if item.get('field') == 'assignee':
                    to_id = item.get('to')
                    to_name = item.get('toString')
                    if to_id and to_name:
                        target_account_id = to_id
                        target_name = to_name
    
    # If Thinh/teammates had assigned this task to someone, make sure it is assigned to that person!
    if target_account_id and curr_id != target_account_id:
        print(f"[{key}] Restoring: '{curr_name}' -> '{target_name}' (ID: {target_account_id}) | {summary[:50]}")
        if set_assignee(key, target_account_id):
            print(f"  ✅ Restored successfully!")
            restored_count += 1
        else:
            print(f"  ❌ Failed to set assignee for {key}")

print(f"\n=======================================================")
print(f"🎉 Restored {restored_count} tasks exactly as assigned by Thịnh / Teammates!")
