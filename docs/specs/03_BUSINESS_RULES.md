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
  1. Người bán chỉ cần nhập Mã vé gốc (`ticket_code`) và chọn Ban tổ chức (`organizer_id`) -> TicketShield gọi gRPC `RequestTicketOtp` sang MockOrganizer.
  2. MockOrganizer kiểm tra vé tồn tại (`VALID`), lấy email chủ vé gốc trong hệ thống BTC để gửi OTP 6 số (hiệu lực 5 phút) qua SMTP.
  3. Người bán nhập đúng OTP -> TicketShield gọi gRPC `ConfirmOtpAndLock`. MockOrganizer chuyển trạng thái vé gốc thành **`LOCKED_FOR_RESALE`** (ngăn chặn việc mang vé đi bán chỗ khác hoặc quét vào cổng trước), đồng thời trả về `VerificationReceipt` chứa thông tin vé chính thức từ BTC (Giá gốc `OriginalPrice`, Khán đài/Ghế `SeatZone`, Mã sự kiện, Hạng vé).
  4. **Triệt tiêu nguy cơ khai man:** Người bán **hoàn toàn không tự nhập** hạng vé, khán đài hay thông tin sự kiện. Hệ thống tự động map dữ liệu chính thức từ BTC vào phiên bán.
- **Hủy tin bán:** Nếu người bán đổi ý hủy niêm yết (khi vé chưa bán), TicketShield gửi gRPC `ReleaseTicketLock` để MockOrganizer hoàn vé về trạng thái `VALID`.

### 1.3. BR-G03: Quy Tắc Giải Ngân Điều Kiện Kép & Kích Hoạt Kép (Dual-Condition Settlement & Dual-Trigger Law)
- **Nội dung:** Tiền thanh toán của Buyer và Vé chuyển nhượng của Buyer đều được đặt trong trạng thái tạm giữ an toàn:
  - **Khóa tiền (Seller Escrow):** Tiền nằm trong Escrow với trạng thái `LOCKED`.
  - **Khóa vé (Buyer Provisional Hold):** Vé mới cấp cho Buyer được gắn cờ `IN_SETTLEMENT_BUFFER` (Buyer sở hữu vé chính chủ, nhưng **bị khóa tính năng đăng bán lại** trong thời gian đệm này).
- **Phân rã 2 Kịch Bản Vận Hành Cốt Lõi:**
  1. **Kịch bản A — Giao dịch khi sự kiện còn xa (Thời gian mua cách giờ G > 24h):**
     - **Khoảng đệm 24h đối soát giao nhận vé chính chủ (T+24h Verification Window):** `UnlockAt = TransferTime + 24h`.
     - Vì hệ thống đã gọi gRPC hủy vé cũ và cấp vé mới chính chủ đứng tên Buyer bên BTC, Seller không thể can thiệp hay gian lận vé được nữa. 24h này là thời gian để Buyer kiểm tra trên app/email của BTC (đúng họ tên, đúng sơ đồ/khán đài/hạng vé).
     - Hết 24h kể từ thời điểm sang tên (`TransferTime`), nếu Buyer không có khiếu nại (`has_dispute = false`), hệ thống tự động Payout chuyển khoản NAPAS 247 cho Seller và gỡ cờ `IN_SETTLEMENT_BUFFER`.
     - *Cơ chế chốt chặn tranh chấp 2 chiều nguyên tử (Bidirectional Atomic CAS):*
       - **Phía Giải ngân (Core Consumer / Sweeper):**
         `UPDATE escrow_transactions SET status = 'Releasing' WHERE id = @id AND status = 'Locked' AND release_at <= now();`
       - **Phía Khiếu nại (CreateDisputeCommandHandler):** Bắt buộc chạy trong cùng 1 Database Transaction:
         `UPDATE escrow_transactions SET status = 'Disputed' WHERE id = @id AND status = 'Locked';`
         Nếu trả về 0 row (do đơn đã `Releasing` hoặc `Released`), hệ thống lập tức từ chối khiếu nại (`DisputeDeadlineExpiredException`). Chỉ khi cập nhật thành công 1 row mới ghi nhận bản ghi vào bảng `disputes`.
     - *Cơ chế hẹn giờ hướng dữ liệu (DB-Driven Timer via release_at Index):* Hệ thống không sử dụng RabbitMQ Delayed Exchange để treo timer 24h (tránh rủi ro mất message và giới hạn lưu trữ Mnesia). Thay vào đó, Core lưu mốc `release_at` có đánh Composite Index `(status, release_at)`. Tiến trình Sweeper quét định kỳ mỗi phút:
       `SELECT id FROM escrow_transactions WHERE status = 'Locked' AND release_at <= now();`
       Khi Buyer khiếu nại, trạng thái chuyển sang `Disputed` -> Bản ghi tự động không còn khớp câu query, vô hiệu hóa tiến trình giải ngân ngay lập tức mà không cần logic hủy timer phức tạp.
     - *Tính nhất quán giao dịch (Transactional Outbox Pattern):* Khi CAS thành công sang `Releasing`, message `PayoutRequestedCommand` được lưu vào bảng `outbox_messages` trong cùng Transaction DB, triệt tiêu rủi ro đơn kẹt `Releasing` nếu tiến trình bị crash trước khi dispatch sang STM.
     - *Ngoại lệ quét cổng sớm:* Nếu sự kiện diễn ra sớm hơn hoặc Buyer vào cổng sớm trong 24h, ngay khi vé đổi sang `USED`, hệ thống giải ngân ngay lập tức.
  2. **Kịch bản B — Giao dịch cận giờ G (Thời gian mua cách giờ G <= 24h đến hạn cut-off 2h):**
     - **Hạn chót ngừng giao dịch (Cut-off Time):** Hệ thống đóng toàn bộ chức năng đăng bán và mua vé mới trước giờ khai mạc sự kiện **đúng 2 giờ** (`EventStartTime - 2h`) để Ban tổ chức chốt danh sách khách vào cổng.
     - **Nguyên tắc bảo lưu ký quỹ tuyệt đối (Không giải ngân trước giờ diễn):** Tuyệt đối **KHÔNG** giải ngân tiền cho Seller trước giờ khai mạc để triệt tiêu rủi ro Người mua tới cổng bị từ chối vé mà Seller đã nhận tiền bỏ trốn.
     - **Giải ngân tức thì theo Sự kiện vào cổng (Gate Entry Trigger):** Tiền ký quỹ được giữ an toàn 100% trong két cho đến khi Buyer đến sự kiện và máy quét tại cổng BTC chấp thuận mã vé thành công (`scannedAt` ghi nhận, vé chuyển sang `USED` với kết quả `VALID_ENTRY`) -> Hệ thống mới kích hoạt lệnh Payout giải ngân cho Seller.
     - **Kênh dự phòng (Buyer không đi quét vé):** Nếu Buyer bận việc đột xuất không đến quét vé tại cổng, lệnh giải ngân sẽ được thực thi sau khi sự kiện kết thúc an toàn nếu không phát sinh bất kỳ khiếu nại nào.
- **Xử lý khiếu nại sự cố:**
  - Trong vòng 24h (Kịch bản A) hoặc trước khi quét cổng thành công (Kịch bản B), nếu Buyer gửi khiếu nại -> Escrow lập tức chuyển sang `DISPUTED` và đình chỉ toàn bộ bộ đếm giải ngân. Tiền tiếp tục nằm an toàn 100% trong két ký quỹ của sàn.

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

### 1.6. BR-G06: Quy Tắc Bán Theo Gói / Combo Không Tách Rời (All-or-Nothing Bundle Listing Law)
- **Bản chất nghiệp vụ:** Người bán có nhiều vé (kể cả vé ghế liền kề hoặc vé ở các khán đài khác nhau) có quyền gộp chung vào 1 tin đăng bán dạng **Gói / Combo trọn bộ**.
- **Nguyên tắc không thể tách rời (Indivisible / All-or-Nothing):** Người mua bắt buộc phải mua trọn gói toàn bộ các vé con trong Combo. Hệ thống tuyệt đối không cho phép xé lẻ mua từng vé đơn lẻ để bảo vệ người bán khỏi rủi ro bán được 1 vé và bị ế vé còn lại.
- **Ràng buộc Trần giá Gói (Bundle Price Ceiling):** Tổng giá bán lại của cả gói phải tuân thủ nghiêm ngặt luật trần giá tổng:
  `TotalResalePrice <= Sum(PriceCeiling của từng vé con)`
- **Toàn vẹn giao dịch All-or-Nothing qua gRPC:** Khi thanh toán thành công, hệ thống kích hoạt đổi chủ đồng thời cho tất cả các vé con trong gói. Nếu có bất kỳ vé nào bị lỗi bên Ban tổ chức, toàn bộ giao dịch mua gói vé phải được Rollback 100% và hoàn tiền cho Người mua.
- **Phân kỳ triển khai (Roadmap):** Nghiệp vụ bán trọn gói Combo vé thuộc phạm vi mở rộng kiến trúc chuyển nhượng ở Sprint tiếp theo (hậu Hardening Sprint 2). Xem chi tiết tại [07_MULTI_TICKET_BUNDLE_SPEC.md](file:///d:/Capstone/docs/specs/07_MULTI_TICKET_BUNDLE_SPEC.md).

---

## 2. Quy Tắc Vòng Đời Đăng Bán Lại (Resale Listing Rules)

| Mã luật | Tên quy tắc | Mô tả chi tiết |
| :--- | :--- | :--- |
| **BR-L01** | Trạng thái hợp lệ để đăng bán | Chỉ những vé có trạng thái `VALID` trên hệ thống Ban tổ chức mới được phép xác thực và chuyển sang `LOCKED_FOR_RESALE`. Vé đã qua sử dụng (`USED`), đã hết hạn (`EXPIRED`) hoặc bị hủy (`CANCELLED`) sẽ bị từ chối ngay từ bước gửi OTP. |
| **BR-L02** | Chống bán trùng lặp (Anti-Double Listing) | Mỗi mã vé gốc (`original_ticket_code`) chỉ được phép tồn tại duy nhất 1 bản ghi có trạng thái `VERIFIED` hoặc `TRANSACTING` trên sàn (được bảo vệ kép bằng PostgreSQL Partial Unique Index). |
| **BR-L03** | Chế độ Bán riêng tư (Private Sale Mode) | Người bán có thể chọn bán kín cho bạn bè/người quen (`is_private = true`). Hệ thống sinh `private_access_token` bí mật (32 ký tự hex). Vé Private ẩn khỏi Marketplace và chỉ truy cập được qua Link bí mật. <br>• **Chính sách phí:** Áp dụng biểu phí đồng nhất với vé Public (người dùng trả phí không phải để tìm khách, mà để mua dịch vụ **Ký quỹ Escrow bảo hiểm dòng tiền** và **gRPC sang tên chính chủ 100% từ BTC** mà giao dịch ngoài không thể có). |
| **BR-L04** | Thời hạn chốt giao dịch (Resale Cut-off Time) | Hệ thống tự động đóng toàn bộ tính năng đăng bán và mua vé trước giờ khai mạc sự kiện **đúng 2 giờ** (`EventStartTime - 2h`) để Ban tổ chức chốt danh sách vé vào cổng. **Lưu ý:** Mốc 2h này chỉ là hạn chót ngừng nhận giao dịch, TUYỆT ĐỐI KHÔNG PHẢI là mốc giải ngân tiền cho Seller (tiền ký quỹ chỉ được giải ngân sau khi vé quét cổng thành công `USED` hoặc sau khi sự kiện kết thúc an toàn). |
| **BR-L05** | Quyền hủy niêm yết của Seller | Người bán có quyền hủy tin đăng bất kỳ lúc nào nếu vé đang ở trạng thái `VERIFIED` (chưa có người mua thanh toán). Khi hủy thành công, vé gốc bên BTC được tự động mở khóa về `VALID`. |
| **BR-L06** | Khóa tạm giữ vòng đời vé (Provisional Ticket Hold) | Vé sau khi chuyển quyền sở hữu sang Buyer sẽ bị khóa đăng bán lại trong suốt thời gian đệm ký quỹ (`IN_SETTLEMENT_BUFFER`), ngăn ngừa việc tạo bẫy tranh chấp dây chuyền giữa nhiều người mua liên tiếp. |

---

## 3. Quy Tắc Ký Quỹ & Thanh Toán (Escrow & Payment Rules)

| Mã luật | Tên quy tắc | Mô tả chi tiết |
| :--- | :--- | :--- |
| **BR-E01** | Khóa giữ chỗ (Purchase Hold Timeout) | Khi Buyer nhấn "Mua vé", Listing chuyển sang `TRANSACTING` và khóa giữ chỗ trong tối đa **10 phút** để Buyer quét mã thanh toán VietQR. Quá 10 phút không nhận được Webhook thanh toán, vé tự động nhả về trạng thái `VERIFIED`. |
| **BR-E02** | Tính bất biến thanh toán (Payment Idempotency) | Mỗi Webhook thanh toán gửi kèm mã tham chiếu độc nhất (`transaction_reference`). Xử lý lặp lại Webhook cùng mã sẽ trả về kết quả thành công cũ mà không tạo thêm giao dịch ký quỹ mới. |
| **BR-E03** | Đổi chủ trước khi giải ngân & Kích hoạt kép (Transfer-before-Release & Dual-Trigger) | Tiền ký quỹ chỉ được phép giải ngân (Status: `RELEASED`) khi mã vé mới đã được cấp thành công cho Buyer (`status = SOLD` và `owner_email = buyer_email`). Lệnh giải ngân được kích hoạt theo cơ chế kép: 1) Tức thì ngay khi vé ghi nhận quét cổng thành công (`ticket_status == 'USED'`), hoặc 2) Hết hạn đếm ngược T+24h nếu không có khiếu nại. |
| **BR-E04** | Quy tắc thụ hưởng ngân hàng (Payout Beneficiary) | Lệnh Payout giải ngân cho Seller chỉ được gửi đi khi Seller đã liên kết tài khoản ngân hàng hợp lệ (`UserBankAccount`), bao gồm: Mã ngân hàng (BankCode), Số tài khoản (AccountNumber), Tên chủ tài khoản (AccountHolderName) trùng khớp với danh tính tài khoản. |
| **BR-E05** | Tách rời trách nhiệm giải ngân & Atomic CAS (Decoupling & Non-blocking) | Core sở hữu bộ đếm giờ nội bộ và ra quyết định bằng lệnh nguyên tử CAS. Việc thực thi chuyển tiền thực tế được bàn giao hoàn toàn cho `Settlement Service (STM)` xử lý độc lập qua NAPAS 247 (kèm snapshot, không gọi ngược Core), đảm bảo hệ thống phi phong tỏa và chống race condition ở giây cuối cùng. |
| **BR-E06** | Tính bất biến & Thử lại lệnh Payout (Idempotency & Retry Policy) | Mỗi lệnh Payout bắt buộc gắn kèm `idempotency_key` duy nhất (dựa trên `payout_code` / quan hệ 1-1 `escrow_id` có Unique Index). Khi worker gặp sự cố timeout từ phía ngân hàng, áp dụng cơ chế Retry an toàn (tối đa 3 lần với Exponential Backoff, ghi vết `retry_count`), triệt tiêu 100% rủi ro giải ngân trùng tiền khi worker quét lại. |

---

## 4. Quy Tắc Xử Lý Tranh Chấp & Thu Thập Bằng Chứng Tự Động (Dispute & Automated Harvest Rules)

> [!IMPORTANT]
> **Ranh giới hệ thống (System Boundaries):**
> 1. Do quy trình gRPC `TransferOwnership` hủy vé cũ và cấp vé mới 100% chính chủ từ BTC, **hệ thống KHÔNG TỒN TẠI khái niệm "vé giả" từ Seller**.
> 2. Sự cố phát sinh tại cổng chỉ gồm:
>    - Trục trặc kỹ thuật giữa hệ thống và máy quét của Ban tổ chức (máy quét chưa đồng bộ kịp mã vé mới, lỗi firmware).
>    - Vé bị quét trước thời điểm chuyển giao quyền sở hữu (`transferred_at`).
>    - Khiếu nại gian dối từ Người mua (**Friendly Fraud**): Người mua đã quét mã QR vào sự kiện thành công nhưng sau đó cố tình báo lỗi tại cổng để đòi hoàn tiền ký quỹ.
> 3. Hệ thống triển khai tiến trình tự động `DisputeHarvestWorker` đối soát nhật ký quét cổng để hỗ trợ Admin ra quyết định khách quan.

| Mã luật | Tên quy tắc | Mô tả chi tiết |
| :--- | :--- | :--- |
| **BR-D01** | Báo cáo khiếu nại sự cố (Dispute Submission) | Buyer có quyền gửi báo cáo khiếu nại ngay trên ứng dụng nếu gặp trục trặc tại cổng soát vé sự kiện.<br>• Các mã lý do chính thức (`DisputeReasonCode`):<br>  - `TicketInvalid`: Máy quét báo vé không hợp lệ hoặc không nhận diện mã vé mới.<br>  - `DuplicateEntry`: Máy quét báo vé đã được quét trước thời điểm Buyer nhận vé.<br>• Yêu cầu nộp: Chọn mã lý do, nhập mô tả sự cố (`reason`), tải lên tệp ảnh bằng chứng xác thực (`evidence`) qua API `POST /api/v1/disputes/{id}/evidence`. |
| **BR-D02** | Đóng băng ký quỹ tự động tức thì (Instant Escrow Freeze) | Khi Buyer gửi Dispute trong lúc giao dịch **chưa giải ngân** (tiền đang ở `LOCKED`):<br>• Thực thi Atomic CAS chuyển trạng thái Escrow sang **`DISPUTED`** (`has_dispute = true`).<br>• Bộ đếm tự động giải ngân lập tức bị đình chỉ, ngăn chặn tuyệt đối lệnh Payout chuyển tiền sang tài khoản Seller.<br>• Vé tiếp tục bị giữ cờ `IN_SETTLEMENT_BUFFER = true` để ngăn chặn Buyer đem vé tranh chấp đi bán lại. |
| **BR-D03** | Nguồn tiền hoàn trả lấy từ Quỹ Ký quỹ (Escrow Refund Source) | **Nguồn tiền hoàn trả lấy trực tiếp từ Quỹ Ký quỹ (Escrow):** Vì giao dịch chưa giải ngân, 100% tiền thanh toán của Buyer vẫn đang nằm an toàn trong két Escrow của TicketShield, chưa hề chuyển cho Seller.<br>• Khi Admin duyệt hoàn tiền (`RefundBuyer`): Hệ thống kích hoạt lệnh hoàn tiền 100% từ Quỹ Ký quỹ về tài khoản ngân hàng của Buyer.<br>• Đổi trạng thái Escrow sang **`REFUNDED`**, vé chuyển sang `CANCELLED_DISPUTED`. |
| **BR-D04** | Tự động thu thập log cổng & Khuyến nghị phân xử (Automated Harvest & Recommendation) | Khi nhận khiếu nại, Core lưu Outbox Message `DisputeHarvestRequested`. Background Worker `DisputeHarvestWorker` tự động kích hoạt:<br>1. Gọi API đối soát của BTC (`GET /api/v1/gate/access-logs?ticketCode={newTicketCode}`) để lấy toàn bộ lịch sử quét của mã vé mới.<br>2. Lưu snapshot nhật ký (`gate_log_snapshot`) bất biến vào database.<br>3. Chạy động cơ phân tích `DisputeRecommendation.Decide`:<br>   - Nếu phát hiện có lượt quét `SUCCESS` tại hoặc sau mốc `transferred_at` -> Đề xuất **`RECOMMEND_REJECT_FRAUD`** (Buyer đã vào xem sự kiện bình thường).<br>   - Nếu vé bị quét trước `transferred_at` hoặc máy quét trả về `INVALID_TICKET` / `REVOKED` -> Đề xuất **`RECOMMEND_REFUND`** (Vé lỗi kỹ thuật hoặc bị chiếm dụng trước).<br>4. Hiển thị khuyến nghị và đầy đủ bằng chứng lên Admin Dashboard để Admin đối soát nhanh chóng. |
| **BR-D05** | Phán quyết của Admin & Tính dứt điểm giao dịch (Admin Finality & Audit Logging) | **Phán quyết dứt điểm của Admin (`POST /api/v1/disputes/{id}/resolution`):**<br>- **Chấp thuận (`RefundBuyer`):** Nếu xác nhận lỗi kỹ thuật từ phía BTC / vé lỗi -> Duyệt hoàn 100% tiền cho Buyer từ Escrow.<br>- **Bác bỏ (`ReleaseSeller`):** Nếu chứng minh được Buyer đã quét vé vào xem sự kiện bình thường nhưng gian dối báo lỗi -> Bác bỏ khiếu nại, tiếp tục tiến trình Payout giải ngân cho Seller.<br>• **Tính dứt điểm giao dịch:** Khi Escrow đã giải ngân (`RELEASED`), giao dịch chuyển sang hoàn tất (`DONE`). Hệ thống đóng cổng khiếu nại tự động để bảo đảm tính dứt điểm tài chính.<br>• Mọi quyết định của Admin bắt buộc ghi nhật ký lý do (`admin_note`) và lưu vết kiểm toán vào hệ thống. |
| **BR-D06** | SLA Xử lý khiếu nại 72h (Dispute Resolution SLA) | Toàn bộ khiếu nại phải được Admin/CSKH đối soát và ra phán quyết trong tối đa **72 giờ** (3 ngày làm việc). Quá 48h chưa xử lý, hệ thống tự động kích hoạt cảnh báo High-Priority Escalation lên Dashboard Quản trị cấp cao. |
| **BR-D07** | Chế tài gian lận khiếu nại & Uy tín người dùng (Friendly Fraud Penalty & Reputation) | **Nguyên tắc phân định chế tài uy tín:**<br>• **Không phạt Seller vì 'vé giả':** Hệ thống không trừ điểm Seller vì lý do vé giả (do gRPC đã triệt tiêu hoàn toàn vé giả).<br>• **Xử phạt nghiêm khắc Buyer gian dối:** Buyer cố tình mở Dispute khống khi nhật ký cổng xác nhận vé đã quét thành công (`RECOMMEND_REJECT_FRAUD`) sẽ bị khóa tài khoản 6 tháng, hạ điểm tín nhiệm người dùng và cấm giao dịch trên sàn.<br>• **Xử phạt Seller vi phạm vận hành:** Seller cố tình vi phạm tần suất hủy bán hoặc lạm dụng nền tảng sẽ bị hạ cấp hạn mức niêm yết vé. |
