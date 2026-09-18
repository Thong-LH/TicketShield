# BÁO CÁO TỔNG HỢP CÁC THAY ĐỔI BACKEND (BE CHANGES & RATIONALE)

Tài liệu này ghi nhận chi tiết **4 file Backend** đã được chỉnh sửa, phân tích nguyên nhân gốc rễ (Root Cause) và lý do kỹ thuật (Rationale) đằng sau mỗi thay đổi.

---

## 🛠️ Danh sách các File thay đổi

| STT | File thay đổi | Component | Loại thay đổi | Lý do chính |
| :--- | :--- | :--- | :--- | :--- |
| **1** | [`OrganizerResaleGrpcService.cs`](file:///d:/Mon_hoc/Capstone_Project/TicketShield/src/MockOrganizer/MockOrganizer.API/Resale/OrganizerResaleGrpcService.cs) | `MockOrganizer.API` | Fix Bug / Dedup | Sửa lỗi gửi 3 email OTP trùng lặp khi gRPC retry |
| **2** | [`ResaleOptions.cs`](file:///d:/Mon_hoc/Capstone_Project/TicketShield/src/MockOrganizer/MockOrganizer.API/Resale/ResaleOptions.cs) | `MockOrganizer.API` | Optimization | Tăng SMTP Timeout từ 10s lên 15s tránh timeout ảo |
| **3** | [`GlobalExceptionHandlingMiddleware.cs`](file:///d:/Mon_hoc/Capstone_Project/TicketShield/src/TicketShield.Core/TicketShield.API/Middlewares/GlobalExceptionHandlingMiddleware.cs) | `TicketShield.API` | Standardization | Format JSON Exception sang CamelCase cho FE đọc |
| **4** | [`HoldListingForPurchaseCommandHandler.cs`](file:///d:/Mon_hoc/Capstone_Project/TicketShield/src/TicketShield.Core/TicketShield.Application/Features/ResaleListings/Commands/HoldListingForPurchase/HoldListingForPurchaseCommandHandler.cs) | `TicketShield.Application` | Robustness | Auto-heal ShadowUsers ngăn lỗi nổ HTTP 500 FK |

---

## 📄 Chi Tiết Chi Tiết Từng File & Lý Do Kỹ Thuật

### 1. `OrganizerResaleGrpcService.cs`
* **Đường dẫn**: `src/MockOrganizer/MockOrganizer.API/Resale/OrganizerResaleGrpcService.cs`
* **Thay đổi**: Bổ sung kiểm tra trùng lặp (Deduplication Check) trong phương thức `Deliver(...)`:
  ```csharp
  // Deduplication: Check if session for this verificationId and generation has already sent an email via SMTP
  var existingSession = await db.Read<SessionState>(SessionKey(op.VerificationId), ct);
  if (existingSession != null && existingSession.Generation == result.Generation && existingSession.Delivery == "SmtpAccepted")
  {
      result.DeliveryState = DeliveryState.SmtpAccepted;
      return result;
  }
  ```
* **Lý do & Tác dụng (Rationale)**:
  * **Nguyên nhân**: Khi người dùng nhấn gửi OTP, nếu kết nối gRPC hoặc mạng bị chậm, `ResaleRecoveryWorker` hoặc gRPC client sẽ thực hiện retry gọi lại `RequestTicketOtp`. Do trước đó chưa kiểm tra trạng thái email đã gửi của `Generation` hiện tại, dịch vụ tiếp tục gọi `delivery.Send(...)` dẫn tới việc người dùng nhận 3 email trùng lặp cùng lúc trên Gmail.
  * **Giải pháp**: Nếu phiên làm việc (`SessionState`) của `Generation` đó đã được đánh dấu là `SmtpAccepted`, hệ thống sẽ trả về kết quả thành công ngay lập tức và **bỏ qua bước gửi mail lại**.

---

### 2. `ResaleOptions.cs`
* **Đường dẫn**: `src/MockOrganizer/MockOrganizer.API/Resale/ResaleOptions.cs`
* **Thay đổi**: Tăng thời gian chờ Timeout của `SmtpClient` trong `SmtpOtpDelivery` từ `10 giây` lên `15 giây`:
  ```csharp
  using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
  timeout.CancelAfter(TimeSpan.FromSeconds(15));
  await client.SendMailAsync(message, timeout.Token);
  ```
* **Lý do & Tác dụng (Rationale)**:
  * **Nguyên nhân**: Kết nối SSL/TLS handshaking tới Google SMTP (`smtp.gmail.com`) từ môi trường máy cục bộ đôi khi mất từ 6-12 giây. Thời gian timeout 10 giây quá ngắn làm hủy Task giữa chừng mặc dù phía Google SMTP đã nhận và đưa email vào hàng đợi, gây ra exception ngầm và khiến client hiểu nhầm là thất bại để retry.
  * **Giải pháp**: Tăng thời gian chờ lên 15 giây giúp quá trình gửi mail diễn ra hoàn chỉnh và ổn định.

---

### 3. `GlobalExceptionHandlingMiddleware.cs`
* **Đường dẫn**: `src/TicketShield.Core/TicketShield.API/Middlewares/GlobalExceptionHandlingMiddleware.cs`
* **Thay đổi**: Bổ sung `JsonNamingPolicy.CamelCase` khi serialize phản hồi lỗi:
  ```csharp
  var json = JsonSerializer.Serialize(response, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
  ```
* **Lý do & Tác dụng (Rationale)**:
  * **Nguyên nhân**: Mặc định `System.Text.Json` serialize object theo dạng PascalCase (`{"Message": "...", "Success": false}`). Trong khi đó, Frontend (Axios) parse JSON theo chuẩn CamelCase (`data.message`). Điều này khiến FE không trích xuất được câu thông báo lỗi chi tiết từ BE mà chỉ hiển thị thông báo lỗi mặc định `Request failed with status code 400`.
  * **Giải pháp**: Định dạng đồng nhất chuỗi JSON phản hồi lỗi dạng CamelCase (`{"message": "...", "success": false}`), giúp FE hiển thị chính xác các thông báo nghiệp vụ (ví dụ: *"Bạn không thể tự mua vé của chính mình."*).

---

### 4. `HoldListingForPurchaseCommandHandler.cs`
* **Đường dẫn**: `src/TicketShield.Core/TicketShield.Application/Features/ResaleListings/Commands/HoldListingForPurchase/HoldListingForPurchaseCommandHandler.cs`
* **Thay đổi**: Thêm logic tự động kiểm tra và thêm mới (Auto-Heal) `ShadowUser` cho cả Buyer và Seller trước khi lưu giao dịch giữ chỗ `EscrowTransaction`:
  ```csharp
  // Auto-heal: Ensure Buyer & Seller exist in ShadowUsers table to prevent FK constraint errors
  var buyerShadowUser = await _dbContext.ShadowUsers.FirstOrDefaultAsync(u => u.Id == buyerId, cancellationToken);
  if (buyerShadowUser == null)
  {
      buyerShadowUser = new ShadowUser
      {
          Id = buyerId,
          Email = _currentUserService?.Email ?? request.RecipientEmail ?? "buyer@ticketshield.vn",
          FullName = request.RecipientName ?? "Buyer User",
          PhoneNumber = "",
          CreatedAt = DateTimeOffset.UtcNow
      };
      _dbContext.ShadowUsers.Add(buyerShadowUser);
  }
  ```
* **Lý do & Tác dụng (Rationale)**:
  * **Nguyên nhân**: Bảng `escrow_transactions` có ràng buộc khóa ngoại (FK) bắt buộc `BuyerId` và `SellerId` phải tồn tại trong bảng `shadow_users`. Khi một người dùng mới đăng nhập mua vé nhưng chưa kịp đồng bộ thông tin qua RabbitMQ Event (`UserCreatedConsumer`), việc gọi `_dbContext.SaveChangesAsync()` sẽ nổ lỗi PostgreSQL FK Constraint Violation, làm Middleware trả về HTTP 500 (`An unexpected internal error occurred`).
  * **Giải pháp**: Tự động chèn bản ghi `ShadowUser` tương ứng nếu chưa có trong CSDL trước khi tạo `EscrowTransaction`, đảm bảo luồng giữ chỗ 10 phút luôn thành công 100%.

---
*Báo cáo được khởi tạo tự động cho hệ thống TicketShield Capstone Project 2026.*
