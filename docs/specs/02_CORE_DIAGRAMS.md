# TicketShield AI - 5 Mental Model Diagrams

Tài liệu thể hiện **5 sơ đồ bắt buộc** theo chuẩn Checklist nghiên cứu FA26SE261 bằng cú pháp Mermaid trực quan, phản ánh chính xác quy tắc nghiệp vụ và kiến trúc 3 services của dự án.

---

## Sơ đồ 1: Vòng Đời Của Vé (Ticket Lifecycle State Machine)

Mô tả toàn bộ các trạng thái mà một chiếc vé trải qua từ lúc Ban tổ chức phát hành, khóa niêm yết bán lại, chuyển giao và giải ngân:

```mermaid
stateDiagram-v2
    [*] --> Created: Organizer tạo vé gốc
    Created --> Valid: Primary Sale (Buyer 1 mua vé gốc)
    
    state "Vé hợp lệ thuộc Buyer 1 (BTC)" as Valid
    Valid --> LockedForResale: Xác thực OTP thành công (gRPC Lock)
    
    state "Vé gốc bị Khóa trên hệ thống BTC" as LockedForResale
    LockedForResale --> Valid: Seller hủy tin niêm yết (gRPC Release Lock)
    LockedForResale --> Listed: Seller cấu hình Giá trần & Chế độ Public/Private
    
    state "Đang niêm yết trên TicketShield" as Listed
    Listed --> Transacting: Buyer 2 đặt mua & quét VietQR (Hold 10p)
    Listed --> Valid: Hủy tin bán trước khi có thanh toán
    
    state "Đang giao dịch (Escrow LOCKED)" as Transacting
    Transacting --> Listed: Quá hạn thanh toán 10p / Hủy đơn
    Transacting --> Transferred: Webhook thanh toán -> Hủy vé cũ & Cấp vé mới cho Buyer 2
    
    state "Đã chuyển quyền sở hữu chính chủ cho Buyer 2" as Transferred
    Transferred --> Settled: Quá hạn T+24h sau khi sang tên chính chủ (No Dispute)
    Transferred --> Disputed: Buyer 2 khiếu nại trong vòng 24h sau sang tên (Escrow đóng băng)
    
    state "Tranh chấp (Admin đối soát thủ công)" as Disputed
    Disputed --> Settled: Bác bỏ khiếu nại (Không có lỗi từ vé) -> Giải ngân Seller
    Disputed --> Cancelled: Khiếu nại đúng (Vé lỗi/Seller gian lận) -> Hoàn tiền 100%
    
    state "Giải ngân thành công cho Seller (T+24h Post-Transfer)" as Settled
    Settled --> Used: Buyer 2 quét QR vào cổng sự kiện sau đó (VALID_ENTRY)
    Used --> Expired: Sự kiện kết thúc
    Cancelled --> [*]
    Expired --> [*]
```

---

## Sơ đồ 2: Luồng Mua Sơ Cấp & Phát Hiện Bot AI (Primary Purchase Flow & AI Bot Detection)

Minh họa cơ chế thu thập sinh trắc học hành vi client và đánh giá rủi ro (Risk Scoring) phân loại 3 mức Allow / Throttle / Block:

```mermaid
sequenceDiagram
    autonumber
    actor Buyer as Người mua (Client / Browser)
    participant SDK as Client Telemetry SDK
    participant AI as AIEngine (FastAPI :8000)
    participant Core as TicketShield / Ticketing Core
    participant Gateway as Cổng thanh toán (VietQR)

    Buyer->>SDK: Tương tác trang (di chuột, gõ phím, click checkout)
    SDK->>AI: POST /api/v1/bot-detection/evaluate (Telemetry Data)
    
    Note over AI: Trích xuất features & chạy Model:<br/>- Tốc độ hoàn thành (time_on_page)<br/>- Độ trễ phím (keystroke std_dev)<br/>- Quỹ đạo chuột (entropy)<br/>- Dấu vân tay trình duyệt (fingerprint)
    
    alt Risk Score <= 0.3 (LOW RISK)
        AI-->>SDK: Status: ALLOW (Cấp Token truy cập)
        SDK->>Core: Gửi yêu cầu giữ vé & thanh toán
        Core->>Gateway: Sinh mã VietQR động
        Gateway-->>Buyer: Hiển thị mã QR thanh toán
    else 0.4 <= Risk Score <= 0.7 (MEDIUM RISK)
        AI-->>SDK: Status: THROTTLE (Yêu cầu thử thách)
        SDK-->>Buyer: Hiển thị CAPTCHA hành vi / Xếp hàng ảo
        Buyer->>SDK: Giải CAPTCHA thành công
        SDK->>Core: Gửi request sau xác thực
    else Risk Score > 0.7 (HIGH RISK)
        AI-->>SDK: Status: BLOCK (Phát hiện Tool/Bot)
        SDK-->>Buyer: Chặn truy cập (HTTP 403 Forbidden / Rate Limit)
    end
```

---

## Sơ đồ 3: Luồng Chuyển Nhượng P2P & Ký Quỹ Escrow (P2P Resale & Escrow Flow)

Thể hiện quy trình: **gRPC Verification -> Configurable Markup -> Dual-Hold (Tiền & Vé) -> Transfer Ownership -> Dual-Condition Payout**:

```mermaid
sequenceDiagram
    autonumber
    actor Seller as Người bán (Reseller)
    actor Buyer as Người mua (Buyer)
    participant CoreAPI as Core API (Controllers)
    participant CoreCQRS as Core Application (CQRS / Domain)
    participant MockOrg as MockOrganizer.API (:5001 gRPC)
    participant Escrow as Quỹ ký quỹ (Escrow DB)
    participant Worker as Background Settlement Worker

    Note over Seller,MockOrg: BƯỚC 1: XÁC THỰC gRPC & KHÓA VÉ BAN TỔ CHỨC (MF-02)
    Seller->>CoreAPI: POST /api/v1/ticket-verifications (Mã vé gốc)
    CoreAPI->>MockOrg: gRPC RequestTicketOtp(ticket_code)
    MockOrg-->>Seller: Gửi mã OTP 6 số qua Email chính chủ (SMTP)
    Seller->>CoreAPI: POST /api/v1/ticket-verifications/{id}/confirm (Nhập OTP)
    CoreAPI->>MockOrg: gRPC ConfirmTicketOtp(session_id, otp)
    MockOrg-->>CoreAPI: OTP hợp lệ -> Khóa vé gốc: LOCKED_FOR_RESALE
    
    Note over Seller,CoreCQRS: BƯỚC 2: THIẾT LẬP GIÁ TRẦN & ĐĂNG TIN (MF-02)
    Seller->>CoreAPI: POST /api/v1/resale-listings (ResalePrice <= OriginalPrice * (1 + Delta), is_private)
    CoreAPI->>CoreCQRS: CreateResaleListingCommand (Kiểm tra trần giá có biên độ BR-G01)
    alt is_private == true
        CoreCQRS->>CoreCQRS: Sinh private_access_token (32 hex chars)
        CoreCQRS-->>Seller: Trả link bí mật: ticketshield.vn/p2p/listing/{id}?token=... (Phí ưu đãi)
    else is_private == false
        CoreCQRS-->>Seller: Niêm yết vé công khai lên Marketplace
    end

    Note over Buyer,Escrow: BƯỚC 3: MUA VÉ, SANG TÊN & KHÓA HAI ĐẦU (MF-03)
    Buyer->>CoreAPI: Mua vé & Thanh toán VietQR (Price + BuyerFee)
    Buyer->>CoreAPI: Webhook xác nhận thanh toán thành công
    CoreAPI->>MockOrg: gRPC TransferOwnership(Hủy vé cũ, cấp mã vé mới cho Buyer)
    MockOrg-->>CoreAPI: Cấp vé mới chính chủ thành công
    CoreAPI->>CoreAPI: Listing -> SOLD. Khóa tạm giữ vé Buyer: in_settlement_buffer = true
    CoreAPI->>Escrow: Tạo Escrow (Status: LOCKED). Xác định kịch bản: T+24h nếu sự kiện còn xa, hoặc Ký quỹ chờ quét cổng nếu cận giờ G
    CoreAPI->>Worker: Bắn RabbitMQ Event: IOwnershipTransferredEvent(EscrowId, UnlockAt)

    Note over Worker,Seller: BƯỚC 4: GIẢI NGÂN ĐIỀU KIỆN KÉP (DUAL-TRIGGER PAYOUT)
    alt Kịch bản A: Sự kiện còn xa (> 24h) -> Hết 24h không khiếu nại
        Worker->>CoreAPI: Đến hạn T+24h: GỌI PRE-FLIGHT CHECK (POST /api/v1/internal/escrows/{id}/claim-payout)
    else Kịch bản B: Cận giờ G (<= 24h) -> Quét cổng vào sự kiện thành công (USED)
        MockOrg->>CoreAPI: Webhook/Event quét cổng thành công (ticket_status = USED)
        CoreAPI->>Worker: Kích hoạt Payout tức thì
    end
    CoreAPI->>Escrow: Atomic Update (WHERE status = 'LOCKED' AND has_dispute = false) -> status = PAYING_OUT
    CoreAPI-->>Worker: HTTP 200 OK (Đã khóa thành công, không có Dispute)
    Worker->>Seller: Lệnh Payout chuyển khoản NAPAS 247 về UserBankAccount của Seller
    Worker->>CoreAPI: PUT /api/v1/internal/escrows/{id}/release (Báo hoàn tất)
    CoreAPI->>Escrow: Cập nhật Escrow -> RELEASED
    CoreAPI->>CoreAPI: Gỡ cờ tạm giữ vé Buyer (in_settlement_buffer = false) -> Mở quyền bán lại nếu bận đột xuất
```



---

## Sơ đồ 4: Luồng Xử Lý Khiếu Nại & Đối Soát Thủ Công (Manual Dispute Management Flow)

Xử lý sự cố khi Buyer gặp trục trặc tại cửa soát vé sự kiện. Quá trình này **100% do Admin/CSKH xử lý thủ công (Manual Review)**, TicketShield là trung gian không can thiệp vào hệ thống soát vé của Ban tổ chức:

```mermaid
sequenceDiagram
    autonumber
    actor Buyer as Người mua (Buyer)
    actor Admin as Quản trị viên TicketShield (Admin / CSKH)
    participant Core as TicketShield.Core (:5000)
    participant Escrow as Quỹ ký quỹ (Escrow DB)
    participant MockOrg as MockOrganizer.API (:5001 - Read Only)

    Note over Buyer,Core: BƯỚC 1: BUYER GẶP LỖI TẠI CỔNG & TẠO REPORT THỦ CÔNG
    Buyer->>Core: Tạo Dispute thủ công trên App:<br/>- Nhập lý do (bị từ chối tại cổng)<br/>- Đính kèm bằng chứng (ảnh máy quét báo lỗi / biên bản viết tay của nhân viên soát vé)
    Core->>Escrow: TỰ ĐỘNG ĐÓNG BĂNG KÝ QUỸ (Status: DISPUTED, has_dispute = true)
    Core-->>Buyer: Thông báo: Lệnh giải ngân T+24h đã tạm dừng, Admin đang tiếp nhận hồ sơ

    Note over Admin,MockOrg: BƯỚC 2: ADMIN ĐIỀU TRA & ĐỐI SOÁT THỦ CÔNG (MANUAL INVESTIGATION)
    Admin->>Core: Mở giao diện Admin xem báo cáo và ảnh bằng chứng viết tay của Buyer
    Admin->>MockOrg: Tra cứu mã vé trên API đối soát Read-Only của BTC (GET /api/v1/gate/access-logs)
    MockOrg-->>Admin: Trả về lịch sử quét vé của BTC (thời gian quét thực tế tại cổng)

    Note over Admin,Core: BƯỚC 3: ADMIN RA QUYẾT ĐỊNH THỦ CÔNG (MANUAL RESOLUTION)
    alt Bằng chứng hợp lệ (Vé bị lỗi hệ thống hoặc bị quét trước khi Buyer nhận vé)
        Admin->>Core: Bấm thủ công: [CHẤP THUẬN HOÀN TIỀN] + Nhập ghi chú AdminNote
        Core->>Escrow: Cập nhật Escrow -> REFUNDED
        Core-->>Buyer: Hoàn 100% tiền lại cho Buyer
        Core->>Core: Đánh dấu vi phạm cảnh cáo / khóa quyền bán của Seller
    else Bằng chứng gian lận (BTC xác nhận Buyer đã quét vé vào xem sự kiện bình thường)
        Admin->>Core: Bấm thủ công: [BÁC BỎ TRANH CHẤP] + Nhập lý do từ chối
        Core->>Escrow: Cập nhật Escrow -> RELEASED
        Core-->>Escrow: Giải ngân tiền khả dụng cho Seller
        Core-->>Buyer: Gửi thông báo từ chối hoàn tiền & ghi nhận lịch sử gian lận
    end
```

---

## Sơ đồ 5: Kiến Trúc Hệ Thống Tổng Thể (System Architecture & Topology)

Minh họa 3 services độc lập, các cơ sở dữ liệu riêng biệt và tích hợp ngoại vi:

```mermaid
flowchart TB
    subgraph ClientLayer ["Lớp Khách Hàng (Client Layer)"]
        WebUI["TicketShield Web App (Next.js / Vanilla TS)"]
        MobileSDK["Client Telemetry SDK (Mouse / Keystroke / Fingerprint)"]
    end

    subgraph CoreService ["TicketShield.Core (.NET 8 Web API :5000)"]
        API_GW["API Gateway / Controllers"]
        subgraph CleanArch ["Clean Architecture"]
            AppCQRS["Application (CQRS / MediatR / FluentValidation)"]
            DomainLogic["Domain (Entities / Value Objects / Global Business Laws)"]
            InfraPersist["Infrastructure (EF Core Fluent API / Polly Retry)"]
        end
        BgWorker["Background Worker (Escrow T+24h Settlement)"]
    end

    subgraph AIService ["AIEngine Service (Python FastAPI :8000)"]
        FastAPI_App["FastAPI Server"]
        ML_Model["ML Inference Engine (Random Forest / XGBoost)"]
    end

    subgraph MockOrganizerService ["MockOrganizer.API (.NET 8 Web API :5001)"]
        OrgControllers["Organizer Partner API"]
        TicketLifecycleMgr["Ticket Issue / Cancel / Transfer Engine"]
        GateSimulator["Gate Access & Barcode Scanner Simulator"]
    end

    subgraph DataStorage ["Lớp Dữ Liệu & Hạ Tầng (PostgreSQL Cluster)"]
        CoreDB[("ticketshield_db<br/>- resale_listings<br/>- escrow_transactions<br/>- payout_transactions<br/>- user_bank_accounts<br/>- disputes")]
        OrgDB[("organizer_db<br/>- mock_tickets<br/>- mock_otps<br/>- gate_access_logs")]
    end

    subgraph ExternalIntegrations ["Dịch Vụ Tích Hợp Ngoại Vi"]
        VietQR["Cổng Thanh Toán VietQR / NAPAS 247"]
        SMTP["Hệ Thống Gửi Email OTP (SMTP Server)"]
    end

    %% Interactions
    WebUI -->|REST API / JWT| API_GW
    MobileSDK -->|Telemetry Payload| FastAPI_App
    FastAPI_App --> ML_Model
    ML_Model -->|Risk Score & Outcome| API_GW

    API_GW --> AppCQRS
    AppCQRS --> DomainLogic
    AppCQRS --> InfraPersist
    InfraPersist -->|Npgsql / EF Core| CoreDB
    BgWorker -->|Scan Locked Escrows & Payouts| CoreDB

    InfraPersist -->|gRPC Contract (organizer_resale.proto)| OrgControllers
    OrgControllers --> TicketLifecycleMgr
    TicketLifecycleMgr -->|Npgsql| OrgDB
    GateSimulator -->|Log Scans| OrgDB


    InfraPersist -->|Generate Dynamic QR| VietQR
    OrgControllers -->|Send OTP Code| SMTP
```

---

## Sơ đồ 6: Luồng Giải Ngân Điều Kiện Kép & Xử Lý Sự Cố (Settlement Worker Swimlane)

Mô tả chi tiết sự phối hợp giữa 7 làn vai trò (Swimlanes) theo 2 kịch bản thời gian (Kịch bản A: Sự kiện còn xa > 24h; Kịch bản B: Giao dịch cận giờ G <= 24h) và nhánh xử lý khiếu nại đối soát:

```mermaid
flowchart TB
    %% ======================= SWIMLANES =======================
    subgraph LaneBuyer ["1. Người mua (Buyer)"]
        B_Start(["Nhận vé chính chủ mới từ Ban tổ chức"])
        B_CheckType{"Thời điểm mua vé so với sự kiện?"}
        
        %% Kịch bản A
        B_Verify["Kiểm tra thông tin vé trên app/email BTC trong 24h"]
        B_VerifyResult{"Thông tin đúng niêm yết?"}
        B_FileDisputeA["Bấm Khiếu nại sai lệch thông tin vé"]
        B_HoldDone["Hết 24h: Sở hữu vé chính chủ độc lập"]

        %% Kịch bản B
        B_GoGate["Đến địa điểm sự kiện quét mã QR tại cổng"]
        B_GateResult{"Máy quét chấp thuận?"}
        B_Enjoy(["Vào xem sự kiện thành công"])
        B_FileDisputeB["Bấm Khiếu nại sự cố cổng tại chỗ"]
        B_ReceiveRefund(["Nhận lại 100% tiền hoàn"])
    end

    subgraph LaneGate ["2. Cổng soát vé Ban tổ chức (Turnstile / Scanner)"]
        G_Scan["Máy quét đọc mã QR của Buyer"]
        G_Success["Chấp thuận: Ghi nhận ScannedAt, Vé đổi sang USED"]
        G_Fail["Từ chối: Báo lỗi mã vé không hợp lệ / không nhận diện"]
    end

    subgraph LaneCore ["3. TicketShield Core & Quỹ Ký Quỹ (Escrow DB)"]
        C_Lock["Tạo Escrow: LOCKED, Vé Buyer: IN_SETTLEMENT_BUFFER"]
        C_Freeze["Đóng băng Ký quỹ tức thì: Escrow -> DISPUTED"]
        C_Preflight["Pre-flight Claim: Đổi trạng thái -> PAYING_OUT"]
        C_Release["Đổi Escrow -> RELEASED, Gỡ IN_SETTLEMENT_BUFFER"]
        C_Refund["Trích két Escrow: Đổi -> REFUNDED, Hủy vé"]
    end

    subgraph LaneWorker ["4. Background Settlement Worker"]
        W_Poll{"Quét điều kiện kích hoạt giải ngân"}
        W_TriggerA["Trigger A: Hết hạn 24h đệm không có khiếu nại"]
        W_TriggerB["Trigger B: Nhận sự kiện vé đổi trạng thái USED"]
        W_InitPayout["Tạo PayoutTransaction kèm IdempotencyKey"]
        W_CallBank["Gửi lệnh chuyển tiền sang cổng ngân hàng"]
    end

    subgraph LaneAdmin ["5. Quản trị viên đối soát (Admin Review)"]
        A_Audit["Tiếp nhận khiếu nại & Tra cứu GET /api/v1/gate/access-logs"]
        A_Verdict{"Kết quả đối soát nhật ký?"}
        A_Approve["Duyệt Hoàn tiền (Lỗi BTC / Vé bị chiếm dụng trước)"]
        A_Reject["Bác bỏ khiếu nại (Buyer gian dối đã vào cổng)"]
    end

    subgraph LaneBank ["6. Cổng thanh toán & NAPAS 247"]
        P_Transfer["Chuyển khoản NAPAS 247 tiền Payout"]
        P_Refund["Chuyển khoản hoàn tiền 100% cho Buyer"]
    end

    subgraph LaneSeller ["7. Người bán (Seller)"]
        S_Wait["Chờ giải ngân tiền an toàn"]
        S_Receive(["Nhận đủ tiền NetSellerPayout vào tài khoản ngân hàng"])
        S_Penalty(["Bị khóa quyền bán do vi phạm"])
    end

    %% ======================= FLOW CONNECTIONS =======================
    B_Start --> C_Lock
    C_Lock --> B_CheckType

    %% Nhánh Kịch bản A: Sự kiện còn xa (> 24h)
    B_CheckType -->|"Sự kiện còn xa (> 24h)"| B_Verify
    B_Verify --> B_VerifyResult
    B_VerifyResult -->|"Hợp lệ / Không khiếu nại"| B_HoldDone
    B_HoldDone --> W_TriggerA
    B_VerifyResult -->|"Phát hiện sai lệch"| B_FileDisputeA

    %% Nhánh Kịch bản B: Cận giờ G (<= 24h)
    B_CheckType -->|"Cận giờ G (<= 24h)"| B_GoGate
    B_GoGate --> G_Scan
    G_Scan --> G_Success
    G_Scan --> G_Fail

    G_Success --> B_GateResult
    G_Fail --> B_GateResult

    B_GateResult -->|"Thành công (VALID_ENTRY)"| B_Enjoy
    B_Enjoy --> W_TriggerB

    B_GateResult -->|"Bị từ chối tại cổng"| B_FileDisputeB

    %% Khiếu nại kích hoạt đóng băng
    B_FileDisputeA --> C_Freeze
    B_FileDisputeB --> C_Freeze
    C_Freeze --> A_Audit

    %% Xử lý Quản trị viên đối soát
    A_Audit --> A_Verdict
    A_Verdict -->|"Hợp lệ"| A_Approve
    A_Verdict -->|"Gian dối"| A_Reject

    A_Approve --> C_Refund
    C_Refund --> P_Refund
    P_Refund --> B_ReceiveRefund
    C_Refund --> S_Penalty

    A_Reject --> C_Preflight

    %% Luồng giải ngân của Worker
    W_TriggerA --> W_Poll
    W_TriggerB --> W_Poll
    W_Poll --> C_Preflight
    C_Preflight --> W_InitPayout
    W_InitPayout --> W_CallBank
    W_CallBank --> P_Transfer
    P_Transfer --> S_Receive
    P_Transfer --> C_Release
```

