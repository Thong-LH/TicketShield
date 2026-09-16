# TicketShield AI - Core Business Rules

Tài liệu quy định chi tiết toàn bộ các **Quy tắc Nghiệp vụ (Business Rules - BR)** bắt buộc áp dụng trong toàn bộ hệ thống TicketShield.

---

## 1. Bốn Quy Tắc Bất Biến Toàn Cục (Global Business Laws)

Đây là 4 định luật cốt lõi của hệ thống, vi phạm sẽ ném ngoại lệ `BusinessRuleViolationException` (HTTP 422):

### 1.1. BR-G01: Quy Tắc Trần Giá Linh Hoạt Có Biên Độ & Snapshot (Configurable Price Ceiling & Snapshot Law)
- **Nội dung:** Giá bán lại vé thứ cấp bắt buộc phải tuân theo biên độ trần do Ban tổ chức cấu hình cho từng sự kiện:
  `ResalePrice <= Ceiling = Truncate(OriginalPrice * (1 + MarkupPercentage / 100))`
  Trong đó: `MarkupPercentage` do Ban tổ chức / Admin cấu hình cho từng sự kiện (`Event.MaxResaleMarkupPercentage`) trong khoảng từ `0% -> 100%` (mặc định là `0%` - không bán vượt giá gốc; sự kiện đặc biệt có thể cho phép biên độ linh hoạt 5% - 10%).
- **Nguyên tắc Snapshot Bất biến (Listing Markup Snapshot):**
  - Tại thời điểm người bán tạo tin, hệ thống tính và lưu cố định `AppliedMarkupPercentage` và trần giá vào bản ghi `resale_listings`.
  - Mọi thay đổi về sau của Ban tổ chức đối với `MaxResaleMarkupPercentage` trên sự kiện chỉ áp dụng cho các tin đăng mới tạo sau đó. Các tin đang niêm yết hợp lệ giữ nguyên tính hiệu lực, tuyệt đối **không áp dụng hồi tố (non-retroactive)** làm ảnh hưởng đến tin đã đăng.
- **Quy tắc Làm tròn Số nguyên VNĐ (Zero-Cent Truncation):**
  - Trần giá bắt buộc được tính toán bằng hàm cắt đuôi số thập phân `decimal.Truncate(...)` để trả về số nguyên VNĐ chính xác (ví dụ: trần giá là `2.750.000 VNĐ`).
  - Triệt tiêu 100% rủi ro lệch 1 đồng giữa Client và Server do sai số số thực.
- **Ràng buộc toàn diện:**
  - **Client & Presentation:** Form nhập giá hiển thị ngưỡng giá trần VNĐ nguyên bản lấy từ Backend và thanh trượt giá (Slider) chặn cứng không cho kéo quá trần.
  - **Application (CQRS & FluentValidation):** `UpdateEventResaleMarkupCommandValidator` kiểm tra `MarkupPercentage` trong khoảng `[0, 100]`. Service `TicketVerificationService.Publish` kiểm tra `ResalePrice <= Ceiling` (ném lỗi `PRICE_EXCEEDS_CEILING` - HTTP 422 nếu vi phạm).
  - **Domain Model:** Entity `ResaleListing` hàm `ValidatePriceCeiling()` và `ComputePriceCeiling()` kiểm tra hợp lệ và ném `BusinessRuleViolationException`.

### 1.2. BR-G02: Quy Tắc Chính Chủ & Khóa Vé Ban Tổ Chức (Ownership & Ticket Lock Law)
- **Nội dung:** Người đăng bán chỉ được phép chuyển nhượng vé khi chứng minh được quyền sở hữu chính chủ hợp pháp và vé đã được niêm phong khóa bên hệ thống BTC.
- **Quy trình qua gRPC:**
  1. Người bán nhập Mã vé gốc (`ticket_code`) -> TicketShield gọi gRPC `RequestTicketOtp` sang MockOrganizer.
  2. MockOrganizer kiểm tra vé tồn tại (`VALID`), lấy email chủ vé gốc gửi OTP 6 số (hiệu lực 5 phút) qua SMTP.
  3. Người bán nhập đúng OTP -> TicketShield gọi gRPC `ConfirmTicketOtp`. MockOrganizer chuyển trạng thái vé gốc thành **`LOCKED_FOR_RESALE`** (ngăn chặn việc mang vé đi bán chỗ khác hoặc quét vào cổng trước).
- **Hủy tin bán:** Nếu người bán đổi ý hủy niêm yết (khi vé chưa bán), TicketShield gửi gRPC `ReleaseTicketLock` để MockOrganizer hoàn vé về trạng thái `VALID`.

### 1.3. BR-G03: Quy Tắc Giải Ngân Điều Kiện Kép & Khóa Hai Đầu (Dual-Condition Settlement & Dual-Hold Law)
- **Nội dung:** Tiền thanh toán của Buyer và Vé chuyển nhượng của Buyer đều được đặt trong trạng thái tạm giữ an toàn:
  - **Khóa tiền (Seller Escrow):** Tiền nằm trong Escrow với trạng thái `LOCKED`.
  - **Khóa vé (Buyer Provisional Hold):** Vé mới cấp cho Buyer được gắn cờ `IN_SETTLEMENT_BUFFER` (Buyer sở hữu vé để chuẩn bị đi sự kiện, nhưng **bị khóa tính năng đăng bán lại** trong thời gian này).
- **Công thức tính thời hạn giải ngân kép (Settlement Deadline Formula):**
  `SettlementDeadline = Min(TransferTime + 24h, EventStartTime - CutoffHours)`
  *(Trong đó: `CutoffHours` mặc định là 2 giờ trước khi sự kiện bắt đầu).*
  - **Mốc thời gian chuẩn (T+24h từ lúc sang tên):** Thời hạn 24h được tính **kể từ thời điểm sang tên chính chủ thành công (`TransferTime`)**, không bắt buộc phải đợi sự kiện diễn ra xong mới giải ngân.
  - **Giao dịch cận giờ G (Mua trước sự kiện < 26 giờ):** Thời hạn giải ngân tự động co lại vào mốc trước sự kiện 2 giờ (`EventStartTime - 2h`). Điều này đảm bảo tiền của Seller **vẫn đang nằm an toàn trong Ký quỹ (Escrow LOCKED)** khi Buyer đến cổng sự kiện.
- **Giải ngân an toàn:** Hết `SettlementDeadline`, nếu Buyer không có Khiếu nại (`has_dispute = false`), hệ thống tự động Payout chuyển khoản NAPAS 247 cho Seller và gỡ cờ `IN_SETTLEMENT_BUFFER` trên vé của Buyer.

### 1.4. BR-G04: Tính Toàn Vẹn Giao Dịch Tài Chính (ACID Law)
- **Nội dung:** Mọi thao tác thay đổi trạng thái của `resale_listings` và `escrow_transactions` bắt buộc phải thực thi trong cùng một Database Transaction trên PostgreSQL (`ReadCommitted` hoặc `Serializable`).
- **Ràng buộc:** Nếu bước cập nhật Escrow thất bại, trạng thái Listing phải tự động Rollback về trạng thái trước đó.

### 1.5. BR-G05: Quy Tắc Chuyển Nhượng Nhiều Lần & Neo Trần Giá Gốc BTC (Multi-Resale & Original-Price Ceiling Law)
- **Quyền định đoạt tài sản chính chủ (Multi-Resale Rights):** Sau khi hoàn tất sang tên chính chủ thành công trên hệ thống Ban tổ chức, Buyer trở thành chủ sở hữu hợp pháp duy nhất của vé mới. Do đó, người dùng có quyền tiếp tục đăng bán lại vé nếu gặp sự cố bận đột xuất trước giờ diễn ra sự kiện.
- **Phân rã phạm vi triển khai (Sprint Scope):** Nghiệp vụ chọn vé đã mua để bán lại thuộc phạm vi **Epic MF-03 (ticket SCRUM-98 / task FE-3.4.3)**, không nằm trong các task cơ sở của MF-02.
- **Nguyên tắc Neo Trần Giá Gốc Bất Biến (Anchor to OriginalPrice):** Biên độ giá bán lại ở **mọi lần giao dịch (F0, F1, F2...) bắt buộc luôn luôn được đo và kiểm soát theo Giá vé gốc niêm yết của Ban tổ chức (`OriginalPrice`)**:
  `0 < ResalePrice <= Truncate(OriginalPrice * (1 + MarkupPercentage / 100))`
- **Bảo vệ người hâm mộ tối thượng:**
  - Dù vé có sang tay bao nhiêu lần, người mua sau cùng **luôn luôn được bảo đảm mua vé với giá trong biên độ trần cho phép so với giá gốc BTC**.
  - Triệt tiêu hoàn toàn vấn nạn phe vé chợ đen thổi giá lên gấp 2-3 lần.
  - Chi phí giao dịch sàn (biểu phí 2 đầu) tự động làm hao mòn lợi nhuận nếu ai đó cố tình mua đi bán lại liên tục, tự triệt tiêu động cơ lướt sóng vé.
- **Điều kiện mở khóa bán lại (Settlement Cooldown):** Để triệt tiêu rủi ro "Tranh chấp dây chuyền" (Chained Dispute Loop), chủ vé mới chỉ được phép đăng bán lại sau khi giao dịch mua trước đó đã hoàn tất thời gian đệm ký quỹ an toàn (`IN_SETTLEMENT_BUFFER` hết hạn và Escrow đã giải ngân xong).

---

## 2. Quy Tắc Vòng Đời Đăng Bán Lại (Resale Listing Rules)

| Mã luật | Tên quy tắc | Mô tả chi tiết |
| :--- | :--- | :--- |
| **BR-L01** | Trạng thái hợp lệ để đăng bán | Chỉ những vé có trạng thái `VALID` trên hệ thống Ban tổ chức mới được phép xác thực và chuyển sang `LOCKED_FOR_RESALE`. Vé đã qua sử dụng (`USED`), đã hết hạn (`EXPIRED`) hoặc bị hủy (`CANCELLED`) sẽ bị từ chối ngay từ bước gửi OTP. |
| **BR-L02** | Chống bán trùng lặp (Anti-Double Listing) | Mỗi mã vé gốc (`original_ticket_code`) chỉ được phép tồn tại duy nhất 1 bản ghi có trạng thái `VERIFIED` hoặc `TRANSACTING` trên sàn (được bảo vệ kép bằng PostgreSQL Partial Unique Index). |
| **BR-L03** | Chế độ Bán riêng tư (Private Sale Mode) | Người bán có thể chọn bán kín cho bạn bè/người quen (`is_private = true`). Hệ thống sinh `private_access_token` bí mật (32 ký tự hex). Vé Private ẩn khỏi Marketplace và chỉ truy cập được qua Link bí mật. <br>• **Chính sách phí:** Áp dụng biểu phí đồng nhất với vé Public (người dùng trả phí không phải để tìm khách, mà để mua dịch vụ **Ký quỹ Escrow bảo hiểm dòng tiền** và **gRPC sang tên chính chủ 100% từ BTC** mà giao dịch ngoài không thể có). |
| **BR-L04** | Thời hạn chốt giao dịch (Resale Cut-off Time) | Các tin đăng bán sẽ tự động đóng trước giờ khai mạc sự kiện **2 giờ** (Cut-off time) để đảm bảo kịp tiến hành thủ tục chuyển giao quyền sở hữu và giải ngân theo công thức kép. |
| **BR-L05** | Quyền hủy niêm yết của Seller | Người bán có quyền hủy tin đăng bất kỳ lúc nào nếu vé đang ở trạng thái `VERIFIED` (chưa có người mua thanh toán). Khi hủy thành công, vé gốc bên BTC được tự động mở khóa về `VALID`. |
| **BR-L06** | Khóa tạm giữ vòng đời vé (Provisional Ticket Hold) | Vé sau khi chuyển quyền sở hữu sang Buyer sẽ bị khóa đăng bán lại trong suốt thời gian đệm ký quỹ (`IN_SETTLEMENT_BUFFER`), ngăn ngừa việc tạo bẫy tranh chấp dây chuyền giữa nhiều người mua liên tiếp. |

---

## 3. Quy Tắc Ký Quỹ & Thanh Toán (Escrow & Payment Rules)

| Mã luật | Tên quy tắc | Mô tả chi tiết |
| :--- | :--- | :--- |
| **BR-E01** | Khóa giữ chỗ (Purchase Hold Timeout) | Khi Buyer nhấn "Mua vé", Listing chuyển sang `TRANSACTING` và khóa giữ chỗ trong tối đa **10 phút** để Buyer quét mã thanh toán VietQR. Quá 10 phút không nhận được Webhook thanh toán, vé tự động nhả về trạng thái `VERIFIED`. |
| **BR-E02** | Tính bất biến thanh toán (Payment Idempotency) | Mỗi Webhook thanh toán gửi kèm mã tham chiếu độc nhất (`transaction_reference`). Xử lý lặp lại Webhook cùng mã sẽ trả về kết quả thành công cũ mà không tạo thêm giao dịch ký quỹ mới. |
| **BR-E03** | Đổi chủ trước khi giải ngân (Transfer-before-Release) | Tiền ký quỹ chỉ được phép giải ngân (Status: `RELEASED`) khi mã vé mới đã được cấp thành công cho Buyer (`status = SOLD` và `owner_email = buyer_email`). |
| **BR-E04** | Quy tắc thụ hưởng ngân hàng (Payout Beneficiary) | Lệnh Payout giải ngân cho Seller chỉ được gửi đi khi Seller đã liên kết tài khoản ngân hàng hợp lệ (`UserBankAccount`), bao gồm: Mã ngân hàng (BankCode), Số tài khoản (AccountNumber), Tên chủ tài khoản (AccountHolderName) trùng khớp với danh tính tài khoản. |
| **BR-E05** | Tách rời trách nhiệm giải ngân (Decoupling & Non-blocking) | Trading Service chỉ quản lý cờ trạng thái Escrow. Việc thực thi chuyển tiền thực tế được bàn giao hoàn toàn cho `Payout Worker` xử lý bất đồng bộ ngầm, đảm bảo hệ thống không bị nghẽn (non-blocking) nếu phía Ngân hàng/NAPAS phản hồi chậm hoặc bảo trì. |
| **BR-E06** | Tính bất biến & Thử lại lệnh Payout (Idempotency & Retry Policy) | Mỗi lệnh Payout bắt buộc gắn kèm `idempotency_key` duy nhất (dựa trên `payout_code` / quan hệ 1-1 `escrow_id` có Unique Index). Khi worker gặp sự cố timeout từ phía ngân hàng, áp dụng cơ chế Retry an toàn (tối đa 3 lần với Exponential Backoff, ghi vết `retry_count`), triệt tiêu 100% rủi ro giải ngân trùng tiền khi worker quét lại. |

---

## 4. Quy Tắc Xử Lý Tranh Chấp & Khiếu Nại Thủ Công (Manual Dispute Resolution Rules)

> [!IMPORTANT]
> **Ranh giới hệ thống (System Boundaries):** TicketShield là sàn trung gian thứ cấp, **tuyệt đối không can thiệp, không kiểm soát và không chỉnh sửa** hệ thống phần cứng cổng soát vé (Turnstiles / Barcode Scanners) của Ban tổ chức. Toàn bộ quy trình xử lý Dispute là **quy trình điều tra thủ công có con người tham gia (Human-in-the-loop / Manual Review)**.

| Mã luật | Tên quy tắc | Mô tả chi tiết |
| :--- | :--- | :--- |
| **BR-D01** | Báo cáo khiếu nại sự cố tại cổng (Gate Dispute Submission) | Khi gặp sự cố tại cổng (bị từ chối vào cổng, máy quét báo lỗi mã vé không hợp lệ), Buyer gửi báo cáo khiếu nại thủ công ngay trên ứng dụng: <br>- Nhập lý do sự cố (`reason`).<br>- Tải lên bằng chứng xác thực (`evidence_url`): ảnh chụp màn hình máy quét báo lỗi, hoặc ảnh chụp biên bản sự cố viết tay do nhân viên soát vé tại hiện trường lập và ký nhận. |
| **BR-D02** | Đóng băng ký quỹ tự động tức thì (Instant Escrow Freeze) | Khi Buyer gửi Dispute trong lúc giao dịch **chưa giải ngân** (tiền đang ở `LOCKED`):<br>• Hệ thống tự động chuyển trạng thái Escrow sang **`DISPUTED`** (`has_dispute = true`).<br>• Bộ đếm tự động giải ngân lập tức bị đình chỉ, ngăn chặn tuyệt đối lệnh Payout chuyển tiền sang tài khoản Seller.<br>• Vé tiếp tục bị giữ cờ `IN_SETTLEMENT_BUFFER = true` để ngăn chặn Buyer đem vé tranh chấp đi bán lại. |
| **BR-D03** | Nguồn tiền hoàn trả & Cơ chế Payout Refund (Escrow Refund Source) | **Nguồn tiền hoàn trả lấy trực tiếp từ Quỹ Ký quỹ (Escrow):** Vì giao dịch chưa giải ngân, 100% tiền thanh toán của Buyer vẫn đang nằm trong két Escrow của TicketShield, chưa hề chuyển cho Seller.<br>• Sau khi Admin đối soát xác nhận vé bị lỗi thật từ phía BTC/Seller, Admin bấm duyệt hoàn tiền.<br>• Hệ thống kích hoạt lệnh hoàn tiền 100% từ Quỹ Ký quỹ về số tài khoản của Buyer qua cổng thanh toán ngân hàng.<br>• Đổi trạng thái Escrow sang **`REFUNDED`**, vé chuyển sang `CANCELLED_DISPUTED`. |
| **BR-D04** | Đối soát nhật ký cổng BTC (Manual Investigation) | Admin / CSKH của TicketShield đóng vai trò trọng tài độc lập:<br>- Thẩm định bằng chứng ảnh chụp máy quét lỗi / biên bản hiện trường do Buyer cung cấp.<br>- Tra cứu lịch sử quét vé trên API đối soát chỉ đọc (Read-Only) của Ban tổ chức (`GET /api/v1/gate/access-logs` hoặc qua gRPC) để kiểm tra thời điểm quét và mã phản hồi của máy quét tại cổng. |
| **BR-D05** | Phán quyết & Tính dứt điểm giao dịch (Finality & Audit Logging) | **Phán quyết của Admin:**<br>- **Chấp thuận (Approve):** Nếu vé bị quét trước thời điểm sang tên hoặc lỗi từ phía BTC -> Duyệt hoàn 100% tiền cho Buyer từ Escrow, khóa quyền bán của Seller.<br>- **Bác bỏ (Reject):** Nếu chứng minh được Buyer đã quét vé vào xem sự kiện bình thường nhưng gian dối báo lỗi -> Bác bỏ khiếu nại, tiếp tục tiến trình Payout cho Seller.<br>• **Tính dứt điểm giao dịch (Transaction Done):** Khi vé đã hết thời hạn đệm và giải ngân thành công (`RELEASED`), giao dịch chuyển sang trạng thái hoàn tất (`DONE`). Hệ thống đóng tính năng khiếu nại tự động trên giao diện đơn hàng để bảo đảm tính dứt điểm tài chính. Mọi khiếu nại phát sinh sau đó được chuyển sang luồng CSKH đặc biệt.<br>• Mọi phán quyết của Admin bắt buộc ghi nhật ký lý do (`admin_note`) lưu vào bảng `audit_logs`. |
