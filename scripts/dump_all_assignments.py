import json
import requests
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
    "Accept": "application/json"
}

def get_issue(key):
    url = f"{base_url}/rest/api/3/issue/{key}?expand=changelog"
    try:
        res = session.get(url, headers=headers, timeout=10)
        if res.status_code == 200:
            return res.json()
    except Exception:
        pass
    return None

print("Fetching exact assignment history for all tasks...")
for i in range(1, 125):
    key = f"SCRUM-{i}"
    data = get_issue(key)
    if not data:
        continue
    fields = data.get('fields', {})
    summary = fields.get('summary', '')
    curr = fields.get('assignee', {})
    curr_name = curr.get('displayName') if curr else 'Unassigned'
    
    histories = data.get('changelog', {}).get('histories', [])
    assign_events = []
    for h in histories:
        author = h.get('author', {}).get('displayName', 'Unknown')
        created = h.get('created', '')
        for item in h.get('items', []):
            if item.get('field') == 'assignee':
                assign_events.append((created[:19], author, item.get('fromString'), item.get('toString'), item.get('to')))
    
    if assign_events:
        print(f"[{key}] Curr: {curr_name} | {summary}")
        for e in assign_events:
            print(f"    - {e[0]} by {e[1]}: '{e[2]}' -> '{e[3]}' (toId: {e[4]})")
