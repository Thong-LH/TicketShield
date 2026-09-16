# TICKETSHIELD - SPRINT PLAN: MF-03 (BUYER PURCHASE & ESCROW LOCK)
## Epic: Epic-3_MF-03: P2P Purchase, Escrow Holding & Ownership Transfer

> **Mục tiêu Sprint (Sprint Goal):**  
> Xây dựng hoàn chỉnh luồng nghiệp vụ MF-03 cho phép Người mua (Buyer) giữ chỗ vé trong 10 phút (`TRANSACTING`), quét mã VietQR động để thanh toán, hệ thống tiếp nhận Webhook xử lý bất biến (Idempotent), tự động đóng băng tiền vào quỹ ký quỹ [EscrowTransaction](file:///d:/Capstone/src/TicketShield.Core/TicketShield.Domain/Entities/EscrowTransaction.cs) (`Status = LOCKED`), đồng thời gọi gRPC sang Ban tổ chức (MockOrganizer) hủy vé cũ và cấp vé mới chính chủ cho Buyer. Tích hợp RabbitMQ (MassTransit) làm hạ tầng Message Broker chuẩn bị sẵn sàng cho tiến trình giải ngân tự động MF-04.

---

## 🛠️ PHẦN 1: KẾ HOẠCH CÔNG NGHỆ & HẠ TẦNG (TECH MIGRATION)

### 1.1. Bổ sung Message Broker (RabbitMQ)
* Cấu hình container `rabbitmq:3-management` vào file [docker-compose.yml](file:///d:/Capstone/docker-compose.yml):
  * Cổng AMQP: `5672` (giao tiếp nội bộ giữa các service).
  * Cổng Management UI: `15672` (quản trị, theo dõi hàng đợi Queue).
* Tích hợp thư viện **MassTransit.RabbitMQ** vào `TicketShield.Core`.

### 1.2. Hợp đồng Sự kiện (Event Contract in `TicketShield.Contracts`)
Định nghĩa Interface sự kiện phát tán ngay sau khi sang tên chính chủ thành công:
```csharp
namespace TicketShield.Contracts.Events;

public interface IOwnershipTransferredEvent
{
    Guid EscrowId { get; }
    Guid ListingId { get; }
    Guid SellerId { get; }
    decimal NetSellerPayout { get; }
    string RecipientBankCode { get; }
    string RecipientAccountNumber { get; }
    string RecipientAccountName { get; }
    DateTimeOffset TransferredAt { get; }
    DateTimeOffset UnlockAt { get; } // Min(TransferredAt + 24h, EventStartTime - 2h)
}
```

### 1.3. Nâng cấp Hợp đồng gRPC (`organizer_resale.proto`)
Bổ sung RPC Method và Message phục vụ thủ tục sang tên đổi chủ:
```protobuf
service OrganizerResaleService {
    // Các methods xác thực OTP vé cũ từ MF-02...

    // MF-03: Chuyển quyền sở hữu chính chủ
    rpc TransferOwnership (TransferOwnershipRequest) returns (TransferOwnershipResponse);
}

message TransferOwnershipRequest {
    string original_ticket_code = 1;
    string new_owner_name = 2;
    string new_owner_email = 3;
    string new_owner_id_card = 4;
}

message TransferOwnershipResponse {
    bool is_success = 1;
    string new_ticket_code = 2;
    string message = 3;
    int64 transferred_at_unix = 4;
}
```

---

## 👥 PHẦN 2: PHÂN CHIA CÔNG VIỆC THEO VAI TRÒ (TASK ALLOCATION & RACI)

### 👤 Dev 1: Backend Core (.NET 8 Clean Architecture & CQRS)
* `BE-CORE-3.1.1` **[Hold Ticket & VietQR]**: Tạo `HoldListingForPurchaseCommand` khóa vé 10 phút sang `TRANSACTING`, sinh chuỗi VietQR chuyển khoản chứa `transfer_content` độc nhất.
* `BE-CORE-3.1.2` **[VietQR Webhook Handler]**: Endpoint `POST /api/v1/payments/vietqr-webhook` xử lý đối soát thanh toán tự động, kiểm tra tính bất biến (`transaction_reference`).
* `BE-CORE-3.1.3` **[Escrow Creation & gRPC Call]**: Tạo bản ghi `EscrowTransaction` (`Status = LOCKED`), tính `UnlockAt = Min(Now + 24h, EventStartTime - 2h)`, gắn cờ tạm giữ vé Buyer trong thời gian đệm (`in_settlement_buffer = true`, tự động nhả cờ mở quyền bán lại khi Escrow giải ngân thành công), gọi `OrganizerGateway.TransferOwnershipAsync()` sang MockOrganizer.
* `BE-CORE-3.1.4` **[RabbitMQ Event Publisher]**: Cấu hình MassTransit `IPublishEndpoint`, bắn `IOwnershipTransferredEvent` sau khi gRPC thành công.
* `BE-CORE-3.1.5` **[Auto Release Lock Expired]**: Background Service tự động nhả vé từ `TRANSACTING` về `VERIFIED` nếu sau 10 phút Buyer không thanh toán.
* `BE-CORE-3.1.6` **[Internal Pre-flight Claim API]**: Endpoint nội bộ `POST /api/v1/internal/escrows/{id}/claim-payout` (Row-level lock atomic update sang `PAYING_OUT`) phục vụ Worker xác nhận giải ngân chống Race Condition.

### 👤 Dev 2: Backend Partner (MockOrganizer .NET 8 & gRPC Server)
* `BE-MOCK-3.2.1` **[gRPC TransferOwnership]**: Hiện thực RPC `TransferOwnership` trong `OrganizerResaleGrpcService.cs`:
  * Kiểm tra vé cũ đang ở trạng thái `LOCKED_FOR_RESALE`.
  * Đổi vé cũ sang `TRANSFERRED` (vô hiệu hóa mã QR cũ).
  * Sinh mã vé mới (`new_ticket_code`), lưu vào `mock_tickets` với thông tin định danh của Buyer, trạng thái `VALID`.
* `BE-MOCK-3.2.2` **[Buyer Ticket Query API]**: Endpoint `GET /api/v1/mock-tickets/my-tickets` phục vụ giả lập app BTC hiển thị vé mới cho Buyer.

### 👤 Dev 3: Frontend Flow (Checkout, Countdown & VietQR Modal)
* `FE-3.3.1` **[Hold & Order Trigger]**: Nút "Mua ngay" trên trang chi tiết vé, gọi API Hold vé, hiển thị Modal thanh toán.
* `FE-3.3.2` **[10-Minute Countdown Timer]**: Đồng hồ đếm ngược 10 phút giữ vé; tự động đóng modal và cảnh báo hết hạn khi về 0.
* `FE-3.3.3` **[Dynamic VietQR Display]**: Hiển thị hình ảnh mã QR VietQR, thông tin số tài khoản, số tiền và mã nội dung chuyển khoản có nút sao chép nhanh (Copy to clipboard).
* `FE-3.3.4` **[Payment Polling / WebSocket Listener]**: Lắng nghe trạng thái thanh toán thành công để tự động chuyển hướng Buyer sang trang thông báo thành công.

### 👤 Dev 4: Frontend Management (Buyer Tickets & Seller Escrow Status)
* `FE-3.4.1` **[Buyer My Tickets Page]**: Màn hình xem danh sách vé đã mua thành công, hiển thị mã vé mới và mã QR vào cổng.
* `FE-3.4.2` **[Seller Order Status Badge]**: Cập nhật danh sách vé của Seller, đổi badge sang `SOLD` kèm dòng trạng thái: *"Tiền đang được bảo vệ trong quỹ Ký quỹ (Escrow LOCKED) - Sẽ giải ngân sau 24h"*.
* `FE-3.4.3` **[Sell Form Resale Dropdown]**: Dropdown "Chọn từ vé của tôi" trên form đăng bán vé ([SellTicketPage.tsx](file:///d:/FE_CapstoneProject/apps/web/src/pages/SellTicketPage.tsx)), gọi API danh sách vé đã mua đủ điều kiện (đã giải ngân xong, chưa quét cổng, chưa đăng bán) để tự điền mã vé nhanh cho luồng bán lại.

### 👤 Dev 5: QA / Tester & Automation Testing
* `TEST-3.5.1` **[Unit Test Hold Timeout]**: Test logic nhả vé tự động khi quá hạn 10 phút.
* `TEST-3.5.2` **[Integration Test Idempotent Webhook]**: Bắn 2 request webhook trùng `transaction_reference` kiểm tra hệ thống chỉ tạo duy nhất 1 Escrow.
* `TEST-3.5.3` **[Rollback Test on gRPC Failure]**: Giả lập MockOrganizer trả lỗi khi sang tên $\rightarrow$ Xác minh Listing và Escrow được Rollback an toàn, không bị treo tiền.

---

## 📅 PHẦN 3: LỊCH TRÌNH THỰC HIỆN 5 NGÀY (DAY-BY-DAY PLAN)

```
Thứ 2: Hạ tầng & Hợp đồng gRPC
  ├── Dev 1: Docker RabbitMQ + Cài MassTransit + Định nghĩa Event Interface
  └── Dev 2: Cập nhật file .proto + Generate gRPC Stub
Thứ 3: Luồng Giữ Chỗ & Sang Tên Vé
  ├── Dev 1: Command Hold vé 10p + Worker quét hết hạn
  ├── Dev 2: Code RPC TransferOwnership cấp vé mới bên BTC
  └── Dev 3: UI Modal Checkout VietQR + Bộ đếm ngược 10 phút
Thứ 4: Webhook Thanh Toán & Ký Quỹ
  ├── Dev 1: Webhook VietQR + Tạo Escrow Locked + Bắn RabbitMQ Message
  ├── Dev 3: Polling thanh toán thành công phía Client
  └── Dev 4: UI Màn hình "Vé đã mua" của Buyer
Thứ 5: Ghép Nối Liên Thông (End-to-End Testing)
  ├── Toàn đội: Mua vé thực tế trên UI -> Quét VietQR -> Sang tên BTC -> RabbitMQ nhận Event
  └── Dev 4: Cập nhật badge trạng thái Escrow phía Seller
Thứ 6: Kiểm Thử Tự Động & Đóng Gói Sprint
  ├── Dev 5: Chạy toàn bộ Test Suite (Đạt >= 95% pass rate)
  └── Họp Review & Bàn giao nghiệm thu MF-03
```

---

## ⚠️ PHẦN 4: CÁC QUY TẮC BẤT BIẾN & LƯU Ý SỐNG CÒN (ZERO-TOLERANCE RULES)

### 1. Luật Toàn Vẹn Giao Dịch Tài Chính ([BR-G04](file:///d:/Capstone/docs/03_BUSINESS_RULES.md#L40-L43))
* **Bắt buộc dùng Database Transaction:** Thao tác cập nhật `Listing -> SOLD` và tạo `EscrowTransaction -> LOCKED` phải thực thi trong cùng một `BeginTransactionAsync()`.
* **Quy tắc gRPC sang tên:** Nếu cuộc gọi gRPC `TransferOwnership` sang MockOrganizer gặp sự cố (mạng đứt, timeout, lỗi BTC), transaction phải **Rollback 100%**. Tuyệt đối không để xảy ra trường hợp tiền đã khóa vào Escrow nhưng vé bên BTC chưa đổi chủ.

### 2. Tính Bất Biến Của Webhook Thanh Toán ([BR-E02](file:///d:/Capstone/docs/03_BUSINESS_RULES.md#L61))
* Webhook từ cổng thanh toán VietQR có thể bị bắn lại nhiều lần do cơ chế retry mạng.
* Bắt buộc kiểm tra:
  ```csharp
  if (await _context.EscrowTransactions.AnyAsync(e => e.PaymentReference == dto.TransactionReference))
  {
      return Ok(new { message = "Giao dịch đã được xử lý trước đó (Idempotent OK)" });
  }
  ```

### 3. Không Nuốt Lỗi (No Silent Catch)
* Nghiêm cấm đặt khối lệnh rỗng `catch (Exception) {}` khi gọi gRPC hoặc khi giao tiếp RabbitMQ. Lỗi phải được ném ra để kích hoạt cơ chế hoàn tiền hoặc hủy đơn giữ chỗ.

### 5. Chuẩn Hóa Ngôn Ngữ Giao Diện Người Dùng (User-Facing Copywriting Standard)
* **Tuyệt đối không dùng thuật ngữ kỹ thuật nội bộ trên UI:** Cấm hiển thị các từ khóa kỹ thuật như `gRPC 30s`, `Ký quỹ Escrow T+24h`, `AI Anti-Bot`, `ACID Transaction` cho người dùng cuối (End-User).
* **Bảng chuyển đổi ngôn ngữ chuẩn (Technical -> User-Centric):**
  - `gRPC 30s / Sang tên vé` $\rightarrow$ **"Cấp vé mới chính chủ 100% từ Ban tổ chức"** / **"Đổi mã QR mới đứng tên bạn tức thì"**.
  - `Ký quỹ Escrow T+24h` $\rightarrow$ **"Bảo hiểm tiền 24h"** / **"Giữ tiền an toàn, nhận vé trước - chuyển tiền sau"**.
  - `AI Anti-Bot` $\rightarrow$ **"Người thật - Vé thật"** / **"Chống phe vé & bot gom vé"**.
  - `Fair-Price Cap` $\rightarrow$ **"Cam kết không thổi giá"** / **"Giá niêm yết chuẩn Ban tổ chức"**.

---

## 🎯 PHẦN 5: TIÊU CHÍ NGHIỆM THU CUỐI SPRINT (DEFINITION OF DONE)

1. [x] Buyer bấm "Mua vé" -> Vé chuyển sang `TRANSACTING`, các user khác không thể bấm mua đè.
2. [x] Quá 10 phút Buyer không thanh toán -> Vé tự động mở lại trạng thái `VERIFIED`.
3. [x] Quét mã thanh toán VietQR giả lập -> Webhook ghi nhận tiền thành công.
4. [x] Bảng `escrow_transactions` sinh 1 bản ghi mới ở trạng thái `LOCKED` với số tiền chính xác.
5. [x] Hệ thống BTC (MockOrganizer) hủy vé cũ và cấp mã vé mới cho Buyer.
6. [x] RabbitMQ nhận được 1 message `IOwnershipTransferredEvent` trong queue (bước đệm cho MF-04).
7. [x] Giao diện Homepage & Marketplace mới hiển thị trực quan, thân thiện, 100% không dùng từ khóa kỹ thuật nội bộ.
8. [x] Bộ Unit & Integration Tests cho MF-03 pass 100%, 0 warning, 0 error.

