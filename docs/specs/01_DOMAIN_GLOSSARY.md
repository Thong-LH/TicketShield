# TicketShield AI - Domain Glossary & Actor Matrix

Tài liệu định nghĩa chuẩn xác các thuật ngữ chuyên ngành (Domain Glossary) và Ma trận Phân quyền & Trách nhiệm (Actor & Responsibility Matrix) phục vụ đồ án tốt nghiệp **TicketShield AI**.

---

## 1. Domain Glossary (Từ điển Thuật ngữ)

### 1.1. Lĩnh Vực Bán Vé Sự Kiện (Event Ticketing)

| Thuật ngữ | Định nghĩa nghiệp vụ |
| :--- | :--- |
| **Event Organizer (Ban tổ chức - BTC)** | Đơn vị chủ trì sự kiện (âm nhạc, thể thao, hội nghị), sở hữu quyền phát hành vé gốc, quyết định chính sách trần giá và điều kiện chuyển nhượng. |
| **Ticketing Partner / Platform** | Nền tảng phân phối vé sơ cấp được BTC ủy quyền (ví dụ: Ticketbox, VietID, Ticketmaster) chịu trách nhiệm tạo và bán vé ban đầu. |
| **Venue (Địa điểm tổ chức)** | Không gian diễn ra sự kiện (sân vận động, nhà hát), nơi đặt các cổng soát vé (Gate / Turnstile). |
| **Primary Ticket Sale (Bán vé sơ cấp)** | Giai đoạn mở bán vé đợt đầu trực tiếp từ BTC/Ticketing Partner đến khán giả theo giá niêm yết ban đầu (Face Value / Original Price). |
| **Secondary Resale (Thị trường thứ cấp)** | Thị trường nơi những người đã mua vé hợp lệ sang nhượng lại vé cho người khác do không có nhu cầu tham gia. |
| **Ticket Ownership (Quyền sở hữu vé)** | Quyền hợp pháp gắn liền với danh tính người mua (thông qua Email, SĐT, Số CCCD). Chỉ chủ sở hữu chính thức mới có quyền chuyển nhượng hoặc vào cổng. |
| **Ticket Transfer (Chuyển giao quyền sở hữu)** | Hành động hủy mã vé cũ của người bán và cấp phát mã vé hoàn toàn mới (QR code/Barcode mới) cho người mua trong hệ thống dữ liệu của BTC. |
| **Ticket Lifecycle** | Vòng đời của vé: `Created` (Đã tạo) -> `Valid` (Hợp lệ) -> `Listed` (Đang rao bán) -> `Transferred` (Đã chuyển nhượng) -> `Used` (Đã quét vào cổng) / `Cancelled` (Bị hủy) / `Expired` (Hết hạn). |

---

### 1.2. Vấn Nạn Phe Vé & Bot (Ticket Scalping & Bots)

| Thuật ngữ | Định nghĩa nghiệp vụ |
| :--- | :--- |
| **Ticket Scalping (Phe vé / Đầu cơ vé)** | Hành vi mua gom vé sự kiện số lượng lớn với mục đích bán lại với giá chênh lệch cao gấp nhiều lần (thổi giá) trên thị trường chợ đen. |
| **Automated Ticket Bot** | Các kịch bản phần mềm tự động (script/headless browser) giả lập thao tác con người với tốc độ mili-giây để chiếm chỗ và quét sạch vé ngay khi mở bán. |
| **Purchase Velocity (Tốc độ giao dịch)** | Chỉ số đo lường tần suất gửi request mua, thời gian từ lúc tải trang đến lúc submit đơn hàng. Tốc độ < 1.5s là dấu hiệu rõ ràng của bot. |
| **Behavioral Biometrics (Sinh trắc học hành vi)** | Dữ liệu chuyển động chuột (mouse trajectory), độ cong đường đi con trỏ, nhịp gõ phím (keystroke dynamics), scroll entropy để phân biệt người và máy. |
| **Device/Browser Fingerprint** | Chuỗi hash nhận diện đặc trưng phần cứng và trình duyệt (Canvas fingerprint, WebGL, Audio context, User-Agent) để phát hiện môi trường bot headless (Puppeteer, Selenium). |
| **Purchase Outcome** | 3 mức quyết định của bộ lọc AI: `ALLOW` (Cho phép thanh toán ngay), `THROTTLE` (Bắt thử thách Captcha/xếp hàng), `BLOCK` (Chặn request do nghi vấn bot cao). |

---

### 1.3. Ký Quỹ & Chuyển Nhượng P2P (Escrow & Resale)

| Thuật ngữ | Định nghĩa nghiệp vụ |
| :--- | :--- |
| **P2P Marketplace** | Nền tảng kết nối trực tiếp Người bán (Reseller) và Người mua (Buyer) mà không qua trung gian chợ đen truyền thống. |
| **Escrow (Ký quỹ tài chính)** | Cơ chế bên thứ ba trung gian (TicketShield) tạm khóa tiền của Buyer sau khi thanh toán. Tiền chưa được chuyển cho Seller cho đến khi giao dịch hoàn tất an toàn. |
| **Settlement Buffer (Khoảng đệm ký quỹ & đối soát)** | Khoảng đệm thời gian an toàn bảo vệ hai đầu: Với sự kiện còn xa (> 24h), chạy bộ đếm đối soát giao nhận vé chính chủ 24h (T+24h) để giải ngân cho Seller; Với giao dịch cận giờ G (<= 24h), tiền được giữ an toàn trong két Escrow cho đến khi vé được quét vào cổng thành công (USED) hoặc sau khi sự kiện kết thúc. |
| **Price Ceiling Law (Luật trần giá có biên độ)** | Giá bán lại <= OriginalPrice * (1 + MarkupPercentage / 100) (trong đó MarkupPercentage từ 0% đến 10% tùy BTC cấu hình). |
| **Private Resale Trust Model** | Mô hình bán riêng qua Link bảo mật: áp dụng biểu phí đồng nhất với Marketplace vì giá trị cốt lõi khách hàng chi trả là để mua dịch vụ **Ký quỹ Escrow bảo hiểm tiền** và **Hủy vé cũ - cấp vé mới chính chủ 100% từ BTC** mà giao dịch ngoài không thể có. |
| **Multi-Resale with Original-Price Anchor** | Cho phép vé được chuyển nhượng tự do nhiều lần theo quyền sở hữu tài sản chính chủ, với trần giá ở mọi vòng đời luôn neo cố định theo `OriginalPrice` của BTC. Ngăn chặn phe vé chợ đen đẩy giá cao, trong khi người mua sau cùng luôn được bảo đảm mua vé <= giá gốc BTC. |
| **Ticket Lock (`LOCKED_FOR_RESALE`)** | Trạng thái vé gốc trên hệ thống BTC bị khóa lại qua gRPC ngay sau khi chủ vé xác thực OTP thành công, ngăn ngừa tái sử dụng hoặc quét vào cổng trong thời gian niêm yết. |
| **Provisional Ticket Hold (Khóa hai đầu)** | Cơ chế khóa đồng thời cả 2 đầu trong thời gian ký quỹ: Tiền của Seller bị giữ trong Escrow (`LOCKED`), vé của Buyer bị khóa tính năng bán lại (`IN_SETTLEMENT_BUFFER`). |
| **User Bank Account (Tài khoản thụ hưởng)** | Thông tin tài khoản ngân hàng chính chủ của Seller (BankCode, AccountNumber, AccountHolderName) dùng để thực hiện lệnh chuyển khoản tự động qua cổng NAPAS 247 khi giải ngân. |
| **Dynamic VietQR** | Mã QR thanh toán ngân hàng sinh tự động theo chuẩn NAPAS 247 kèm số tiền chính xác và nội dung chuyển khoản độc nhất gắn với transaction ID. |
| **Webhook Reconciliation (Đối soát Webhook)** | Cơ chế nhận tín hiệu xác nhận thanh toán từ ngân hàng/cổng thanh toán để kích hoạt luồng chuyển nhượng vé tức thì. |
| **Idempotency (Tính bất biến/Trùng lặp)** | Đảm bảo một lệnh thanh toán hoặc giải ngân chỉ được xử lý chính xác 1 lần duy nhất, ngăn ngừa thanh toán lặp (Double Payment) hoặc giải ngân lặp (Double Release). |

---

### 1.4. Khiếu Nại, Đối Soát & Phân Xử Tranh Chấp (Dispute & Automated Recommendation)

| Thuật ngữ | Định nghĩa nghiệp vụ |
| :--- | :--- |
| **Dispute (Khiếu nại / Tranh chấp)** | Báo cáo sự cố do Buyer gửi trên app khi gặp trục trặc kỹ thuật tại cổng soát vé sự kiện (ví dụ: máy quét không nhận diện mã vé mới, lỗi firmware cổng) hoặc vé bị quét trước thời điểm chuyển giao. **Lưu ý:** Không tồn tại vé giả từ Seller vì vé mới được BTC cấp trực tiếp 100% qua gRPC. |
| **Gate Access Logs (Nhật ký vào cổng của BTC)** | Dữ liệu quét cổng lưu trữ độc lập thuộc quyền sở hữu của Ban tổ chức / Địa điểm. TicketShield tra cứu Read-Only qua API/gRPC (`gate_access_logs`) làm căn cứ đối soát tuyệt đối. |
| **Dispute Harvest Worker** | Background worker tự động thu thập nhật ký soát vé từ BTC ngay khi có Dispute, lưu snapshot bằng chứng bất biến (`gate_log_snapshot`) vào database. |
| **Automated Dispute Recommendation** | Động cơ phân tích bằng chứng tự động đối soát mốc thời gian sang tên (`transferred_at`) với nhật ký quét cổng: đề xuất `RECOMMEND_REFUND` (lỗi cổng / vé bị quét trước) hoặc `RECOMMEND_REJECT_FRAUD` (Buyer đã vào cổng bình thường). |
| **Friendly Fraud (Gian lận khiếu nại)** | Hành vi Buyer đã quét mã QR vào xem sự kiện thành công nhưng cố tình mở Dispute báo lỗi tại cổng để chiếm đoạt tiền hoàn từ Quỹ Ký quỹ Escrow. |
| **Dispute Freezing (Đóng băng ký quỹ)** | Cơ chế tự động đóng băng lệnh Escrow sang trạng thái `DISPUTED` ngay khi nhận khiếu nại, đình chỉ tức thì luồng giải ngân T+24h để chờ đối soát. |
| **Admin Dispute Resolution (Phán quyết của Admin)** | Quản trị viên / CSKH kiểm tra bằng chứng, tham khảo đề xuất tự động từ hệ thống và nhật ký cổng BTC để bấm nút phán quyết dứt điểm: Hoàn tiền (`RefundBuyer`) hoặc Giải ngân (`ReleaseSeller`). |

---

## 2. Actor & Responsibility Matrix (Ma trận Phân quyền & Trách nhiệm)

| Tác nhân (Actor) | Mô tả vai trò | Trách nhiệm chính trong hệ thống |
| :--- | :--- | :--- |
| **Buyer (Người mua)** | Người dùng tìm mua vé sự kiện trên nền tảng | - Tìm kiếm vé resale hợp lệ trên Marketplace hoặc qua Private Link.<br>- Trải qua bước kiểm tra sinh trắc học hành vi Bot AI.<br>- Thanh toán đơn hàng qua Dynamic VietQR.<br>- Nhận mã vé mới chính chủ 100% cấp trực tiếp từ BTC.<br>- Khiếu nại (Dispute) nếu gặp lỗi kỹ thuật tại cổng sự kiện. |
| **Seller / Reseller (Người bán lại)** | Người sở hữu vé gốc muốn sang nhượng lại | - Chỉ cần nhập Mã vé gốc (`ticket_code`) và chọn Ban tổ chức.<br>- Xác thực OTP do BTC gửi về email chính chủ đã đăng ký vé gốc.<br>- Hệ thống tự động điền toàn bộ thông tin vé từ BTC (không thể khai man thông tin).<br>- Thiết lập giá bán lại (bắt buộc <= Giá trần quy định) và chế độ Public/Private.<br>- Nhận tiền ký quỹ giải ngân tự động sau thời gian đệm T+24h an toàn hoặc khi vé đã quét vào cổng. |
| **Event Organizer (Ban tổ chức sự kiện)** | Đơn vị chủ quản phát hành vé (thông qua MockOrganizer.API) | - Cung cấp API/gRPC xác thực mã vé và thông tin chủ vé gốc.<br>- Gửi mã OTP xác thực chính chủ qua Email.<br>- Thực hiện khóa vé gốc (`LOCKED_FOR_RESALE`) khi xác thực thành công.<br>- Hủy mã vé cũ và phát hành mã vé hoàn toàn mới cho Buyer khi thanh toán thành công.<br>- Ghi nhận và cung cấp nhật ký soát vé (`gate_access_logs`). |
| **TicketShield System (Core Engine)** | Lõi xử lý giao dịch và bảo vệ thị trường | - Quản lý vòng đời tin đăng bán vé (Listing) và gói vé (Bundle).<br>- Khóa tiền ký quỹ (Escrow) theo quy tắc ACID.<br>- Điều phối chuyển giao vé với Organizer qua gRPC Saga.<br>- Background Worker tự động giải ngân sau T+24h hoặc khi quét cổng thành công.<br>- Background Worker tự động thu thập log cổng và đưa ra khuyến nghị xử lý Dispute. |
| **AI & Recommendation Engine** | Hệ thống con trí tuệ nhân tạo (FastAPI & Core Analytics) | - Vòng 1 (Primary / Checkout): Thu thập telemetry client, tính điểm rủi ro `risk_score` (0.0 đến 1.0), phân loại Allow / Throttle / Block.<br>- Vòng 2 (Dispute & Evidence): Phân tích tương quan nhật ký quét cổng và phát hiện dấu hiệu bất thường / gian lận khiếu nại (Friendly Fraud). |
| **Admin (Quản trị viên nền tảng)** | Người kiểm soát và xử lý tranh chấp | - Tra cứu bằng chứng và snapshot nhật ký soát vé cổng BTC.<br>- Xem khuyến nghị phân tích tự động từ hệ thống.<br>- Bấm nút đưa ra phán quyết cuối cùng: Hoàn tiền (Refund) hoặc Giải ngân (Release). |

