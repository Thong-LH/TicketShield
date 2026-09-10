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
