"""
TicketShield Safe Read-Only Jira Board Viewer
Strictly performs HTTP GET operations only. No mutations (PUT/POST/DELETE) can ever occur.
"""
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

print("=========================================================================")
print("             🛡️ TICKETSHIELD JIRA BOARD - READ-ONLY REPORT              ")
print("=========================================================================\n")

summary_by_user = {}
user_tasks = {}

for i in range(1, 125):
    key = f"SCRUM-{i}"
    url = f"{base_url}/rest/api/3/issue/{key}"
    try:
        res = session.get(url, headers=headers, timeout=10)
        if res.status_code == 200:
            data = res.json()
            fields = data.get('fields', {})
            summary = fields.get('summary', '')
            status = fields.get('status', {}).get('name', 'Unknown')
            assignee = fields.get('assignee')
            name = assignee.get('displayName', 'Unassigned') if assignee else 'Unassigned'
            
            if name not in summary_by_user:
                summary_by_user[name] = 0
                user_tasks[name] = []
            summary_by_user[name] += 1
            user_tasks[name].append((key, status, summary))
    except Exception:
        pass

for name, count in sorted(summary_by_user.items(), key=lambda x: -x[1]):
    print(f"👤 {name} ({count} tasks):")
    print("-" * 73)
    for k, st, sm in user_tasks[name]:
        print(f"  • [{k:<9}] [{st:^11}] {sm[:48]}")
    print()

print("=========================================================================")
print(f"✅ Báo cáo Read-Only hoàn tất: Tổng {sum(summary_by_user.values())} tasks được kiểm tra an toàn 100%.")
print("=========================================================================")
