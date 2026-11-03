import os
import sys
import sqlite3
import datetime
import re

sys.path.append(r'd:\Capstone\scripts')
from export_chat_history import parse_pb, is_assistant_noise

def is_user_noise(s):
    s_l = s.lower().strip()
    if any(s_l.startswith(p) for p in [
        'mcp(', 'file:///', 'type.googleapis', 'sessionid', '<plan>', 'using ', 'import ', 'namespace ',
        'kế hoạch chi tiết', 'kế hoạch sửa', 'vm967:58', 'vm4:58', 'vm2693:6',
        '- [202', '-\t[202', 'docs_refinement', 'activity_log', '# mục lục',
        'export const', 'export function', 'export type', 'export interface', 'export default',
        'public class', 'private readonly', 'const ', 'let ', 'var ', '"""'
    ]):
        return True
    if '\x0f' in s or '\x00' in s or 'typescriptreact' in s:
        return True
    if re.match(r'^[A-F0-9]{32}$', s.strip(), re.I):
        return True
    return False

def extract_all_turns(db_path):
    conn = sqlite3.connect(db_path)
    c = conn.cursor()
    c.execute("SELECT idx, step_type, step_payload FROM steps ORDER BY idx ASC")
    rows = c.fetchall()
    conn.close()

    user_steps = []
    seen_prompts = set()
    for idx, stype, payload in rows:
        if stype in (14, 23):
            strs = parse_pb(payload)
            for f, s in strs:
                if f in (2, 19):
                    s_strip = s.strip()
                    if len(s_strip) >= 2 and not is_user_noise(s_strip):
                        # Bỏ qua nếu trùng prompt liền kề
                        if not user_steps or user_steps[-1][1] != s_strip:
                            user_steps.append((idx, s_strip))
                        break

    turns = []
    for i in range(len(user_steps)):
        start_idx, u_text = user_steps[i]
        end_idx = user_steps[i+1][0] if i+1 < len(user_steps) else 9999999

        ai_replies = []
        for idx, stype, payload in rows:
            if start_idx < idx < end_idx and stype == 15:
                strs = parse_pb(payload)
                for f, s in strs:
                    if f in (1, 8) and len(s.strip()) > 30 and not is_assistant_noise(s):
                        ai_replies.append(s.strip())

        best_ai = max(ai_replies, key=len) if ai_replies else ""
        turns.append((u_text, best_ai))

    return turns

def main():
    db_path = r'C:\Users\USER\.gemini\antigravity-ide\conversations\4a18bf8f-8c86-49a4-809d-cf0283e86503.db'
    out_dir = r'd:\Capstone\local_docs\chat_sessions'
    os.makedirs(out_dir, exist_ok=True)

    turns = extract_all_turns(db_path)
    print(f"Extracted total {len(turns)} turns from 4a18bf8f")

    # Xuất thành Session 22 (tập trung toàn bộ đợt triển khai US-8.6 Gateway-First)
    s22_file = os.path.join(out_dir, 'session_22_2026-10-20_chuyen_doi_anti_bot_gateway_first_us86_yarp_passthrough_va_ablation_test.md')
    with open(s22_file, 'w', encoding='utf-8') as f:
        f.write("# Session 22: Chuyển Đổi Kiến Trúc Anti-Bot Gateway-First (US-8.6), YARP Passthrough, Redis ZSET Atomic Hold & Ablation Test\n\n")
        f.write("> **Dự án:** TicketShield - AI-Based Bot Detection & Verified Resale Platform\n")
        f.write("> **Thời gian ghi nhận:** `2026-10-19 đến 2026-10-20`\n")
        f.write("> **Mã phiên (Session ID):** `4a18bf8f-8c86-49a4-809d-cf0283e86503`\n")
        f.write(f"> **Tổng số lượt tương tác:** {len(turns)} lượt trao đổi (User <-> AI Assistant)\n\n")
        f.write("[ Quay lại Mục Lục Toàn Bộ Phiên Làm Việc](file:///d:/Capstone/local_docs/chat_sessions/INDEX.md)\n\n")
        f.write("---\n\n")

        for idx, (u, a) in enumerate(turns, 1):
            f.write(f"## [Turn {idx:02d}] Người Dùng (Sinh Viên)\n\n")
            f.write(f"```text\n{u.strip()}\n```\n\n")
            f.write(f"## [Turn {idx:02d}] Trợ Lý Lập Trình (Antigravity Assistant)\n\n")
            if a.strip():
                f.write(f"{a.strip()}\n\n")
            else:
                f.write("*(Trợ lý thực hiện chuỗi lệnh kiểm tra hệ thống / sửa mã nguồn và hoàn tất tác vụ)*\n\n")
            f.write("---\n\n")

    print(f"[SUCCESS] Exported: {s22_file}")

    # Cập nhật INDEX.md
    index_path = os.path.join(out_dir, "INDEX.md")
    if os.path.exists(index_path):
        with open(index_path, 'a', encoding='utf-8') as f:
            f.write(f"| Session 22 | `2026-10-20 14:20` | `4a18bf8f-8c86...` | [session_22_2026-10-20_chuyen_doi_anti_bot_gateway_first_us86_yarp_passthrough_va_ablation_test.md](file:///d:/Capstone/local_docs/chat_sessions/session_22_2026-10-20_chuyen_doi_anti_bot_gateway_first_us86_yarp_passthrough_va_ablation_test.md) | Chuyển Đổi Kiến Trúc Anti-Bot Gateway-First (US-8.6)... |\n")
        print(f"[SUCCESS] Appended Session 22 to {index_path}")

if __name__ == '__main__':
    main()
