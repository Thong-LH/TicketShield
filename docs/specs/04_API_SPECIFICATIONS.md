# TicketShield AI - External API Specifications Draft

Tài liệu đặc tả các giao diện lập trình ứng dụng (RESTful API Contracts) kết nối 3 dịch vụ độc lập trong kiến trúc TicketShield:
1. **TicketShield.Core API** (Port 5000)
2. **MockOrganizer.API** (Port 5001)
3. **AIEngine API** (Port 8000)

---

## 1. MockOrganizer Service (Port 5001 - gRPC :5002 & REST)

Dịch vụ giả lập hệ thống Ban tổ chức phát hành vé sơ cấp. Giao tiếp chính với Core thông qua **gRPC Protocol (`organizer_resale.proto`)**:

### 1.1. gRPC Service: `OrganizerResaleService`

```protobuf
syntax = "proto3";

package organizer.v1;

service OrganizerResaleService {
    // 1. Gửi yêu cầu xác thực vé: BTC gửi OTP qua email chủ vé gốc
    rpc RequestTicketOtp(RequestTicketOtpRequest) returns (RequestTicketOtpResponse);

    // 2. Gửi lại mã OTP qua email chính chủ
    rpc ResendTicketOtp(ResendTicketOtpRequest) returns (ResendTicketOtpResponse);

    // 3. Xác nhận OTP và Khóa vé gốc (LOCKED_FOR_RESALE), trả về thông tin vé gốc
    rpc ConfirmOtpAndLock(ConfirmOtpAndLockRequest) returns (ConfirmOtpAndLockResponse);

    // 4. Tra cứu thông tin khóa vé resale
    rpc GetResaleLock(GetResaleLockRequest) returns (GetResaleLockResponse);

    // 5. Mở khóa vé gốc về trạng thái VALID khi Seller hủy tin bán
    rpc ReleaseTicketLock(ReleaseTicketLockRequest) returns (ReleaseTicketLockResponse);

    // 6. Chuyển quyền sở hữu sau thanh toán: Hủy vé cũ của Seller, cấp vé mới 100% cho Buyer
    rpc TransferOwnership(TransferOwnershipRequest) returns (TransferOwnershipResponse);
}
```

- **`RequestTicketOtp`:**
  - Nhận: `TicketReference` (`organizer_id`, `ticket_code`), `OperationContext`.
  - Xử lý: Kiểm tra vé tồn tại (`VALID`) trong `organizer_db`. Sinh mã OTP 6 số ngẫu nhiên, lưu hash với hạn 5 phút và gửi email thực tế qua SMTP đến hòm thư chính chủ của vé.
  - Trả về: `ChallengeView` (`challenge_id`, `expires_at`, `resend_after`).
- **`ConfirmOtpAndLock`:**
  - Nhận: `challenge_id`, `expected_generation`, `otp`.
  - Xử lý: Đối soát OTP. Nếu hợp lệ, chuyển trạng thái vé gốc bên BTC thành **`LOCKED_FOR_RESALE`** (ngăn chặn tái sử dụng hoặc quét vào cổng).
  - Trả về: `VerificationReceipt` chứa thông tin vé chính thức từ BTC: `original_price` (VNĐ), `seat_zone`, `external_event_id`, `external_tier_id` và `ResaleLock`.
- **`ReleaseTicketLock`:**
  - Nhận: `VerificationReference`, `lock_id`.
  - Xử lý: Mở khóa vé gốc bên BTC từ `LOCKED_FOR_RESALE` về lại `VALID`.
- **`TransferOwnership`:**
  - Nhận: `VerificationReference`, `lock_id`, thông tin người nhận mới (`buyer_id`, `buyer_email`, `buyer_name`, `buyer_phone`).
  - Xử lý: **Hủy vé cũ của Seller** và **phát hành vé mới hoàn toàn (mã vé mới, mã QR mới)** cho Buyer.
  - Trả về: `NewTicket` (mã vé mới, hạng vé, thông tin sự kiện).

### 1.2. RESTful Log Tra Cứu Vào Cổng (Đối Soát Tranh Chấp Read-Only)
- **Endpoint:** `GET /api/v1/gate/access-logs?ticketCode={ticketCode}`
- **Mô tả:** Ban tổ chức cung cấp log chỉ đọc phục vụ `DisputeHarvestWorker` và Admin đối soát khi có Dispute.
- **Response (200 OK):**
```json
{
  "success": true,
  "data": [
    {
      "id": 1,
      "ticketCode": "ATSH-VIP-888",
      "scannedAt": "2026-09-15T18:30:15Z",
      "gateName": "Cổng A - Khán đài VIP",
      "scannerDeviceId": "GATE-DEV-01",
      "scanResult": "VALID_ENTRY"
    }
  ]
}
```

---

## 2. AIEngine API (FastAPI - Port 8000)

Dịch vụ AI phân tích hành vi và chấm điểm rủi ro bot.

### 2.1. Đánh Giá Hành Vi Người Dùng
- **Endpoint:** `POST /api/v1/bot-detection/evaluate`
- **Request Body:**
```json
{
  "session_id": "sess_89201928_abc",
  "user_id": "usr_991823",
  "mouse_movement_count": 48,
  "mouse_curvature_entropy": 2.45,
  "keystroke_intervals_std_dev_ms": 38.5,
  "time_on_page_seconds": 12.8,
  "requests_per_second": 0.8,
  "is_headless_browser": false,
  "device_fingerprint_hash": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
}
```
- **Response (200 OK):**
```json
{
  "session_id": "sess_89201928_abc",
  "risk_score": 0.12,
  "risk_level": "LOW",
  "outcome": "ALLOW",
  "confidence": 0.94,
  "reasons": [
    "Natural human interaction patterns validated"
  ]
}
```

---

## 3. TicketShield.Core API (Port 5000)

Dịch vụ lõi xử lý sàn chuyển nhượng vé P2P theo Clean Architecture và CQRS MediatR.

### 3.1. Quản Lý Xác Thực Vé gRPC (`TicketVerificationsController`)
Chuyên trách điều phối vòng đời xác thực chính chủ và khóa vé gRPC với Ban Tổ Chức (`/api/v1/ticket-verifications`):
- **`POST /api/v1/ticket-verifications`:** Khởi tạo phiên xác thực vé.
  - Headers: `Idempotency-Key: <UUID>`
  - Request Body:
  ```json
  {
    "ticketCode": "ATSH-VIP-888",
    "organizerId": "org-mock-01"
  }
  ```
- **`POST /api/v1/ticket-verifications/{id}/resend`:** Yêu cầu BTC gửi lại mã OTP (cooldown 60s).
- **`POST /api/v1/ticket-verifications/{id}/confirm`:** Nhập OTP xác thực và yêu cầu BTC khóa vé (`LOCKED_FOR_RESALE`).
  - Request Body:
  ```json
  {
    "otp": "123456"
  }
  ```
- **`GET /api/v1/ticket-verifications/{id}`:** Tra cứu trạng thái phiên xác thực (nhận thông tin `originalPrice`, `markupPercent`, `priceCeiling`).
- **`POST /api/v1/ticket-verifications/{id}/close`:** Đóng / hủy phiên xác thực chưa niêm yết và mở khóa vé gốc bên BTC về `VALID`.

---

### 3.2. Quản Lý Tin Đăng Bán Lại (`ResaleListingsController`)

#### A. Đăng Bán Vé Đơn Lẻ (`POST /api/v1/resale-listings`)
- Người bán **chỉ gửi `verificationId`** đã được xác thực thành công qua OTP, `resalePrice` và `isPrivate`. Toàn bộ thông tin sự kiện, hạng vé, khán đài và giá gốc do hệ thống tự lấy từ biên lai `VerificationReceipt` của BTC.
- Headers: `Idempotency-Key: <UUID>`
- Request Body:
```json
{
  "verificationId": "550e8400-e29b-41d4-a716-446655440000",
  "resalePrice": 1400000,
  "isPrivate": false
}
```
- Response (200 OK / 202 Accepted):
```json
{
  "success": true,
  "data": {
    "verificationId": "550e8400-e29b-41d4-a716-446655440000",
    "status": "Published",
    "listingId": "c0a80123-7b49-490e-bf65-16e734079475",
    "priceCeiling": 1500000,
    "markupPercent": 0.0,
    "privateAccessToken": null
  }
}
```

#### B. Đăng Bán Nhiều Vé / Combo Bundle (`POST /api/v1/resale-listings/bulk`)
- Hỗ trợ đăng bán danh sách N vé thành 1 bundle với 1 Root `Idempotency-Key` duy nhất, thực thi nguyên tử trong cùng 1 Database Transaction.
- Request Body:
```json
{
  "items": [
    {
      "verificationId": "550e8400-e29b-41d4-a716-446655440001",
      "resalePrice": 1200000,
      "isPrivate": false
    },
    {
      "verificationId": "550e8400-e29b-41d4-a716-446655440002",
      "resalePrice": 1200000,
      "isPrivate": false
    }
  ],
  "allOrNothing": true
}
```

#### C. Xem Chợ Vé Công Khai & Chi Tiết Tin Đăng
- **`GET /api/v1/resale-listings?page=1&size=20&keyword=concert&organizerId={orgId}`:** Xem chợ vé (tự động loại bỏ vé `isPrivate = true` và các vé sự kiện sắp diễn ra trong vòng 2 tiếng theo luật BR-L04).
- **`GET /api/v1/resale-listings/{id}?token={privateAccessToken}`:** Xem chi tiết tin đăng (bắt buộc truyền token nếu vé là Private).
- **`GET /api/v1/resale-listings/private/{shareToken}`:** Xem tin đăng bán kín qua đường dẫn chia sẻ trực tiếp.
- **`GET /api/v1/resale-listings/my-listings`:** Xem danh sách vé đang bán của người bán hiện tại.
- **`POST /api/v1/resale-listings/{id}/cancel`:** Người bán hủy niêm yết vé (chỉ khi vé chưa có ai thanh toán), tự động mở khóa vé bên BTC về `VALID`.

---

### 3.3. Giữ Chỗ Mua Vé, Thanh Toán & Giải Ngân Ký Quỹ

#### A. Khóa Giữ Chỗ Mua Vé 10 Phút (`POST /api/v1/resale-listings/{id}/hold`)
- Request Body:
```json
{
  "recipientName": "Nguyễn Văn A",
  "recipientEmail": "nguyenvana@gmail.com",
  "recipientIdCard": "079201009988",
  "privateAccessToken": null
}
```
- Response (200 OK): Trả về mã VietQR động, chuỗi tham chiếu thanh toán độc nhất và hạn chót giữ chỗ (`expiresAt` sau 10 phút).
- **`POST /api/v1/resale-listings/{id}/release-hold`:** Người mua chủ động hủy giữ chỗ sớm để nhả vé về lại trạng thái `VERIFIED`.
- **`GET /api/v1/resale-listings/{id}/payment-status`:** Frontend polling kiểm tra trạng thái thanh toán VietQR.

#### B. Webhook Thanh Toán Ngân Hàng (`POST /api/v1/payments/sepay/webhook`)
- Nhận Webhook SePay/VietQR: Chuyển tiền vào Quỹ Ký quỹ Escrow (`LOCKED`), tự động gọi gRPC `TransferOwnership` sang MockOrganizer để hủy vé cũ của Seller và cấp mã vé mới chính chủ đứng tên Buyer.

#### C. Quản Lý Tài Khoản Ngân Hàng & Giải Ngân
- **`POST /api/v1/user-bank-accounts`:** Liên kết tài khoản ngân hàng thụ hưởng của Seller.
- Tiến trình `AutomaticSettlementWorker` tự động quét và giải ngân Payout cho Seller theo 2 điều kiện:
  1. Hết 24h đối soát sau khi sang tên chính chủ (Kịch bản A) mà không có tranh chấp.
  2. Ngay khi vé ghi nhận quét cổng thành công (`ticket_status == 'USED'`) tại sự kiện (Kịch bản B).

---

### 3.4. Quản Lý Khiếu Nại Tranh Chấp Cổng (`DisputesController`)

#### A. Tạo Báo Cáo Khiếu Nại (`POST /api/v1/disputes`)
- Endpoint mở cho Người mua khi gặp trục trặc tại cổng soát vé:
```json
{
  "escrowId": "f1b80123-1111-490e-bf65-16e734079475",
  "reason": "Máy quét tại Cổng A không nhận diện được mã vé mới",
  "reasonCode": 0
}
```
*(reasonCode: 0: TicketInvalid, 1: DuplicateEntry)*
- Ngay khi tạo thành công: Lệnh Escrow lập tức chuyển sang `DISPUTED` (đóng băng giải ngân tự động), Core sinh Outbox Message `DisputeHarvestRequested`.

#### B. Tải Lên Tệp Bằng Chứng (`POST /api/v1/disputes/{id}/evidence`)
- Upload file ảnh chụp màn hình / ảnh máy quét lỗi (Content-Type: `multipart/form-data`, file tối đa 6MB).

#### C. Lấy Đề Xuất Phân Xử Tự Động (`GET /api/v1/disputes/{id}/recommendation`)
- Dành cho Quản trị viên / CSKH:
- Trả về khuyến nghị tự động do `DisputeHarvestWorker` phân tích từ nhật ký quét cổng:
```json
{
  "success": true,
  "data": {
    "disputeId": "e5b80123-2222-490e-bf65-16e734079475",
    "recommendation": "RECOMMEND_REJECT_FRAUD",
    "reason": "Vé mới đã được quét thành công tại Cổng A lúc 18:35:10, sau thời điểm chuyển nhượng.",
    "gateScansCount": 1
  }
}
```

#### D. Admin Phán Quyết Dứt Điểm Tranh Chấp (`POST /api/v1/disputes/{id}/resolution`)
- Admin / CSKH bấm phán quyết:
```json
{
  "disputeId": "e5b80123-2222-490e-bf65-16e734079475",
  "resolution": 1,
  "adminNote": "Nhật ký cổng xác nhận Buyer đã quét vé vào xem thành công lúc 18:35. Bác bỏ khiếu nại gian dối và giải ngân cho Seller."
}
```
*(resolution: 0: RefundBuyer - Hoàn 100% tiền cho Buyer; 1: ReleaseSeller - Giải ngân tiền cho Seller)*
