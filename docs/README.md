# TICKETSHIELD - TÀI LIỆU HỆ THỐNG (DOCUMENTATION MASTER INDEX)

> 📘 **Hệ thống Quản lý Bán vé Thứ cấp Xác thực & Chống Bot AI (Capstone Project FA26SE261)**  
> **GVHD:** Thân Thị Ngọc Vân  
> **Repository:** [Thong-LH/TicketShield](https://github.com/Thong-LH/TicketShield)

---

## 📑 1. Đặc Tả Nghiệp Vụ & Quy Tắc Hệ Thống (`specs/`)
* **[01_DOMAIN_GLOSSARY.md](file:///d:/Capstone/docs/specs/01_DOMAIN_GLOSSARY.md):** Bảng giải nghĩa thuật ngữ cốt lõi (Vé F0, P2P, Ký quỹ Escrow, Trần giá Price Ceiling, Bot Telemetry).
* **[02_CORE_DIAGRAMS.md](file:///d:/Capstone/docs/specs/02_CORE_DIAGRAMS.md):** 5 Sơ đồ Mental Model tổng quan (Vòng đời vé, Luồng bot AI, Luồng ký quỹ P2P, Xử lý Dispute, Kiến trúc hệ thống).
* **[03_BUSINESS_RULES.md](file:///d:/Capstone/docs/specs/03_BUSINESS_RULES.md):** Toàn bộ Quy tắc Nghiệp vụ Bất biến (Trần giá BR-G01, Khóa vé gRPC BR-G02, Giải ngân kép T+24h & Quét cổng BR-G03, Toàn vẹn ACID BR-G04, Payout Idempotency BR-E06).
* **[04_API_SPECIFICATIONS.md](file:///d:/Capstone/docs/specs/04_API_SPECIFICATIONS.md):** Đặc tả các REST API endpoints chuẩn hóa của TicketShield Core.
* **[06_SECURITY_AND_COMPLIANCE.md](file:///d:/Capstone/docs/specs/06_SECURITY_AND_COMPLIANCE.md):** Quy chuẩn an toàn bảo mật, chống giả mạo token và lưu vết kiểm toán audit.
* **[07_MULTI_TICKET_BUNDLE_SPEC.md](file:///d:/Capstone/docs/specs/07_MULTI_TICKET_BUNDLE_SPEC.md):** Đặc tả nghiệp vụ Đăng bán và Mua vé gói theo Combo (Bundle Law, Atomic Database Transaction, Hold/Release đồng bộ theo BundleId).
* **[08_ANTI_BOT_FEATURE_SPECIFICATION.md](file:///d:/Capstone/docs/specs/08_ANTI_BOT_FEATURE_SPECIFICATION.md):** Hợp đồng kỹ thuật 6 chiều chuẩn v1.0 của Module Anti-Bot (`time_to_checkout`, `action_interval_std_dev`, `burst_rate_10s`, `concurrent_hold`, `is_webdriver`, `device_account_overlap`).
* **[09_B2B_PARTNER_GATEWAY_SPEC.md](file:///d:/Capstone/docs/specs/09_B2B_PARTNER_GATEWAY_SPEC.md):** Đặc tả Cổng kết nối Đối tác B2B động (US-7.5), Vòng đời đối tác 4 bước (`Draft -> Connected -> CatalogSynced -> Active`), gRPC Channel Pool và Catalog Sync Engine.

---

## 🏛️ 2. Thiết Kế Kiến Trúc Phần Mềm (`architecture/`)
* **[09_TARGET_ARCHITECTURE_MIGRATION_PLAN.md](file:///d:/Capstone/docs/architecture/09_TARGET_ARCHITECTURE_MIGRATION_PLAN.md):** Bản thiết kế kiến trúc mục tiêu phân tán 5 Services, cơ chế Event-Driven qua RabbitMQ, triệt tiêu lỗi Shared Database Anti-Pattern khi bảo vệ đồ án tốt nghiệp.
* **[12_MICROSERVICES_DEBATE_AND_CANONICAL_BLUEPRINT.md](file:///d:/Capstone/docs/architecture/12_MICROSERVICES_DEBATE_AND_CANONICAL_BLUEPRINT.md):** Phân tích so sánh kiến trúc Monolith vs Microservices, luận điểm bảo vệ tính khả thi và mở rộng trước Hội đồng.

---

## 🎨 3. Sơ Đồ Trạng Thái & Mermaid Studio (`diagrams/`)
* **[viewer.html](file:///d:/Capstone/docs/diagrams/viewer.html):** Ứng dụng Live Mermaid Viewer & Editor Studio tương tác trực quan 60fps ngoại tuyến.
* **[state.md](file:///d:/Capstone/docs/diagrams/state/state.md):** Đặc tả chi tiết hai máy trạng thái cốt lõi: Escrow Transaction và Resale Listing.
* **[escrow_transaction.mmd](file:///d:/Capstone/docs/diagrams/state/escrow_transaction.mmd):** Mã nguồn Mermaid vòng đời giao dịch Ký quỹ Escrow.
* **[resale_listing.mmd](file:///d:/Capstone/docs/diagrams/state/resale_listing.mmd):** Mã nguồn Mermaid vòng đời niêm yết vé Resale Listing.
* **[system.mmd](file:///d:/Capstone/docs/diagrams/system/system.mmd):** Sơ đồ Topo tương tác các Service trong hệ sinh thái TicketShield.

---

## 🤖 4. Nghiên Cứu & Trụ Cột AI Anti-Bot (`ai/`)
* **[ai_bot_detection.md](file:///d:/Capstone/docs/ai/ai_bot_detection.md):** Tài liệu Masterclass về cơ chế phát hiện bot mua vé hàng loạt, phân tích 3 trụ cột tín hiệu (Timing, Purchase Velocity, Device Environment) và bác bỏ bẫy đo chuột ngây thơ.

---

## 🤝 5. Hợp Đồng Tích Hợp Dịch Vụ (`contracts/`)
* **[GRPC_RESALE_CONTRACT.md](file:///d:/Capstone/docs/contracts/GRPC_RESALE_CONTRACT.md):** Hợp đồng giao tiếp gRPC giữa TicketShield Core và MockOrganizer Ban tổ chức.

---

## 📊 6. Báo Cáo Kỹ Thuật & Nhật Ký Kiểm Toán (`reports/`)
* **[08_MF02_INTEGRATION_AND_REFACTOR_REPORT.md](file:///d:/Capstone/docs/reports/08_MF02_INTEGRATION_AND_REFACTOR_REPORT.md):** Báo cáo tích hợp và refactor Clean Architecture cho MF-02.
* **[11_ARCHITECTURE_AUDIT_AND_DEBT_REPORT.md](file:///d:/Capstone/docs/reports/11_ARCHITECTURE_AUDIT_AND_DEBT_REPORT.md):** Báo cáo kiểm toán kiến trúc và nợ kỹ thuật (Technical Debt).
* **[13_SYSTEM_AUDIT_BUGS_AND_REFACTOR_PLAN.md](file:///d:/Capstone/docs/reports/13_SYSTEM_AUDIT_BUGS_AND_REFACTOR_PLAN.md):** Báo cáo kiểm toán lỗi logic, lỗ hổng tài chính Escrow và kế hoạch refactor.
* **[14_POST_SPRINT_AUDIT_TEAM_RETROSPECTIVE_REPORT.md](file:///d:/Capstone/docs/reports/14_POST_SPRINT_AUDIT_TEAM_RETROSPECTIVE_REPORT.md):** Báo cáo hồi tưởng Sprint và đánh giá hiệu năng làm việc nhóm.
* **[MF02_HANDOVER_REPORT.md](file:///d:/Capstone/docs/reports/MF02_HANDOVER_REPORT.md):** Biên bản bàn giao kỹ thuật giữa Core và Partner MockOrganizer.
* **[MF02_INCIDENTS_AND_FIXES.md](file:///d:/Capstone/docs/reports/MF02_INCIDENTS_AND_FIXES.md):** Nhật ký xử lý sự cố (Global Advisory Lock, Crash test path, Claim JWT).

---

## 🏃 7. Kế Hoạch Sprint & Quản Trị Công Việc (`sprints/`)
* **[07_SPRINT_BACKLOG_MF02.md](file:///d:/Capstone/docs/sprints/07_SPRINT_BACKLOG_MF02.md):** Sprint Backlog chi tiết của MF-02.
* **[10_SPRINT_PLAN_MF03.md](file:///d:/Capstone/docs/sprints/10_SPRINT_PLAN_MF03.md):** Kế hoạch chi tiết tuần tiếp theo MF-03.
* **[SPRINT_05_BACKLOG.md](file:///d:/Capstone/docs/sprints/SPRINT_05_BACKLOG.md):** Kế hoạch chi tiết Sprint 05 (Hoàn thiện toàn diện Anti-Bot & Storefront).
* **[SPRINT_05_NON_AI_BACKLOG.md](file:///d:/Capstone/docs/sprints/SPRINT_05_NON_AI_BACKLOG.md):** Kế hoạch các task nghiệp vụ nền tảng ngoài AI trong Sprint 05.

---

## 🚀 8. Hướng Dẫn Vận Hành & Tích Hợp (`deployment/` & Root)
* **[SEPAY_WEBHOOK_AND_NGROK_SETUP.md](file:///d:/Capstone/docs/SEPAY_WEBHOOK_AND_NGROK_SETUP.md):** Hướng dẫn thiết lập đường truyền Webhook thanh toán VietQR qua Ngrok.
* **[PRE_RELEASE_CLEANUP_CHECKLIST.md](file:///d:/Capstone/docs/deployment/PRE_RELEASE_CLEANUP_CHECKLIST.md):** Danh mục kiểm tra dọn dẹp các endpoint mock trước khi release.
