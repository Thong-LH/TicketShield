# TICKETSHIELD - SPRINT BACKLOG: MF-02 (SELLER SCOPE)
## Epic: Epic-2_MF-02: Ticket Verification & P2P Resale Listing

> **Mục tiêu Sprint (Sprint Goal):**  
> Xây dựng hoàn chỉnh luồng nghiệp vụ MF-02 cho phép Người bán (Seller) nhập mã vé gốc, nhận OTP xác thực chính chủ từ Ban tổ chức (MockOrganizer) qua Email, xác thực và niêm yết vé lên sàn P2P với mức giá tuân thủ nghiêm ngặt Quy tắc trần giá (`resale_price <= original_price`). Cung cấp 2 chế độ đăng bán linh hoạt: **Công khai (Public)** trên sàn hoặc **Bán riêng tư (Private Sale)** qua Link mã hóa bí mật chia sẻ cho bạn bè.

---

## 👥 BẢNG PHÂN VAI TRÒ & TRÁCH NHIỆM (RACI MATRIX)

| Thành viên | Vai trò | Trách nhiệm chính trong Sprint MF-02 |
| :--- | :--- | :--- |
| **Dev 1** | **Backend Core (.NET 8 Clean Architecture)** | Entity `ResaleListing`, MediatR CQRS Commands/Queries, FluentValidation (Trần giá), Chế độ Private token, EF Core Fluent API, Controller API. |
| **Dev 2** | **Backend Partner (MockOrganizer .NET 8)** | Entity `MockTicket`, `MockOtp`, Sinh OTP 6 số, Gửi Email SMTP, API xác thực và khóa vé gốc `LOCKED_FOR_RESALE` phía BTC. |
| **Dev 3** | **Frontend (Seller Creation Flow)** | UI Form đăng bán (Nhập mã vé, Popup đếm ngược OTP, Slider cấu hình giá bán & cảnh báo trần giá, Selector Công khai vs Riêng tư, Modal Link/QR chia sẻ). |
| **Dev 4** | **Frontend (Seller Management & Private Preview)** | UI Quản lý vé của Seller (Bảng danh sách vé, Badge trạng thái, Hủy đăng bán) & Trang xem trước vé theo Link riêng tư (Private Preview Page). |
| **Dev 5** | **QA / Tester / Test Engineer** | Bộ dữ liệu vé mẫu Concert, Viết Unit Test logic trần giá & hết hạn OTP, Test bảo mật chặn truy cập vé Private nếu thiếu token. |

---

## 📋 CHI TIẾT USER STORIES (US), ACCEPTANCE CRITERIA (AC) & TASKS

---

### 🟢 USER STORY 01 (US-2.1): Gửi OTP Xác Thực Sở Hữu Vé Gốc từ Ban Tổ Chức
> **Summary:** `US-2.1_Seller requests ticket ownership verification OTP from Organizer`  
> **Mô tả:** Người bán nhập mã vé gốc và email đã mua vé F0 để Ban tổ chức gửi mã OTP 6 số qua email xác nhận quyền sở hữu hợp pháp.

#### 🎯 Acceptance Criteria (Gherkin):
* **AC-2.1.1 (Vé không tồn tại hoặc đã sử dụng):**  
  * **Given** Seller nhập mã vé gốc không có trong hệ thống BTC hoặc vé đã sử dụng / đã bị hủy,  
  * **When** Seller bấm nút "Gửi mã OTP",  
  * **Then** hệ thống trả về lỗi `404 Not Found` hoặc `409 Conflict: Vé không tồn tại hoặc không đủ điều kiện chuyển nhượng`.
* **AC-2.1.2 (Email không khớp với chủ vé gốc):**  
  * **Given** Mã vé hợp lệ nhưng Email nhập vào không khớp với Email người mua ban đầu F0,  
  * **When** Seller gửi yêu cầu xác thực,  
  * **Then** hệ thống từ chối với lỗi `403 Forbidden: Email does not match original ticket owner`.
* **AC-2.1.3 (Gửi OTP thành công):**  
  * **Given** Mã vé và Email hoàn toàn trùng khớp,  
  * **When** Yêu cầu được gửi đến MockOrganizer,  
  * **Then** MockOrganizer tạo mã OTP 6 số ngẫu nhiên (hạn 5 phút), gửi email thực tế qua SMTP và lưu hash vào `mock_otps`.
* **AC-2.1.4 (Chống Spam):**  
  * **Given** Seller vừa bấm gửi OTP,  
  * **When** Seller bấm lại trong vòng 60 giây,  
  * **Then** hệ thống từ chối và hiển thị đồng hồ đếm ngược chờ cooldown.

#### 🛠️ Sub-tasks:
* `FE-2.1.1` **[Dev 3]**: `FE-2.1.1_Ticket code & email verification input component` *(2 SP)*
* `FE-2.1.2` **[Dev 3]**: `FE-2.1.2_OTP countdown timer & resend button component` *(1 SP)*
* `BE-MOCK-2.1.1` **[Dev 2]**: `BE-MOCK-2.1.1_Send ticket verification OTP api` *(3 SP)*
* `BE-MOCK-2.1.2` **[Dev 2]**: `BE-MOCK-2.1.2_SMTP email service for OTP delivery` *(2 SP)*
* `BE-CORE-2.1.1` **[Dev 1]**: `BE-CORE-2.1.1_Request OTP proxy api with resilient client` *(2 SP)*
* `DB-2.1.1` **[Dev 2]**: `DB-2.1.1_Setup mock_tickets and mock_otps schema` *(1 SP)*

---

### 🟢 USER STORY 02 (US-2.2): Xác Thực OTP & Cài Đặt Giá Bán Tuân Thủ Trần Giá
> **Summary:** `US-2.2_Seller verifies OTP and sets price within ceiling`  
> **Mô tả:** Người bán nhập OTP và cài đặt giá bán lại tuân thủ nghiêm ngặt Quy tắc trần giá (ResalePrice <= OriginalPrice).

#### 🎯 Acceptance Criteria (Gherkin):
* **AC-2.2.1 (Global Law 1 - Quy tắc trần giá):**  
  * **Given** Giá gốc của vé là `1.000.000 VNĐ`,  
  * **When** Seller cố tình nhập giá bán lại `1.000.001 VNĐ` hoặc cao hơn,  
  * **Then** Hệ thống chặn ngay lập tức, trả về `422 Unprocessable Entity` với thông báo: *"Giá bán lại không được vượt quá giá gốc theo Quy định chống đầu cơ của TicketShield"*.
* **AC-2.2.2 (Xác thực OTP chính xác & Khóa vé gốc):**  
  * **Given** Seller nhập đúng mã OTP còn hạn 5 phút,  
  * **When** Bấm xác nhận,  
  * **Then** MockOrganizer chuyển trạng thái vé gốc thành `LOCKED_FOR_RESALE` (không thể quét vào cổng hoặc đăng bán lần 2 bên BTC).
* **AC-2.2.3 (OTP sai hoặc hết hạn):**  
  * **Given** Seller nhập sai OTP hoặc OTP đã quá 5 phút,  
  * **When** Bấm xác nhận,  
  * **Then** MockOrganizer trả về lỗi `400 Bad Request: Mã OTP không chính xác hoặc đã hết hạn`.

#### 🛠️ Sub-tasks:
* `FE-2.2.1` **[Dev 3]**: `FE-2.2.1_OTP 6-digit input modal component` *(2 SP)*
* `FE-2.2.2` **[Dev 3]**: `FE-2.2.2_Price ceiling slider & alert component` *(2 SP)*
* `BE-MOCK-2.2.1` **[Dev 2]**: `BE-MOCK-2.2.1_Verify OTP and lock ticket for resale api` *(3 SP)*
* `BE-CORE-2.2.1` **[Dev 1]**: `BE-CORE-2.2.1_Validate price ceiling domain rule` *(3 SP)*

---

### 🟢 USER STORY 03 (US-2.3): Chọn Chế Độ Bán (Công Khai vs Riêng Tư) & Niêm Yết Vé
> **Summary:** `US-2.3_Seller selects listing mode (Public vs Private) and publishes ticket`  
> **Mô tả:** Người bán chọn đăng bán Công khai lên sàn hoặc Bán riêng tư cho bạn bè kèm Link mã hóa chứa token bí mật.

#### 🎯 Acceptance Criteria (Gherkin):
* **AC-2.3.1 (Chế độ Bán riêng tư - Private Sale):**  
  * **Given** Seller chọn chế độ "Bán riêng cho bạn bè (`is_private = true`)",  
  * **When** Vé được tạo thành công,  
  * **Then** Hệ thống sinh một `private_access_token` ngẫu nhiên và đường link định dạng: `https://ticketshield.vn/p2p/listing/{id}?token={private_access_token}` kèm mã QR để gửi bạn bè.
* **AC-2.3.2 (Bảo vệ vé Private khỏi Chợ công khai):**  
  * **Given** Vé được tạo ở chế độ Private,  
  * **When** Người dùng tìm kiếm trên Chợ vé công khai hoặc gọi API danh sách vé,  
  * **Then** Vé Private TUYỆT ĐỐI KHÔNG xuất hiện trong kết quả trả về.
* **AC-2.3.3 (Kiểm soát quyền truy cập Link riêng tư):**  
  * **Given** Bất kỳ ai truy cập đường link vé riêng tư,  
  * **When** URL không có `token` hoặc `token` không khớp với `private_access_token`,  
  * **Then** Hệ thống trả về `403 Forbidden: Vé này được đặt ở chế độ riêng tư hoặc link không hợp lệ`.
* **AC-2.3.4 (Chống bán trùng lặp - Concurrency):**  
  * **Given** Một mã vé gốc đang có niêm yết `VERIFIED` hoặc `TRANSACTING`,  
  * **When** Bất kỳ ai cố tình đăng bán lại cùng mã vé đó,  
  * **Then** PostgreSQL chặn lại qua Partial Unique Index và trả về lỗi `409 Conflict`.

#### 🛠️ Sub-tasks:
* `FE-2.3.1` **[Dev 3]**: `FE-2.3.1_Listing mode selector component (Public vs Private)` *(1 SP)*
* `FE-2.3.2` **[Dev 3]**: `FE-2.3.2_Private share link modal & QR generator` *(2 SP)*
* `FE-2.3.3` **[Dev 4]**: `FE-2.3.3_Private listing preview page` *(3 SP)*
* `BE-CORE-2.3.1` **[Dev 1]**: `BE-CORE-2.3.1_Create resale listing with private token api` *(3 SP)*
* `BE-CORE-2.3.2` **[Dev 1]**: `BE-CORE-2.3.2_Validate private listing access token api` *(2 SP)*
* `DB-CORE-2.3.1` **[Dev 1]**: `DB-CORE-2.3.1_Setup resale_listings table with partial unique index` *(2 SP)*

---

### 🟢 USER STORY 04 (US-2.4): Quản Lý Vé Đã Đăng & Hủy Niêm Yết
> **Summary:** `US-2.4_Seller manages listed tickets and cancels listing`  
> **Mô tả:** Người bán xem danh sách vé đã đăng, lấy lại link bán riêng hoặc hủy niêm yết để hoàn vé gốc bên BTC.

#### 🎯 Acceptance Criteria (Gherkin):
* **AC-2.4.1 (Xem danh sách vé của tôi):**  
  * **Given** Seller đang mở tab "Vé đang bán",  
  * **When** Trang tải dữ liệu,  
  * **Then** Hiển thị danh sách vé kèm badge rõ ràng: `PUBLIC` / `PRIVATE` và trạng thái `VERIFIED` / `TRANSACTING` / `SOLD`.
* **AC-2.4.2 (Hủy niêm yết & Mở khóa vé gốc):**  
  * **Given** Vé đang ở trạng thái `VERIFIED` (chưa có ai đặt mua),  
  * **When** Seller bấm "Hủy niêm yết" và xác nhận,  
  * **Then** Listing chuyển sang `CANCELLED`, hệ thống tự động gọi sang MockOrganizer để mở khóa vé gốc (`UNLOCK_FOR_RESALE`) về trạng thái `VALID`.
* **AC-2.4.3 (Chặn hủy khi đang giao dịch):**  
  * **Given** Vé đang có người giữ chỗ thanh toán (`TRANSACTING`) hoặc đã bán (`SOLD`),  
  * **When** Seller cố gắng hủy,  
  * **Then** Hệ thống báo lỗi `400 Bad Request: Vé đang trong phiên giao dịch, không thể hủy`.

#### 🛠️ Sub-tasks:
* `FE-2.4.1` **[Dev 4]**: `FE-2.4.1_Seller listed tickets table & status badges component` *(2 SP)*
* `FE-2.4.2` **[Dev 4]**: `FE-2.4.2_Cancel listing confirmation modal & copy link button` *(2 SP)*
* `FE-2.4.3` **[Dev 4]**: `FE-2.4.3_Seller ticket management page` *(2 SP)*
* `BE-CORE-2.4.1` **[Dev 1]**: `BE-CORE-2.4.1_Get seller listings api` *(2 SP)*
* `BE-CORE-2.4.2` **[Dev 1]**: `BE-CORE-2.4.2_Cancel resale listing api` *(2 SP)*
* `BE-MOCK-2.4.1` **[Dev 2]**: `BE-MOCK-2.4.1_Unlock original ticket api` *(1 SP)*

---

### 🟢 USER STORY 05 (US-2.5): Quality Assurance & Security Testing for MF-02
> **Summary:** `US-2.5_Quality Assurance & Security Testing for MF-02`  
> **Mô tả:** Kiểm thử tự động cho toàn bộ luồng MF-02: logic trần giá, vòng đời OTP và bảo mật link riêng tư.

#### 🛠️ Sub-tasks:
* `TEST-2.5.1` **[Dev 5]**: `TEST-2.5.1_Unit test price ceiling domain rule` *(2 SP)*
* `TEST-2.5.2` **[Dev 5]**: `TEST-2.5.2_Integration test OTP verification & expiry` *(2 SP)*
* `TEST-2.5.3` **[Dev 5]**: `TEST-2.5.3_Security test private token protection` *(1 SP)*

---

### 🟢 USER STORY 06 (US-2.6): Review 1 Feedback Refinements (Giảng Viên Góp Ý)
> **Summary:** `US-2.6_Seller Flow Refinements based on Review 1 Feedback`  
> **Mô tả:** Cập nhật các yêu cầu phản biện từ Thầy Đức và Cô giáo cho luồng Người bán: bổ sung biên độ giá trần linh hoạt Delta % và áp dụng mức phí ưu đãi cùng thông điệp bảo vệ cho Private Resale.

#### 🎯 Acceptance Criteria (Gherkin):
* **AC-2.6.1 (Biên độ giá trần linh hoạt BR-G01):**  
  * **Given** Ban tổ chức cấu hình biên độ biến thiên $\Delta_{\text{markup}} \in [0\%, 10\%]$ cho sự kiện,  
  * **When** Seller đặt giá bán $\le \text{OriginalPrice} \times (1 + \Delta_{\text{markup}})$,  
  * **Then** Hệ thống chấp nhận niêm yết; nếu vượt quá trần linh hoạt ném lỗi `422 Unprocessable Entity`.
* **AC-2.6.2 (Ưu đãi phí & Kịch bản Private Resale BR-L03):**  
  * **Given** Seller chọn chế độ Bán riêng tư (`is_private = true`),  
  * **When** Vé được niêm yết,  
  * **Then** Hệ thống áp dụng mức phí ưu đãi $1\% - 2\%$ và hiển thị thông điệp giải thích lợi ích bảo vệ giao dịch P2P.

#### 🛠️ Sub-tasks:
* `BE-CORE-2.6.1` **[Dev 1]**: `BE-CORE-2.6.1_Configurable markup percentage on Event and ResaleListing domain validation` *(2 SP)*
* `FE-2.6.1` **[Dev 3]**: `FE-2.6.1_Price slider ceiling warning with dynamic markup rate` *(1 SP)*
* `BE-CORE-2.6.2` **[Dev 1]**: `BE-CORE-2.6.2_Discounted platform fee calculation for private resale` *(2 SP)*
* `FE-2.6.2` **[Dev 4]**: `FE-2.6.2_Private resale protection benefits modal and badge` *(1 SP)*
* `TEST-2.6.1` **[Dev 5]**: `TEST-2.6.1_Unit test configurable markup price ceiling` *(1 SP)*

---

## 📊 TỔNG HỢP STORY POINTS SPRINT MF-02 (SAU KHI BỔ SUNG FEEDBACK)

```
┌─────────────────────────────────────────────────────────────┐
│                 TỔNG KẾT KHỐI LƯỢNG SPRINT MF-02            │
├────────────────────────────────┬────────────┬───────────────┤
│ Thành viên                     │ Số Task    │ Story Points  │
├────────────────────────────────┼────────────┼───────────────┤
│ Dev 1 (BE TicketShield Core)   │ 8 tasks    │ 18 SP         │
│ Dev 2 (BE MockOrganizer)       │ 4 tasks    │ 9 SP          │
│ Dev 3 (FE Creation Flow)       │ 6 tasks    │ 9 SP          │
│ Dev 4 (FE Mgmt & Preview)      │ 5 tasks    │ 10 SP         │
│ Dev 5 (QA / Test Automation)   │ 4 tasks    │ 6 SP          │
├────────────────────────────────┼────────────┼───────────────┤
│ TỔNG CỘNG                      │ 27 tasks   │ 52 SP         │
└────────────────────────────────┴────────────┴───────────────┘
```

