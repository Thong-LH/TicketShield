# BÁO CÁO TOÀN DIỆN VỀ SỰ CỐ KIẾN TRÚC & CÁC LỖI ĐÃ KHẮC PHỤC
> **Dự án:** TicketShield Core (MF-02)  
> **Thời gian:** 11/09/2026  
> **Trạng thái cuối cùng:** 50/50 Tests Passed (30 Unit Tests + 20 Integration Tests), 0 Warning, 0 Error.

---

## 1. Tổng quan tình trạng ban đầu

Codebase nhánh `feature/MF-02` được tích hợp từ 2 luồng phát triển song song của 2 lập trình viên / AI agent (Hoàng Thông - Dev 1 và Hùng Thịnh - Dev 2). Do chạy theo mục tiêu pass test cục bộ của từng ticket Jira (`SCRUM-25`, `SCRUM-26`, `SCRUM-32`, `SCRUM-33`, `SCRUM-34`), các agent đã tạo ra nhiều đoạn code chắp vá, dẫm chân nhau, vi phạm kiến trúc và để lại các lỗi bảo mật nghiêm trọng.

---

## 2. Chi tiết 9 sự cố phát hiện & Giải pháp đã thực hiện

### 🔴 Sự cố 1: Khóa toàn cục làm treo hệ thống (Global Advisory Lock Bottleneck)
- **Vị trí:** `OrganizerResaleGrpcService.cs` (MockOrganizer)
- **Bản chất:** Lỗi thuật toán đồng thời (Concurrency).
- **Nguyên nhân:** MockOrganizer sử dụng mã khóa cố định `84722001` cho mọi giao dịch (`pg_advisory_xact_lock(84722001)`). Bất kỳ khi nào 1 người dùng gửi yêu cầu OTP hoặc xác thực vé, toàn bộ request của những người dùng khác đều bị chặn lại xếp hàng.
- **Hành động đã làm:** Xóa bỏ hằng số `84722001`, thay bằng hàm băm nhuyễn `ComputeLockKey(verificationId)` để băm mã khóa theo từng Session/Ticket cụ thể. Hệ thống xử lý đồng thời hàng ngàn vé song song mà không nghẽn.

---

### 🔴 Sự cố 2: Lỗ hổng rò rỉ dữ liệu qua Fallback nặc danh (`defaultSeller`)
- **Vị trí:** `CreateResaleListingCommandHandler.cs`, `CancelResaleListingCommandHandler.cs`, `GetSellerListingsQueryHandler.cs`
- **Bản chất:** Lỗi bảo mật nghiêm trọng.
- **Nguyên nhân:** Khi request không có token JWT hoặc token thiếu claim `UserId`, code tự động query `_dbContext.Users.OrderBy(u => u.CreatedAt).FirstOrDefaultAsync()` và gán mặc định cho User đầu tiên trong DB (nhằm mục đích cho Unit Test dễ pass).
- **Hậu quả:** Bất kỳ ai không đăng nhập gọi `GET /my-listings` đều xem được toàn bộ vé của tài khoản Admin/User đầu tiên.
- **Hành động đã làm:** 
  - Tạo mới class ngoại lệ chuẩn `UnauthorizedException.cs` (HTTP 401).
  - Đăng ký `UnauthorizedException` vào `GlobalExceptionHandlingMiddleware.cs`.
  - Xóa bỏ toàn bộ các khối fallback `defaultSeller`, bắt buộc ném 401 nếu chưa xác thực.
  - Viết Unit Test kiểm chứng hành vi ném lỗi 401.

---

### 🔴 Sự cố 3: Hai luồng tạo tin đăng bán song song (Path A vs Path B)
- **Vị trí:** `ResaleListingsController.cs`, thư mục `CreateResaleListing/`
- **Bản chất:** Lỗi cấu trúc & Dead Code nguy hiểm.
- **Nguyên nhân:**
  - **Path A (`Publish`):** Luồng chuẩn: OTP $\rightarrow$ Khóa vé bên BTC qua gRPC $\rightarrow$ Lưu vào bảng `core_resale_records` $\rightarrow$ Vé xuất hiện trên Marketplace.
  - **Path B (`CreateResaleListingCommand`):** Luồng "đi tắt": Client gửi POST trực tiếp $\rightarrow$ Insert thẳng vào bảng `ResaleListings` mà không hề khóa vé bên BTC.
- **Hậu quả:** Vé tạo từ Path B có status `Verified` giả tạo, nhưng không hề tồn tại trong `core_resale_records`. Do đó, khi người mua vào `GET /resale-listings` (Marketplace), vé của Path B **hoàn toàn bị tàng hình**.
- **Hành động đã làm:** Xóa sổ hoàn toàn Path B:
  - Xóa 4 file nghiệp vụ: `CreateResaleListingCommand.cs`, `CreateResaleListingCommandHandler.cs`, `CreateResaleListingCommandValidator.cs`, `CreateResaleListingResponse.cs`.
  - Xóa 2 file test: `CreateResaleListingCommandHandlerTests.cs`, `CreateResaleListingCommandValidatorTests.cs`.
  - Xóa action `CreateListing` trên `ResaleListingsController.cs`, thống nhất `Publish` là endpoint `[HttpPost]` chính thức duy nhất.

---

### 🔴 Sự cố 4: Hai endpoint Hủy tin bán vé dẫm chân nhau
- **Vị trí:** `TicketVerificationsController.cs` (`{id}/cancel-listing`) vs `ResaleListingsController.cs` (`{id}/cancel`)
- **Bản chất:** Lỗi xung đột API.
- **Nguyên nhân:** Hai lập trình viên viết 2 cách cancel khác nhau: 1 bên cancel theo `verificationId` (không cập nhật DB status), 1 bên cancel theo `listingId` (có tra cứu và mở khóa).
- **Hành động đã làm:** 
  - Thống nhất endpoint chuẩn RESTful: `POST /api/v1/resale-listings/{id:guid}/cancel`.
  - Gắn attribute `[Obsolete]` trên action cũ của `TicketVerificationsController` để hướng dẫn FE sử dụng endpoint chuẩn, đồng thời giữ tương thích cho test suite.

---

### 🟡 Sự cố 5: Nuốt lỗi gRPC phân tán (`catch (Exception) {}`)
- **Vị trí:** `CancelResaleListingCommandHandler.cs`
- **Bản chất:** Lỗi xử lý ngoại lệ sai (Silent Failure).
- **Nguyên nhân:** Khi gọi sang MockOrganizer để mở khóa vé, code bọc trong `try { ... } catch (Exception) { /* nuốt lỗi */ }` để tránh crash test.
- **Hậu quả:** Nếu gRPC server bị sập hoặc từ chối mở khóa, vé bên BTC vẫn bị kẹt ở trạng thái `LOCKED_FOR_RESALE`, nhưng DB của Core lại đổi trạng thái thành `Cancelled` $\rightarrow$ Lệch pha dữ liệu giữa hai hệ thống.
- **Hành động đã làm:** Xóa bỏ block nuốt lỗi. Khi gRPC mở khóa thất bại, ngoại lệ sẽ kích hoạt và chặn việc cập nhật trạng thái trong DB.

---

### 🟡 Sự cố 6: Thiếu Attribute `[Authorize]` trên Controller
- **Vị trí:** `ResaleListingsController.cs`
- **Bản chất:** Sơ suất cấu hình bảo mật.
- **Nguyên nhân:** Action tạo tin đăng bán không có `[Authorize]`, phụ thuộc vào việc code bên trong tự ném lỗi nếu thiếu claim.
- **Hành động đã làm:** Bổ sung đầy đủ `[Authorize]` ở mức action/controller cho tất cả endpoint yêu cầu quyền người dùng.

---

### 🟡 Sự cố 7: Vi phạm DRY — Duplicate code tính Discount & Mask Ticket Code
- **Vị trí:** `GetResaleListingDetailQueryHandler.cs`, `GetResaleListingByPrivateTokenQueryHandler.cs`, `GetSellerListingsQueryHandler.cs`
- **Bản chất:** Lặp code mapping và tính toán nghiệp vụ.
- **Hành động đã làm:**
  - Chuyển logic tính `DiscountAmount`, `DiscountPercentage`, và `MaskedTicketCode` thành computed properties bên trong domain entity `ResaleListing.cs`.
  - Viết extension method `ToDetailDto()` trong `ResaleListingExtensions.cs`. Cả hai handler `GetResaleListingDetail` và `GetResaleListingByPrivateToken` đều dùng chung method này.

---

### 🟢 Sự cố 8: Tàn dư code thừa sau refactor (Dead Aliases)
- **Vị trí:** `ResaleController.cs`, `ITicketResaleWorkflow.cs`, `ResaleRegistration.cs`
- **Bản chất:** Rác mã nguồn (Bloatware).
- **Nguyên nhân:** AI agent khi đổi tên class/interface cũ đã tạo thêm các class rỗng và interface rỗng để "tương thích ngược" thay vì refactor triệt để.
- **Hành động đã làm:**
  - Xóa bỏ file `ResaleController.cs` (`[NonController]`).
  - Xóa bỏ interface `ITicketResaleWorkflow` trong `ITicketVerificationService.cs`.
  - Xóa dòng đăng ký `services.AddScoped<ITicketResaleWorkflow, ...>()` trong `ResaleRegistration.cs`.

---

### 🟡 Sự cố 9: Ném sai HTTP Status Code khi chưa đăng nhập
- **Vị trí:** `GetCurrentUserQueryHandler.cs`
- **Bản chất:** Sai lệch chuẩn RESTful.
- **Nguyên nhân:** Khi người dùng chưa có token hoặc token rỗng, handler ném `ForbiddenAccessException` (HTTP 403) thay vì `UnauthorizedException` (HTTP 401).
- **Hành động đã làm:** Đổi sang ném `UnauthorizedException` (HTTP 401).

---

## 3. Bảng tổng kết kết quả kiểm định

| Dự án kiểm thử | Số lượng test | Kết quả | Ghi chú |
|---|---|---|---|
| **TicketShield.UnitTests** | 30 tests | ✅ **30/30 Passed** | Đã loại 5 test rác của Path B, bổ sung test xác thực 401 |
| **TicketShield.Resale.Tests** | 20 tests | ✅ **20/20 Passed** | Toàn bộ luồng gRPC/OTP, PostgreSQL transaction và Idempotency đều xanh |
| **Compiler Build** | Toàn giải pháp | ✅ **0 Warning, 0 Error** | Mã nguồn sạch sẽ, tuân thủ strict typing |
