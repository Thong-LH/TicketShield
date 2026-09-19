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

headers = {"Accept": "application/json"}

summary_by_user = {}
user_tasks = {}

for i in range(1, 125):
    key = f"SCRUM-{i}"
    url = f"{base_url}/rest/api/3/issue/{key}"
    try:
        res = session.get(url, headers=headers, timeout=10)
        if res.status_code == 200:
            data = res.json()
            assignee = data.get('fields', {}).get('assignee')
            name = assignee.get('displayName', 'Unassigned') if assignee else 'Unassigned'
            summary_by_user[name] = summary_by_user.get(name, 0) + 1
            if name not in user_tasks:
                user_tasks[name] = []
            user_tasks[name].append(key)
    except Exception:
        pass

print("=== 📊 TỔNG KẾT PHÂN CÔNG HIỆN TẠI TRÊN JIRA SAU KHI KHÔI PHỤC ===")
for name, count in sorted(summary_by_user.items(), key=lambda x: -x[1]):
    print(f"\n👤 {name}: {count} tasks")
    print(f"   Tasks: {', '.join(user_tasks[name])}")
