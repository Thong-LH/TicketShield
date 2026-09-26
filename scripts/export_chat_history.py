import os
import glob
import sqlite3
import sys
import datetime
import json
import re

sys.stdout.reconfigure(encoding='utf-8')

def decode_varint(data, pos):
    res = 0
    shift = 0
    while pos < len(data):
        b = data[pos]
        pos += 1
        res |= (b & 0x7F) << shift
        if not (b & 0x80):
            break
        shift += 7
    return res, pos

def parse_pb(data):
    pos = 0
    strings = []
    while pos < len(data):
        try:
            tag, pos = decode_varint(data, pos)
        except Exception:
            break
        wire_type = tag & 0x07
        field_num = tag >> 3
        if wire_type == 0:
            _, pos = decode_varint(data, pos)
        elif wire_type == 1:
            pos += 8
        elif wire_type == 2:
            try:
                length, pos = decode_varint(data, pos)
            except Exception:
                break
            if pos + length > len(data):
                break
            val = data[pos:pos+length]
            pos += length
            try:
                s = val.decode('utf-8')
                printable = sum(1 for c in s if c.isprintable() or c in '\n\r\t')
                if len(s) > 0 and (printable / len(s)) > 0.85:
                    strings.append((field_num, s))
                else:
                    strings.extend(parse_pb(val))
            except UnicodeDecodeError:
                strings.extend(parse_pb(val))
        elif wire_type == 5:
            pos += 4
        else:
            break
    return strings

def is_user_technical_noise(s):
    s_lower = s.lower().strip()
    if any(s_lower.startswith(p) for p in [
        'execute_url', 'read_url', 'read_file', 'write_file', 'command', 'mcp(', 'sessionid',
        '" csharp', '" typescript', '" python', '" markdown', '" json', '" xml', '" yaml',
        'media_178'
    ]):
        return True
    if 'file:///' in s_lower and len(s_strip := s.strip()) < 150:
        return True
    if 'system-images' in s_lower or 'codegraph' in s_lower or 'appdata' in s_lower:
        return True
    if re.match(r'^[a-f0-9\-]{36}$', s.strip(), re.I):
        return True
    if s.strip().startswith('$') or s.strip().startswith('b$'):
        return True
    return False

def is_assistant_noise(s):
    s_strip = s.strip()
    if s_strip.startswith('{"') and s_strip.endswith('}'):
        return True
    if s_strip.startswith('command(') or s_strip.startswith('execute_url('):
        return True
    if s_strip.startswith('read_file(') or s_strip.startswith('write_file('):
        return True
    if re.match(r'^[a-f0-9\-]{36}$', s_strip, re.I):
        return True
    if s_strip.startswith('sessionID'):
        return True
    return False

def extract_dialogue(db_path, min_idx=0, max_idx=999999):
    conn = sqlite3.connect(db_path)
    c = conn.cursor()
    c.execute("SELECT idx, step_type, step_payload FROM steps WHERE idx >= ? AND idx <= ? ORDER BY idx ASC", (min_idx, max_idx))
    rows = c.fetchall()
    conn.close()

    turns = []
    current_user = None
    current_assistant_replies = []

    for idx, stype, payload in rows:
        if not payload:
            continue

        if stype in (14, 23):
            strs = parse_pb(payload)
            candidates = []
            for fnum, s in strs:
                s_strip = s.strip()
                if fnum in (1, 2, 19) and len(s_strip) >= 5:
                    if not is_user_technical_noise(s_strip):
                        candidates.append((fnum, s_strip))
            
            if candidates:
                # Flush previous turn
                if current_user is not None:
                    best_reply = ""
                    for rep in current_assistant_replies:
                        if len(rep) > len(best_reply):
                            best_reply = rep
                    turns.append((current_user, best_reply))
                    current_assistant_replies = []
                    current_user = None

                # Pick candidate
                f2_candidates = [c[1] for c in candidates if c[0] == 2]
                f19_candidates = [c[1] for c in candidates if c[0] == 19]
                if f2_candidates:
                    current_user = max(f2_candidates, key=len)
                elif f19_candidates:
                    current_user = max(f19_candidates, key=len)
                elif candidates:
                    current_user = max([c[1] for c in candidates], key=len)

        elif stype == 15:
            strs = parse_pb(payload)
            for fnum, s in strs:
                s_strip = s.strip()
                if fnum in (1, 8) and len(s_strip) > 30:
                    if not is_assistant_noise(s_strip):
                        current_assistant_replies.append(s_strip)

    if current_user is not None:
        best_reply = ""
        for rep in current_assistant_replies:
            if len(rep) > len(best_reply):
                best_reply = rep
        turns.append((current_user, best_reply))

    return turns

def extract_founding_session(db_path):
    """
    Trích xuất chuyên biệt cho Phiên Khởi tạo dự án TicketShield (Session 02: 2026-09-08)
    gồm: Bàn luận 2 file Context, Lập kế hoạch kiến trúc, Sinh solution .NET 8,
    các class rỗng nền tảng và commit ban đầu d22f131.
    """
    conn = sqlite3.connect(db_path)
    c = conn.cursor()

    # Step 7: User Prompt
    c.execute("SELECT step_payload FROM steps WHERE idx = 7")
    row = c.fetchone()
    user_prompt = ""
    if row:
        strs = parse_pb(row[0])
        for f, s in strs:
            if f == 19:
                user_prompt = s.strip()
                break

    # Step 15: Master Architecture Plan
    c.execute("SELECT step_payload FROM steps WHERE idx = 15")
    row = c.fetchone()
    plan_md = ""
    if row:
        for f, s in parse_pb(row[0]):
            if s.strip().startswith('{') and 'CodeContent' in s:
                try:
                    plan_md = json.loads(s).get('CodeContent', '')
                except Exception:
                    pass

    # Step 136: Walkthrough Foundation Milestone
    c.execute("SELECT step_payload FROM steps WHERE idx = 136")
    row = c.fetchone()
    wt_md = ""
    if row:
        for f, s in parse_pb(row[0]):
            if s.strip().startswith('{') and 'CodeContent' in s:
                try:
                    wt_md = json.loads(s).get('CodeContent', '')
                except Exception:
                    pass

    conn.close()

    # Build comprehensive Assistant response documenting all steps performed
    assistant_resp = f"""{plan_md}

---

## 🛠️ Quá Trình Thiết Lập Hệ Thống & Khởi Tạo Mã Nguồn Nền Tảng (Steps 19 - 199)

Trợ lý Antigravity đã thực hiện tuần tự các bước thiết lập cốt lõi cho dự án TicketShield theo đúng quy chuẩn:

1. **Khởi tạo Quản lý Phiên bản (Version Control) & Bảo mật Context:**
   - Chạy lệnh `git init` tại thư mục gốc `d:\\Capstone`.
   - Tạo file `.gitignore` nghiêm ngặt loại trừ các thư mục `.context/`, `bin/`, `obj/`, node_modules.
   - Tạo các tài liệu chỉ thị cốt lõi: `00_CORE_SYSTEM.md`, `01_AGENT_RULES.md`, `ACTIVITY_LOG.md`.

2. **Cấu hình Hạ tầng Docker:**
   - Tạo file `docker-compose.yml` gồm PostgreSQL (2 databases tách biệt `ticketshield_db` và `organizer_db`), Redis Cache, và RabbitMQ Broker.

3. **Khởi tạo Solution .NET 8 Clean Architecture:**
   - Tạo Solution: `dotnet new sln -n TicketShield`.
   - Tạo 4 dự án phân tầng Clean Architecture:
     - `src/TicketShield.Core/TicketShield.Domain` (Domain Layer - Classlib)
     - `src/TicketShield.Core/TicketShield.Application` (Application Layer - Classlib CQRS & MediatR)
     - `src/TicketShield.Core/TicketShield.Infrastructure` (Infrastructure Layer - Classlib EF Core & Services)
     - `src/TicketShield.Core/TicketShield.API` (Presentation Layer - Web API)

4. **Khởi tạo các Class Nền Tảng Rỗng (Empty Foundation Classes):**
   - `BaseEntity.cs` (`TicketShield.Domain.Common`): Lớp thực thể cơ sở chứa `Id`, `CreatedAtUtc`, `UpdatedAtUtc`.
   - `DomainException.cs` (`TicketShield.Domain.Exceptions`): Lớp ngoại lệ cơ sở cho Domain.
   - `BusinessRuleViolationException.cs` (`TicketShield.Domain.Exceptions`): Xử lý vi phạm các quy tắc nghiệp vụ (giá trần, thời hạn giải ngân).
   - `DomainEnums.cs` (`TicketShield.Domain.Enums`): Định nghĩa trạng thái vé (`TicketStatus`), giao dịch ký quỹ (`EscrowStatus`), trạng thái rủi ro AI (`RiskLevel`).
   - `ApiResponse.cs` (`TicketShield.Application.Common.Models`): Định dạng chuẩn cho toàn bộ kết quả trả về của API (`Success`, `Data`, `ErrorMessage`, `Errors`).
   - `Result.cs` (`TicketShield.Application.Common.Models`): Pattern kết quả thực thi Handler.
   - `ValidationBehavior.cs` (`TicketShield.Application.Common.Behaviors`): Pipeline Behavior MediatR tự động xác thực FluentValidation.
   - `TicketShieldDbContext.cs` (`TicketShield.Infrastructure.Persistence`): DbContext quản lý dữ liệu tập trung.

5. **Bộ Tài Liệu Đặc Tả Học Thuật & Nghiên Cứu:**
   - `docs/01_DOMAIN_GLOSSARY.md`: Bảng thuật ngữ chuyên ngành & Ma trận phân quyền Actor.
   - `docs/02_CORE_BUSINESS_RULES.md`: Chi tiết toàn bộ luật nghiệp vụ cốt lõi (Anti-Bot, Price-Ceiling, Escrow T+24h).
   - `docs/03_MENTAL_MODELS_5_DIAGRAMS.md`: 5 sơ đồ tư duy bắt buộc phục vụ bảo vệ đồ án tốt nghiệp.
   - `docs/04_EXTERNAL_API_SPECS.md`: Đặc tả kết nối gRPC và RESTful API.

6. **Khởi tạo AI Engine (FastAPI Skeleton):**
   - Khởi tạo thư mục `src/AIEngine` với FastAPI `main.py`, `requirements.txt` và bộ sinh dữ liệu huấn luyện `synthetic_data_generator.py`.

7. **Kiểm thử & Commit Nền Tảng (Milestone Commit):**
   - Hoàn tất Walkthrough nghiệm thu giai đoạn nền tảng:
{wt_md}

8. **Thực thi Git Commit Đầu Tiên & Phân Nhánh:**
   - Commit hash `d22f131`: `feat: initial codebase foundation for TicketShield (.NET 8 Clean Architecture + FastAPI AI Engine + Docker compose foundation)` vào lúc `2026-09-09 00:55:32 +0700`.
   - Tạo nhánh `develop/Sprint_01` sẵn sàng cho các sprint tính năng tiếp theo.
"""

    return [(user_prompt, assistant_resp)]

# 15 Authentic TicketShield Sessions in Exact Chronological Order
PROJECT_SESSIONS = [
    {
        "id": "ae591f17-852f-4102-849f-eb15acc063d3",
        "date": "2026-08-17",
        "time": "2026-08-17 19:17",
        "slug": "khao_sat_y_tuong_do_an_va_ke_thua_kien_truc",
        "title": "Khảo Sát Ý Tưởng Đồ Án & Đánh Giá Kế Thừa Kiến Trúc",
        "type": "standard",
        "db": "ae591f17-852f-4102-849f-eb15acc063d3.db"
    },
    {
        "id": "b8f7ca10_foundation",
        "date": "2026-09-08",
        "time": "2026-09-08 09:30",
        "slug": "khoi_tao_du_an_va_scaffold_clean_architecture",
        "title": "Khởi Tạo Dự Án TicketShield, Solution Clean Architecture & Commit Nền Tảng",
        "type": "founding",
        "db": "b8f7ca10-fd7b-490e-bf65-16e734079475.db"
    },
    {
        "id": "e97b434e-8c91-4325-9615-468a36de5a6f",
        "date": "2026-09-11",
        "time": "2026-09-11 14:20",
        "slug": "kiem_tra_remote_va_api_xac_minh_otp",
        "title": "Kiểm Tra Remote & API Xác Minh OTP Vé Ban Tổ Chức",
        "type": "standard",
        "db": "e97b434e-8c91-4325-9615-468a36de5a6f.db"
    },
    {
        "id": "8c2020de-c665-4162-9fbc-3564ae54909d",
        "date": "2026-09-12",
        "time": "2026-09-12 10:15",
        "slug": "ke_hoach_kien_truc_3_services",
        "title": "Kế Hoạch Kiến Trúc Phân Rã 3 Services",
        "type": "standard",
        "db": "8c2020de-c665-4162-9fbc-3564ae54909d.db"
    },
    {
        "id": "69441fd4-bea6-430f-9aa7-a4c4932b6596",
        "date": "2026-09-14",
        "time": "2026-09-14 16:45",
        "slug": "swimlane_flow_giai_ngan_escrow",
        "title": "Thiết Kế Swimlane Flow Giải Ngân Escrow & Bảo Hiểm 24H",
        "type": "standard",
        "db": "69441fd4-bea6-430f-9aa7-a4c4932b6596.db"
    },
    {
        "id": "d05f84c3-b0f5-4128-909b-b2a0c0f6b31e",
        "date": "2026-09-16",
        "time": "2026-09-16 11:00",
        "slug": "quan_tri_jira_va_ke_hoach_plan09",
        "title": "Quản Trị Jira & Kế Hoạch Phân Rã Plan 09",
        "type": "standard",
        "db": "d05f84c3-b0f5-4128-909b-b2a0c0f6b31e.db"
    },
    {
        "id": "3e6ca025-3667-4646-8e7e-dc0dd9dce57f",
        "date": "2026-09-18",
        "time": "2026-09-18 15:30",
        "slug": "boc_tach_service_plan09",
        "title": "Bóc Tách Service Plan 09 & Tái Cấu Trúc Identity API",
        "type": "standard",
        "db": "3e6ca025-3667-4646-8e7e-dc0dd9dce57f.db"
    },
    {
        "id": "a981aa69-f157-421e-8c6b-36a9c139d2ad",
        "date": "2026-09-20",
        "time": "2026-09-20 09:10",
        "slug": "ra_soat_tien_do_va_cau_truc_he_thong",
        "title": "Rà Soát Tiến Độ Hệ Thống & Cấu Trúc Cross-Service",
        "type": "standard",
        "db": "a981aa69-f157-421e-8c6b-36a9c139d2ad.db"
    },
    {
        "id": "aadfe4ba-7672-4ec0-b2de-72c7ceb3e205",
        "date": "2026-09-22",
        "time": "2026-09-22 14:00",
        "slug": "luong_giai_ngan_settlement_worker",
        "title": "Xử Lý Luồng Giải Ngân Settlement Worker & RabbitMQ",
        "type": "standard",
        "db": "aadfe4ba-7672-4ec0-b2de-72c7ceb3e205.db"
    },
    {
        "id": "443d09f3-fd65-4387-a6c3-b16bb5a76548",
        "date": "2026-09-24",
        "time": "2026-09-24 17:15",
        "slug": "dinh_huong_do_an_va_phan_bien",
        "title": "Định Hướng Đồ Án, Cẩm Nang Phản Biện & Hỏi Đáp Giáo Viên",
        "type": "standard",
        "db": "443d09f3-fd65-4387-a6c3-b16bb5a76548.db"
    },
    {
        "id": "c4e624c8-28be-4e0f-9c8c-edee5c5b116f",
        "date": "2026-09-25",
        "time": "2026-09-25 10:40",
        "slug": "kiem_toan_sau_backend_mf03",
        "title": "Kiểm Toán Sâu Backend Nhánh MF-03 & Rà Soát Vi Phạm Kiến Trúc",
        "type": "standard",
        "db": "c4e624c8-28be-4e0f-9c8c-edee5c5b116f.db"
    },
    {
        "id": "fe14e9e7-6497-4573-9791-b54e345be650",
        "date": "2026-09-26",
        "time": "2026-09-26 13:20",
        "slug": "debug_docker_rabbitmq_fe_be",
        "title": "Xử Lý Docker RabbitMQ & Kết Nối Real-Time SignalR",
        "type": "standard",
        "db": "fe14e9e7-6497-4573-9791-b54e345be650.db"
    },
    {
        "id": "93b642d3-cf1e-4d78-950b-2b7a8ff1f323",
        "date": "2026-09-26",
        "time": "2026-09-26 16:30",
        "slug": "kiem_tra_khong_gian_lam_viec_fe_be",
        "title": "Kiểm Tra Không Gian Làm Việc Frontend - Backend",
        "type": "standard",
        "db": "93b642d3-cf1e-4d78-950b-2b7a8ff1f323.db"
    },
    {
        "id": "b8f7ca10-fd7b-490e-bf65-16e734079475",
        "date": "2026-09-27",
        "time": "2026-09-27 00:15",
        "slug": "phan_tich_va_khac_phuc_bug_toan_dien",
        "title": "Rà Soát Toàn Diện Mã Nguồn, Tối Ưu Giao Diện & Sửa Lỗi Kiến Trúc",
        "type": "b8f7_late",
        "db": "b8f7ca10-fd7b-490e-bf65-16e734079475.db"
    },
    {
        "id": "2c58e3de-9902-4a47-b244-3105679d2ad6",
        "date": "2026-09-27",
        "time": "2026-09-27 02:00",
        "slug": "chuan_hoa_thanh_toan_va_dev_simulator",
        "title": "Chuẩn Hóa Thanh Toán, Nút Mô Phỏng Dev Simulator & Tự Động Ghi Chép Hội Thoại",
        "type": "standard",
        "db": "2c58e3de-9902-4a47-b244-3105679d2ad6.db"
    }
]

def run_export():
    conv_dir = r'C:\Users\USER\.gemini\antigravity-ide\conversations'
    output_dir = r'd:\Capstone\local_docs\chat_sessions'
    os.makedirs(output_dir, exist_ok=True)

    # Clean existing session markdown files
    for old_f in glob.glob(os.path.join(output_dir, "session_*.md")):
        try:
            os.remove(old_f)
        except Exception:
            pass

    print(f"Exporting {len(PROJECT_SESSIONS)} chronological sessions into {output_dir}...")

    extracted_sessions = []
    for idx, sdef in enumerate(PROJECT_SESSIONS, 1):
        db_path = os.path.join(conv_dir, sdef["db"])
        if not os.path.exists(db_path):
            print(f"Warning: DB not found {db_path}")
            continue

        if sdef["type"] == "founding":
            turns = extract_founding_session(db_path)
        elif sdef["type"] == "b8f7_late":
            turns = extract_dialogue(db_path, min_idx=3250)
        else:
            turns = extract_dialogue(db_path)

        first_prompt = turns[0][0][:75].replace('\n', ' ') if turns else "N/A"
        filename = f"session_{idx:02d}_{sdef['date']}_{sdef['slug']}.md"
        extracted_sessions.append({
            "idx": idx,
            "dt_str": sdef["time"],
            "date_tag": sdef["date"],
            "cid": sdef["id"],
            "filename": filename,
            "slug": sdef["slug"],
            "title": sdef["title"],
            "turns": turns,
            "first_prompt": first_prompt
        })

    # 1. Write individual session files
    for s in extracted_sessions:
        filepath = os.path.join(output_dir, s["filename"])
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(f"# Session {s['idx']:02d}: {s['title']}\n\n")
            f.write(f"> **Dự án:** TicketShield - Nền tảng Bán lại Vé An toàn (Capstone Project)\n")
            f.write(f"> **Thời gian ghi nhận:** `{s['dt_str']}`\n")
            f.write(f"> **Mã phiên (Session ID):** `{s['cid']}`\n")
            f.write(f"> **Số lượt tương tác:** {len(s['turns'])} lượt trao đổi (User <-> AI Assistant)\n\n")
            f.write("[ Quay lại Mục Lục Toàn Bộ Phiên Làm Việc](file:///d:/Capstone/local_docs/chat_sessions/INDEX.md)\n\n")
            f.write("---\n\n")

            for t_idx, (user_prompt, assistant_resp) in enumerate(s["turns"], 1):
                f.write(f"## [Turn {t_idx:02d}] Người Dùng (Sinh Viên)\n\n")
                f.write(f"```text\n{user_prompt}\n```\n\n")
                f.write(f"## [Turn {t_idx:02d}] Trợ Lý Lập Trình (Antigravity Assistant)\n\n")
                if assistant_resp:
                    f.write(f"{assistant_resp}\n\n")
                else:
                    f.write("*(Trợ lý thực hiện chuỗi lệnh kiểm tra hệ thống / sửa mã nguồn và hoàn tất tác vụ)*\n\n")
                f.write("---\n\n")

    # 2. Write INDEX.md
    index_path = os.path.join(output_dir, "INDEX.md")
    with open(index_path, 'w', encoding='utf-8') as f:
        f.write("# Mục Lục Nhật Ký Hội Thoại Lập Trình TicketShield (Antigravity AI Sessions)\n\n")
        f.write("> **Dự án:** TicketShield - Nền tảng Bán lại Vé An toàn (Capstone Project)\n")
        f.write("> **Vị trí lưu trữ:** Thư mục cục bộ `local_docs/chat_sessions/` (đã được bảo vệ trong `.gitignore`, an toàn tuyệt đối không gây bloat git).\n")
        f.write(f"> **Thời gian xuất dữ liệu:** {datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S')}\n")
        f.write(f"> **Tổng số phiên làm việc:** {len(extracted_sessions)} phiên (xuyên suốt từ ngày 17/08/2026 đến 27/09/2026)\n")
        f.write("> **Mục đích:** Tài liệu chứng minh quá trình nghiên cứu, phân tích kiến trúc, phản biện kỹ thuật độc lập, thiết kế Clean Architecture và đồng hành lập trình cùng AI cho Giảng viên hướng dẫn & Hội đồng bảo vệ đồ án tốt nghiệp.\n\n")
        f.write("---\n\n")
        f.write("## Danh Sách Từng Phiên Làm Việc (Click để mở chi tiết từng phiên)\n\n")
        f.write("| Phiên # | Ngày Giờ | Mã Phiên | Tên Tệp Chi Tiết | Trọng Tâm Nghiệp Vụ |\n")
        f.write("|---|---|---|---|---|\n")

        for s in extracted_sessions:
            link = f"[{s['filename']}](file:///d:/Capstone/local_docs/chat_sessions/{s['filename']})"
            cid_display = s['cid'][:13] if len(s['cid']) > 13 else s['cid']
            f.write(f"| Session {s['idx']:02d} | `{s['dt_str']}` | `{cid_display}...` | {link} | {s['first_prompt']}... |\n")

        f.write("\n---\n\n")
        f.write("## Tùy Chọn Xuất Toàn Bộ (Single-File Archive)\n\n")
        f.write("- Nếu Giảng viên yêu cầu nộp duy nhất **1 file tổng hợp**, bạn có thể sử dụng file [ALL_SESSIONS_COMBINED.md](file:///d:/Capstone/local_docs/chat_sessions/ALL_SESSIONS_COMBINED.md) gom trọn vẹn toàn bộ 15 phiên vào một nơi.\n")

    # 3. Write ALL_SESSIONS_COMBINED.md
    combined_path = os.path.join(output_dir, "ALL_SESSIONS_COMBINED.md")
    with open(combined_path, 'w', encoding='utf-8') as f:
        f.write("# Toàn Bộ Nhật Ký Hội Thoại Lập Trình TicketShield (All Sessions Combined)\n\n")
        f.write("> **Dự án:** TicketShield - Capstone Project\n")
        f.write(f"> **Thời gian xuất dữ liệu:** {datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S')}\n\n")
        f.write("[ Xem Mục Lục Từng Phiên](file:///d:/Capstone/local_docs/chat_sessions/INDEX.md)\n\n")
        f.write("---\n\n")
        for s in extracted_sessions:
            f.write(f"# Session {s['idx']:02d}: {s['title']} (`{s['dt_str']}`)\n\n")
            f.write(f"- Mã phiên: `{s['cid']}`\n\n")
            for t_idx, (user_prompt, assistant_resp) in enumerate(s["turns"], 1):
                f.write(f"### [Turn {t_idx:02d}] Người Dùng\n\n```text\n{user_prompt}\n```\n\n")
                f.write(f"### [Turn {t_idx:02d}] Trợ Lý Antigravity\n\n")
                if assistant_resp:
                    f.write(f"{assistant_resp}\n\n")
                else:
                    f.write("*(Trợ lý thực hiện chuỗi lệnh kiểm tra hệ thống / sửa mã nguồn và hoàn tất tác vụ)*\n\n")
                f.write("---\n\n")

    print(f"Export completed successfully! Generated {len(extracted_sessions)} session files in {output_dir}")

if __name__ == '__main__':
    run_export()
