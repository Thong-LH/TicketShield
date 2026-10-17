# TICKETSHIELD - SPRINT 05 BACKLOG (FULL 1-WEEK SPRINT)

> **Quy chuẩn quản trị Sprint 05 (Chu kỳ 1 tuần):**
> - **Thời lượng:** 1 Tuần (Sprint 05).
> - **2 Epics song hành:** Epic Non-AI (Nền tảng BTC & Khiếu nại) và Epic AI (Chống bot sơ cấp tại cổng BTC).
> - **100% Unassigned:** Không gán cứng tên thành viên (tuân thủ kỷ luật Jira).
> - **Double Linked List:** Mọi task đều có quan hệ hai chiều `[Phụ thuộc: ... | Mở khóa cho: ...]`.
> - **Chuẩn kiến trúc AI:** Tuân thủ Cẩm nang [09_AI_ANTI_BOT_EXPERT_CONSULTATION_AND_ARCHITECTURE_SYNTHESIS.md](file:///d:/Capstone/local_docs/ai_research/09_AI_ANTI_BOT_EXPERT_CONSULTATION_AND_ARCHITECTURE_SYNTHESIS.md) (Vector 6 chiều, In-Process ONNX Runtime trong C#, Redis Sliding Window, Loại bỏ bẫy chuột).

---

## 🏛️ EPIC-7_SPRINT-05: PRIMARY TICKETING STOREFRONT, ORGANIZER PORTAL & DISPUTE TRACKING (NON-AI)

### 1. US-5.1: Quản lý Kho vé Sơ cấp & API Đặt vé Ban Tổ Chức (Primary Ticketing Engine)
- **BE-MOCK-5.1.1: Thiết kế Model Hạng vé & API Danh mục vé sự kiện**
  - **Nội dung:** Bổ sung thực thể `TicketTier` (EventId, TierName, Price, TotalQuantity, AvailableQuantity). Viết endpoint `GET /api/organizer/events/{id}/tiers`.
  - **Liên kết:** `[Phụ thuộc: Bắt đầu ngay | Mở khóa cho: BE-MOCK-5.1.2, FE-5.2.1]`
- **BE-MOCK-5.1.2: Concurrency Lock Trừ Kho Vé & Tạo Đơn hàng Mua vé Gốc**
  - **Nội dung:** Xử lý lock trừ tồn kho nguyên tử (`AvailableQuantity >= requested`) và tạo đơn hàng sơ cấp `POST /api/organizer/orders/checkout`.
  - **Liên kết:** `[Phụ thuộc: sau BE-MOCK-5.1.1 | Mở khóa cho: FE-5.2.2, TEST-5.1.3]`
- **TEST-5.1.3: Kiểm thử Tải Đồng thời Trừ Kho Vé (Overselling Race Condition Test)**
  - **Nội dung:** Viết test giả lập 50 request cùng mua 5 vé cuối cùng để kiểm chứng không bị âm kho vé.
  - **Liên kết:** `[Phụ thuộc: sau BE-MOCK-5.1.2 | Mở khóa cho: Hoàn tất US-5.1]`

### 2. US-5.2: Giao diện Cổng Đặt vé Sơ cấp Ban Tổ Chức (OG Public Storefront)
- **FE-5.2.1: Trang Danh mục Sự kiện & Chọn Hạng Vé (`OGTicketStorePage.tsx`)**
  - **Nội dung:** Giao diện hiển thị poster sự kiện, danh sách các hạng vé (VIP, Standard), trạng thái còn vé/hết vé và bộ đếm chọn số lượng.
  - **Liên kết:** `[Phụ thuộc: sau BE-MOCK-5.1.1 | Mở khóa cho: FE-5.2.2]`
- **FE-5.2.2: Modal Checkout Thông tin Người Mua & Kết quả Nhận Vé**
  - **Nội dung:** Form nhập Họ tên, Email, SĐT nhận vé. Gọi API checkout, hiển thị màn hình chúc mừng kèm mã QR vé gốc và nút tắt sang sàn TicketShield.
  - **Liên kết:** `[Phụ thuộc: sau FE-5.2.1, sau BE-MOCK-5.1.2 | Mở khóa cho: TEST-5.2.3, FE-5.5.3]`
- **TEST-5.2.3: Kiểm thử Toàn trình Luồng Mua vé Sơ cấp (E2E Storefront Flow)**
  - **Nội dung:** Kiểm tra luồng từ chọn vé -> nhập form -> nhận vé -> hiển thị đầy đủ trong `MockTicketsController`.
  - **Liên kết:** `[Phụ thuộc: sau FE-5.2.2 | Mở khóa cho: Hoàn tất US-5.2]`

### 3. US-5.3: Khép kín Trải nghiệm Theo dõi Khiếu nại Phía Người dùng (Dispute Tracking UX)
- **BE-CORE-5.3.1: Endpoint Chi tiết Tiến độ Tranh chấp Người dùng (`GET /api/v1/disputes/my-disputes/{id}`)**
  - **Nội dung:** Trả về timeline trạng thái khiếu nại (Đã nộp -> Chờ đối soát log cổng -> Đã phân xử), lý do giải quyết và trạng thái hoàn tiền.
  - **Liên kết:** `[Phụ thuộc: Bắt đầu ngay | Mở khóa cho: FE-5.3.2]`
- **FE-5.3.2: Drawer/Modal Tiến độ Tranh chấp trên MyTicketsPage & MyListingsPage**
  - **Nội dung:** Thêm action "Xem tiến độ khiếu nại" khi vé có badge `DISPUTED`. Hiển thị chi tiết dòng thời gian xử lý và phán quyết từ hệ thống.
  - **Liên kết:** `[Phụ thuộc: sau BE-CORE-5.3.1 | Mở khóa cho: TEST-5.3.3]`
- **TEST-5.3.3: Kiểm thử Trạng thái Đồng bộ Khiếu nại giữa 2 đầu Buyer - Seller**
  - **Nội dung:** Đảm bảo khi Admin/Staff bấm duyệt hoàn tiền, cả phía Buyer và Seller đều cập nhật trạng thái tương ứng tức thì.
  - **Liên kết:** `[Phụ thuộc: sau FE-5.3.2 | Mở khóa cho: Hoàn tất US-5.3]`
- **EXT-7.3.4: Tích hợp Dịch vụ Lưu Trữ Ảnh Khiếu Nại lên Cloudinary CDN**
  - **Nội dung:** Tích hợp Cloudinary DotNet SDK vào TicketShield Core, xây dựng CloudinaryEvidenceFileStore thay thế lưu file cục bộ. Upload ảnh bằng chứng khiếu nại vé lỗi của Buyer lên Cloudinary CDN và lưu secure URL vào bảng `dispute_evidences`.
  - **Liên kết:** `[Phụ thuộc: sau BE-CORE-5.3.1 | Mở khóa cho: FE-5.3.2, TEST-5.3.3]`


### 4. US-5.4: Báo cáo Chuyển nhượng An toàn cho Ban Tổ Chức (OG Resale Metrics)
- **BE-MOCK-5.4.1: API Thống kê Đối soát Vé Chuyển nhượng (`GET /api/organizer/resale-metrics`)**
  - **Nội dung:** Tổng hợp chỉ số: Số vé đã đổi chủ qua gRPC, số vé đang trong phiên giao dịch, số vé đang mở tranh chấp.
  - **Liên kết:** `[Phụ thuộc: Bắt đầu ngay | Mở khóa cho: FE-5.4.2]`
- **FE-5.4.2: Tab Báo cáo Đối tác trên Cổng Quản trị BTC (`OrganizerPortalPage.tsx`)**
  - **Nội dung:** Hiển thị biểu đồ/bảng số liệu đối soát chuyển nhượng vé giữa BTC và TicketShield Core.
  - **Liên kết:** `[Phụ thuộc: sau BE-MOCK-5.4.1 | Mở khóa cho: Hoàn tất US-5.4]`

---

## 🛡️ EPIC-8_SPRINT-05: AI ANTI-BOT PERIMETER DEFENSE, TELEMETRY PIPELINE & IN-PROCESS INFERENCE (AI ENGINE)

> **Ràng buộc phụ thuộc liên Epic (Cross-Epic Dependencies):**
> Epic 8 (Phân hệ AI) là chốt chặn an ninh cắm trực tiếp vào Cổng bán vé sơ cấp BTC của Epic 7 (`[SCRUM-223]`). Một số task trong Epic 8 bắt buộc phải đợi các mốc bàn giao của Epic 7 hoàn thành mới có thể nhúng và tích hợp toàn trình.

### 5. US-8.1: Thư Viện Client Đo Đạc Tín Hiệu & Ký Số HMAC (Client Telemetry SDK)
**Mô tả:** Thu thập 2 đặc trưng client chuẩn xác (`action_interval_std_dev`, `is_webdriver`), loại bỏ hoàn toàn bẫy chuột, đóng gói payload ký số HMAC chống giả mạo.

- **AI-SDK-8.1.1: Xây dựng Thư viện Client Telemetry (`anti-bot-sdk.ts`)**
  - **Nội dung:** Thu thập độ lệch chuẩn thời gian tương tác `action_interval_std_dev` (tối thiểu 3 thao tác click/tap), cờ `is_webdriver` (`navigator.webdriver`), và hash `device_fingerprint`.
  - **Liên kết:** `[Phụ thuộc: Bắt đầu ngay (độc lập thư viện) | Mở khóa cho: AI-SDK-8.1.2, FE-8.1.3]`

- **AI-SDK-8.1.2: Cơ chế Ký số HMAC & Replay Prevention (Nonce Guard)**
  - **Nội dung:** Ký payload bằng SHA256 HMAC kèm timestamp và nonce dùng một lần để chống bot bắt gói tin gửi lại.
  - **Liên kết:** `[Phụ thuộc: sau AI-SDK-8.1.1 | Mở khóa cho: BE-PIPE-8.2.3]`

- **FE-8.1.3: Nhúng SDK vào Modal Checkout Cổng Đặt Vé BTC**
  - **Nội dung:** Tích hợp SDK vào Modal Checkout sơ cấp, tự động đính kèm telemetry payload vào body/header khi bấm đặt vé.
  - **Liên kết:** `[Phụ thuộc: sau AI-SDK-8.1.1, sau SCRUM-230 (FE-5.2.2 Modal Checkout của Epic-7) | Mở khóa cho: TEST-8.1.4, LEAD-8.5.1]`

- **TEST-8.1.4: Kiểm thử Phát hiện Cờ Tự động hóa Puppeteer/Selenium**
  - **Nội dung:** Viết script kiểm thử bằng headless browser để đảm bảo SDK phát hiện chính xác `is_webdriver == 1`.
  - **Liên kết:** `[Phụ thuộc: sau FE-8.1.3 | Mở khóa cho: Hoàn tất US-8.1]`

---

### 6. US-8.2: Bộ Thu Thập Phía Server & Tra Cứu Redis Tốc Độ Cao (Server Telemetry Pipeline)
**Mô tả:** Thu thập 4 đặc trưng server-observed (`time_to_checkout`, `burst_rate_10s`, `concurrent_hold`, `device_account_overlap`) bằng Redis Sliding Window với thời gian phản hồi dưới 2ms.

- **BE-PIPE-8.2.1: Đo Đạc Thời Gian Giao Dịch & Khóa Giữ Chỗ Server-Side**
  - **Nội dung:** Đo `time_to_checkout` bằng server timestamp và tra cứu số vé đang giữ đồng thời `concurrent_hold` trong DB.
  - **Liên kết:** `[Phụ thuộc: sau SCRUM-226 (BE-MOCK-5.1.2 API Checkout & Lock Kho của Epic-7) | Mở khóa cho: BE-PIPE-8.2.2]`

- **BE-PIPE-8.2.2: Bộ Đếm Tần Suất & Trùng Lặp Thiết Bị qua Redis Sliding Window**
  - **Nội dung:** Sử dụng Redis Sorted Set để tính `burst_rate_10s` = Max(IP, Account) và đếm `device_account_overlap` trong cửa sổ 24 giờ dưới 2ms.
  - **Liên kết:** `[Phụ thuộc: sau BE-PIPE-8.2.1 | Mở khóa cho: BE-PIPE-8.2.3]`

- **BE-PIPE-8.2.3: Tổng Hợp Vector 6 Chiều & Cơ Chế Fallback Fail-Open (20ms)**
  - **Nội dung:** Đóng gói mảng `double[6]`, thiết lập timeout 20ms: nếu Redis/Inference lỗi thì tự động Fail-Open sang luật tối thiểu để không gián đoạn khách hàng.
  - **Liên kết:** `[Phụ thuộc: sau BE-PIPE-8.2.2, sau AI-SDK-8.1.2, sau AI-CORE-8.3.2 | Mở khóa cho: TEST-8.2.4, LEAD-8.5.1]`

- **TEST-8.2.4: Benchmark Pipeline Tra Cứu Redis Dưới Tải 500 req/s**
  - **Nội dung:** Kiểm tra thời gian thu thập 4 đặc trưng server đảm bảo p99 < 2ms.
  - **Liên kết:** `[Phụ thuộc: sau BE-PIPE-8.2.3 | Mở khóa cho: Hoàn tất US-8.2]`

---

### 7. US-8.3: Huấn Luyện Offline & Nhúng In-Process ONNX Runtime (AI In-Process Engine)
**Mô tả:** Huấn luyện mô hình Random Forest 100 cây trên tập dữ liệu kiểm soát, xuất file `model.onnx`, nhúng trực tiếp vào C# Backend qua `Microsoft.ML.OnnxRuntime` chạy CPU dưới 1ms.

- **AI-CORE-8.3.1: Huấn Luyện Mô Hình Random Forest & Xuất ONNX (Python Pipeline)**
  - **Nội dung:** Huấn luyện mô hình 100 cây trên tập synthetic data có vùng xám (Grey Zone), đánh giá bằng Leave-One-Cohort-Out, xuất file `anti_bot_model.onnx`.
  - **Liên kết:** `[Phụ thuộc: Bắt đầu ngay (Python offline train) | Mở khóa cho: AI-CORE-8.3.2]`

- **AI-CORE-8.3.2: Tích Hợp In-Process ONNX Runtime trong C# Backend**
  - **Nội dung:** Nhúng thư viện `Microsoft.ML.OnnxRuntime` vào `MockOrganizer.API`, nạp model trực tiếp vào RAM, chấm điểm vector `double[6]` thành `RiskScore` (0.0 -> 1.0) trong 1ms.
  - **Liên kết:** `[Phụ thuộc: sau AI-CORE-8.3.1 | Mở khóa cho: BE-PIPE-8.2.3, FE-8.4.1, LEAD-8.5.1]`

- **TEST-8.3.3: Benchmark Độ Trễ Suy Luận In-Process C# (SLA p99 < 2ms trên CPU)**
  - **Nội dung:** Kiểm thử 10.000 lượt suy luận liên tục để chứng minh không có độ trễ mạng HTTP microservice.
  - **Liên kết:** `[Phụ thuộc: sau AI-CORE-8.3.2 | Mở khóa cho: Hoàn tất US-8.3]`

---

### 8. US-8.4: Ma Trận Quyết Định 3 Tầng & Bảng Điều Khiển An Ninh BTC (Decision Matrix & Dashboard)
**Mô tả:** Thực thi chính sách 3 tầng (Allow / Challenge / Block) và bảng điều khiển cho BTC quan sát lưu lượng.

- **FE-8.4.1: Màn Hình Giám Sát Tỷ Lệ Bot Theo Thời Gian Thực trên Cổng BTC**
  - **Nội dung:** Hiển thị biểu đồ phân bổ RiskScore, số lượt Allow, số lượt Challenge và số lượt Block trên Cổng quản trị BTC.
  - **Liên kết:** `[Phụ thuộc: sau AI-CORE-8.3.2, sau SCRUM-238 (FE-5.4.2 Tab Báo Cáo Cổng BTC của Epic-7) | Mở khóa cho: FE-8.4.2]`

- **FE-8.4.2: Bảng Cấu Hình Ngưỡng Quyết Định Động (Dynamic Policy Thresholds)**
  - **Nội dung:** Cho phép BTC điều chỉnh ngưỡng Allow (ví dụ < 0.35) và Block (>= 0.75) linh hoạt theo độ nóng của từng sự kiện.
  - **Liên kết:** `[Phụ thuộc: sau FE-8.4.1 | Mở khóa cho: TEST-8.4.3]`

- **TEST-8.4.3: Kiểm Thử Toàn Trình E2E Phân Luồng Giao Dịch 3 Tầng**
  - **Nội dung:** Giả lập 3 nhóm người mua (Người thường -> Allow; Người mua nhanh -> Challenge; Bot spam -> Block) để nghiệm thu khép kín hệ thống.
  - **Liên kết:** `[Phụ thuộc: sau FE-8.4.2, sau BE-PIPE-8.2.3, sau SCRUM-231 (TEST-5.2.3 E2E Storefront của Epic-7) | Mở khóa cho: Hoàn tất US-8.4]`

---

### 9. US-8.5: Quản Trị Tích Hợp Toàn Trình, Kiểm Soát Hợp Đồng & Hồ Sơ Học Thuật (Lead Integration & Academic Dossier)
**Mô tả:** Đảm bảo tính toàn vẹn E2E của toàn bộ pipeline chống bot (FE -> BE -> ONNX -> Decision), rà soát hợp đồng kỹ thuật và hoàn thiện hồ sơ nghiên cứu lý thuyết phục vụ GVHD Thân Thị Ngọc Vân.

- **LEAD-8.5.1: Kiểm Soát Hợp Đồng Dữ Liệu & Tích Hợp Toàn Trình E2E Pipeline**
  - **Nội dung:** Review PR, kiểm tra tính đồng bộ 100%: Payload FE của Linh gửi lên -> Backend Tùng gom đủ mảng double[6] -> đưa vào hàm ONNX của Thịnh -> trả về quyết định Allow/Challenge/Block chuẩn xác.
  - **Liên kết:** `[Phụ thuộc: sau FE-8.1.3, sau BE-PIPE-8.2.3, sau AI-CORE-8.3.2, sau SCRUM-228 (US-7.2 Storefront của Epic-7) | Mở khóa cho: LEAD-8.5.4]`

- **LEAD-8.5.2: Khảo Cứu 3 Bài Báo Học Thuật Quốc Tế (IEEE/ACM/arXiv) & Trích Dẫn APA**
  - **Nội dung:** Tìm 3 bài báo khoa học bình duyệt chứng minh cho 3 trụ cột (Timing độ trễ nhận thức > 1.5s, Velocity cửa sổ trượt, Fingerprint phát hiện tự động hóa) theo Mục 8 file 03.
  - **Liên kết:** `[Phụ thuộc: Bắt đầu ngay (Khảo cứu paper độc lập) | Mở khóa cho: LEAD-8.5.4]`

- **LEAD-8.5.3: Thu Thập Bằng Chứng Thực Tế Việt Nam (Báo Chí & Quy Chế Trần Vé Ticketbox)**
  - **Nội dung:** Thu thập 3 bằng chứng thực tế tại VN: Báo VietNamNet/VnExpress phản ánh vé concert cháy hàng, bài báo cảnh báo lừa đảo QR thứ cấp, và chụp màn hình điều khoản Ticketbox quy định trần tối đa 4 vé/người (BR-P01).
  - **Liên kết:** `[Phụ thuộc: Bắt đầu ngay (Thu thập báo chí & Ticketbox) | Mở khóa cho: LEAD-8.5.4]`

- **LEAD-8.5.4: Biên Soạn Dự Thảo Chương 2 Luận Văn (Cơ Sở Lý Thuyết & Nghiên Cứu Liên Quan)**
  - **Nội dung:** Tổng hợp 3 bài báo quốc tế, 3 bằng chứng Việt Nam, bảng ma trận Scientific Proof Matrix và đối chiếu OWASP OAT-005/021/019 thành dự thảo Chương 2 nộp GVHD Thân Thị Ngọc Vân.
  - **Liên kết:** `[Phụ thuộc: sau LEAD-8.5.1, sau LEAD-8.5.2, sau LEAD-8.5.3 | Mở khóa cho: Hoàn tất Epic-8]`
