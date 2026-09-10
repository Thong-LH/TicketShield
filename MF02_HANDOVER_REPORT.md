# TICKETSHIELD - BÁO CÁO TỔNG HỢP REFACTOR & TÍCH HỢP NHÁNH MF-02
> **Người bàn giao:** Hoàng Thông (Dev 1 - Backend Core)  
> **Người nhận bàn giao:** Nguyễn Hùng Thịnh (Dev 2 - MockOrganizer & gRPC Integration)  
> **Nhánh gốc đối chiếu:** `features/Contract-gRPC` (Commit: `43005fb`)  
> **Nhánh hiện tại:** `feature/MF-02` (Commit: `b7c04a2`)  
> **Trạng thái kiểm thử:** **40/40 Tests Passed 100% (23 Unit Tests + 17 Integration Tests), 0 Warning, 0 Error**

---

## 1. ĐỔI TÊN CÁI GÌ (Renaming & Standardization)?

| Thành phần cũ | Thành phần mới | Lý do kỹ thuật |
| :--- | :--- | :--- |
| **`ResaleController`** | **`TicketVerificationsController`** | Phản ánh chính xác bản chất: Controller này chuyên trách điều phối vòng đời xác thực OTP và Khóa/Mở khóa vé gốc với BTC qua gRPC. *(Class `ResaleController` cũ vẫn được lưu trong `Controllers/ResaleController.cs` với attribute `[NonController]` làm bí danh tương thích ngược)*. |
| **`TicketResaleWorkflow`** | **`TicketVerificationService`** | Chuẩn hóa theo mô hình Service và triển khai thông qua interface `ITicketVerificationService` tại tầng Application layer, giải quyết triệt để lỗi vi phạm Dependency Inversion Principle (DIP). |
| **Route `api/resale-listings`** (trong controller gRPC) | **`api/v1/ticket-verifications`** | Đưa toàn bộ các hành động OTP, Confirm, Close, Cancel về đúng resource name chuẩn RESTful `/ticket-verifications`. Giữ route alias `api/ticket-verifications` để test suite của Thịnh chạy bình thường. |

---

## 2. TÁCH CÁI GÌ (Modularization & Clean Architecture)?

### 2.1. Tách bạch 2 Controller độc lập (Giải quyết xung đột dẫm chân Route)
* **`TicketVerificationsController.cs`**:
  - **Chỉ giữ lại:** Các endpoint xác thực gRPC (`RequestOtp`, `Resend`, `Confirm`, `Get`, `Close`, `Cancel`).
  - **Loại bỏ hoàn toàn:** Các route liên quan đến tin đăng bán `/api/resale-listings` và `/api/v1/resale-listings`.
* **`ResaleListingsController.cs`**:
  - **Gom toàn bộ quản lý tin đăng bán:** `CreateListing` (CQRS Command US-2.3), `Publish` (niêm yết vé đã khóa), `Marketplace` (danh sách chợ vé công khai), `GetListingDetail` (CQRS Query xem chi tiết vé & xác thực Private token).
* **`ResaleErrorsAttribute.cs`**:
  - Tách Exception Filter ra file riêng biệt trong thư mục `src/TicketShield.Core/TicketShield.API/Filters/` (tuân thủ nghiêm ngặt nguyên tắc **1 Type per File**).

### 2.2. Tách thư mục rác `Infrastructure/Resale/` thành 3 phân vùng chuẩn
Trước đây tất cả gRPC client, Service, và Store đều bị nhét chung vào một thư mục `Resale`. Hiện đã được phân bổ về đúng vị trí kiến trúc:
1. **`Infrastructure/ExternalServices/Organizer/`**: Chứa `OrganizerGateway.cs`, `OrganizerConnectionOptions.cs`, `ResaleRegistration.cs` (gRPC client kết nối sang MockOrganizer).
2. **`Infrastructure/Services/`**: Chứa `TicketVerificationService.cs` (logic nghiệp vụ verify).
3. **`Infrastructure/Persistence/Resale/`**: Chứa store quản lý session trên PostgreSQL.

### 2.3. Tách các file Monolithic nhiều Class (Tuân thủ 1 File = 1 Class/Record)
* **File `CoreResaleStore.cs` cũ** (trước gộp 4 class vào một file) được tách thành 4 file độc lập:
  - `CoreSession.cs`: Thực thể phiên xác thực vé.
  - `CoreOperation.cs`: Lịch sử các operation của phiên.
  - `CoreResaleRow.cs`: Record ánh xạ CSDL PostgreSQL.
  - `CoreResaleStore.cs`: Lớp thao tác dữ liệu với CSDL.
* **File `ResaleModels.cs` cũ** (trước gộp 6 record vào một file) được tách thành 6 file độc lập tại `Application/Resale/`:
  - `RequestOtpBody.cs`
  - `ConfirmOtpBody.cs`
  - `PublishBody.cs`
  - `ListingResult.cs`
  - `VerificationResult.cs`
  - `ResaleWorkflowException.cs`

### 2.4. De-minify Code (Chuẩn C# Clean Code)
* Bung toàn bộ 315 dòng mã viết dồn một hàng (minified code) trong `TicketResaleWorkflow` thành 850 dòng C# chuẩn mực, định dạng thụt đầu dòng rõ ràng, có XML documentation đầy đủ để dễ bảo trì và đọc hiểu.

---

## 3. SỬA CÁI GÌ (Bugfixes & Performance)?

1. **Xóa Thắt Cổ Chai Toàn Cục (Global Advisory Lock):**
   - Trong `OrganizerResaleGrpcService.cs` của MockOrganizer, đã **xóa bỏ** mã khóa cứng `84722001` (nguyên nhân khiến 1 người xác thực vé làm treo toàn bộ hệ thống).
   - Thay thế bằng cơ chế băm nhuyễn advisory lock theo từng `SessionId` riêng biệt (`SELECT pg_advisory_xact_lock(...)`).
2. **Sửa Crash Test Suite do Hardcode Đường Dẫn PostgreSQL:**
   - Trong `PostgresCluster.cs`, triển khai hàm tự động dò tìm (auto-discovery) thư mục binary của PostgreSQL trên Windows (quét tự động các bản v18, v17, v16), loại bỏ lỗi crash khi chạy test trên máy dev khác.
3. **Sửa Lỗi 401 Unauthorized do Lệch Claim JWT:**
   - Xây dựng cơ chế fallback nhận diện cả `ClaimTypes.NameIdentifier` và JWT `sub`, đồng thời tích hợp `CurrentUserService` lấy `UserId` chuẩn xác từ Token đăng nhập.
4. **Sửa Nguy Cơ 503 Service Unavailable do Thiếu Config:**
   - Bổ sung cấu hình gRPC loopback development và chuỗi kết nối vào cả 2 file `appsettings.json` và `appsettings.Development.json` ở cả 2 project (`TicketShield.API` và `MockOrganizer.API`).
5. **Khôi Phục Tính Năng Vé Riêng Tư (Private Mode):**
   - Sửa lỗi nuốt flag `isPrivate`. Khôi phục logic sinh chuỗi ngẫu nhiên 32 ký tự hex cho `private_access_token` khi `isPrivate = true`.

---

## 4. SO SÁNH: Nhánh Cũ (`features/Contract-gRPC`) vs Nhánh Hiện Tại (`feature/MF-02`)

| Tiêu chí | Nhánh cũ của Thịnh (`43005fb`) | Nhánh hiện tại (`feature/MF-02`) |
| :--- | :--- | :--- |
| **Mô hình Kiến trúc** | Viết dồn vào Infrastructure, Controller gọi thẳng DB, vi phạm DIP | Clean Architecture 4 tầng phân lập rõ ràng + CQRS MediatR |
| **Hệ thống Auth** | Chưa có, sellerId bị hardcode hoặc giả lập chuỗi | Tích hợp hoàn chỉnh Epic-1: JWT Bearer, Login/Register, Google OAuth, Hash BCrypt |
| **User Stories Đã Xong** | Khung xương gRPC cho US-2.1 | Hoàn thành Backend cho cả **US-2.1, US-2.2, US-2.3** |
| **Kiểm soát Trần giá** | Chỉ có 1 dòng check thô ở service | Domain Rule + FluentValidation (chặn trần giá `resale_price <= original_price` ném lỗi `422`) |
| **Độ phủ Test Suite** | 17 integration tests của gRPC | **40/40 tests pass 100%** (23 Unit Tests + 17 Integration Tests) |
| **Chất lượng Codebase** | Code bị nén minified, nhiều class/file, lock toàn cục | 0 Warning, 0 Error, tuân thủ nghiêm ngặt quy tắc 1 Type per File |

---

## 5. LƯU Ý KHI THỊNH TIẾP NHẬN TRIỂN KHAI US-2.4 (Jira SCRUM-32, 33, 34)

Khi Thịnh làm tiếp 3 subtask backend của US-2.4:
* **`SCRUM-32` (`BE-CORE-2.4.1_Get seller listings api`):**
  - Viết Query MediatR đặt tại: `src/TicketShield.Core/TicketShield.Application/Features/ResaleListings/Queries/GetSellerListings/`.
  - Lấy dữ liệu theo `CurrentUserService.UserId`.
  - Đặt endpoint tại `ResaleListingsController` (`GET /api/v1/resale-listings/my-listings`).
* **`SCRUM-33` (`BE-CORE-2.4.2_Cancel resale listing api`):**
  - Viết Command MediatR đặt tại: `src/TicketShield.Core/TicketShield.Application/Features/ResaleListings/Commands/CancelResaleListing/`.
  - Kiểm tra trạng thái vé: chỉ cho phép hủy nếu vé đang là `VERIFIED` (chưa ai mua/giữ chỗ).
  - Gọi sang `ITicketVerificationService.Cancel` hoặc gRPC để mở khóa vé gốc về `VALID` bên BTC.
* **`SCRUM-34` (`BE-MOCK-2.4.1_Unlock original ticket api`):**
  - Endpoint mở khóa vé gốc phía MockOrganizer (đã có sẵn trong gRPC method `ReleaseTicketLock`).
