# KẾ HOẠCH BÓC TÁCH KIẾN TRÚC MỤC TIÊU (3 SERVICES + RABBITMQ)
> **Dự án:** TicketShield AI  
> **Tài liệu:** Chuẩn hóa kiến trúc phân rã Monolith thành Multi-Service Bounded Context  
> **Ngày lập:** 12/09/2026

---

## 1. Mục tiêu & Nguyên lý phân tách

Hiện tại `TicketShield.Core` đang là một Monolith gánh 6 Bounded Context khác nhau. Nhằm chuẩn hóa kiến trúc cho các sprint tiếp theo (MF-03 Escrow, MF-04 Settlement), hệ thống được tái cấu trúc thành **3 Services chính thức của TicketShield** + 2 Service vệ tinh ngoại vi.

### Nguyên lý cốt lõi: "What changes together, stays together"
- Các nghiệp vụ cần tính toàn vẹn giao dịch ACID cao (**Đặt mua vé $\leftrightarrow$ Khóa vé $\leftrightarrow$ Ký quỹ Escrow**) bắt buộc phải nằm chung một cơ sở dữ liệu `trading_db` trong `Trading Core API` để tránh bài toán Distributed Transaction phức tạp (không cần 2-Phase Commit).
- Các phân hệ độc lập về mặt dữ liệu (**Identity/Auth** và **Settlement Worker T+24h**) được bóc tách thành các dịch vụ độc lập.

---

## 2. Bản đồ 5 Services của toàn bộ hệ sinh thái

| # | Tên Service | Công nghệ | Cổng / Giao thức | Database | Trách nhiệm chính |
|---|---|---|---|---|---|
| **1** | **`TicketShield.Identity`** | .NET 8 Web API | REST (`:5002`) | `identity_db` | Đăng ký, Đăng nhập, Google OAuth, Cấp phát JWT Bearer Token, Quản lý User Profile & Số tài khoản ngân hàng. |
| **2** | **`TicketShield.TradingCore`** | .NET 8 Web API | REST (`:5000`) | `trading_db` | Chợ vé (Marketplace), Kiểm tra giá trần, Đặt mua & Hold vé 10p, Ký quỹ Escrow (LOCKED), gRPC Client với BTC. |
| **3** | **`TicketShield.SettlementWorker`** | .NET 8 Worker | Daemon ngầm | `settlement_db` (hoặc Redis Scheduler) | Lắng nghe `IOwnershipTransferredEvent` qua RabbitMQ, đếm ngược T+24h, tự động gọi NAPAS 247 giải ngân tiền cho Seller, gọi API nội bộ cập nhật Escrow. |
| **4** | **`MockOrganizer.API`** | .NET 8 gRPC/REST | gRPC (`:5001`) | `organizer_db` | Giả lập hệ thống Ban tổ chức (BTC): Xác thực vé gốc, gửi OTP qua Email (SMTP), Khóa/Mở khóa vé, Cấp vé mới. |
| **5** | **`AIEngine`** | Python FastAPI | REST (`:8000`) | In-memory / ML | Thu thập dữ liệu sinh trắc học hành vi (chuột, phím) để phân loại rủi ro bot mua vé sơ cấp. |

*(Client Web: **TicketShield Web App** viết bằng Next.js/React kết nối đến các service trên).*

---

## 3. Cơ chế giao tiếp thời gian thực & Giải quyết Race Condition (RabbitMQ + Pre-flight Sync)

Để triệt tiêu lỗi **Shared Database Anti-pattern** khi bảo vệ đồ án, `SettlementWorker` **hoàn toàn không truy vấn trực tiếp vào `trading_db`**. Hai service giao tiếp qua **RabbitMQ (MassTransit)** kết hợp cơ chế **Pre-flight Sync Check**:

```
[ Buyer quét VietQR ]
        │
        ▼ (Webhook thanh toán thành công)
[ Trading Core API ] ──(gRPC: Sang tên vé)──► [ MockOrganizer (BTC) ]
        │
        ├──► Bắn Event lên RabbitMQ: `IOwnershipTransferredEvent` (Mang toàn bộ thông tin thanh toán)
        │
        ▼ (RabbitMQ đẩy tức thì)
[ Settlement Worker ]
        ├── 1. Nhận Event, lưu lịch hẹn đếm ngược T + 24 giờ vào Hangfire/Redis
        ├── 2. Hết 24h: GỌI PRE-FLIGHT SYNC CHECK sang TradingCore:
        │      POST /api/v1/internal/escrows/{id}/claim-payout
        │      └── TradingCore khóa atomic: Status = PAYING_OUT (nếu có Dispute -> trả HTTP 409 từ chối)
        ├── 3. Nhận HTTP 200 OK: Kích hoạt lệnh chuyển khoản NAPAS 247 cho Seller
        └── 4. Hoàn tất: Gọi PUT /api/v1/internal/escrows/{id}/release để TradingCore đổi Escrow -> RELEASED
```

### Chốt chặn chống Race Condition lúc 23:59:59:
* `TradingCore` đóng vai trò là **State Orchestrator** quản lý trạng thái Escrow trong `trading_db`.
* Khi Worker hết 24h, bắt buộc phải gọi `claim-payout`. `TradingCore` thực thi `UPDATE ... WHERE status = 'LOCKED'` có khóa dòng (Row-Level Lock) trên PostgreSQL.
* Nếu Buyer bấm Dispute đúng giây cuối $\rightarrow$ `TradingCore` xử lý tuần tự: ai đến trước thì thắng, triệt tiêu 100% rủi ro chuyển tiền khi đã bị hủy.

### Event Contract (`TicketShield.Contracts`):
```csharp
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
}
```

---

## 4. Chiến lược Monorepo & Đóng gói Deploy độc lập

### Tại sao giữ chung 1 Git Repository (Monorepo)?
1. **Dùng chung Contracts:** Cả Core và Worker đều tham chiếu trực tiếp đến `TicketShield.Contracts` (Protobuf, Event Interfaces) qua `ProjectReference`, loại bỏ rủi ro lệch phiên bản.
2. **Kiểm thử liên thông toàn diện:** Chỉ cần 1 lệnh `dotnet test` là kiểm tra được toàn bộ hệ thống.
3. **Quản lý hạ tầng tập trung:** 1 file `docker-compose.yml` duy nhất khởi động cả PostgreSQL và RabbitMQ.

### Cơ chế Deploy độc lập từng Service:
- **Build độc lập:** `dotnet publish src/TicketShield.Identity/...` chỉ đóng gói đúng dll của Identity, không dính code của Core hay Worker.
- **Docker độc lập:** Mỗi service có 1 `Dockerfile` riêng (`ticketshield-identity:latest`, `ticketshield-core:latest`, `ticketshield-worker:latest`).
- **CI/CD GitHub Actions:** Dùng bộ lọc `paths:` để khi commit vào thư mục service nào thì chỉ build và deploy đúng container của service đó.

---

## 5. Kế hoạch triển khai 5 bước

1. **Bước 1 — Tách Identity Service:** Tạo `TicketShield.Identity.API` (:5002), di chuyển `AuthController` và cụm `Features/Auth/`.
2. **Bước 2 — Tinh giản Trading Core API:** Gỡ bỏ Auth logic khỏi Core (:5000), chỉ giữ middleware xác thực JWT Bearer Token.
3. **Bước 3 — Tích hợp RabbitMQ:** Bổ sung container RabbitMQ vào `docker-compose.yml`, cài MassTransit và định nghĩa Event `IOwnershipTransferredEvent`.
4. **Bước 4 — Xây dựng Settlement Worker:** Tạo project `.NET Worker Service`, cài đặt Consumer lắng nghe Event và thực thi logic đếm ngược giải ngân T+24h.
5. **Bước 5 — Kiểm thử liên thông:** Chạy toàn bộ test suite, cập nhật biến môi trường Frontend trỏ đúng 2 cổng API (5002 cho Auth, 5000 cho Core).
