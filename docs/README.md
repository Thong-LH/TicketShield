# TICKETSHIELD AI - TÀI LIỆU DỰ ÁN (DOCUMENTATION INDEX)

Tài liệu dự án TicketShield AI được chuẩn hóa và phân loại theo 6 chuyên mục chức năng dưới đây:

---

## 📑 1. Đặc Tả Nghiệp Vụ & Hệ Thống (`specs/`)
* **[01_DOMAIN_GLOSSARY.md](file:///d:/Capstone/docs/specs/01_DOMAIN_GLOSSARY.md):** Thuật ngữ cốt lõi (F0, P2P, Escrow, Price Ceiling, BOT Telemetry).
* **[02_CORE_DIAGRAMS.md](file:///d:/Capstone/docs/specs/02_CORE_DIAGRAMS.md):** 5 Sơ đồ Mental Model (State Machine vòng đời vé, Luồng bot AI, Luồng ký quỹ P2P, Xử lý Dispute thủ công, Kiến trúc hệ thống).
* **[03_BUSINESS_RULES.md](file:///d:/Capstone/docs/specs/03_BUSINESS_RULES.md):** Toàn bộ quy tắc nghiệp vụ bất biến (Trần giá BR-G01, Khóa vé gRPC BR-G02, Giải ngân T+24h BR-G03, Toàn vẹn ACID BR-G04, Decoupling BR-E05, Payout Idempotency BR-E06).
* **[04_API_SPECIFICATIONS.md](file:///d:/Capstone/docs/specs/04_API_SPECIFICATIONS.md):** Đặc tả các REST API endpoints chuẩn hóa.
* **[05_AI_BOT_DETECTION_RESEARCH.md](file:///d:/Capstone/docs/specs/05_AI_BOT_DETECTION_RESEARCH.md):** Nghiên cứu và thuật toán phân loại hành vi Bot (Telemetry, Features, ML Inference).
* **[06_SECURITY_AND_COMPLIANCE.md](file:///d:/Capstone/docs/specs/06_SECURITY_AND_COMPLIANCE.md):** Quy chuẩn an toàn bảo mật, chống giả mạo token và lưu vết kiểm toán.

---

## 🏛️ 2. Thiết Kế Kiến Trúc Hệ Thống (`architecture/`)
* **[09_TARGET_ARCHITECTURE_MIGRATION_PLAN.md](file:///d:/Capstone/docs/architecture/09_TARGET_ARCHITECTURE_MIGRATION_PLAN.md):** Bản thiết kế kiến trúc mục tiêu phân tán 5 Services, cơ chế Event-Driven qua RabbitMQ (MassTransit), triệt tiêu lỗi Shared Database Anti-Pattern khi bảo vệ đồ án tốt nghiệp.

---

## 🏃 3. Kế Hoạch Sprint & Quản Trị Công Việc (`sprints/`)
* **[07_SPRINT_BACKLOG_MF02.md](file:///d:/Capstone/docs/sprints/07_SPRINT_BACKLOG_MF02.md):** Sprint Backlog chi tiết của MF-02 (Xác thực vé gốc OTP & Đăng bán P2P công khai/riêng tư).
* **[10_SPRINT_PLAN_MF03.md](file:///d:/Capstone/docs/sprints/10_SPRINT_PLAN_MF03.md):** Kế hoạch chi tiết tuần tiếp theo MF-03 (Mua vé, Giữ chỗ 10p, VietQR Webhook, Ký quỹ Escrow LOCKED & tích hợp RabbitMQ).
* **`jira_import_mf02.csv`:** Dữ liệu task import trực tiếp lên Jira Scrum Board.

---

## 📊 4. Báo Cáo Kỹ Thuật & Bàn Giao (`reports/`)
* **[08_MF02_INTEGRATION_AND_REFACTOR_REPORT.md](file:///d:/Capstone/docs/reports/08_MF02_INTEGRATION_AND_REFACTOR_REPORT.md):** Báo cáo kết quả tích hợp và refactor Clean Architecture cho MF-02.
* **[13_SYSTEM_AUDIT_BUGS_AND_REFACTOR_PLAN.md](file:///d:/Capstone/docs/reports/13_SYSTEM_AUDIT_BUGS_AND_REFACTOR_PLAN.md):** Báo cáo kiểm toán toàn diện lỗi logic, lỗ hổng tài chính Escrow, gRPC và kế hoạch refactor Sprint MF-03.
* **[MF02_HANDOVER_REPORT.md](file:///d:/Capstone/docs/reports/MF02_HANDOVER_REPORT.md):** Biên bản bàn giao kỹ thuật giữa Dev 1 (Backend Core) và Dev 2 (Partner MockOrganizer).
* **[MF02_INCIDENTS_AND_FIXES.md](file:///d:/Capstone/docs/reports/MF02_INCIDENTS_AND_FIXES.md):** Nhật ký xử lý sự cố (Global Advisory Lock, Crash test path PostgreSQL, Claim JWT).

---

## 🤝 5. Hợp Đồng Tích Hợp Dịch Vụ (`contracts/`)
* **[GRPC_RESALE_CONTRACT.md](file:///d:/Capstone/docs/contracts/GRPC_RESALE_CONTRACT.md):** Đặc tả hợp đồng giao tiếp gRPC giữa TicketShield Core và MockOrganizer.

---

## 🚀 6. Quy Trình Release & Dọn Dẹp (`deployment/`)
* **[PRE_RELEASE_CLEANUP_CHECKLIST.md](file:///d:/Capstone/docs/deployment/PRE_RELEASE_CLEANUP_CHECKLIST.md):** Checklist các hạng mục mock/debug cần đóng lại trước khi Release lên môi trường Production.
