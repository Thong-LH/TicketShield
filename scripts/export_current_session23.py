import os
import sys
import sqlite3
import datetime
import re

sys.path.append(r'd:\Capstone\scripts')
from export_current_session22 import extract_all_turns

def main():
    db_path = r'C:\Users\USER\.gemini\antigravity-ide\conversations\1c725d49-00cc-4b4a-8585-becd29c0fc42.db'
    out_dir = r'd:\Capstone\local_docs\chat_sessions'
    os.makedirs(out_dir, exist_ok=True)

    turns = extract_all_turns(db_path)
    print(f"Extracted total {len(turns)} turns from 1c725d49")

    # Xuất thành Session 23
    s23_filename = 'session_23_2026-10-27_giai_thich_escrow_vietqr_state_machine_va_git_sync_mf01.md'
    s23_file = os.path.join(out_dir, s23_filename)
    
    with open(s23_file, 'w', encoding='utf-8') as f:
        f.write("# Session 23: Đối Soát Ngân Hàng VietQR, Phân Biện State Machine Escrow, Ranh Giới Kiến Trúc & Đồng Bộ Remote flow/MF_01\n\n")
        f.write("> **Dự án:** TicketShield - AI-Based Bot Detection & Verified Resale Platform\n")
        f.write("> **Thời gian ghi nhận:** `2026-10-21 đến 2026-10-27`\n")
        f.write("> **Mã phiên (Session ID):** `1c725d49-00cc-4b4a-8585-becd29c0fc42`\n")
        f.write(f"> **Tổng số lượt tương tác:** {len(turns)} lượt trao đổi (User <-> AI Assistant)\n\n")
        f.write("[← Quay lại Mục Lục Toàn Bộ Phiên Làm Việc](file:///d:/Capstone/local_docs/chat_sessions/INDEX.md)\n\n")
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

    print(f"[SUCCESS] Exported: {s23_file}")

    # Cập nhật INDEX.md
    index_path = os.path.join(out_dir, "INDEX.md")
    
    # Đọc danh sách tất cả các session files trong thư mục
    session_files = sorted([f for f in os.listdir(out_dir) if f.startswith('session_') and f.endswith('.md')])
    
    with open(index_path, 'w', encoding='utf-8') as f:
        f.write("# Mục Lục Nhật Ký Hội Thoại Lập Trình TicketShield (Antigravity AI Sessions)\n\n")
        f.write("> **Dự án:** TicketShield - Nền tảng Bán lại Vé An toàn (Capstone Project)\n")
        f.write(f"> **Vị trí lưu trữ:** Thư mục cục bộ `local_docs/chat_sessions/` (đã được bảo vệ trong `.gitignore`, an toàn tuyệt đối không gây bloat git).\n")
        f.write(f"> **Thời gian xuất dữ liệu:** {datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S')}\n")
        f.write(f"> **Tổng số phiên làm việc:** {len(session_files)} phiên (xuyên suốt từ nền tảng Sprint 01 đến Sprint hiện tại)\n")
        f.write("> **Mục đích:** Tài liệu chứng minh quá trình nghiên cứu, phân tích kiến trúc, phản biện kỹ thuật độc lập, thiết kế Clean Architecture và đồng hành lập trình cùng AI cho Giảng viên hướng dẫn & Hội đồng bảo vệ đồ án tốt nghiệp.\n\n")
        f.write("---\n\n")
        f.write("## Danh Sách Từng Phiên Làm Việc (Click để mở chi tiết từng phiên)\n\n")
        f.write("| Phiên # | Ngày | Tên Tệp Chi Tiết |\n")
        f.write("|---|---|---|\n")
        
        for sf in session_files:
            # extract session number and date
            m = re.match(r'session_(\d+)_(\d{4}-\d{2}-\d{2})_(.*)\.md', sf)
            if m:
                s_num, s_date, s_slug = m.groups()
                title_clean = s_slug.replace('_', ' ').title()
                f.write(f"| Session {s_num} | `{s_date}` | [{sf}](file:///d:/Capstone/local_docs/chat_sessions/{sf}) |\n")
            else:
                f.write(f"| Session | - | [{sf}](file:///d:/Capstone/local_docs/chat_sessions/{sf}) |\n")

        f.write("\n---\n\n")
        f.write("## Tùy Chọn Xuất Toàn Bộ (Single-File Archive)\n\n")
        f.write("- File tổng hợp hợp nhất toàn bộ các phiên làm việc: [ALL_SESSIONS_COMBINED.md](file:///d:/Capstone/local_docs/chat_sessions/ALL_SESSIONS_COMBINED.md).\n")

    print(f"[SUCCESS] Updated index: {index_path}")

    # Ghép thêm Session 23 vào ALL_SESSIONS_COMBINED.md
    combined_path = os.path.join(out_dir, "ALL_SESSIONS_COMBINED.md")
    with open(s23_file, 'r', encoding='utf-8') as f_in, open(combined_path, 'a', encoding='utf-8') as f_out:
        f_out.write("\n\n" + "="*80 + "\n\n")
        f_out.write(f_in.read())
    print(f"[SUCCESS] Appended Session 23 to {combined_path}")

if __name__ == '__main__':
    main()
