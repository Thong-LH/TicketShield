import os
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
    if 'file:///' in s_lower and len(s.strip()) < 150:
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

def main():
    cid = "ad0b7f09-9c5e-440b-a8bc-ace2ccba88d0"
    db_path = os.path.join(r"C:\Users\USER\.gemini\antigravity-ide\conversations", f"{cid}.db")
    output_dir = r"d:\Capstone\local_docs\chat_sessions"
    os.makedirs(output_dir, exist_ok=True)

    print(f"Extracting dialogue from {db_path}...")
    turns = extract_dialogue(db_path)
    print(f"Extracted {len(turns)} turns.")

    session_num = 28
    date_str = "2026-11-23"
    time_str = "2026-11-23 11:18"
    slug = "bo_dem_vi_pham_thong_nhat_redis_cooldown_luy_tien_va_jira_epic14"
    title = "Thiết Kế Bộ Đếm Vi Phạm Thống Nhất Redis, Thang Cooldown Lũy Tiến, Quản Trị Jira Epic-14 & Hợp Nhất flow/MF_01"
    filename = f"session_{session_num:02d}_{date_str}_{slug}.md"
    filepath = os.path.join(output_dir, filename)

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(f"# Session {session_num:02d}: {title}\n\n")
        f.write(f"> **Dự án:** TicketShield - Nền tảng Bán lại Vé An toàn (Capstone Project)\n")
        f.write(f"> **Thời gian ghi nhận:** `{time_str}`\n")
        f.write(f"> **Mã phiên (Session ID):** `{cid}`\n")
        f.write(f"> **Số lượt tương tác:** {len(turns)} lượt trao đổi (User <-> AI Assistant)\n\n")
        f.write("[ Quay lại Mục Lục Toàn Bộ Phiên Làm Việc](file:///d:/Capstone/local_docs/chat_sessions/INDEX.md)\n\n")
        f.write("---\n\n")

        for t_idx, (user_prompt, assistant_resp) in enumerate(turns, 1):
            f.write(f"## [Turn {t_idx:02d}] Người Dùng (Sinh Viên / Chuyên Gia)\n\n")
            f.write(f"```text\n{user_prompt}\n```\n\n")
            f.write(f"## [Turn {t_idx:02d}] Trợ Lý Lập Trình (Antigravity Assistant)\n\n")
            if assistant_resp:
                f.write(f"{assistant_resp}\n\n")
            else:
                f.write("*(Trợ lý thực hiện chuỗi lệnh kiểm tra hệ thống / sửa mã nguồn và hoàn tất tác vụ)*\n\n")
            f.write("---\n\n")

    print(f"Saved session file: {filepath}")

    # Update manifest
    manifest_path = os.path.join(output_dir, "sessions_manifest.json")
    manifest = {}
    if os.path.exists(manifest_path):
        try:
            with open(manifest_path, 'r', encoding='utf-8') as mf:
                manifest = json.load(mf)
        except Exception:
            manifest = {}

    first_prompt = turns[0][0][:100].replace('\n', ' ') if turns else "N/A"
    manifest[cid] = {
        "session_number": session_num,
        "cid": cid,
        "date": date_str,
        "time": time_str,
        "slug": slug,
        "title": title,
        "filename": filename,
        "turns_count": len(turns),
        "first_prompt": first_prompt,
        "last_exported": datetime.datetime.now().isoformat()
    }

    # Also make sure Session 27 is properly in manifest if missing
    cid_27 = "3f0d422b-7063-41f4-b4f4-37c735a4e28f"
    if cid_27 not in manifest:
        manifest[cid_27] = {
            "session_number": 27,
            "cid": cid_27,
            "date": "2026-11-23",
            "time": "2026-11-22 10:54 -> 2026-11-23 11:18",
            "slug": "technical_memo_v4_ablation_4_tang_va_ai_model_v2",
            "title": "Tham Vấn Chuyên Gia Về Technical Memo v4, Thực Nghiệm Ablation 4 Tầng & AI Model v2.0",
            "filename": "session_27_2026-11-23_technical_memo_v4_ablation_4_tang_va_ai_model_v2.md",
            "turns_count": 69,
            "first_prompt": "D:\\THONG\\Downloads\\Ticky tôi đang có cái này để làm animation cho cái chat box...",
            "last_exported": datetime.datetime.now().isoformat()
        }

    with open(manifest_path, 'w', encoding='utf-8') as mf:
        json.dump(manifest, mf, ensure_ascii=False, indent=2)

    print(f"Updated sessions manifest at {manifest_path}")

    # Rebuild INDEX.md
    index_path = os.path.join(output_dir, "INDEX.md")
    sorted_sessions = sorted(manifest.values(), key=lambda s: s.get("session_number", 999))
    with open(index_path, 'w', encoding='utf-8') as f:
        f.write("# Mục Lục Nhật Ký Hội Thoại Lập Trình TicketShield (Antigravity AI Sessions)\n\n")
        f.write("> **Dự án:** TicketShield - Nền tảng Bán lại Vé An toàn (Capstone Project)\n")
        f.write("> **Vị trí lưu trữ:** Thư mục cục bộ `local_docs/chat_sessions/` (đã được bảo vệ trong `.gitignore`, an toàn tuyệt đối không gây bloat git).\n")
        f.write(f"> **Thời gian xuất dữ liệu:** {datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S')}\n")
        f.write(f"> **Tổng số phiên làm việc:** {len(sorted_sessions)} phiên (xuyên suốt từ ngày 03/07/2026 đến 23/11/2026)\n")
        f.write("> **Mục đích:** Tài liệu chứng minh quá trình nghiên cứu, phân tích kiến trúc, phản biện kỹ thuật độc lập, thiết kế Clean Architecture và đồng hành lập trình cùng AI cho Giảng viên hướng dẫn & Hội đồng bảo vệ đồ án tốt nghiệp.\n\n")
        f.write("---\n\n")
        f.write("## Danh Sách Từng Phiên Làm Việc (Click để mở chi tiết từng phiên)\n\n")
        f.write("| Phiên # | Ngày Giờ | Mã Phiên | Tên Tệp Chi Tiết | Trọng Tâm Nghiệp Vụ |\n")
        f.write("|---|---|---|---|---|\n")

        for s in sorted_sessions:
            link = f"[{s['filename']}](file:///d:/Capstone/local_docs/chat_sessions/{s['filename']})"
            cid_display = s['cid'][:13] if len(s['cid']) > 13 else s['cid']
            f.write(f"| Session {s.get('session_number', 0):02d} | `{s.get('time', s.get('date'))}` | `{cid_display}...` | {link} | {s.get('title', '')} |\n")

        f.write("\n---\n\n")
        f.write("## Tùy Chọn Xuất Toàn Bộ (Single-File Archive)\n\n")
        f.write("- Bạn có thể sử dụng file [ALL_SESSIONS_COMBINED.md](file:///d:/Capstone/local_docs/chat_sessions/ALL_SESSIONS_COMBINED.md) để tra cứu toàn bộ các phiên làm việc tổng hợp.\n")

    print(f"Rebuilt INDEX.md with {len(sorted_sessions)} sessions.")

    # Append Session 28 to ALL_SESSIONS_COMBINED.md
    combined_path = os.path.join(output_dir, "ALL_SESSIONS_COMBINED.md")
    with open(combined_path, 'a', encoding='utf-8') as f:
        f.write(f"\n\n# Session {session_num:02d}: {title} (`{time_str}`)\n\n")
        f.write(f"- Mã phiên: `{cid}`\n\n")
        for t_idx, (user_prompt, assistant_resp) in enumerate(turns, 1):
            f.write(f"### [Turn {t_idx:02d}] Người Dùng\n\n```text\n{user_prompt}\n```\n\n")
            f.write(f"### [Turn {t_idx:02d}] Trợ Lý Antigravity\n\n")
            if assistant_resp:
                f.write(f"{assistant_resp}\n\n")
            else:
                f.write("*(Trợ lý thực hiện chuỗi lệnh kiểm tra hệ thống / sửa mã nguồn và hoàn tất tác vụ)*\n\n")
            f.write("---\n\n")

    print("Appended Session 28 to ALL_SESSIONS_COMBINED.md successfully!")

if __name__ == '__main__':
    main()
