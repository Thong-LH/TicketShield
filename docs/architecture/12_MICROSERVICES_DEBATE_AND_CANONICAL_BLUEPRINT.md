# TỔNG KẾT TRANH BIỆN KIẾN TRÚC & BẢN THIẾT KẾ MICROSERVICES CHUẨN MỰC
> **Dự án:** TicketShield AI  
> **Ngày lập:** 16/09/2026  
> **Trạng thái:** Tài liệu nội bộ (Local Architecture Blueprint) — Đã chốt sau phiên phản biện kỹ thuật chuyên sâu  
> **Định hướng chiến lược:** Làm bài bản, chuẩn công nghiệp 100%, không chắp vá, sẵn sàng bảo vệ đồ án xuất sắc.

---

## PHẦN 1: TỔNG KẾT PHIÊN TRANH BIỆN KIẾN TRÚC (DEBATE SUMMARY)

Cuộc tranh luận xoay quanh việc lựa chọn giữa **"Làm nhanh để kịp tiến độ (Pragmatic / Modular Monolith)"** và **"Xây dựng Microservices thực thụ, chuẩn công nghiệp (True Microservices)"**.

### 1. Vấn đề `CoreResaleStore` (JSONB)
- **Luận điểm ban đầu:** Cho rằng JSONB là nợ kỹ thuật, đòi đập bỏ để thay bằng bảng quan hệ `ticket_verification_sessions`.
- **Phản biện & Thừa nhận (Thống nhất):** 
  - `CoreResaleStore` chứa toàn bộ State Machine, Idempotency keys (`op:actor:kind:id`), Fencing generation tokens chống race condition với gRPC bên BTC, và hàng đợi của `ResaleRecoveryWorker` chạy 15s/lần.
  - Đây là workload Key-Value với value đa hình dạng (IMessage protobuf payload).
  - **Quyết định:** Giữ nguyên bảng `core_resale_records` (JSONB) nhưng đưa `DbSet<CoreResaleRow>` vào chung `TicketShieldDbContext`. Toàn bộ luồng Publish vé sẽ gom về **chính xác 1 Transaction duy nhất** (`BeginTransactionAsync`), xóa bỏ 100% raw SQL chèn vé và triệt tiêu nguy cơ Desync.

### 2. Vấn đề Đồng bộ User & Ràng buộc Khóa Ngoại
- **Luận điểm ban đầu:** Đề xuất denormalize tên và email từ JWT Claims để bỏ luôn Event Bus và Read-model.
- **Phản biện kỹ thuật:**
  - JWT không mang thông tin nhạy cảm như Số tài khoản ngân hàng (Payout ở MF-04).
  - JWT có hạn 24h: Nếu user bị khóa tài khoản (`is_active = false`) thì Trading Core không thể phát hiện được nếu thiếu bản ghi trạng thái.
- **Giải pháp chuẩn hóa được chốt:**
  - Bỏ `FOREIGN KEY` cứng ở `trading_db`, chuyển `seller_id`, `buyer_id` thành UUID thuần túy.
  - Tạo bảng bóng `shadow_users` (`id`, `full_name`, `email`, `phone_number`) tại `trading_db` để phục vụ query Marketplace (tránh N+1 call sang Identity, nâng cao tính chịu lỗi Resilience: Identity sập thì Marketplace vẫn hiển thị bình thường).
  - Rút ngắn thời hạn Access Token xuống **15 phút** + cấp **Refresh Token** (kiểm tra `is_active` mỗi lần cấp mới).
  - Đối với nghiệp vụ Payout (MF-04): Trading Core gọi API đồng bộ sang Identity lấy thông tin ngân hàng ngay tại thời điểm chi tiền (chính xác, bảo mật PII, không lưu thừa).

### 3. Vấn đề Transactional Outbox (Trụ cột còn thiếu)
- **Vấn đề nhận diện:** Nếu lưu User vào `identity_db` xong mới bắn Event lên RabbitMQ, khi RabbitMQ gặp sự cố thì Event mất vĩnh viễn $\rightarrow$ `shadow_users` không bao giờ nhận được user đó.
- **Giải pháp chuẩn:** Bắt buộc áp dụng **Transactional Outbox Pattern** (tích hợp sẵn trong MassTransit EF Core Outbox): Lưu User và ghi bản tin Outbox vào DB trong cùng 1 Transaction, sau đó Outbox Publisher tự động đẩy lên RabbitMQ.

### 4. Vấn đề API Gateway & Cổng kết nối
- Cổng **5000**: Gán cho **`TicketShield.Gateway`** (YARP Reverse Proxy). Frontend chỉ nói chuyện với cổng 5000, không phải sửa bất kỳ dòng code nào.
- Cổng **5001**: **`MockOrganizer.API`** (Hệ thống đối tác BTC).
- Cổng **5002**: **`TicketShield.Identity.API`** (Identity Service độc lập).
- Cổng **5003**: **`TicketShield.TradingCore`** (Core API nội bộ phía sau Gateway).

---

## PHẦN 2: BẢN VẼ KIẾN TRÚC MỤC TIÊU (CANONICAL ARCHITECTURE)

```
                       [ Frontend Web App ]
                                 │
                                 ▼ (Cổng 5000 - Không đổi)
        ┌──────────────────────────────────────────────────┐
        │          TicketShield.Gateway (YARP)             │
        └────────────┬────────────────────────┬────────────┘
                     │ /api/v1/auth/**        │ /api/v1/** (còn lại)
                     ▼                        ▼
     ┌──────────────────────────────┐  ┌──────────────────────────────┐
     │    TicketShield.Identity     │  │   TicketShield.TradingCore   │
     │      (Port 5002 - .NET 8)    │  │     (Port 5003 - .NET 8)     │
     ├──────────────────────────────┤  ├──────────────────────────────┤
     │ CSDL: identity_db            │  │ CSDL: trading_db             │
     │ Bảng:                        │  │ Bảng:                        │
     │ - users                      │  │ - shadow_users (Bản sao đọc) │
     │ - user_bank_accounts         │  │ - events, ticket_tiers       │
     │ - refresh_tokens             │  │ - resale_listings (Bỏ FK)    │
     │ - outbox_messages (Outbox)   │  │ - escrow_transactions        │
     └──────────────┬───────────────┘  │ - core_resale_records (JSONB)│
                    │                  └──────────────▲───────────────┘
                    │                                 │
                    ▼ (MassTransit Outbox)            │ (Idempotent Upsert)
            [ RabbitMQ Bus ] ─────────────────────────┘
              - UserRegisteredEvent
              - UserProfileUpdatedEvent
```

---

## PHẦN 3: LỘ TRÌNH THỰC THI CHI TIẾT (STRANGLER FIG ROADMAP)

Để đảm bảo **toàn bộ hệ thống luôn biên dịch thành công (Compile Green) và toàn bộ 80/80 tests luôn pass 100% qua từng commit**, các giai đoạn được sắp xếp tuần tự theo mẫu thiết kế Strangler Fig:

### 🟢 GIAI ĐOẠN 1: Dọn Nợ Kỹ Thuật & Thống Nhất Transaction trong Core (1 ngày)
- **1.1:** Đưa `DbSet<CoreResaleRow> CoreResaleRecords` vào [TicketShieldDbContext.cs](file:///d:/Capstone/src/TicketShield.Core/TicketShield.Infrastructure/Persistence/TicketShieldDbContext.cs) với mapping tường minh (`core_resale_records`, jsonb). Kiểm tra migration rỗng để không làm hỏng cấu trúc bảng cũ.
- **1.2:** Inject `ITicketShieldDbContext` vào [TicketVerificationService.cs](file:///d:/Capstone/src/TicketShield.Core/TicketShield.Infrastructure/Services/TicketVerificationService.cs).
- **1.3:** Sửa hàm `Publish`: Bọc toàn bộ thao tác lưu session và chèn vé vào **1 Transaction duy nhất** (`BeginTransactionAsync`).
- **1.4:** Xóa bỏ 100% raw SQL `_db.Database.ExecuteSqlInterpolatedAsync`, thay bằng EF Core entity `ResaleListing` chuẩn type-safe. Chạy 80/80 unit tests xác minh.

### 🟢 GIAI ĐOẠN 2: Xây Dựng YARP API Gateway & Cấu Hình Phân Luồng Cổng (1 ngày)
- **2.1:** Tạo project mới `TicketShield.Gateway` (.NET 8 Web API), cài đặt thư viện chính hãng `Yarp.ReverseProxy`.
- **2.2:** Cấu hình Kestrel cho Gateway chiếm giữ cổng **5000**.
- **2.3:** Đổi cổng của Trading Core (`TicketShield.API`) từ 5000 sang cổng nội bộ **5003** trong [launchSettings.json](file:///d:/Capstone/src/TicketShield.Core/TicketShield.API/Properties/launchSettings.json).
- **2.4:** Cấu hình routing rules trong `appsettings.json` của Gateway:
  - Route Auth: `/api/v1/auth/{**catch-all}` $\rightarrow$ Cụm đích `http://localhost:5002`.
  - Route Core: `/api/v1/{**catch-all}` $\rightarrow$ Cụm đích `http://localhost:5003`.
- **2.5:** Gom toàn bộ cấu hình CORS về Gateway ("AllowFrontend" cho port 3000, 5173). Tắt CORS ở các service con. Xác nhận Frontend đăng nhập và gọi API mượt mà qua cổng 5000.

### 🟢 GIAI ĐOẠN 3: Tạo Bảng Shadow Users & Chuyển Đổi Navigation trong Core (1 ngày)
*(Thực hiện trước khi tách Identity để repo không bao giờ bị đỏ compile)*
- **3.1:** Tạo entity `ShadowUser` (`Id`, `FullName`, `Email`, `PhoneNumber`, `UpdatedAt`) trong Core Domain.
- **3.2:** Bỏ ràng buộc `FOREIGN KEY` cứng `REFERENCES users(id)` ở các bảng `resale_listings`, `escrow_transactions`, `disputes`. Giữ nguyên toàn bộ Index phục vụ truy vấn tốc độ cao.
- **3.3:** Viết migration tạo bảng `shadow_users` và chạy script Backfill sao chép toàn bộ user hiện có từ `users` sang `shadow_users`.
- **3.4:** Đổi navigation property của `ResaleListing.Seller` trỏ sang `ShadowUser`. Cập nhật [GetMarketplaceListingsQueryHandler.cs](file:///d:/Capstone/src/TicketShield.Core/TicketShield.Application/Features/ResaleListings/Queries/GetMarketplaceListings/GetMarketplaceListingsQueryHandler.cs) xác nhận Marketplace hiển thị trơn tru 100%.

### 🟢 GIAI ĐOẠN 4: Bóc Tách Hoàn Chỉnh Cụm Microservice Identity (2 ngày)
- **4.1:** Tạo 3 project độc lập cho Identity:
  - `TicketShield.Identity.Domain`: Entity `User`, `UserBankAccount`, `RefreshToken`, enum `UserRole`.
  - `TicketShield.Identity.Application`: Commands/Queries Auth, DTOs, interface Token Generator.
  - `TicketShield.Identity.Infrastructure`: `IdentityDbContext` (chỉ quản lý 3 bảng trên), BCrypt, Google OAuth, Email OTP.
- **4.2:** Tách kết nối sang cơ sở dữ liệu vật lý riêng biệt **`identity_db`** (cấu hình trong Docker và connection string).
- **4.3:** Cài đặt cơ chế an ninh chuẩn:
  - Rút ngắn hạn Access Token xuống **15 phút**.
  - Bổ sung bảng `refresh_tokens` và endpoint `POST /api/v1/auth/refresh-token` (kiểm tra trạng thái `is_active` khi cấp mới token).
- **4.4:** Gỡ bỏ toàn bộ `ProjectReference` từ Identity sang Core. Xóa vĩnh viễn thư mục `Features/Auth/` và entity `User` khỏi Core.

### 🟢 GIAI ĐOẠN 5: Tích Hợp Transactional Outbox & RabbitMQ Idempotent Sync (1–2 ngày)
- **5.1:** Cấu hình `MassTransit EF Core Outbox` trong `TicketShield.Identity.Infrastructure` trên `IdentityDbContext`.
- **5.2:** Trong `RegisterCommandHandler` và `UpdateProfileCommandHandler`: Publish event `UserRegisteredEvent` / `UserProfileUpdatedEvent` vào Outbox trong cùng Transaction lưu User.
- **5.3:** Viết Consumer tại Core: `UserRegisteredConsumer`, lắng nghe Event từ RabbitMQ và thực thi **Upsert Idempotent** (nếu chưa có thì Add, nếu có rồi thì Update) vào bảng `shadow_users`.
- **5.4:** Viết Integration Test kiểm tra luồng: Đăng ký user mới tại Identity (5002) $\rightarrow$ Gateway chuyển tiếp $\rightarrow$ Outbox đẩy qua RabbitMQ $\rightarrow$ Core nhận tin $\rightarrow$ User lập tức có trong `shadow_users` $\rightarrow$ Đăng bán vé và hiện tên ngay lập tức.

---

## PHẦN 4: TIÊU CHÍ HOÀN THÀNH (DEFINITION OF DONE - DOD)

1. **Độc lập:** `Identity.API` và `TradingCore` sở hữu 2 database riêng biệt (`identity_db` và `trading_db`), deploy độc lập, không dùng chung bất kỳ bảng hay DLL nghiệp vụ nào.
2. **Liền mạch:** Frontend giữ nguyên URL cổng 5000, người dùng thực hiện toàn bộ luồng Auth và Trading bình thường không gặp bất kỳ lỗi 404 hay CORS nào.
3. **An toàn dữ liệu:** Không còn một dòng raw SQL nào trong luồng Publish vé. Publish và Session cùng 1 Transaction, triệt tiêu 100% rủi ro Desync.
4. **Không mất Event:** Áp dụng Transactional Outbox, RabbitMQ sập bật lại vẫn tự động đồng bộ đầy đủ dữ liệu.
5. **Kiểm thử:** Toàn bộ Unit Tests và Integration Tests đạt trạng thái xanh 100%.
