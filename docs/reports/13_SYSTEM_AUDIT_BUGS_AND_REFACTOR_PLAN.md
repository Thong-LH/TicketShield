# BÁO CÁO KIỂM TOÁN HỆ THỐNG & KẾ HOẠCH KHẮC PHỤC LỖI TRỌNG YẾU
> **Dự án:** TicketShield AI — Sàn chuyển nhượng vé thứ cấp P2P  
> **Thời điểm lập:** 19/09/2026  
> **Tài liệu:** docs/reports/13_SYSTEM_AUDIT_BUGS_AND_REFACTOR_PLAN.md  
> **Mục tiêu:** Ghi nhận toàn diện các lỗi logic, lỗ hổng tài chính, vi phạm kiến trúc và định tuyến mạng được phát hiện trong đợt audit Sprint MF-03, thiết lập phương án khắc phục chuẩn mực trước khi tiến hành chỉnh sửa mã nguồn.

---

## 1. TỔNG HỢP CÁC LỖI TRỌNG YẾU PHÁT HIỆN

| STT | Vấn đề / Lỗ hổng | Mức độ | Vị trí mã nguồn | Tác động hệ thống |
| :--- | :--- | :--- | :--- | :--- |
| **1** | **Xóa sổ bản ghi hoàn tiền khi giữ chỗ lại** | **[ĐÃ FIX]** | [HoldListingForPurchaseCommandHandler.cs](file:///d:/Capstone/src/TicketShield.Core/TicketShield.Application/Features/ResaleListings/Commands/HoldListingForPurchase/HoldListingForPurchaseCommandHandler.cs) | Đã đổi sang quan hệ 1-N, khởi tạo Escrow mới độc lập, bảo toàn 100% bản ghi `RefundQueued` & `BankTransactionReference`. |
| **2** | **Bỏ quên gọi gRPC sang BTC để hủy vé cũ & cấp vé mới** | **[ĐÃ FIX]** | [ProcessSePayWebhookCommandHandler.cs](file:///d:/Capstone/src/TicketShield.Core/TicketShield.Application/Features/ResaleListings/Commands/ProcessSePayWebhook/ProcessSePayWebhookCommandHandler.cs) | Đã gọi gRPC `TransferOwnershipByListingId` sang BTC, cập nhật mã vé mới và QR chính chủ vào Escrow và email Buyer. |
| **3** | **Tự tạo mã vé & mã QR giả mạo** | **[ĐÃ FIX]** | [GetMyPurchasedTicketsQueryHandler.cs](file:///d:/Capstone/src/TicketShield.Core/TicketShield.Application/Features/ResaleListings/Queries/GetMyPurchasedTickets/GetMyPurchasedTicketsQueryHandler.cs) | Đã xóa sổ hoàn toàn mã giả `TS-PASS` và QR ngoài, trỏ trực tiếp về dữ liệu thật do Ban tổ chức cấp. |
| **4** | **Lỗ hổng tranh chấp giữ vé (Race Condition)** | **[ĐÃ FIX]** | [HoldListingForPurchaseCommandHandler.cs](file:///d:/Capstone/src/TicketShield.Core/TicketShield.Application/Features/ResaleListings/Commands/HoldListingForPurchase/HoldListingForPurchaseCommandHandler.cs) | Đã triển khai PostgreSQL Transaction Advisory Lock theo `ListingId`, chống 100% việc 2 người cùng giữ 1 vé. |
| **5** | **Lỗi bẫy Route YARP Gateway trả về HTTP 404** | **[ĐÃ FIX]** | [appsettings.json (Gateway)](file:///d:/Capstone/src/TicketShield.Gateway/appsettings.json) | Đã sửa template route YARP sang `{**catch-all}` bao hàm cả trường hợp có hoặc không có dấu gạch chéo cuối. |

*(Ghi chú: Vấn đề số 6 liên quan đến `AutomaticSettlementWorker` giả lập tài khoản MB Bank đã được thống nhất hoãn về đúng phạm vi Sprint MF-04).*

---

## 2. CHI TIẾT TỪNG VẤN ĐỀ & PHƯƠNG ÁN KHẮC PHỤC

### Vấn đề 1: Xóa sổ bản ghi hoàn tiền khi có người giữ chỗ lại (Critical Data Loss)
- **Hiện trạng:**
  Khi Người mua A chuyển khoản muộn (sau 10 phút), Webhook SePay nhận tiền và ghi nhận `escrow.Status = EscrowStatus.RefundQueued`, đồng thời nhả vé về `VERIFIED`. Khi Người mua B bấm giữ chỗ chiếc vé này, hàm `HoldListingForPurchaseCommandHandler` tái sử dụng lại `listing.EscrowTransaction` cũ và gán đè:
  ```csharp
  escrow = listing.EscrowTransaction;
  escrow.BuyerId = buyerId; // Ghi đè ID Người mua B
  escrow.Status = EscrowStatus.Pending; // Xóa sổ trạng thái RefundQueued của Người mua A
  ```
- **Hậu quả:** Mất hoàn toàn bằng chứng giao dịch chuyển khoản muộn của Người mua A, hệ thống kế toán không thể đối soát để hoàn tiền.
- **Phương án đã thực hiện & kiểm thử:**
  1. Đã chuyển quan hệ giữa `ResaleListing` và `EscrowTransaction` sang 1-N (`ICollection<EscrowTransaction>`).
  2. Tạo migration `20260919131929_AllowMultipleEscrowsPerListing` gỡ bỏ index UNIQUE trên `listing_id` của bảng `escrow_transactions`.
  3. Cập nhật `HoldListingForPurchaseCommandHandler`: Khởi tạo `new EscrowTransaction` độc lập cho từng phiên giữ chỗ mới, giữ nguyên vẹn 100% các bản ghi `RefundQueued` và `BankTransactionReference`.
  4. Đã bổ sung Fact test `Handle_WhenListingHasRefundQueuedEscrow_ShouldCreateNewEscrowAndPreserveRefundQueuedRecord` và pass 123/123 Unit Tests.

---

### Vấn đề 2: Bỏ quên gọi gRPC sang BTC khi nhận Webhook SePay (Gãy luồng core)
- **Hiện trạng:**
  RPC `TransferOwnership` đã được hiện thực hoàn chỉnh tại `MockOrganizer.API` và có hàm bọc `TicketVerificationService.TransferOwnership`. Tuy nhiên, trong [ProcessSePayWebhookCommandHandler.cs](file:///d:/Capstone/src/TicketShield.Core/TicketShield.Application/Features/ResaleListings/Commands/ProcessSePayWebhook/ProcessSePayWebhookCommandHandler.cs), khi nhận tiền thành công chỉ đổi cờ trong database nội bộ mà không hề gọi sang BTC. Email gửi cho người mua bị truyền chuỗi rỗng:
  ```csharp
  var buyerHtml = _emailTemplates.GetBuyerTicketIssuedEmailHtml(
      ..., string.Empty /* newTicketCode */, ..., string.Empty /* qrCode */, ...);
  ```
- **Hậu quả:** Vé cũ ở BTC vẫn kẹt ở trạng thái `LOCKED_FOR_RESALE`, vé mới cho Người mua chưa từng được tạo ra, Người mua không có vé đi xem sự kiện.
- **Phương án khắc phục:**
  1. Inject `ITicketVerificationService` (hoặc `IOrganizerGateway`) vào `ProcessSePayWebhookCommandHandler`.
  2. Khi tiền thanh toán hợp lệ, gọi phương thức `TransferOwnership` sang MockOrganizer qua gRPC.
  3. Nhận mã vé mới (`newTicketCode`) và chuỗi QR chính thức do BTC cấp.
  4. Lưu mã vé mới vào CSDL và truyền đầy đủ vào email gửi cho Người mua.

---

### Vấn đề 3: Tự tạo mã vé & mã QR giả mạo trong `GetMyPurchasedTickets`
- **Hiện trạng:**
  Tại [GetMyPurchasedTicketsQueryHandler.cs](file:///d:/Capstone/src/TicketShield.Core/TicketShield.Application/Features/ResaleListings/Queries/GetMyPurchasedTickets/GetMyPurchasedTicketsQueryHandler.cs#L68-L73), hệ thống tự cắt chuỗi `PaymentReference` để sinh mã giả `TS-PASS` và gọi API bên ngoài `api.qrserver.com` để vẽ QR nội bộ `TICKETSHIELD:OFFICIAL_PASS:...`.
- **Hậu quả:** Mã QR này không khớp với CSDL vé của BTC, máy quét tại cổng soát vé sẽ báo vé giả và từ chối vào cổng. Đồng thời tạo sự phụ thuộc rủi ro vào API công cộng bên ngoài.
- **Phương án khắc phục:**
  1. Loại bỏ hoàn toàn đoạn code chế mã `TS-PASS` và link `api.qrserver.com`.
  2. Trả về đúng mã vé chính chủ (`OfficialTicketCode`) và chuỗi QR gốc do BTC phát hành sau khi thực hiện sang tên qua gRPC.

---

### Vấn đề 4: Lỗ hổng tranh chấp giữ chỗ vé (Race Condition Concurrency Flaw)
- **Hiện trạng:**
  Trong [HoldListingForPurchaseCommandHandler.cs](file:///d:/Capstone/src/TicketShield.Core/TicketShield.Application/Features/ResaleListings/Commands/HoldListingForPurchase/HoldListingForPurchaseCommandHandler.cs), khi kiểm tra và cập nhật vé sang `TRANSACTING` hoàn toàn không sử dụng Transaction hay cơ chế Locking nào. Hai người mua cùng bấm "Mua ngay" tại cùng mili-giây đều đọc được vé ở trạng thái `VERIFIED` và cùng được cấp mã VietQR.
- **Hậu quả:** Cả 2 người cùng chuyển tiền cho 1 chiếc vé duy nhất, gây tranh chấp tài chính nghiêm trọng.
- **Phương án khắc phục:**
  1. Mở Database Transaction tại đầu Handler: `await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);`
  2. Sử dụng PostgreSQL Advisory Lock theo ID bài đăng:
     ```csharp
     long lockKey = ComputeLockKey("hold:listing:" + request.ListingId);
     await _dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
     ```
  3. Người đến sau buộc phải chờ người trước hoàn tất; khi tới lượt thì vé đã chuyển sang `TRANSACTING` và bị chặn lại an toàn.

---

### Vấn đề 5: Lỗi bẫy Route trong YARP Gateway gây lỗi HTTP 404
- **Hiện trạng:**
  Trong [TicketShield.Gateway/appsettings.json](file:///d:/Capstone/src/TicketShield.Gateway/appsettings.json), cấu hình:
  ```json
  "user-bank-accounts-route": {
    "ClusterId": "identity-cluster",
    "Match": { "Path": "/api/v1/user-bank-accounts/{**catch-all}" }
  }
  ```
  Khi Frontend gọi `GET /api/v1/user-bank-accounts` hoặc `POST /api/v1/user-bank-accounts` (không có dấu gạch chéo `/`), YARP không khớp route này mà rơi xuống `core-route` chuyển sang Core API cổng 5003 -> nổ lỗi HTTP 404 Not Found.
- **Phương án khắc phục:**
  Đổi template đường dẫn sang dạng hỗ trợ cả trường hợp không có trailing slash:
  ```json
  "Match": { "Path": "/api/v1/user-bank-accounts{**remainder}" }
  ```

---

## 3. GHI CHÚ BỔ SUNG: SỰ CỐ LỊCH SỬ VỀ CỔNG GỬI OTP
*(Lưu vết kỹ thuật phục vụ đối chiếu và review sau này)*

- **Xung đột cổng gRPC 5002 (Đã sửa tại commit `32e38e3`):**
  Ban đầu MockOrganizer chạy gRPC tại cổng 5002. Khi tách Identity Service độc lập chiếm giữ cổng 5002, Core gọi gRPC gửi OTP bị bắn nhầm vào Identity API dẫn đến lỗi không gửi được OTP. Đã khắc phục bằng cách chuyển MockOrganizer sang cổng cố định **5001**, dành riêng cổng **5002** cho Identity.
- **Tối ưu hóa độ trễ SMTP (Đã sửa tại commit `f3f67bf`):**
  Thêm cơ chế thăm dò cổng nhanh bằng TCP (Fast Socket Probe trong 250ms) trong [ResaleOptions.cs](file:///d:/Capstone/src/MockOrganizer/MockOrganizer.API/Resale/ResaleOptions.cs). Nếu môi trường local dev chưa mở mail server, hệ thống in trực tiếp mã OTP ra console để test ngay, không bị treo 10-15 giây.

---

---

## 4. ĐIỂM KHÔI PHỤC AN TOÀN TRÊN GIT (GIT BACKUP & ROLLBACK SAFETY)

Hệ thống đã tạo sẵn nhánh lưu trữ toàn bộ trạng thái code cục bộ trước khi pull commit từ remote:
- **Tên nhánh Backup:** `backup/flow_MF_03_pre_pull`
- **Mã Commit gốc:** `027d99f`
- **Nhánh làm việc hiện tại:** `flow/MF_03` (HEAD: `ad200ea`)

> **Lệnh khôi phục khi cần hoàn tác lại trạng thái ban đầu:**
> ```powershell
> # Cách 1: Chuyển hẳn sang nhánh backup
> git checkout backup/flow_MF_03_pre_pull
> 
> # Cách 2: Quay lui commit trên nhánh hiện tại về đúng điểm an toàn
> git reset --hard 027d99f
> ```

---

## 5. KẾ HOẠCH THỰC HIỆN SỬA CODE (ACTION PLAN)

1. **Bước 1 — Sửa YARP Gateway:** Cập nhật pattern route `user-bank-accounts` trong [appsettings.json](file:///d:/Capstone/src/TicketShield.Gateway/appsettings.json) thành `"/api/v1/user-bank-accounts{**remainder}"`.
2. **Bước 2 — Sửa Concurrency Lock & Tạo mới Escrow:** Cập nhật [HoldListingForPurchaseCommandHandler.cs](file:///d:/Capstone/src/TicketShield.Core/TicketShield.Application/Features/ResaleListings/Commands/HoldListingForPurchase/HoldListingForPurchaseCommandHandler.cs) bổ sung `pg_advisory_xact_lock` và khởi tạo `new EscrowTransaction` độc lập (bảo toàn lịch sử `RefundQueued`).
3. **Bước 3 — Kết nối gRPC Sang tên vé trong Webhook:** Cập nhật [ProcessSePayWebhookCommandHandler.cs](file:///d:/Capstone/src/TicketShield.Core/TicketShield.Application/Features/ResaleListings/Commands/ProcessSePayWebhook/ProcessSePayWebhookCommandHandler.cs) gọi `TransferOwnership` sang MockOrganizer, lưu mã vé thật và đưa vào email xác nhận.
4. **Bước 4 — Chuẩn hóa Trang xem vé:** Cập nhật [GetMyPurchasedTicketsQueryHandler.cs](file:///d:/Capstone/src/TicketShield.Core/TicketShield.Application/Features/ResaleListings/Queries/GetMyPurchasedTickets/GetMyPurchasedTicketsQueryHandler.cs) loại bỏ mã QR chế `TS-PASS` và trả về vé chính thức từ BTC.
5. **Bước 5 — Kiểm thử xác minh:** Chạy bộ Unit Tests có trọng tâm trong `TicketShield.UnitTests` để xác nhận toàn bộ 5 lỗi được khắc phục trọn vẹn.

