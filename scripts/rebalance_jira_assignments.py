import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent.parent))

from scripts.jira_helper import JiraHelper

def main():
    h = JiraHelper()

    # User Account IDs
    THONG_ID = "712020:5af6d284-798d-4bca-8a9b-173809cb77bc"  # Dev 1 (Hoang Thong)
    THINH_ID = "712020:df8cebe7-0f9d-4a73-bd03-6d14b2e9ab64"  # Dev 2 (Nguyen Hung Thinh)
    TUNG_ID  = "712020:df35eb53-ddc3-4413-8b7c-decc2fc84ba3"  # Dev 3 (Nguyễn Thanh Tùng)
    LINH_ID  = "712020:35459af9-f434-4150-9b7b-e64bcb455363"  # Dev 4 (Linh Trần)

    # 1. Dev 2 (Thịnh) tasks
    dev2_tasks = ["SCRUM-12", "SCRUM-13", "SCRUM-15", "SCRUM-19", "SCRUM-34", "SCRUM-92", "SCRUM-93"]
    print("=== Assigning Dev 2 (Thịnh) Tasks ===")
    for k in dev2_tasks:
        h._request(f"/rest/api/3/issue/{k}/assignee", method="PUT", data={"accountId": THINH_ID})
        print(f"  -> [{k}] assigned to Thịnh")

    # 2. Dev 3 (Tùng) tasks
    dev3_tasks = ["SCRUM-10", "SCRUM-11", "SCRUM-17", "SCRUM-18", "SCRUM-22", "SCRUM-23", "SCRUM-55", "SCRUM-83", "SCRUM-89"]
    print("\n=== Assigning Dev 3 (Tùng) Tasks ===")
    for k in dev3_tasks:
        h._request(f"/rest/api/3/issue/{k}/assignee", method="PUT", data={"accountId": TUNG_ID})
        print(f"  -> [{k}] assigned to Tùng")

    # 3. Dev 4 (Linh) tasks
    dev4_tasks = ["SCRUM-24", "SCRUM-29", "SCRUM-30", "SCRUM-31", "SCRUM-57", "SCRUM-84", "SCRUM-96", "SCRUM-97", "SCRUM-98"]
    print("\n=== Assigning Dev 4 (Linh) Tasks ===")
    for k in dev4_tasks:
        h._request(f"/rest/api/3/issue/{k}/assignee", method="PUT", data={"accountId": LINH_ID})
        print(f"  -> [{k}] assigned to Linh")

    # 4. Dev 1 (Thông) - Completed Core & Auth & Microservices & Homepage
    thong_tasks = [
        "SCRUM-14", "SCRUM-20", "SCRUM-21", "SCRUM-25", "SCRUM-26", "SCRUM-27",
        "SCRUM-32", "SCRUM-33", "SCRUM-35", "SCRUM-38",
        "SCRUM-39", "SCRUM-40", "SCRUM-41", "SCRUM-42", "SCRUM-43", "SCRUM-44", "SCRUM-45",
        "SCRUM-46", "SCRUM-47", "SCRUM-48", "SCRUM-49", "SCRUM-50", "SCRUM-51", "SCRUM-52",
        "SCRUM-53", "SCRUM-54", "SCRUM-56", "SCRUM-58",
        "SCRUM-59", "SCRUM-60", "SCRUM-61", "SCRUM-62", "SCRUM-63",
        "SCRUM-64", "SCRUM-65", "SCRUM-66", "SCRUM-67",
        "SCRUM-68", "SCRUM-69", "SCRUM-70", "SCRUM-71",
        "SCRUM-73", "SCRUM-74", "SCRUM-75", "SCRUM-76", "SCRUM-77",
        "SCRUM-99", "SCRUM-100", "SCRUM-101", "SCRUM-102", "SCRUM-103", "SCRUM-104", "SCRUM-105", "SCRUM-106",
        "SCRUM-115", "SCRUM-116"
    ]
    print("\n=== Assigning Dev 1 (Thông) Completed Tasks ===")
    for k in thong_tasks:
        h._request(f"/rest/api/3/issue/{k}/assignee", method="PUT", data={"accountId": THONG_ID})
        print(f"  -> [{k}] assigned to Thông")

    # 5. New Unassigned tasks (waiting for assignment)
    unassigned_tasks = [
        "SCRUM-112", "SCRUM-113", "SCRUM-114",
        "SCRUM-117", "SCRUM-118", "SCRUM-119", "SCRUM-120",
        "SCRUM-78", "SCRUM-79", "SCRUM-80", "SCRUM-81", "SCRUM-82",
        "SCRUM-85", "SCRUM-86", "SCRUM-87", "SCRUM-88",
        "SCRUM-90", "SCRUM-91", "SCRUM-94", "SCRUM-95",
        "SCRUM-107", "SCRUM-108", "SCRUM-109", "SCRUM-110", "SCRUM-111"
    ]
    print("\n=== Ensuring Future Tasks are Unassigned ===")
    for k in unassigned_tasks:
        h._request(f"/rest/api/3/issue/{k}/assignee", method="PUT", data={"accountId": None})
        print(f"  -> [{k}] left Unassigned")

    print("\n🎉 ALL DONE! Jira board assignments perfectly restored across the entire team!")

if __name__ == "__main__":
    main()
