# TICKETSHIELD - SPRINT 05 BACKLOG (NON-AI MODULES)

> **Quy chuẩn quản trị:**
> - **100% Unassigned:** Không chỉ định tên thành viên cụ thể.
> - **Double Linked List:** Mọi task đều khai báo rõ `[Phụ thuộc: ... | Mở khóa cho: ...]`.
> - **Phân tách phân tầng rõ ràng:** `BE-CORE`, `BE-MOCK`, `FE`, `TEST`.

---

## 📌 EPIC: Primary Ticketing Storefront, Organizer Portal & Dispute Tracking (Non-AI)

---

### 1. US-5.1: Quản lý Kho vé Sơ cấp & API Đặt vé Ban Tổ Chức (Primary Ticketing Engine)
**Mô tả:** Cung cấp cơ chế quản lý các hạng vé sự kiện (VIP, GA, Standard) với kiểm soát số lượng tồn kho nguyên tử (tránh overselling) và API checkout cấp vé gốc cho khán giả tại `MockOrganizer.API`.

- **BE-MOCK-5.1.1: Thiết kế Model Hạng vé & API Danh mục vé sự kiện**
  - **Nội dung:** Bổ sung thực thể `TicketTier` (EventId, TierName, Price, TotalQuantity, AvailableQuantity). Viết endpoint `GET /api/organizer/events/{id}/tiers`.
  - **Liên kết:** `[Phụ thuộc: Bắt đầu ngay | Mở khóa cho: BE-MOCK-5.1.2, FE-5.2.1]`

- **BE-MOCK-5.1.2: Concurrency Lock Trừ Kho Vé & Tạo Đơn hàng Mua vé Gốc**
  - **Nội dung:** Xử lý lock trừ tồn kho nguyên tử (`AvailableQuantity >= requested`) và tạo đơn hàng sơ cấp `POST /api/organizer/orders/checkout`.
  - **Liên kết:** `[Phụ thuộc: sau BE-MOCK-5.1.1 | Mở khóa cho: FE-5.2.2, TEST-5.1.3]`

- **TEST-5.1.3: Kiểm thử Tải Đồng thời Trừ Kho Vé (Overselling Race Condition Test)**
  - **Nội dung:** Viết test giả lập 50 request cùng mua 5 vé cuối cùng để kiểm chứng không bị âm kho vé.
  - **Liên kết:** `[Phụ thuộc: sau BE-MOCK-5.1.2 | Mở khóa cho: Hoàn tất US-5.1]`

---

### 2. US-5.2: Giao diện Cổng Đặt vé Sơ cấp Ban Tổ Chức (OG Public Storefront)
**Mô tả:** Xây dựng giao diện công khai cho khán giả/người mua vé truy cập vào chọn sự kiện của BTC, chọn số lượng hạng vé và thực hiện checkout mua vé ban đầu.

- **FE-5.2.1: Trang Danh mục Sự kiện & Chọn Hạng Vé (`OGTicketStorePage.tsx`)**
  - **Nội dung:** Giao diện hiển thị poster sự kiện, danh sách các hạng vé (VIP, Standard), trạng thái còn vé/hết vé và bộ đếm chọn số lượng.
  - **Liên kết:** `[Phụ thuộc: sau BE-MOCK-5.1.1 | Mở khóa cho: FE-5.2.2]`

- **FE-5.2.2: Modal Checkout Thông tin Người Mua & Kết quả Nhận Vé**
  - **Nội dung:** Form nhập Họ tên, Email, SĐT nhận vé. Gọi API checkout, hiển thị màn hình chúc mừng kèm mã QR vé gốc và nút tắt sang sàn TicketShield.
  - **Liên kết:** `[Phụ thuộc: sau FE-5.2.1, sau BE-MOCK-5.1.2 | Mở khóa cho: TEST-5.2.3]`

- **TEST-5.2.3: Kiểm thử Toàn trình Luồng Mua vé Sơ cấp (E2E Storefront Flow)**
  - **Nội dung:** Kiểm tra luồng từ chọn vé -> nhập form -> nhận vé -> hiển thị đầy đủ trong `MockTicketsController`.
  - **Liên kết:** `[Phụ thuộc: sau FE-5.2.2 | Mở khóa cho: Hoàn tất US-5.2]`

---

### 3. US-5.3: Khép kín Trải nghiệm Theo dõi Khiếu nại Phía Người dùng (Dispute Tracking UX)
**Mô tả:** Giúp Buyer và Seller theo dõi minh bạch tiến độ và lý do phán quyết khi giao dịch bị tranh chấp, không để người dùng hoang mang khi tiền bị giữ trong Escrow.

- **BE-CORE-5.3.1: Endpoint Chi tiết Tiến độ Tranh chấp Người dùng (`GET /api/v1/disputes/my-disputes/{id}`)**
  - **Nội dung:** Trả về timeline trạng thái khiếu nại (Đã nộp -> Chờ đối soát log cổng -> Đã phân xử), lý do giải quyết và trạng thái hoàn tiền.
  - **Liên kết:** `[Phụ thuộc: Bắt đầu ngay | Mở khóa cho: FE-5.3.2]`

- **FE-5.3.2: Drawer/Modal Tiến độ Tranh chấp trên MyTicketsPage & MyListingsPage**
  - **Nội dung:** Thêm action "Xem tiến độ khiếu nại" khi vé có badge `DISPUTED`. Hiển thị chi tiết dòng thời gian xử lý và phán quyết từ hệ thống.
  - **Liên kết:** `[Phụ thuộc: sau BE-CORE-5.3.1 | Mở khóa cho: TEST-5.3.3]`

- **TEST-5.3.3: Kiểm thử Trạng thái Đồng bộ Khiếu nại giữa 2 đầu Buyer - Seller**
  - **Nội dung:** Đảm bảo khi Admin/Staff bấm duyệt hoàn tiền, cả phía Buyer và Seller đều cập nhật trạng thái tương ứng tức thì.
  - **Liên kết:** `[Phụ thuộc: sau FE-5.3.2 | Mở khóa cho: Hoàn tất US-5.3]`

---

### 4. US-5.4: Báo cáo Chuyển nhượng An toàn cho Ban Tổ Chức (OG Resale Metrics)
**Mô tả:** Cung cấp số liệu thống kê cho BTC về số lượng vé đã được chuyển nhượng chính chủ sang người mua mới qua sàn TicketShield.

- **BE-MOCK-5.4.1: API Thống kê Đối soát Vé Chuyển nhượng (`GET /api/organizer/resale-metrics`)**
  - **Nội dung:** Tổng hợp chỉ số: Số vé đã đổi chủ qua gRPC, số vé đang trong phiên giao dịch, số vé đang mở tranh chấp.
  - **Liên kết:** `[Phụ thuộc: Bắt đầu ngay | Mở khóa cho: FE-5.4.2]`

- **FE-5.4.2: Tab Báo cáo Đối tác trên Cổng Quản trị BTC (`OrganizerPortalPage.tsx`)**
  - **Nội dung:** Hiển thị biểu đồ/bảng số liệu đối soát chuyển nhượng vé giữa BTC và TicketShield Core.
  - **Liên kết:** `[Phụ thuộc: sau BE-MOCK-5.4.1 | Mở khóa cho: Hoàn tất US-5.4]`
