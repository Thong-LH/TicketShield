import os
import sys
import sqlite3
import datetime
import re

sys.path.append(r'd:\Capstone\scripts')
from export_current_session22 import extract_all_turns

def main():
    db_path = r'C:\Users\USER\.gemini\antigravity-ide\conversations\32752c32-2411-4b62-8e15-d4de402c90de.db'
    out_dir = r'd:\Capstone\local_docs\chat_sessions'
    os.makedirs(out_dir, exist_ok=True)

    turns = extract_all_turns(db_path)
    print(f"Extracted total {len(turns)} turns from 32752c32")

    # Xuất thành Session 24
    s24_filename = 'session_24_2026-10-29_giai_ao_ai_anti_bot_random_forest_gini_va_training_pipeline.md'
    s24_file = os.path.join(out_dir, s24_filename)
    
    with open(s24_file, 'w', encoding='utf-8') as f:
        f.write("# Session 24: Giải Ảo Bản Chất AI Anti-Bot, Cơ Chế Random Forest, Độ Vẩn Đục Gini, Bù Trừ Đa Chiều & Toàn Bộ Training Pipeline\n\n")
        f.write("> **Dự án:** TicketShield - AI-Based Bot Detection & Verified Resale Platform\n")
        f.write("> **Thời gian ghi nhận:** `2026-10-28 đến 2026-10-29`\n")
        f.write("> **Mã phiên (Session ID):** `32752c32-2411-4b62-8e15-d4de402c90de`\n")
        f.write(f"> **Tổng số lượt tương tác:** {len(turns)} lượt trao đổi (User <-> AI Assistant)\n")
        f.write("> **Nội dung trọng tâm:**\n")
        f.write("> 1. Chuẩn bị tài liệu và phạm vi bảo vệ Review 2 (Context, Feature, Diagrams, Payment flow).\n")
        f.write("> 2. Khảo sát tài liệu nghiên cứu AI, ranh giới OWASP OAT (OAT-005, OAT-021, OAT-019/020) và GOMS Model.\n")
        f.write("> 3. Phân biệt bản chất Machine Learning (Random Forest) vs Deep Learning (Mạng nơ-ron) và If-Else tuyến tính.\n")
        f.write("> 4. Cơ chế toán học của Cây quyết định: Độ vẩn đục Gini (Gini Impurity), Bốc mẫu phân tầng (Stratified Sampling), Xử lý bẫy mất cân bằng dữ liệu (Class Imbalance).\n")
        f.write("> 5. Chi tiết luồng thực thi bù trừ đa chiều (Multi-dimensional Trade-off) và toàn bộ 5 bước trong quy trình huấn luyện (Training Pipeline) xuất file `anti_bot_model.onnx`.\n\n")
        f.write("[← Quay lại Mục Lục Toàn Bộ Phiên Làm Việc](file:///d:/Capstone/local_docs/chat_sessions/INDEX.md)\n\n")
        f.write("---\n\n")

        for idx, (u, a) in enumerate(turns, 1):
            f.write(f"## [Turn {idx:02d}] Người Dùng (Sinh Viên)\n\n")
            f.write(f"```text\n{u.strip()}\n```\n\n")
            f.write(f"## [Turn {idx:02d}] Trợ Lý Lập Trình (Antigravity Assistant)\n\n")
            if a.strip():
                f.write(f"{a.strip()}\n\n")
            else:
                f.write("*(Trợ lý thực hiện chuỗi lệnh kiểm tra hệ thống / giải thích cặn kẽ và hoàn tất sao lưu dữ liệu)*\n\n")
            f.write("---\n\n")

    print(f"[SUCCESS] Exported: {s24_file}")

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
            m = re.match(r'session_(\d+)_(\d{4}-\d{2}-\d{2})_(.*)\.md', sf)
            if m:
                s_num, s_date, s_slug = m.groups()
                f.write(f"| Session {s_num} | `{s_date}` | [{sf}](file:///d:/Capstone/local_docs/chat_sessions/{sf}) |\n")
            else:
                f.write(f"| Session | - | [{sf}](file:///d:/Capstone/local_docs/chat_sessions/{sf}) |\n")

        f.write("\n---\n\n")
        f.write("## Tùy Chọn Xuất Toàn Bộ (Single-File Archive)\n\n")
        f.write("- File tổng hợp hợp nhất toàn bộ các phiên làm việc: [ALL_SESSIONS_COMBINED.md](file:///d:/Capstone/local_docs/chat_sessions/ALL_SESSIONS_COMBINED.md).\n")

    print(f"[SUCCESS] Updated index: {index_path}")

    # Ghép thêm Session 24 vào ALL_SESSIONS_COMBINED.md
    combined_path = os.path.join(out_dir, "ALL_SESSIONS_COMBINED.md")
    with open(s24_file, 'r', encoding='utf-8') as f_in, open(combined_path, 'a', encoding='utf-8') as f_out:
        f_out.write("\n\n" + "="*80 + "\n\n")
        f_out.write(f_in.read())
    print(f"[SUCCESS] Appended Session 24 to {combined_path}")

if __name__ == '__main__':
    main()
