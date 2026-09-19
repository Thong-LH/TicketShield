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

def get_issue(key):
    url = f"{base_url}/rest/api/3/issue/{key}?expand=changelog"
    for _ in range(3):
        try:
            res = session.get(url, timeout=10)
            if res.status_code == 200:
                return res.json()
            elif res.status_code == 404:
                return None
        except Exception as e:
            time.sleep(1)
    return None

print("Auditing Jira issues SCRUM-1 to SCRUM-125 for unassigned / modified issues...")
unassigned_or_modified = []
for i in range(1, 125):
    key = f"SCRUM-{i}"
    data = get_issue(key)
    if not data:
        continue
    fields = data.get('fields', {})
    summary = fields.get('summary', '')
    assignee = fields.get('assignee')
    assignee_name = assignee.get('displayName', 'Unassigned') if assignee else 'Unassigned'
    histories = data.get('changelog', {}).get('histories', [])
    
    assignee_history = []
    for h in histories:
        author = h.get('author', {}).get('displayName', 'Unknown')
        created = h.get('created', '')
        for item in h.get('items', []):
            if item.get('field') == 'assignee':
                assignee_history.append((created, author, item.get('from'), item.get('fromString'), item.get('to'), item.get('toString')))
    
    # Check if task was unassigned by Hoang Thong / our script recently
    if assignee_history:
        last_change = assignee_history[-1]
        if last_change[5] is None or last_change[5] == 'Unassigned' or assignee_name == 'Unassigned':
            print(f"\n[UNASSIGNED TASK FOUND] {key}: {summary}")
            print(f"   Current Assignee: {assignee_name}")
            print(f"   Original Assignee: {last_change[3]} (Id: {last_change[2]})")
            for h in assignee_history:
                print(f"     -> at {h[0][:19]} by {h[1]}: '{h[3]}' -> '{h[5]}'")
            unassigned_or_modified.append({
                'key': key,
                'summary': summary,
                'original_name': last_change[3],
                'original_id': last_change[2]
            })

print(f"\n=======================================================")
print(f"TOTAL UNASSIGNED TASKS FOUND WITH PRIOR ASSIGNEE: {len(unassigned_or_modified)}")
for item in unassigned_or_modified:
    print(f" - {item['key']}: Originally had '{item['original_name']}' ({item['original_id']}) | {item['summary'][:50]}")
