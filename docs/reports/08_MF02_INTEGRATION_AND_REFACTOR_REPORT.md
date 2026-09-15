# TỔNG HỢP REFACTOR & TÍCH HỢP NHÁNH MF-02 (GỬI BẠN THỊNH)

> **Nhánh hiện tại:** `feature/MF-02`  
> **Trạng thái kiểm thử:** 40/40 tests pass 100% (23 Unit Tests + 17 Integration Tests), 0 Warning, 0 Error.  
> **Trạng thái Git:** Sạch hoàn toàn, đã sync đồng bộ với `origin/feature/MF-02`.

---

### 1. Đổi tên cái gì (Renaming & Standardization)
- **Đổi tên Controller:** Đổi `ResaleController` $\rightarrow$ `TicketVerificationsController` (chuyên trách xác thực OTP và khóa vé gRPC với BTC qua `/api/v1/ticket-verifications`). Class cũ `ResaleController` vẫn được giữ làm bí danh với `[NonController]` để đảm bảo không vỡ code cũ.
- **Đổi tên Service:** Đổi `TicketResaleWorkflow` $\rightarrow$ `TicketVerificationService` (triển khai interface trừu tượng `ITicketVerificationService` tại Application layer theo đúng chuẩn Clean Architecture).
- **Chuẩn hóa Route:** Toàn bộ route xác thực được đưa về chuẩn `/api/v1/ticket-verifications` (vẫn giữ route alias `/api/ticket-verifications` để test suite cũ chạy bình thường).

---

### 2. Tách cái gì (Modularization & Clean Code)
- **Tách bạch 2 Controller dẫm chân nhau:**
  - `TicketVerificationsController`: Chuyên trách 100% luồng xác thực OTP & Khóa/Mở khóa vé gRPC với BTC (`RequestOtp`, `Resend`, `Confirm`, `Get`, `Close`, `Cancel`).
  - `ResaleListingsController`: Gom toàn bộ việc niêm yết tin bán, marketplace, và xem vé private token (`CreateListing`, `Publish`, `Marketplace`, `GetListingDetail`).
  - `ResaleErrorsAttribute`: Tách riêng ra file `Filters/ResaleErrorsAttribute.cs` theo chuẩn 1 Type = 1 File.
- **Tách thư mục rác `Infrastructure/Resale/`:** Xóa sạch thư mục gom chung cũ, chia thành 3 phân vùng chuẩn kiến trúc:
  - `ExternalServices/Organizer/`: Chứa gRPC client kết nối sang MockOrganizer (`OrganizerGateway`, `ResaleRegistration`,...).
  - `Services/`: Chứa `TicketVerificationService.cs` thực thi interface nghiệp vụ.
  - `Persistence/Resale/`: Chứa store quản lý session trên PostgreSQL.
- **Tách các file Monolithic nhiều Class (Chuẩn 1 Type = 1 File):**
  - `CoreResaleStore.cs` tách thành 4 file riêng: `CoreSession.cs`, `CoreOperation.cs`, `CoreResaleRow.cs`, `CoreResaleStore.cs`.
  - `ResaleModels.cs` tách thành 6 file riêng trong `Application/Resale/`: `RequestOtpBody.cs`, `ConfirmOtpBody.cs`, `PublishBody.cs`, `ListingResult.cs`, `VerificationResult.cs`, `ResaleWorkflowException.cs`.
- **De-minify Code:** Bung đoạn code 315 dòng viết nén một hàng thành 850 dòng C# chuẩn mực, thụt đầu dòng rõ ràng, có comment XML đầy đủ.

---

### 3. Sửa cái gì (Bugfixes & Performance)
- **Bỏ nghẽn Lock:** Đã xóa mã khóa cứng toàn cục `84722001` trong `OrganizerResaleGrpcService.cs` của MockOrganizer, thay bằng cơ chế băm advisory lock theo session ID (`pg_advisory_xact_lock`), tránh deadlock/bottleneck toàn hệ thống khi nhiều người cùng xác thực vé.
- **Sửa Test Suite:** Integration test (`PostgresCluster.cs`) đã tự động nhận diện binary PostgreSQL 18/17/16 trên Windows, không còn bị crash do hardcode đường dẫn cục bộ.
- **Sửa lỗi 401 JWT:** Bổ sung cơ chế fallback nhận diện cả `ClaimTypes.NameIdentifier` lẫn JWT `sub`, tích hợp `CurrentUserService` lấy ID người dùng đăng nhập.
- **Sửa nguy cơ lỗi 503:** Đồng bộ cấu hình gRPC dev loopback và connection strings vào cả `appsettings.json` và `appsettings.Development.json` ở cả 2 project (`TicketShield.API` và `MockOrganizer.API`).
- **Kích hoạt Private Mode:** Khôi phục logic sinh chuỗi ngẫu nhiên 32 ký tự hex cho `private_access_token` khi người bán chọn `isPrivate = true`.

---

### 4. Khác biệt giữa Nhánh Cũ (`features/Contract-gRPC`) vs Nhánh Hiện Tại (`feature/MF-02`)
- **Nhánh cũ của Thịnh:** Chỉ có phần gRPC thô, `ResaleController` gọi thẳng DB, chưa có Auth JWT (dùng sellerId giả lập), lock hệ thống bị nghẽn, test suite bị hardcode đường dẫn.
- **Nhánh hiện tại (`feature/MF-02`):**
  - Đã tích hợp trọn vẹn Epic-1 Auth (JWT Bearer, Register, Login, Google OAuth, BCrypt).
  - Đã tích hợp trọn vẹn US-2.3 (tạo tin bán, kiểm tra trần giá Price Ceiling, sinh token bán riêng tư).
  - Chuẩn Clean Architecture 4 tầng + CQRS MediatR.
  - 40/40 tests pass 100% (23 Unit Tests + 17 Integration Tests), 0 Warning, 0 Error.

---

### 5. Lưu ý khi Thịnh tiếp nhận triển khai US-2.4 (Jira SCRUM-32, 33, 34)
- **`SCRUM-32` (API lấy danh sách vé của Seller):**
  - Viết Query MediatR tại `Application/Features/ResaleListings/Queries/GetSellerListings/`.
  - Lấy dữ liệu theo `CurrentUserService.UserId`.
  - Đặt endpoint tại `ResaleListingsController` (`GET /api/v1/resale-listings/my-listings`).
- **`SCRUM-33` (API hủy tin bán):**
  - Viết Command MediatR tại `Application/Features/ResaleListings/Commands/CancelResaleListing/`.
  - Kiểm tra trạng thái vé: chỉ cho phép hủy nếu vé đang là `VERIFIED` (chưa có ai đặt mua/giữ chỗ).
  - Gọi sang `ITicketVerificationService.Cancel` hoặc gRPC để mở khóa vé gốc về `VALID` bên BTC.
- **`SCRUM-34` (MockOrganizer Unlock API):**
  - Dùng gRPC method `ReleaseTicketLock` đã có sẵn bên MockOrganizer.

---

### 6. Lưu ý về vấn đề Vượt Scope (Scope Creep) của Sprint MF-02
- **Phạm vi chuẩn của MF-02 (Seller Scope):** Toàn bộ sprint này chỉ dành riêng cho **Người bán (Seller)** hoàn tất việc kiểm định vé gốc với BTC và đăng tin bán (Public hoặc Private link).
- **Về vai trò Người mua (Buyer):** Trong MF-02, Người mua **DUY NHẤT** chỉ xuất hiện ở tính năng xem trước vé qua Link riêng tư (`GET /api/v1/resale-listings/{id}?token=...` - đã được làm chuẩn bằng CQRS `GetResaleListingDetailQuery`).
- **Về Chợ vé Marketplace (`GET /api/v1/resale-listings`):**
  - Tính năng sàn giao dịch / chợ vé công khai cho Buyer lướt tìm, lọc và đặt mua vé thực chất thuộc về **Epic-3 (Buyer Scope)** ở Sprint sau.
  - Endpoint `Marketplace` mà Thịnh viết ở nhánh cũ thực chất là **vượt scope** và mang tính chất tạm bợ (mì ăn liền để pass tiêu chí test AC-2.3.2 - kiểm tra vé private không bị lọt ra chợ).
  - Hiện tại hàm này đã được gom tạm về `ResaleListingsController` để bảo đảm test suite không bị gãy. Sang Sprint sau (Epic-3), nhóm sẽ cần đập đi xây lại API Marketplace hoàn chỉnh bằng CQRS (Search, Filter theo Event/Giá, Phân trang Paging, Escrow Hold) thay vì dùng logic tạm bợ này.

