# TicketShield Diagrams & Architecture Documentation

Thư mục này chứa toàn bộ biểu đồ kiến trúc hệ thống và máy trạng thái phục vụ đồ án tốt nghiệp Capstone FA26SE261.

## 1. Cấu trúc thư mục

```
docs/diagrams/
  ├── system/                    # Sơ đồ Kiến trúc Hệ thống (5 Tầng Microservices)
  │   ├── system.mmd             # File Mermaid kiến trúc hệ thống độc lập
  │   ├── system.md              # Tài liệu thuyết minh & Q&A bảo vệ đồ án
  │   └── system.svg             # File vector SVG độ nét cao
  │
  ├── state/                     # Biểu đồ Trạng thái Toàn diện (Ticket, Listing, Escrow)
  │   ├── escrow_transaction.mmd # File Mermaid máy trạng thái EscrowTransaction (Trọng tâm)
  │   ├── resale_listing.mmd     # File Mermaid máy trạng thái ResaleListing (Bổ trợ)
  │   ├── state.md               # Tài liệu thuyết minh & Ma trận CAS state machine
  │   └── state.svg              # File vector SVG độ nét cao
  │
  ├── viewer.html                # Architecture Studio (Docs + Canvas + Live Mermaid Editor)
  ├── marked.min.js              # Thư viện render Markdown offline (0ms latency, không lỗi CORS)
  └── mermaid.min.js             # Thư viện render Mermaid offline phục vụ Live Editor
```

## 2. Công cụ Trực quan hóa & Live Editor

- **Studio tương tác:** Mở [viewer.html](file:///d:/Capstone/docs/diagrams/viewer.html) trực tiếp trên trình duyệt hoặc qua Live Server/Simple Browser trong IDE để:
  1. Đọc thuyết minh và tài liệu đối chiếu song song với sơ đồ vector SVG.
  2. Bấm nút **Live Mermaid Editor** trên thanh công cụ để chỉnh sửa trực tiếp mã Mermaid, xem trước theo thời gian thực (Live SVG Preview), áp dụng thẳng lên Canvas chính, hoặc xuất ra file `.svg` / `.mmd`.
- **Biên dịch cập nhật tự động:** Khi chỉnh sửa các file `.mmd` hoặc `.md`, chạy lệnh sau để tự động render lại toàn bộ SVG và đồng bộ vào Studio:
  ```bash
  python local_docs/scripts/diagrams/build_diagrams.py
  ```
