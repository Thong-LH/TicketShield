# Architectural Standards & Code Quality Directives

Tài liệu này là chỉ thị bắt buộc về Kiến trúc & Tiêu chuẩn Code cho toàn bộ workspace TicketShield. Agent bắt buộc phải ghi nhớ và tuân thủ trong mọi lần làm việc.

---

## 1. Clean Architecture & Phân tầng nghiêm ngặt (Strict Layering)
- **Dependency Inversion Principle (DIP):** Tầng Presentation/API Controllers TUYỆT ĐỐI KHÔNG ĐƯỢC phụ thuộc hay `using` concrete classes ở tầng Infrastructure. Mọi giao tiếp với external services/hạ tầng phải thông qua Interface định nghĩa tại `TicketShield.Application/Common/Interfaces` (như `ITicketVerificationService`, `ITicketShieldDbContext`).
- **CQRS & MediatR:** Ưu tiên luồng xử lý qua Command/Query Handlers. Các service điều phối phân tán (Saga/Workflow) phải được trừu tượng hóa bằng Interface chuẩn tại Application layer.
- **Tách biệt Database Context:** Không tự ý tạo thêm DbContext phụ để bắn raw SQL bypass Domain Entity. Bảng dữ liệu chính phải được quản lý tập trung và tuân thủ Domain Laws (như luật trần giá `ValidatePriceCeiling`).

---

## 2. Chuẩn mực Mã nguồn C# (C# Code Quality)
- **1 Class = 1 File:** Nghiêm cấm gộp nhiều class nghiệp vụ, DbContext, Migration hoặc Model vào chung một file. Mỗi entity, model, service, migration phải có file riêng biệt với namespace chuẩn.
- **Không dồn dòng (No Minified Style):** Mỗi câu lệnh phải xuống dòng rõ ràng, có thụt lề chuẩn theo C# conventions, có comment/XML doc cho các phương thức quan trọng.
- **Chủ động phát hiện (Proactive Detection):** Khi người dùng yêu cầu kiểm tra hoặc review code, Agent BẮT BUỘC phải tự giác rà soát và chỉ ra các vi phạm kiến trúc (chồng chéo controller, thiếu interface, phá vỡ CQRS, code dồn dòng) ngay từ đầu, không được chờ người dùng nhắc nhở.

---

## 3. Nhật ký hoạt động (Activity Logging)
- Mọi thay đổi code, commit, refactor, bugfix quan trọng đều phải được ghi lại ngay lập tức vào file `ACTIVITY_LOG.md` theo format chuẩn của dự án:
  `- [YYYY-MM-DD HH:mm] | [TAG] | Mô tả chi tiết hành động và kết quả | [Danh sách file ảnh hưởng]`

---

## 4. Quy tắc Chống Tái Diễn Lỗi Hệ Thống (Zero-Tolerance Anti-Patterns)
Dựa trên sự cố tích hợp nhánh `feature/MF-02`, Agent BẮT BUỘC tuân thủ nghiêm ngặt các điều cấm sau:
- **Cấm Fallback nặc danh (No Anonymous/Mock Fallback):** Tuyệt đối KHÔNG viết logic tự động query `Users.FirstOrDefault()` hoặc gán hardcode `UserId` khi request thiếu JWT Token. Bắt buộc ném `UnauthorizedException` (HTTP 401). Mock user chỉ được phép xuất hiện trong project Test.
- **Cấm đường tắt song song (No Parallel Fake Paths):** Khi đã có workflow xác thực với bên thứ ba (gRPC/OTP), KHÔNG ĐƯỢC tạo thêm endpoint CRUD "đi tắt" lưu trực tiếp vào DB để đối phó với ticket Jira hoặc làm xanh test.
- **Cấm vỏ bọc phòng thủ khi Refactor (No Defensive Wrappers / Dead Aliases):** Khi đổi tên service/controller, phải refactor triệt để và xóa code cũ. Nghiêm cấm tạo class `[NonController][Obsolete]`, interface rỗng kế thừa vô nghĩa, hoặc đăng ký DI thừa thãi.
- **Cấm nuốt lỗi phân tán (No Silent Error Catching):** Khi gọi service gRPC / External API, tuyệt đối không dùng `catch (Exception) {}` rỗng để tránh crash test. Lỗi phải được lan truyền để kích hoạt rollback trạng thái DB.
- **Cấm Global Advisory Lock:** Khi dùng database advisory lock, TUYỆT ĐỐI KHÔNG dùng mã khóa cứng toàn cục (như `84722001`). Phải luôn băm theo ID tài nguyên (`ComputeLockKey(resourceId)`).
- **Phân biệt rạch ròi HTTP 401 vs 403:**
  - Chưa đăng nhập / thiếu token $\rightarrow$ HTTP 401 `UnauthorizedException`.
  - Đã đăng nhập nhưng không có quyền trên tài nguyên của người khác $\rightarrow$ HTTP 403 `ForbiddenAccessException`.
- **Thông báo bắt buộc nếu có ngoại lệ (Mandatory User Notification):** Nếu vì lý do kỹ thuật đặc thù mà BUỘC PHẢI tạo mock tạm hoặc ngoại lệ kiến trúc, Agent BẮT BUỘC PHẢI giải thích rõ ràng và xin phép người dùng trước khi viết vào codebase, không được tự ý âm thầm thực hiện.

