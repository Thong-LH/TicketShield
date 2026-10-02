# Core Directives & Architectural Standards (TicketShield)

Tài liệu này là **chỉ thị cốt lõi luôn luôn kích hoạt (Always-on Rules)** cho mọi Context Window của Antigravity trong workspace TicketShield (`d:\Capstone`). Agent BẮT BUỘC tuân thủ nghiêm ngặt trong mọi phản hồi và hành động.

---

## 1. Quy trình Bắt buộc Trước khi Hoàn tất Phản hồi (Pre-Response Checklist)
Mỗi khi Agent có bất kỳ hành động nào thay đổi code, refactor, thêm tính năng, fix bug hoặc cập nhật file trong workspace:
1. **Bắt buộc cập nhật [ACTIVITY_LOG.md](file:///d:/Capstone/ACTIVITY_LOG.md) cho Backend (BE):**
   - Đối với Backend (`d:\Capstone`), MỌI lượt thay đổi code, refactor, thêm tính năng, fix bug BẮT BUỘC phải thêm 1 dòng nhật ký vào cuối file [ACTIVITY_LOG.md](file:///d:/Capstone/ACTIVITY_LOG.md) theo định dạng chuẩn:
     `- [YYYY-MM-DD HH:mm] | [TAG] | Mô tả chi tiết hành động và kết quả | [Danh sách file ảnh hưởng]`
   - Thời gian `YYYY-MM-DD HH:mm` bắt buộc lấy thời gian cục bộ (Local Time) tại thời điểm thực hiện.
   - Nếu chưa cập nhật [ACTIVITY_LOG.md](file:///d:/Capstone/ACTIVITY_LOG.md), tác vụ Backend được coi là **CHƯA HOÀN THÀNH**.
   - **Quy chuẩn riêng cho Frontend (FE - `d:\FE_CapstoneProject`):**
     - **Chỉ ghi nhật ký các tác vụ từ TRUNG BÌNH đến LỚN (Medium -> Major):**
       - Tạo trang mới / luồng chức năng mới (`PayoutAccountsPage`, `SellTicketPage`...).
       - Tái cấu trúc lớn (Refactor module, tách file nghìn dòng thành component con).
       - Tích hợp nghiệp vụ / API phân tầng (State Store, SignalR, Auth Google, gRPC sync).
       - Xử lý lỗi kiến trúc / hạ tầng (CORS, 401/403 mismatch giữa các service).
     - **BỎ QUA hoàn toàn các vi chỉnh nhỏ (Micro-edits):**
       - Đổi màu sắc, chỉnh khoảng cách CSS padding/margin.
       - Thêm/bớt icon, ẩn/hiện badge số lượng nhỏ, sửa câu từ hiển thị trên UI.
2. **Kỷ luật Kiểm thử Nhanh & Trúng Đích (Fast Targeted Testing - Zero Redundant Re-test):**
   - **Cấm chạy quét toàn bộ:** Tuyệt đối KHÔNG chạy full test cả giải pháp (`dotnet test`) hoặc chạy trọn bộ 196+ unit tests (`dotnet test tests/TicketShield.UnitTests`) trong các phiên làm việc thông thường.
   - **Chỉ test trúng đích đúng module/class vừa sửa:** Bắt buộc dùng cờ `--filter` chỉ định chính xác tên class hoặc handler vừa chỉnh sửa:
     `dotnet test tests/TicketShield.UnitTests --filter "FullyQualifiedName~<FeatureOrClassName>"`
     Mỗi lần chạy chỉ được thực thi từ 1 đến 5 test case liên quan trực tiếp và kết thúc dưới 3 giây.
   - **Nguyên tắc "Module Đã Xanh Thì Khóa Lại" (No Redundant Re-testing):** Trong cùng một phiên làm việc, module/tính năng nào đã chạy test đạt kết quả xanh (Pass 100%) thì TUYỆT ĐỐI KHÔNG CHẠY TEST LẠI trong các lượt phản hồi tiếp theo.
   - **Bảo toàn số lượng Unit Tests:** Không được tự ý xóa bớt test case cũ trong codebase để duy trì chỉ số Code Coverage báo cáo hội đồng đồ án và CI/CD.
   - **Kiểm thử tích hợp (Integration Tests):** Chỉ chạy bộ `TicketShield.Resale.Tests` khi có yêu cầu bằng văn bản cụ thể từ người dùng hoặc trước khi chốt Pull Request lớn.
3. **Chủ động Nhắc Nhở Sao Lưu Nhật Ký Hội Thoại (Chat History Backup Reminder):**
   - Mỗi khi hoàn thành một chức năng quan trọng (major feature), một đợt refactor kiến trúc lớn hoặc xử lý xong chuỗi lỗi phức tạp:
   - Agent BẮT BUỘC chủ động nhắc người dùng sao lưu (hoặc đề xuất chạy lệnh `python scripts/export_chat_history.py`) để đồng bộ phiên làm việc hiện tại vào thư mục [local_docs/chat_sessions](file:///d:/Capstone/local_docs/chat_sessions) làm tài liệu minh chứng bảo vệ đồ án tốt nghiệp.

---

## 2. Phong cách Giao tiếp & Tương tác (Communication Style)
- **Trực diện & Vào việc ngay:** Bỏ qua mọi câu chào hỏi, xin lỗi, hoặc câu chốt xã giao rườm rà. Trả lời thẳng vào trọng tâm vấn đề.
- **Action-First:** Ưu tiên hành động cụ thể (đọc file, sửa code, chạy lệnh) hơn là giải thích lý thuyết.
- **Đánh số & Giới hạn:** Mọi hướng dẫn quy trình phải được đánh số thứ tự rõ ràng (tối đa 5 bước) để đảm bảo trực quan, chống xao nhãng.
- **One Next Step:** Luôn kết thúc phản hồi bằng đúng **MỘT hành động cụ thể tiếp theo** (ví dụ: gợi ý lệnh chạy test, đề xuất mở file kế tiếp, hoặc yêu cầu log lỗi cần thiết).
- **Clickable Links:** Mọi đường dẫn file và biểu tượng code phải luôn dùng chuẩn Markdown `[filename](file:///path/to/file)`.
- **Cấm cú pháp LaTeX (No LaTeX in Chat):** Tuyệt đối KHÔNG dùng cú pháp toán học LaTeX ($$..., $...$, \text{}, \min, \le, \Delta, \rightarrow, \leftrightarrow, \Rightarrow, \Leftarrow). Bắt buộc dùng mũi tên thuần (->, <->, -->) hoặc ký tự Unicode chuẩn (→, ↔) và inline code plain text.
- **Cấm thuật ngữ kỹ thuật thô trên UI (No Technical Jargon on User-Facing UI):** Tuyệt đối KHÔNG hiển thị các từ khóa kỹ thuật nội bộ (gRPC 30s, Ký quỹ Escrow T+24h, AI Anti-Bot, ACID Transaction) ra giao diện người dùng cuối.

---

## 3. Quy chuẩn Kiến trúc & Mã nguồn C# (Architectural Standards)
- **Clean Architecture & DIP:** Tầng Presentation/API Controllers TUYỆT ĐỐI KHÔNG ĐƯỢC phụ thuộc hay `using` concrete classes ở tầng Infrastructure. Mọi giao tiếp với external services/hạ tầng phải thông qua Interface định nghĩa tại `TicketShield.Application/Common/Interfaces` (như `ITicketVerificationService`, `ITicketShieldDbContext`).
- **CQRS & MediatR:** Ưu tiên luồng xử lý qua Command/Query Handlers. Các service điều phối phân tán (Saga/Workflow) phải được trừu tượng hóa bằng Interface chuẩn tại Application layer.
- **Tách biệt Database Context:** Không tự ý tạo thêm DbContext phụ để bắn raw SQL bypass Domain Entity. Bảng dữ liệu chính phải được quản lý tập trung và tuân thủ Domain Laws (như luật trần giá `ValidatePriceCeiling`).
- **1 Class = 1 File:** Nghiêm cấm gộp nhiều class nghiệp vụ, DbContext, Migration hoặc Model vào chung một file. Mỗi entity, model, service, migration phải có file riêng biệt với namespace chuẩn.
- **Không sửa mò (No Blind Edits):** Bắt buộc dùng công cụ (`view_file`, `grep_search`, `list_dir`) đọc hiểu bối cảnh và các file phụ thuộc trước khi sửa đổi bất kỳ đoạn code nào.
- **YAGNI & DRY:** Chỉ viết đoạn code giải quyết chính xác vấn đề hiện tại. Tuyệt đối không tự ý viết thêm hàm, interface, class hoặc logic "phòng hờ cho tương lai".

---

## 4. Quy tắc Chống Tái Diễn Lỗi Hệ Thống (Zero-Tolerance Anti-Patterns)
- **Cấm Fallback nặc danh (No Anonymous/Mock Fallback):** Tuyệt đối KHÔNG viết logic tự động query `Users.FirstOrDefault()` hoặc gán hardcode `UserId` khi request thiếu JWT Token. Bắt buộc ném `UnauthorizedException` (HTTP 401). Mock user chỉ được phép xuất hiện trong project Test.
- **Cấm đường tắt song song (No Parallel Fake Paths):** Khi đã có workflow xác thực với bên thứ ba (gRPC/OTP), KHÔNG ĐƯỢC tạo thêm endpoint CRUD "đi tắt" lưu trực tiếp vào DB để đối phó với ticket Jira hoặc làm xanh test.
- **Cấm vỏ bọc phòng thủ khi Refactor (No Defensive Wrappers / Dead Aliases):** Khi đổi tên service/controller, phải refactor triệt để và xóa code cũ. Nghiêm cấm tạo class `[NonController][Obsolete]`, interface rỗng kế thừa vô nghĩa, hoặc đăng ký DI thừa thãi.
- **Cấm nuốt lỗi phân tán (No Silent Error Catching):** Khi gọi service gRPC / External API, tuyệt đối không dùng `catch (Exception) {}` rỗng để tránh crash test. Lỗi phải được lan truyền để kích hoạt rollback trạng thái DB.
- **Cấm Global Advisory Lock:** Khi dùng database advisory lock, TUYỆT ĐỐI KHÔNG dùng mã khóa cứng toàn cục (như `84722001`). Phải luôn băm theo ID tài nguyên (`ComputeLockKey(resourceId)`).
- **Phân biệt rạch ròi HTTP 401 vs 403:**
  - Chưa đăng nhập / thiếu token -> HTTP 401 `UnauthorizedException`.
- **Cấm gọi Service trực tiếp từ Controller để bypass MediatR:** Mọi nghiệp vụ đăng bán (đơn lẻ hay đa vé) BẮT BUỘC phải đóng gói qua MediatR Command & Handler. Tuyệt đối không được lấy lý do "code cũ gọi trực tiếp Service" để bao biện cho việc viết code mới bỏ qua MediatR.
- **Cấm Loop-Commit & Patching trong giao dịch gói:** Nghiêm cấm chạy vòng lặp commit từng item lẻ rồi mới chạy lệnh UPDATE vá `bundle_id` sau. Toàn bộ N vé trong gói bắt buộc phải được bọc trong 1 Database Transaction nguyên tử duy nhất (ACID), chèn sẵn `bundle_id` ngay từ đầu, triệt tiêu 100% nguy cơ vé mồ côi.
- **Cấm Slogan Clutter, Fake Trust Badges & Reassurance Boxes (Zero Visual Noise on UI):**
  - Tuyệt đối CẤM tự ý chèn các Trust Badges ("AN TOÀN 100%", "100% Guaranteed", icon khiên bảo vệ) vào giao diện.
  - Tuyệt đối CẤM tự ý tạo các box thuyết minh cơ chế bảo vệ dài dòng ("Cơ chế bảo vệ thanh toán 24h...") hoặc subtext khẩu hiệu hiển nhiên dưới tiêu đề trang. Thiết kế phải tuân thủ chuẩn Minimalist & Clean theo phong cách Fintech cao cấp.
- **Thông báo bắt buộc nếu có ngoại lệ (Mandatory User Notification):** Nếu vì lý do kỹ thuật đặc thù mà BUỘC PHẢI tạo mock tạm hoặc ngoại lệ kiến trúc, Agent BẮT BUỘC PHẢI giải thích rõ ràng và xin phép người dùng trước khi viết vào codebase, không được tự ý âm thầm thực hiện.

---

## 5. Quy tắc Quản trị Task & Thao tác Jira (Jira Discipline)
- **Mặc định Unassigned khi tạo mới:** Mọi task khi tạo mới trên Jira BẮT BUỘC để trống người phụ trách (`assignee = None / Unassigned`).
- **Chỉ Assign khi có lệnh đích danh từ User:** Tuyệt đối KHÔNG tự ý gán assignee cho bất kỳ ai (kể cả User/Hoàng Thông). Chỉ gán người khi nhận được lệnh chỉ định rõ ràng từ User.
- **Bảo toàn dữ liệu Board & Cấm ghi đè (Zero Overwrite / Read-Only Safety):** Mọi thao tác đọc, kiểm tra board chỉ được dùng phương thức HTTP GET thuần túy (như `view_jira_board.py`). Nghiêm cấm mọi hành vi tự động ghi đè, cập nhật hoặc bulk unassign/assign các record đã tồn tại của đồng đội trên Jira.
