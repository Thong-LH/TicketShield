# BÁO CÁO ĐÁNH GIÁ KIẾN TRÚC & NỢ KỸ THUẬT (ARCHITECTURE AUDIT & TECHNICAL DEBT)
> **Dự án:** TicketShield AI  
> **Thời điểm kiểm tra:** 16/09/2026  
> **Mục tiêu:** Rà soát khách quan toàn bộ hệ thống, chỉ rõ các điểm bất cập, mâu thuẫn giữa thiết kế và thực tế, cùng phương án khắc phục triệt để.

---

## 1. Mâu thuẫn tách Database: Bẫy Foreign Key (`identity_db` vs `trading_db`)
- **Hiện trạng:**
  - Kế hoạch Plan 09 đề xuất tách vật lý `identity_db` và `trading_db`.
  - Tuy nhiên, trong CSDL quan hệ hiện tại, các bảng `resale_listings`, `escrow_transactions`, `disputes` đều có ràng buộc khóa ngoại cứng: `FOREIGN KEY (seller_id/buyer_id) REFERENCES users(id)`.
  - Các câu truy vấn tại Core API (ví dụ `GetMarketplaceListingsQueryHandler`) đang gọi `.Include(l => l.Seller)` để lấy họ tên, số điện thoại người bán.
- **Bất cập & Rủi ro:**
  - PostgreSQL không hỗ trợ Foreign Key xuyên cơ sở dữ liệu.
  - Nếu tách `identity_db` thật, khi người dùng đăng ký ở Identity API và sang Core API đăng bán vé, lệnh lưu vào `resale_listings` sẽ lập tức crash do lỗi vi phạm khóa ngoại (FK Violation `23503`) vì bảng `users` không có dữ liệu trong `trading_db`.
  - Nếu cả 2 service cùng trỏ vào `ticketshield_db`, hệ thống thực chất chỉ là chạy 2 Web API trên 2 cổng khác nhau nhưng dùng chung CSDL (**Shared Database Anti-Pattern**).
- **Phương án giải quyết chuẩn Microservices:**
  1. Xóa bỏ ràng buộc `FOREIGN KEY` vật lý ở tầng PostgreSQL trong `trading_db`; `seller_id` và `buyer_id` chỉ lưu dưới dạng `UUID/Guid` thuần túy (Logical Reference).
  2. Áp dụng kỹ thuật **Event-Driven Data Replication (Shadow Table)**: Identity API phát sự kiện `UserRegisteredEvent` / `UserProfileUpdatedEvent` qua RabbitMQ; Trading Core lắng nghe và ghi thông tin cơ bản vào bảng bóng `trading_db.users` để phục vụ các câu query JOIN nội bộ.

---

## 2. Rủi ro Frontend gãy giao tiếp do thiếu API Gateway
- **Hiện trạng:**
  - Sau Bước 1 & 2, `AuthController` đã chuyển sang `TicketShield.Identity.API` chạy trên cổng **5002**.
  - Frontend (`FE_CapstoneProject/packages/api-client/src/services/client.ts`) hiện chỉ cấu hình một biến môi trường duy nhất: `VITE_API_BASE_URL = http://localhost:5000/api/v1`.
- **Bất cập:**
  - Mọi request từ giao diện (Login, Register, Profile) bắn vào cổng 5000 sẽ nhận lỗi `HTTP 404 Not Found`.
- **Phương án giải quyết:**
  - Triển khai một Reverse Proxy / API Gateway (YARP hoặc Ocelot) đứng tại cổng 5000 để định tuyến tự động: `/api/v1/auth/*` chuyển tiếp sang 5002, các API khác giữ nguyên tại 5000; HOẶC cập nhật Frontend quản lý 2 HTTP client riêng biệt cho Auth và Core.

---

## 3. Kiến trúc "Tách vỏ, không tách ruột" tại `TicketShield.Identity.API`
- **Hiện trạng:**
  - Project `TicketShield.Identity.API` tham chiếu ngược vào `TicketShield.Application.csproj` và `TicketShield.Infrastructure.csproj`.
  - Thư mục `Features/Auth/` vẫn nằm trong `TicketShield.Application` của Core.
- **Bất cập:**
  - `Identity.API` không phải là Microservice tự chủ (Autonomous Microservice), mà thực chất là một HTTP Host thứ hai chạy trên toàn bộ DLL của Monolith. Khi deploy nó vẫn kéo theo MassTransit, gRPC Client, 14 bảng EF Core của Core.
- **Phương án giải quyết:**
  - Bóc tách cụm project độc lập: `TicketShield.Identity.Domain`, `TicketShield.Identity.Application`, `TicketShield.Identity.Infrastructure` (chỉ chứa `IdentityDbContext` map 2 bảng `users`, `user_bank_accounts`).
  - Gỡ bỏ hoàn toàn tham chiếu từ Identity sang Core và xóa thư mục `Features/Auth/` khỏi Core.

---

## 4. Xung đột kiến trúc: `CoreResaleStore` (JSONB) vs `TicketShieldDbContext` (Raw SQL)
- **Nguồn gốc trong Git:**
  - `CoreResaleStore` (lưu JSONB key-value) do **Nguyen-Hung-Thinh** đưa vào tại commit `43005fb` (10/09/2026) khi thiết kế gRPC state machine.
  - Đoạn mã Raw SQL `INSERT INTO resale_listings` do **ThongLH** viết tại commit `674b0c55` (11/09/2026) nhằm chèn vé vào bảng quan hệ cho Marketplace hiển thị mà không phải sửa constructor injection của `TicketVerificationService`.
- **Bất cập:**
  - Trạng thái xác thực vé lưu ở `core_resale_records` (JSONB), còn dữ liệu chợ vé lưu ở `resale_listings` (Bảng quan hệ).
  - Sử dụng raw SQL `_db.Database.ExecuteSqlInterpolatedAsync` vi phạm nguyên tắc trừu tượng hóa của Clean Architecture, không đi qua EF Core Change Tracker, dễ gây lệch pha dữ liệu (Desync).
- **Phương án giải quyết:**
  - Inject `ITicketShieldDbContext` vào `TicketVerificationService`.
  - Thay thế raw SQL bằng EF Core Entity `ResaleListing` chuẩn type-safe, đảm bảo toàn bộ dữ liệu đi qua cùng một Transaction quản lý trạng thái.

---

## 5. Settlement Worker tại MF-03 là vượt Scope (Scope Creep)
- **Đánh giá:**
  - Kế hoạch Plan 09 xếp việc tạo Settlement Worker vào Bước 4 (ngay sau RabbitMQ).
  - Tuy nhiên, luồng Buyer giữ chỗ 10 phút, quét VietQR, thanh toán Escrow và gRPC cấp vé mới thuộc về Sprint **MF-03**.
  - Việc tự động giải ngân sau 24h thuộc về Sprint **MF-04**.
- **Kết luận:**
  - Dừng việc xây dựng Settlement Worker tại thời điểm này để tránh Technical Debt và code rác không có dữ liệu thực tế kiểm thử.
