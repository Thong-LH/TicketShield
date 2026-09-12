# TICKETSHIELD - ĐỊNH HƯỚNG KIẾN TRÚC MỤC TIÊU & LỘ TRÌNH PHÂN TÁCH DỊCH VỤ
> **Tài liệu:** Thiết kế Kiến trúc Hệ thống Phân tán (Distributed Architecture Blueprint)  
> **Áp dụng cho:** Toàn bộ hệ sinh thái TicketShield (Sprint MF-02 trở đi)  
> **Cơ chế giao tiếp:** REST API (Client) + gRPC (Ban tổ chức) + RabbitMQ (Xử lý bất đồng bộ)  
> **Mô hình mã nguồn:** Monorepo (1 Solution, Đóng gói & Deploy độc lập)

---

## 1. Bản chất & Ranh giới giao dịch (Transaction Boundaries)

### 1.1. Nguyên tắc cốt lõi
> **"What changes together, stays together."** (Những thành phần bắt buộc phải đảm bảo tính toàn vẹn giao dịch ACID thì phải nằm chung một cơ sở dữ liệu).

### 1.2. Khối Lõi Giao Dịch (Trading Core) — Bắt buộc ở chung
- **Resale Listing:** Trạng thái vé (`Verified`, `Reserved`, `Sold`).
- **Order / Checkout:** Giữ chỗ 10 phút, đối soát Webhook VietQR.
- **Escrow (Ký quỹ):** Đóng băng tiền khi Buyer thanh toán.
- *Lý do:* Khi thanh toán thành công, hệ thống phải đồng thời đổi `Escrow = LOCKED` và `Listing = SOLD` trong **1 Database Transaction cục bộ duy nhất** để triệt tiêu 100% rủi ro tiền vào mà vé chưa khóa.

### 1.3. Các thành phần được bóc tách độc lập
1. **Identity Service:** Chỉ cấp phát JWT token, các service khác chỉ cần giải mã token, không phụ thuộc DB.
2. **Settlement Worker:** Hoạt động sau 24 tiếng (bất đồng bộ), không can thiệp luồng mua vé trực tiếp.
3. **MockOrganizer.API:** Hệ thống của đối tác Ban tổ chức, giao tiếp qua gRPC.
4. **AIEngine:** Hệ thống chấm điểm bot, giao tiếp qua REST API.

---

## 2. Danh mục 5 Backend Services & Trách nhiệm

| # | Service | Công nghệ | Giao thức / Port | Trách nhiệm chính | Cơ sở dữ liệu |
|---|---|---|---|---|---|
| **1** | **`TicketShield.Identity`** | .NET 8 Web API | REST (`:5002`) | Đăng ký, đăng nhập, Google OAuth, cấp phát JWT Bearer token | `identity_db` (`Users`, `UserBankAccounts`) |
| **2** | **`TicketShield.TradingCore`** | .NET 8 Web API | REST (`:5000`) | Chợ vé, duyệt giá trần, Ký quỹ Escrow, gRPC Client với BTC | `trading_db` (`resale_listings`, `escrow_transactions`...) |
| **3** | **`TicketShield.SettlementWorker`** | .NET 8 Worker | Daemon ngầm | Nhận Event qua RabbitMQ, đếm ngược T+24h, gọi NAPAS giải ngân | Đọc/Ghi `trading_db` |
| **4** | **`MockOrganizer.API`** | .NET 8 gRPC | gRPC (`:5001`) | Ban tổ chức: Quản lý vé gốc, gửi OTP qua SMTP, khóa & cấp vé mới | `organizer_db` |
| **5** | **`AIEngine`** | Python FastAPI | REST (`:8000`) | Thu thập telemetry chuột/phím, chấm điểm chống bot săn vé | SQLite / In-memory |

---

## 3. Luồng Sự Kiện Thời Gian Thực & Giải Ngân (Event-Driven Flow)

```
[ Buyer quét VietQR thành công ]
              │
              ▼ (Bắn Event Real-Time tức thì — 0 giây)
┌────────────────────────────────────────────────────────┐
│  Trading Core API:                                     │
│  1. Đóng băng tiền vào Escrow (Status: LOCKED)         │
│  2. Gọi gRPC sang BTC: Hủy vé cũ, cấp vé mới cho Buyer │
│  3. Listing chuyển thành SOLD                          │
│  4. Bắn SignalR báo Web Buyer: Sang tên thành công     │
│  5. Bắn Event lên RabbitMQ: OwnershipTransferredEvent  │
└──────────────────────────┬─────────────────────────────┘
                           │ (AMQP Message Broker)
                           ▼
┌────────────────────────────────────────────────────────┐
│  Settlement Worker:                                    │
│  1. Lắng nghe Consumer bắt OwnershipTransferredEvent   │
│  2. Lên lịch đếm ngược đúng T+24h                      │
│  3. Hết 24h: Kiểm tra has_dispute == false             │
│  4. Gọi NAPAS 247 chuyển tiền về STK của Seller        │
│  5. Đổi trạng thái Escrow -> RELEASED                  │
└────────────────────────────────────────────────────────┘
```

---

## 4. Mô hình Monorepo & Cơ chế Deploy Độc Lập

### 4.1. Cấu trúc thư mục Monorepo trong 1 Solution duy nhất
```
d:\Capstone\
├── TicketShield.sln                     ← Solution quản lý toàn bộ hệ sinh thái
├── docker-compose.yml                   ← PostgreSQL + RabbitMQ
│
├── src/
│   ├── TicketShield.Contracts/          ← Thư viện DÙNG CHUNG (Protos, Event Interfaces)
│   ├── TicketShield.Identity/           ← Service 1: Auth & User API
│   │   ├── TicketShield.Identity.API/
│   │   └── Dockerfile
│   ├── TicketShield.Core/               ← Service 2: Trading Core & Escrow API
│   │   ├── TicketShield.Domain/
│   │   ├── TicketShield.Application/
│   │   ├── TicketShield.Infrastructure/
│   │   ├── TicketShield.API/
│   │   └── Dockerfile
│   ├── TicketShield.SettlementWorker/   ← Service 3: Tiến trình giải ngân ngầm
│   │   └── Dockerfile
│   └── MockOrganizer/                   ← Service 4: Hệ thống Ban tổ chức giả lập
│       ├── MockOrganizer.API/
│       └── Dockerfile
```

### 4.2. Cơ chế Đóng gói & Deploy độc lập (Không dính chùm)
1. **Build độc lập:** `dotnet publish <đường_dẫn_csproj>` chỉ gom đúng DLL phụ thuộc của service đó, hoàn toàn không dính code service khác.
2. **Container độc lập:** Mỗi service có Dockerfile riêng, sinh ra các Docker Image riêng biệt (`ticketshield-identity:v1`, `ticketshield-core:v1`, `ticketshield-worker:v1`).
3. **CI/CD tự động:** Dùng `paths` filter trên GitHub Actions. Sửa code ở service nào, CI/CD chỉ build và deploy đúng container của service đó, không làm gián đoạn các service còn lại.

---

## 5. Lộ trình 5 bước triển khai kỹ thuật

1. **Bước 1:** Khởi tạo project `TicketShield.Identity.API` (:5002), di chuyển toàn bộ cụm `Auth` và bảng `Users` sang.
2. **Bước 2:** Tinh giản `Trading Core API` (:5000), chỉ giữ middleware xác thực JWT Bearer.
3. **Bước 3:** Thêm RabbitMQ (`rabbitmq:3-management`) vào `docker-compose.yml`, định nghĩa `IOwnershipTransferredEvent` trong `TicketShield.Contracts`.
4. **Bước 4:** Tạo project `TicketShield.SettlementWorker`, cài đặt MassTransit Consumer nhận event và đếm ngược 24h.
5. **Bước 5:** Kiểm thử liên thông End-to-End và cập nhật cấu hình API URL cho Frontend.
