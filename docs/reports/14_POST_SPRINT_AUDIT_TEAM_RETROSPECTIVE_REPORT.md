# BÁO CÁO KIỂM TOÁN HỆ THỐNG VÀ NỘI DUNG SINH HOẠT CHUYÊN MÔN
## Dự án Capstone: TicketShield — Nền tảng Bán lại Vé An toàn
*Ngày kiểm toán: 19/09/2026 | Phiên làm việc: Sprint MF-03 | Nhánh: `flow/MF_03`*

---

## 1. MỤC ĐÍCH TÀI LIỆU
Tài liệu này tổng hợp toàn bộ kết quả kiểm toán kỹ thuật (Technical Post-Mortem & Architecture Audit) sau khi kéo 30 commit mới nhất từ các thành viên về nhánh `flow/MF_03`. 

Tài liệu được biên soạn phục vụ buổi **sinh hoạt chuyên môn (Sprint Retrospective)** của toàn nhóm phát triển, chỉ rõ các lỗi nghiêm trọng về mặt kiến trúc, dòng tiền và dữ liệu; nguyên nhân gốc rễ; trách nhiệm liên đới; cùng các quy chuẩn kỹ thuật bắt buộc phải tuân thủ để chuẩn bị tốt nhất cho các buổi bảo vệ đồ án tốt nghiệp trước Hội đồng chuyên môn.

---

## 2. BẢNG TỔNG HỢP 5 SỰ CỐ KỸ THUẬT NGHIÊM TRỌNG

| STT | Lỗ hổng / Sự cố kỹ thuật | Mức độ rủi ro | Thành viên liên quan | Trạng thái xử lý |
| :--- | :--- | :---: | :---: | :---: |
| **1** | **Xóa sạch dấu vết hoàn tiền khi vé được giữ lại (Escrow 1-1 Overwrite)** | Thảm họa tài chính | Nguyễn Hùng Thịnh | **ĐÃ FIX & TEST PASS 100%** |
| **2** | **Thanh toán thành công nhưng không gọi gRPC đổi chủ vé với BTC** | Gãy luồng cốt lõi | Nguyễn Thanh Tùng | **ĐÃ FIX & TEST PASS 100%** |
| **3** | **Tự tiện viết API Backend chui, bịa mã vé giả `TS-PASS` & QR ngoài** | Vi phạm kiến trúc | Linh Trần | **ĐÃ FIX & TEST PASS 100%** |
| **4** | **Thiếu khóa đồng thời khi giữ vé (Race Condition)** | Tranh chấp vé | Nguyễn Hùng Thịnh | **ĐÃ FIX & TEST PASS 100%** |
| **5** | **Cấu hình Gateway YARP thiếu khớp đường dẫn gây lỗi HTTP 404** | Lỗi định tuyến | Cấu hình Gateway | **ĐÃ FIX & TEST PASS 100%** |

---

## 3. PHÂN TÍCH CHI TIẾT TỪNG VẤN ĐỀ & BÓC TÁCH TRÁCH NHIỆM

### Sự cố 1: Ghi đè Escrow làm biến mất tiền cần hoàn của khách hàng cũ
- **Hiện tượng mã nguồn:**
  Trong file `HoldListingForPurchaseCommandHandler.cs` (commit `340fea2` và `485476e`), khi người mua B vào giữ chỗ một chiếc vé mà người mua A vừa để hết hạn 10 phút, hệ thống tìm thấy bản ghi `EscrowTransaction` cũ và gán đè:
  ```csharp
  escrow.BuyerId = buyerId;
  escrow.Status = EscrowStatus.Pending;
  escrow.UnlockAt = unlockAt;
  ```
- **Hậu quả thực tế:**
  Nếu người mua A chuyển khoản muộn, hệ thống đã đánh dấu giao dịch của A là `RefundQueued` (chờ hoàn tiền) và lưu mã giao dịch ngân hàng `BankTransactionReference`. Việc gán đè này đã **xóa sổ toàn bộ thông tin tài chính của người mua A**, biến giao dịch thành của người mua B. Người mua A bị mất tiền mà hệ thống không còn bất kỳ dấu vết nào để hoàn trả tự động.
- **Trách nhiệm:** **Nguyễn Hùng Thịnh** (Task SCRUM-81). Thiết kế thực thể quan hệ 1-1 cứng giữa Listing và Escrow mà không lường trước vòng đời tài chính của giao dịch.
- **Biện pháp đã khắc phục:**
  - Chuyển quan hệ giữa `ResaleListing` và `EscrowTransaction` sang quan hệ **1-Nhiều (1-to-N)**.
  - Mỗi phiên giữ vé tạo một bản ghi `EscrowTransaction` mới độc lập. Bản ghi `RefundQueued` cũ được bảo toàn vĩnh viễn trong cơ sở dữ liệu.
  - Đã bổ sung migration `20260919131929_AllowMultipleEscrowsPerListing.cs` và kiểm thử tự động đạt 100%.

---

### Sự cố 2: SePay báo thanh toán thành công nhưng không kích hoạt đổi chủ vé với BTC
- **Hiện tượng mã nguồn:**
  Trong file `ProcessSePayWebhookCommandHandler.cs` (commit `b4510ad`, `5bd9e60`):
  Khi tiền vào tài khoản TicketShield, Handler chỉ cập nhật trạng thái trong cơ sở dữ liệu là `LOCKED`, **hoàn toàn không gọi gRPC sang Ban tổ chức (MockOrganizer)** để vô hiệu hóa vé cũ của Seller và cấp vé mới cho Buyer.
  Đến lúc gửi email xác nhận cho Buyer, do không có vé mới, Dev đã lấy luôn mã vé cũ của Seller gửi cho Buyer và để trống mã QR:
  ```csharp
  var buyerHtml = _emailTemplates.GetBuyerTicketIssuedEmailHtml(
      ...,
      escrow.Listing.OriginalTicketCode, // Gửi mã vé CŨ của Seller!
      string.Empty,                      // Bỏ trống mã QR!
      ...);
  ```
- **Hậu quả thực tế:**
  Khách hàng bị trừ tiền thật trong tài khoản, nhận được email nhưng bên trong là mã vé cũ sắp hết hạn và không có QR để vào cổng. Trên hệ thống của BTC, chiếc vé vẫn đứng tên Seller cũ.
- **Trách nhiệm:** **Nguyễn Thanh Tùng** (Task SCRUM-87, SCRUM-88). 
  - *Lời bào chữa:* "Jira không ghi lúc SePay thành công thì Core phải gọi BTC".
  - *Thực tế:* Thanh toán thành công là thời điểm duy nhất để chuyển giao tài sản số. Dev làm tính năng thanh toán nhưng không có tư duy sản phẩm, thấy email thiếu QR và vé nhưng vẫn điền chuỗi rỗng `string.Empty` để bấm "Done" task.
- **Biện pháp đã khắc phục:**
  - Viết phương thức `TransferOwnershipByListingId` trong `TicketVerificationService.cs`.
  - Tích hợp gọi gRPC sang Ban tổ chức ngay trong luồng xử lý Webhook SePay.
  - Lưu trữ `NewTicketCode` và `QrCodeData` chính chủ do BTC cấp vào `EscrowTransaction` và truyền đầy đủ vào email gửi Buyer.

---

### Sự cố 3: Tự tiện viết API Backend chui, bịa mã vé giả `TS-PASS` và QR Server ngoài
- **Hiện tượng mã nguồn:**
  Trong commit `b8aa075` của **Linh Trần**, phát hiện file mới `GetMyPurchasedTicketsQueryHandler.cs` được thêm thẳng vào Backend Core:
  ```csharp
  TicketCode = $"TS-{rawCode}-PASS",
  QrCodeUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=250x250&data=TICKETSHIELD:OFFICIAL_PASS:{rawCode}"
  ```
- **Hậu quả thực tế:**
  Mã vé `TS-...-PASS` và chuỗi `TICKETSHIELD:OFFICIAL_PASS` là dữ liệu hoàn toàn bịa đặt, không tồn tại trong hệ thống vé gốc của Ban tổ chức. Mã QR tạo qua trang web ngoài `api.qrserver.com` không có chữ ký số và sẽ bị máy quét tại cổng sự kiện từ chối 100%. Đây là lỗi vi phạm nghiêm trọng tính toàn vẹn kiến trúc (Architectural Violation).
- **Trách nhiệm:** **Linh Trần** (Task SCRUM-96).
  - *Bối cảnh:* SCRUM-96 là task làm giao diện Frontend. Khi làm Frontend, Linh thấy thiếu API lấy vé đã mua và trong database chưa có vé từ BTC (do Tùng chưa gọi đổi vé).
  - *Sai lầm:* Thay vì yêu cầu nhóm thống nhất tạo task Backend và bắt Tùng nối gRPC, Linh tự ý nhảy sang repo Backend viết API "chui" và chế dữ liệu giả để giao diện Frontend có hình hiển thị khi demo.
- **Biện pháp đã khắc phục:**
  - Xóa bỏ hoàn toàn định dạng mã vé giả và đường dẫn QR ngoài.
  - Viết lại truy vấn lấy trực tiếp `NewTicketCode` và `QrCodeData` chuẩn được Ban tổ chức cấp phát từ bảng `escrow_transactions`.
  - Bổ sung unit test `GetMyPurchasedTicketsQueryHandlerTests.cs` kiểm tra tính chính chủ của vé.

---

### Sự cố 4: Thiếu cơ chế khóa đồng thời khi hai người cùng bấm giữ một vé
- **Hiện tượng mã nguồn:**
  Hàm `HoldListingForPurchaseCommandHandler.cs` đọc và ghi dữ liệu mà không có Database Transaction và không có khóa mức phân tán/bản ghi.
- **Hậu quả thực tế:**
  Khi hai người mua cùng bấm "Mua ngay" tại cùng mili-giây, cả hai cùng đọc trạng thái vé là `Verified`, hệ thống cấp mã VietQR cho cả hai người. Cả hai cùng chuyển tiền, dẫn đến tranh chấp tài chính và quá tải xử lý hoàn tiền.
- **Biện pháp đã khắc phục:**
  - Triển khai PostgreSQL Transaction Advisory Lock cấp cơ sở dữ liệu:
    `ComputeLockKey(request.ListingId)` băm từ SHA-256 theo đúng quy tắc kỹ thuật của dự án.
  - Tích hợp phương thức `BeginAdvisoryLockTransactionAsync` trong `ITicketShieldDbContext` và `TicketShieldDbContext`, đảm bảo tính đóng gói sạch giữa các tầng kiến trúc.

---

### Sự cố 5: Lỗi bẫy Route trong YARP Gateway gây lỗi 404 cho tài khoản ngân hàng
- **Hiện tượng:**
  Trong `TicketShield.Gateway/appsettings.json`, cấu hình route `/api/v1/user-bank-accounts/{**catch-all}` yêu cầu phải có dấu gạch chéo. Khi Frontend gọi `GET /api/v1/user-bank-accounts` không có dấu gạch chéo ở đuôi, YARP bỏ qua route này và đẩy sang Core API cổng 5003 -> báo lỗi 404 Not Found.
- **Biện pháp đã khắc phục:**
  Đổi cấu hình thành `Path: "/api/v1/user-bank-accounts{**catch-all}"` để hỗ trợ cả 2 trường hợp có hoặc không có trailing slash.

---

## 4. BÀI HỌC KINH NGHIỆM VÀ QUY TẮC SINH HOẠT NHÓM (RETROSPECTIVE ACTION ITEMS)

Trong buổi sinh hoạt nhóm, Lead/PO cần quán triệt 4 nguyên tắc sau:

1. **Chấm dứt tư duy Silo (Ai xong việc nấy):**
   - Khi làm việc trên kiến trúc vi dịch vụ và hệ thống giao dịch phân tán, không có tính năng nào là một "hòn đảo".
   - Nếu thấy dữ liệu đầu vào của mình bị thiếu (như email thiếu vé, giao diện thiếu API), **bắt buộc phải trao đổi ngay với người làm module liên quan**. Tuyệt đối không được tự ý điền chuỗi rỗng để qua mặt test.
2. **Không đi đường tắt tạo dữ liệu giả (Zero Tolerance for Fake Paths):**
   - Nghiêm cấm mọi hành vi tự chế mã pass, tự gọi server bên ngoài để fake QR, hoặc mock user nặc danh để làm xanh bài test hoặc làm đẹp giao diện demo.
   - Khi Hội đồng chấm điểm kiểm tra luồng quét vé tại cổng, mọi dữ liệu giả mạo sẽ lập tức bị phát hiện và đánh rớt đồ án.
3. **Đọc kỹ và phản hồi yêu cầu task:**
   - Việc phân rã User Story đôi khi có thể còn kẽ hở kết nối giữa các task. Tuy nhiên, Dev là người nắm chi tiết kỹ thuật, phải có tư duy phản biện và làm rõ yêu cầu nghiệp vụ trước khi bấm nút hoàn thành task trên Jira.
4. **Kiểm thử tích hợp (End-to-End Testing) trước khi bấm "Done":**
   - Một task chỉ được coi là hoàn thành (Definition of Done) khi luồng thực tế đã chạy xuyên suốt từ Frontend -> Gateway -> Core API -> Ban tổ chức -> Ngân hàng -> Người dùng cuối.

---

## 5. BÁO CÁO TRẠNG THÁI HIỆN TẠI CỦA CODEBASE
- Toàn bộ 5 lỗi kỹ thuật nêu trên đã được khắc phục hoàn chỉnh, chuẩn hóa theo Clean Architecture.
- Đã bổ sung 2 EF Core Migrations:
  1. `20260919131929_AllowMultipleEscrowsPerListing` (Quan hệ 1-N cho Escrow).
  2. `20260919143357_AddTicketCodesToEscrowTransaction` (Lưu vé mới và QR từ BTC).
- **Bộ kiểm thử tự động:** **61/61 tests** thuộc các phân hệ Resale, Thanh toán SePay, Vé đã mua, Quản lý tài khoản ngân hàng đều đã **Passed 100%** trong vòng 2 giây.
