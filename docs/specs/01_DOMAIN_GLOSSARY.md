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
| **Settlement Buffer (Giải ngân điều kiện kép)** | Khoảng đệm thời gian an toàn tính theo công thức $\min(\text{TransferTime} + 24\text{h},\; \text{EventStartTime} - 2\text{h})$ để bảo vệ quyền lợi hai đầu: giải ngân cho Seller trước giờ khai mạc nếu mua sát giờ G, hoặc đủ 24h nếu mua trước sự kiện nhiều ngày. |
| **Price Ceiling Law (Luật trần giá có biên độ)** | Giá bán lại $\le \text{OriginalPrice} \times (1 + \Delta_{\text{markup}})$ (trong đó $\Delta_{\text{markup}} \in [0\%, 10\%]$ tùy BTC cấu hình). |
| **Private Resale Trust Model** | Mô hình bán riêng qua Link bảo mật: áp dụng biểu phí đồng nhất với Marketplace vì giá trị cốt lõi khách hàng chi trả là để mua dịch vụ **Ký quỹ Escrow bảo hiểm tiền** và **Hủy vé cũ - cấp vé mới chính chủ 100% từ BTC** mà giao dịch ngoài không thể có. |
| **Multi-Resale with Original-Price Anchor** | Cho phép vé được chuyển nhượng tự do nhiều lần theo quyền sở hữu tài sản chính chủ, với trần giá ở mọi vòng đời luôn neo cố định theo `OriginalPrice` của BTC. Ngăn chặn phe vé chợ đen đẩy giá cao, trong khi người mua sau cùng luôn được bảo đảm mua vé $\le$ giá gốc BTC. |
| **Ticket Lock (`LOCKED_FOR_RESALE`)** | Trạng thái vé gốc trên hệ thống BTC bị khóa lại qua gRPC ngay sau khi chủ vé xác thực OTP thành công, ngăn ngừa tái sử dụng hoặc quét vào cổng trong thời gian niêm yết. |
| **Provisional Ticket Hold (Khóa hai đầu)** | Cơ chế khóa đồng thời cả 2 đầu trong thời gian ký quỹ: Tiền của Seller bị giữ trong Escrow (`LOCKED`), vé của Buyer bị khóa tính năng bán lại (`IN_SETTLEMENT_BUFFER`). |
| **User Bank Account (Tài khoản thụ hưởng)** | Thông tin tài khoản ngân hàng chính chủ của Seller (BankCode, AccountNumber, AccountHolderName) dùng để thực hiện lệnh chuyển khoản tự động qua cổng NAPAS 247 khi giải ngân. |
| **Dynamic VietQR** | Mã QR thanh toán ngân hàng sinh tự động theo chuẩn NAPAS 247 kèm số tiền chính xác và nội dung chuyển khoản độc nhất gắn với transaction ID. |
| **Webhook Reconciliation (Đối soát Webhook)** | Cơ chế nhận tín hiệu xác nhận thanh toán từ ngân hàng/cổng thanh toán để kích hoạt luồng chuyển nhượng vé tức thì. |
| **Idempotency (Tính bất biến/Trùng lặp)** | Đảm bảo một lệnh thanh toán hoặc giải ngân chỉ được xử lý chính xác 1 lần duy nhất, ngăn ngừa thanh toán lặp (Double Payment) hoặc giải ngân lặp (Double Release). |

---

### 1.4. Khiếu Nại & Tranh Chấp Thủ Công (Manual Dispute & Reconciliation)

| Thuật ngữ | Định nghĩa nghiệp vụ |
| :--- | :--- |
| **Dispute (Khiếu nại / Tranh chấp)** | Báo cáo sự cố do Buyer gửi thủ công trên app khi bị từ chối vào cổng, kèm ảnh chụp máy quét lỗi hoặc biên bản viết tay của nhân viên soát vé. |
| **Gate Access Logs (Nhật ký vào cổng của BTC)** | Dữ liệu lưu trữ độc lập thuộc quyền sở hữu của Ban tổ chức/Địa điểm (không thuộc TicketShield). TicketShield chỉ được cấp quyền tra cứu (Read-Only) để làm căn cứ đối soát. |
| **Dispute Freezing (Đóng băng ký quỹ)** | Cơ chế tự động đóng băng lệnh Escrow sang trạng thái `DISPUTED` ngay khi nhận khiếu nại, đình chỉ giải ngân tự động T+24h để chờ con người vào cuộc. |
| **Manual Dispute Resolution (Phán quyết thủ công)** | Quá trình Admin/CSKH của TicketShield thẩm định chứng từ, đối soát log từ BTC và bấm nút ra quyết định: Hoàn tiền cho Buyer hoặc Giải ngân cho Seller. |

---

## 2. Actor & Responsibility Matrix (Ma trận Phân quyền & Trách nhiệm)

| Tác nhân (Actor) | Mô tả vai trò | Trách nhiệm chính trong hệ thống |
| :--- | :--- | :--- |
| **Buyer (Người mua)** | Người dùng tìm mua vé sự kiện trên nền tảng | - Tìm kiếm vé resale hợp lệ.<br>- Trải qua bước kiểm tra hành vi Bot AI.<br>- Thanh toán đơn hàng qua Dynamic VietQR.<br>- Nhận mã vé mới chính chủ từ BTC.<br>- Khiếu nại (Dispute) nếu gặp lỗi tại cổng sự kiện. |
| **Seller / Reseller (Người bán lại)** | Người sở hữu vé gốc muốn sang nhượng lại | - Đăng ký thông tin vé và Email/SĐT gốc.<br>- Xác thực OTP do BTC gửi về hòm thư chính chủ.<br>- Đặt giá bán (bắt buộc $\le$ Giá gốc).<br>- Nhận tiền ký quỹ sau thời gian đệm T+24h an toàn. |
| **Event Organizer (Ban tổ chức sự kiện)** | Đơn vị chủ quản phát hành vé (thông qua MockOrganizer.API) | - Cung cấp API xác thực mã vé và chủ sở hữu ban đầu.<br>- Gửi mã OTP xác thực chính chủ qua Email.<br>- Hủy mã vé cũ và phát hành mã vé mới khi có thanh toán thành công.<br>- Ghi nhận và cung cấp nhật ký soát vé (`gate_access_logs`). |
| **TicketShield System (Core Engine)** | Lõi xử lý giao dịch và bảo vệ thị trường | - Quản lý vòng đời tin đăng bán vé (Listing).<br>- Khóa tiền ký quỹ (Escrow) theo quy tắc ACID.<br>- Điều phối chuyển giao vé với Organizer.<br>- Background Worker tự động giải ngân sau T+24h không có khiếu nại. |
| **AI Bot Engine** | Hệ thống con trí tuệ nhân tạo (FastAPI) | - Thu thập dữ liệu sinh trắc học hành vi client-side.<br>- Tính toán điểm rủi ro `bot_risk_score` (0.0 đến 1.0).<br>- Phân loại 3 mức Allow / Throttle / Block trong thời gian < 100ms. |
| **Admin (Quản trị viên nền tảng)** | Người kiểm soát và xử lý tranh chấp | - Tra cứu nhật ký đối soát vào cổng (`gate_access_logs`).<br>- Quyết định kết quả phân xử Dispute.<br>- Kích hoạt lệnh Hoàn tiền (Refund) hoặc Giải ngân (Release). |
