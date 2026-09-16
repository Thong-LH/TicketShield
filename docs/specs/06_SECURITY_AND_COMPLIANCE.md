# TicketShield AI - Security Architecture & Compliance

Tài liệu thiết kế kiến trúc bảo mật hệ thống và phương án tuân thủ **Nghị định số 13/2023/NĐ-CP của Chính phủ Việt Nam về Bảo vệ Dữ liệu Cá nhân (PDPD)** trong đồ án TicketShield AI.

---

## 1. Kiến Trúc Bảo Mật Hệ Thống

### 1.1. Xác Thực & Phân Quyền (Authentication & RBAC)
- **JWT (JSON Web Token):** Sử dụng chuẩn asymmetric hoặc HMAC-SHA256 với thời hạn sống ngắn (Access Token: 15 phút, Refresh Token: 7 ngày lưu trữ an toàn trong HttpOnly Cookie).
- **Phân quyền dựa trên vai trò (RBAC):**
  - `Buyer`: Tìm kiếm vé, gửi telemetry, thanh toán VietQR, nhận vé mới, khiếu nại Dispute.
  - `Seller`: Gửi yêu cầu bán vé, nhập OTP xác thực chính chủ, quản lý số dư ký quỹ.
  - `Admin`: Tra cứu `gate_access_logs`, phân xử tranh chấp, hoàn tiền hoặc giải ngân thủ công.

### 1.2. Bảo Vệ Dữ Liệu Chuyển Giao (Data in Transit)
- Toàn bộ giao tiếp giữa Client, TicketShield Core, MockOrganizer và AIEngine bắt buộc sử dụng HTTPS / TLS 1.3.
- Chống tấn công Man-in-the-Middle (MitM) và ngăn chặn sniff thông tin nhạy cảm.

### 1.3. Bảo Vệ Dữ Liệu Lưu Trữ (Data at Rest)
- **Mật khẩu người dùng:** Băm bằng thuật toán `BCrypt` hoặc `Argon2id` có sinh muối ngẫu nhiên (Salt).
- **Mã vé gốc & Thông tin thanh toán:** Mã hóa đối xứng AES-256 đối với các trường thông tin nhạy cảm trước khi lưu xuống PostgreSQL.

### 1.4. Phòng Chống Các Lỗ Hổng Bảo Mật Phổ Biến (OWASP Top 10)
- **SQL Injection:** Triệt tiêu hoàn toàn nhờ sử dụng EF Core Fluent API và ORM tham số hóa (Parameterized Queries).
- **Cross-Site Scripting (XSS):** Mã hóa HTML đầu ra, cấu hình Content Security Policy (CSP).
- **Cross-Site Request Forgery (CSRF):** Sử dụng Anti-forgery Token và SameSite cookie policy.
- **Race Condition & Double Spending:** Sử dụng khóa bi quan (`Pessimistic Locking`) hoặc mức cô lập giao dịch `Serializable` trong Database Transaction PostgreSQL khi mua vé và giải ngân Escrow.

---

## 2. Tuân Thủ Nghị Định 13/2023/NĐ-CP Về Bảo Vệ Dữ Liệu Cá Nhân

Nghị định 13/2023/NĐ-CP đặt ra các quy định pháp lý nghiêm ngặt về thu thập, xử lý và lưu trữ dữ liệu cá nhân tại Việt Nam. TicketShield tuân thủ theo 5 nguyên tắc cốt lõi:

### 2.1. Sự Đồng Thuận Của Chủ Thể Dữ Liệu (Consent Management - Điều 11)
- Khi người dùng đăng ký hoặc đăng bán vé, hệ thống hiển thị rõ ràng Điều khoản Dịch vụ và Chính sách Bảo vệ Dữ liệu Cá nhân.
- Nút "Tôi đồng ý cho phép TicketShield xử lý dữ liệu để xác thực vé và thanh toán" là bắt buộc trước khi thực hiện giao dịch.

### 2.2. Nguyên Tắc Tối Thiểu Hóa Dữ Liệu (Data Minimization)
- Hệ thống chỉ thu thập những dữ liệu thực sự cần thiết để xác thực và hoàn tất giao dịch:
  - Email / SĐT của chủ vé (để gửi OTP xác thực chính chủ).
  - Tên hiển thị và thông tin tài khoản nhận tiền giải ngân.
- **Không** lưu trữ số tài khoản ngân hàng đầy đủ nếu không cần thiết; dữ liệu thẻ ngân hàng không lưu trữ trực tiếp trên hệ thống mà chuyển hoàn toàn cho cổng thanh toán được Ngân hàng Nhà nước cấp phép.

### 2.3. Quyền Của Chủ Thể Dữ Liệu (Data Subject Rights - Điều 9)
- **Quyền được biết:** Người dùng xem được lịch sử các giao dịch, trạng thái vé và dữ liệu cá nhân của mình.
- **Quyền chỉnh sửa & Xóa dữ liệu (Right to be Forgotten):** Sau khi sự kiện kết thúc và thời hạn khiếu nại T+24h đã giải quyết xong, người dùng có quyền yêu cầu ẩn danh hoặc xóa thông tin cá nhân khỏi hệ thống.

### 2.4. Lưu Trữ Nhật Ký Xử Lý Dữ Liệu (Audit Logging - Điều 26)
- Mọi thao tác truy cập dữ liệu nhạy cảm (như tra cứu email chủ vé, xem bằng chứng tranh chấp) của Admin đều được ghi lại trong bảng nhật ký hệ thống `audit_logs` (ai xem, xem lúc nào, từ IP nào) phục vụ công tác thanh tra đối soát.

### 2.5. Bảo Vệ Dữ Liệu Sinh Trắc Học Hành Vi (Telemetry SDK)
- Dữ liệu thu thập từ Telemetry SDK (tọa độ chuột, nhịp gõ phím) là **dữ liệu phi định danh (Pseudonymized Data)**:
  - Chỉ được gắn với một mã phiên ngẫu nhiên `session_id`.
  - Không chứa nội dung ký tự gõ phím thực tế (chỉ tính độ lệch chuẩn mili-giây giữa các phím bấm).
  - Sau khi tính xong `risk_score`, dữ liệu chi tiết thô sẽ được xóa sau 48 giờ.
