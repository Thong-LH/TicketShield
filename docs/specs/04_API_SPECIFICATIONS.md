# TicketShield AI - External API Specifications Draft

Tài liệu đặc tả các giao diện lập trình ứng dụng (RESTful API Contracts) kết nối 3 dịch vụ độc lập trong kiến trúc TicketShield:
1. **TicketShield.Core API** (Port 5000)
2. **MockOrganizer.API** (Port 5001)
3. **AIEngine API** (Port 8000)

---

## 1. MockOrganizer Service (Port 5001 - gRPC & REST)

Dịch vụ giả lập hệ thống Ban tổ chức phát hành vé sơ cấp. Giao tiếp chính với Core thông qua **gRPC Protocol (`organizer_resale.proto`)**:

### 1.1. gRPC Service: `OrganizerResaleService`

```protobuf
syntax = "proto3";

package organizer.v1;

service OrganizerResaleService {
    // Gửi yêu cầu xác thực vé: MockOrganizer gửi OTP qua email chủ vé gốc
    rpc RequestTicketOtp(RequestTicketOtpRequest) returns (RequestTicketOtpResponse);

    // Xác nhận mã OTP và Khóa vé gốc (LOCKED_FOR_RESALE)
    rpc ConfirmTicketOtp(ConfirmTicketOtpRequest) returns (ConfirmTicketOtpResponse);

    // Mở khóa vé gốc về trạng thái VALID khi Seller hủy tin bán
    rpc ReleaseTicketLock(ReleaseTicketLockRequest) returns (ReleaseTicketLockResponse);
}
```

- **`RequestTicketOtp`:**
  - Nhận: `ticket_code`, `organizer_id`.
  - Xử lý: Kiểm tra vé tồn tại và hợp lệ trong `organizer_db`. Sinh mã OTP 6 số ngẫu nhiên, lưu hash với hạn 5 phút và gửi email thực tế qua SMTP.
  - Trả về: `session_id`, `ticket_code`, `original_price` (VNĐ), `status` ("PENDING_OTP").
- **`ConfirmTicketOtp`:**
  - Nhận: `session_id`, `otp_code`.
  - Xử lý: Đối soát OTP. Nếu hợp lệ, chuyển trạng thái vé gốc bên BTC thành **`LOCKED_FOR_RESALE`** (ngăn chặn tái sử dụng hoặc quét vào cổng).
  - Trả về: `session_id`, `ticket_code`, `original_price`, `status` ("LOCKED_FOR_RESALE").
- **`ReleaseTicketLock`:**
  - Nhận: `session_id`, `ticket_code`.
  - Xử lý: Mở khóa vé gốc bên BTC từ `LOCKED_FOR_RESALE` về lại `VALID`.
  - Trả về: `ticket_code`, `status` ("RELEASED").

### 1.2. RESTful Log Tra Cứu Vào Cổng (Read-Only Đối Soát Tranh Chấp)
- **Endpoint:** `GET /api/v1/gate/access-logs?ticketCode={ticketCode}`
- **Mô tả:** Ban tổ chức cung cấp log chỉ đọc phục vụ TicketShield Admin đối soát khi có Dispute.
- **Response (200 OK):**
```json
{
  "success": true,
  "data": [
    {
      "id": 1,
      "ticketCode": "TICK-VN-2026-9901",
      "scannedAt": "2026-09-15T18:30:15Z",
      "gateName": "Cổng A - Khán đài VIP",
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
- **`POST /api/v1/ticket-verifications`:** Khởi tạo yêu cầu xác thực vé (nhận `ticketCode`, header `Idempotency-Key`).
- **`POST /api/v1/ticket-verifications/{id}/resend`:** Gửi lại mã OTP qua email.
- **`POST /api/v1/ticket-verifications/{id}/confirm`:** Nhập OTP xác thực và khóa vé gốc phía BTC.
- **`GET /api/v1/ticket-verifications/{id}`:** Tra cứu trạng thái phiên xác thực.
- **`POST /api/v1/ticket-verifications/{id}/cancel-listing`:** Hủy phiên và mở khóa vé gốc bên BTC về `VALID`.

### 3.2. Quản Lý Tin Đăng Bán Lại (`ResaleListingsController`)
Gom toàn bộ các hành động liên quan đến tin đăng bán trên sàn (`/api/v1/resale-listings`):

#### A. Tạo Tin Đăng Bán Lại (SCRUM-25 / US-2.3)
- **Endpoint:** `POST /api/v1/resale-listings`
- **Quy tắc trần giá:** `ResalePrice <= OriginalPrice` (vi phạm ném `422 Unprocessable Entity`).
- **Chế độ Private:** Nếu `isPrivate = true`, hệ thống tự động sinh `privateAccessToken` ngẫu nhiên 32 ký tự hex.
- **Request Body:**
```json
{
  "eventId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "tierId": "7ca85f64-5717-4562-b3fc-2c963f66afa6",
  "originalTicketCode": "TICK-VN-2026-8899",
  "originalPrice": 1500000,
  "resalePrice": 1400000,
  "isPrivate": true
}
```
- **Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "listingId": "c0a80123-7b49-490e-bf65-16e734079475",
    "isPrivate": true,
    "privateAccessToken": "a1b2c3d4e5f6789012345678abcdef01",
    "shareableLink": "https://ticketshield.vn/p2p/listing/c0a80123-7b49-490e-bf65-16e734079475?token=a1b2c3d4e5f6789012345678abcdef01",
    "status": "VERIFIED"
  }
}
```

#### B. Xem Chi Tiết Vé & Xác Thực Link Riêng Tư (SCRUM-26 / US-2.3)
- **Endpoint:** `GET /api/v1/resale-listings/{id}?token={privateAccessToken}`
- **Ràng buộc:** Nếu vé là Private, thiếu token hoặc sai token sẽ bị từ chối với lỗi `403 Forbidden`.
- **Response (200 OK):** Trả về thông tin chi tiết vé để người mua xem trước (EventName, SeatZone, OriginalPrice, ResalePrice, SellerName).

#### C. Xem Chợ Vé Công Khai (Marketplace Feed)
- **Endpoint:** `GET /api/v1/resale-listings?page=1&size=20`
- **Ràng buộc:** Tuyệt đối KHÔNG trả về các vé có trạng thái `isPrivate = true`.

### 3.3. Quản Lý Ký Quỹ & Hai Kênh Giải Ngân Payout (MF-03 & MF-04)
- **Khóa giữ chỗ thanh toán:** `POST /api/v1/resale-listings/{id}/checkout` $\rightarrow$ Sinh mã VietQR động, Listing chuyển `TRANSACTING` (khóa 10 phút).
- **Thanh toán thành công:** Webhook ngân hàng $\rightarrow$ Escrow chuyển `LOCKED`, chuyển giao vé mới cho Buyer.
- **Tài khoản ngân hàng thụ hưởng của Seller:**
  - `POST /api/v1/user-bank-accounts`: Liên kết STK ngân hàng thụ hưởng chính chủ.
- **Cơ chế Kích hoạt Giải ngân Payout (T+24h Sau Sang Tên Chính Chủ):**
  - Ngay khi vé hoàn tất thủ tục hủy vé cũ và phát hành vé mới chính chủ đứng tên Buyer bên BTC (`transfer_completed_at`), hệ thống bắt đầu đếm ngược thời gian đệm bảo vệ an toàn **24 giờ (T+24h)**.
  - Trong vòng 24h này, Buyer kiểm tra vé trên app BTC. Nếu không phát sinh khiếu nại (`has_dispute = false`), Background Worker tự động chuyển Escrow sang `RELEASED` và gửi lệnh chuyển khoản Payout về `UserBankAccount` của Seller.
  - **Lưu ý:** Lệnh giải ngân KHÔNG chờ đến lúc check-in cổng hay khi sự kiện kết thúc.


```json
{
  "paymentReference": "TS_c0a80123",
  "amountPaid": 1400000.0,
  "status": "PAID",
  "transactionId": "BANK_TRANS_998129"
}
```
- **Response (200 OK):**
```json
{
  "success": true,
  "message": "Thanh toán đã được ghi nhận. Quyền sở hữu vé đã được chuyển giao cho người mua."
}
```

### 3.4. Mở Khiếu Nại Tranh Chấp (MF-05)
- **Endpoint:** `POST /api/v1/disputes`
- **Request Body:**
```json
{
  "escrowId": "f1b80123-1111-490e-bf65-16e734079475",
  "reason": "Vé bị báo không hợp lệ tại Cổng A",
  "evidenceUrl": "https://storage.ticketshield.vn/evidence/dispute_gate_error.jpg"
}
```
- **Response (201 Created):**
```json
{
  "success": true,
  "message": "Khiếu nại đã được tiếp nhận. Số tiền ký quỹ đã bị đóng băng để Admin đối soát.",
  "data": {
    "disputeId": "e5b80123-2222-490e-bf65-16e734079475",
    "status": "Open",
    "createdAt": "2026-09-08T16:10:00Z"
  }
}
```
