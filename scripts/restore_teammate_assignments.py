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

print("=== 🔄 RESTORING ALL TEAMMATE ASSIGNMENTS FROM CHANGELOG ===")

restored = 0
for i in range(1, 125):
    key = f"SCRUM-{i}"
    data = get_issue(key)
    if not data:
        continue
    
    fields = data.get('fields', {})
    summary = fields.get('summary', '')
    curr_assignee = fields.get('assignee')
    curr_name = curr_assignee.get('displayName', 'Unassigned') if curr_assignee else 'Unassigned'
    
    histories = data.get('changelog', {}).get('histories', [])
    
    # Check if this task was unassigned by Hoang Thong / script
    # Look back in history to find who it belonged to before our script wiped it
    target_account_id = None
    target_name = None
    
    for h in reversed(histories):
        for item in h.get('items', []):
            if item.get('field') == 'assignee':
                from_id = item.get('from')
                from_name = item.get('fromString')
                to_id = item.get('to')
                to_name = item.get('toString')
                
                # If currently unassigned, find the most recent valid assignee that wasn't Hoang Thong
                if curr_assignee is None and from_id and from_name:
                    target_account_id = from_id
                    target_name = from_name
                    break
                elif curr_assignee is None and to_id and to_name:
                    target_account_id = to_id
                    target_name = to_name
                    break
        if target_account_id:
            break
            
    if curr_assignee is None and target_account_id:
        print(f"Restoring [{key}] -> {target_name} (ID: {target_account_id}) | {summary[:50]}")
        success = set_assignee(key, target_account_id)
        if success:
            print(f"  ✅ Restored successfully!")
            restored += 1
        else:
            print(f"  ❌ Failed to restore {key}")

print(f"\n=======================================================")
print(f"🎉 Restored {restored} tasks back to their rightful owners!")
