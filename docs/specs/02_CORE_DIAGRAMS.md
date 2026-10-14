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
    participant CoreCQRS as Core Application (CQRS / MediatR)
    participant MockOrg as MockOrganizer.API (:5001 gRPC)
    participant Escrow as Quỹ ký quỹ (Escrow DB)
    participant STM as Settlement Service (STM)

    Note over Seller,MockOrg: BƯỚC 1: XÁC THỰC gRPC & KHÓA VÉ BAN TỔ CHỨC (MF-02)
    Seller->>CoreAPI: POST /api/v1/ticket-verifications (ticketCode, organizerId)
    CoreAPI->>MockOrg: gRPC RequestTicketOtp(TicketReference)
    MockOrg-->>Seller: Gửi mã OTP 6 số qua Email chính chủ có sẵn từ BTC
    Seller->>CoreAPI: POST /api/v1/ticket-verifications/{id}/confirm (otp)
    CoreAPI->>MockOrg: gRPC ConfirmOtpAndLock(challengeId, otp)
    MockOrg-->>CoreAPI: Trả VerificationReceipt (giá gốc, hạng vé, khán đài) & Khóa vé: LOCKED_FOR_RESALE
    
    Note over Seller,CoreCQRS: BƯỚC 2: THIẾT LẬP GIÁ TRẦN & ĐĂNG BÁN QUA MEDIATR (MF-02 & BUNDLE)
    Seller->>CoreAPI: POST /api/v1/resale-listings (verificationId, resalePrice <= PriceCeiling, isPrivate)
    Note over CoreAPI,CoreCQRS: Seller không tự nhập thông tin vé. Thông tin được lấy từ Receipt BTC.
    CoreAPI->>CoreCQRS: PublishResaleListingCommand / CreateMultiResaleListingCommand
    alt isPrivate == true
        CoreCQRS->>CoreCQRS: Sinh privateAccessToken (32 hex chars)
        CoreCQRS-->>Seller: Trả link bán kín: ticketshield.vn/p2p/listing/{id}?token=...
    else isPrivate == false
        CoreCQRS-->>Seller: Niêm yết vé công khai lên Marketplace
    end

    Note over Buyer,Escrow: BƯỚC 3: GIỮ CHỖ 10P, THANH TOÁN & SANG TÊN GIAO DỊCH (MF-03)
    Buyer->>CoreAPI: POST /api/v1/resale-listings/{id}/hold (Khóa 10p, sinh VietQR động)
    Buyer->>CoreAPI: Quét thanh toán VietQR -> SePay Webhook
    CoreAPI->>CoreCQRS: ProcessSePayWebhookCommand (Atomic Lock)
    CoreCQRS->>MockOrg: gRPC TransferOwnership(Hủy vé cũ của Seller, cấp mã vé mới cho Buyer)
    MockOrg-->>CoreCQRS: Cấp vé mới 100% chính chủ thành công
    CoreCQRS->>CoreCQRS: Listing -> SOLD. Vé Buyer: IN_SETTLEMENT_BUFFER = true
    CoreCQRS->>Escrow: Tạo Escrow (Status: LOCKED, release_at = now + 24h)

    Note over CoreAPI,Seller: BƯỚC 4: GIẢI NGÂN ĐIỀU KIỆN KÉP & CHỐT CAS NGUYÊN TỬ (MF-04)
    alt Kịch bản A: Sự kiện còn xa (> 24h) -> Hết 24h không khiếu nại
        CoreAPI->>CoreAPI: AutomaticSettlementWorker quét định kỳ release_at <= now()
    else Kịch bản B: Cận giờ G (<= 24h) -> Quét cổng vào sự kiện thành công (USED)
        MockOrg->>CoreAPI: Webhook/Event quét cổng thành công (ticket_status = USED)
    end
    CoreAPI->>Escrow: Atomic CAS: UPDATE escrow_transactions SET status = 'Releasing' WHERE status = 'Locked' AND release_at <= now()
    alt CAS thành công (1 row) - Không có tranh chấp
        CoreAPI->>Escrow: Ghi Outbox Message PayoutRequestedCommand trong cùng Transaction
        CoreAPI->>STM: Dispatch Payout Command (payout_id, amount, seller_bank_snapshot)
        STM->>Seller: Thực thi chuyển khoản NAPAS 247 về UserBankAccount của Seller
        STM-->>CoreAPI: Event PayoutCompleted(payout_id, bank_ref)
        CoreAPI->>Escrow: Cập nhật Escrow -> RELEASED
        CoreAPI->>CoreAPI: Gỡ cờ tạm giữ vé Buyer (IN_SETTLEMENT_BUFFER = false)
    else CAS thất bại (0 row) - Buyer đã khiếu nại trước đó
        CoreAPI->>CoreAPI: Giữ nguyên Escrow ở trạng thái DISPUTED, đóng băng tiền chuyển sang MF-05
    end
```

---

## Sơ đồ 4: Luồng Đối Soát Tranh Chấp & Thu Thập Log Cổng Tự Động (Dispute & Evidence Harvest Flow)

Quy trình xử lý sự cố tại cổng sự kiện: Hệ thống tự động thu thập nhật ký soát vé từ Ban tổ chức qua `DisputeHarvestWorker` và đưa ra khuyến nghị phân xử khách quan hỗ trợ Admin:

```mermaid
sequenceDiagram
    autonumber
    actor Buyer as Người mua (Buyer)
    actor Admin as Quản trị viên (Admin / CSKH)
    participant Core as TicketShield.Core (:5000)
    participant Escrow as Quỹ ký quỹ (Escrow DB)
    participant Worker as DisputeHarvestWorker (Background)
    participant MockOrg as MockOrganizer.API (:5001 Gate Logs)

    Note over Buyer,Core: BƯỚC 1: BUYER BÁO LỖI TẠI CỔNG & TẠO DISPUTE
    Buyer->>Core: POST /api/v1/disputes (escrowId, reason, reasonCode)
    Core->>Escrow: Atomic CAS: UPDATE escrow_transactions SET status = 'Disputed' WHERE id = @id AND status = 'Locked'
    alt CAS thành công (1 row) - Đóng băng ký quỹ ngay lập tức
        Core->>Core: Lưu bản ghi Dispute (Status: Open) & Ghi Outbox Message DisputeHarvestRequested
        Core-->>Buyer: Thông báo: Ký quỹ đã đóng băng tức thì, bảo vệ tiền 100%
    else CAS thất bại (0 row) - Tiền đã Releasing/Released
        Core-->>Buyer: Từ chối khiếu nại: Thời hạn khiếu nại đã kết thúc, tiền đã giải ngân
    end

    Note over Worker,MockOrg: BƯỚC 2: TỰ ĐỘNG THU THẬP NHẬT KÝ CỔNG & KHUYẾN NGHỊ PHÂN XỬ
    Worker->>Core: Đọc Outbox Message DisputeHarvestRequested
    Worker->>MockOrg: GET /api/v1/gate/access-logs?ticketCode={newTicketCode}
    MockOrg-->>Worker: Trả về danh sách GateScans (ScannedAt, ScanResult, GateName)
    Worker->>Core: Chụp snapshot nhật ký (GateLogSnapshot) bất biến vào Dispute
    Worker->>Core: Chạy DisputeRecommendation.Decide(TransferredAt, GateScans)
    Note over Worker: - Scan SUCCESS >= TransferredAt -> RECOMMEND_REJECT_FRAUD<br/>- Scan < TransferredAt hoặc INVALID_TICKET -> RECOMMEND_REFUND

    Note over Admin,Core: BƯỚC 3: ADMIN THẨM ĐỊNH & RA PHÁN QUYẾT TRONG SLA 72H
    Admin->>Core: Xem chi tiết Dispute: Ảnh bằng chứng + Snapshot log cổng + Khuyến nghị tự động
    alt Đề xuất RECOMMEND_REFUND (Lỗi kỹ thuật BTC / Mã vé không hợp lệ)
        Admin->>Core: POST /api/v1/disputes/{id}/resolution (Resolution: RefundBuyer, AdminNote)
        Core->>Escrow: Cập nhật Escrow -> REFUNDED
        Core-->>Buyer: Hoàn 100% tiền lại cho Buyer từ Quỹ Ký quỹ
    else Đề xuất RECOMMEND_REJECT_FRAUD (Buyer gian dối đã vào cổng xem bình thường)
        Admin->>Core: POST /api/v1/disputes/{id}/resolution (Resolution: ReleaseSeller, AdminNote)
        Core->>Escrow: Cập nhật Escrow -> Releasing -> Kích hoạt Payout giải ngân cho Seller
        Core-->>Buyer: Từ chối hoàn tiền & áp dụng chế tài Friendly Fraud theo BR-D07
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
        API_GW["API Gateway (YARP) / Controllers"]
        subgraph CleanArch ["Clean Architecture & CQRS"]
            AppCQRS["Application (MediatR Commands/Queries / FluentValidation)"]
            DomainLogic["Domain (Entities / Value Objects / Price Ceiling Laws)"]
            InfraPersist["Infrastructure (EF Core / Advisory Locks / Polly)"]
        end
        subgraph Workers ["Background Workers"]
            AutoSettleWorker["AutomaticSettlementWorker (T+24h & Gate Entry)"]
            HoldWorker["ExpiredHoldReleaseWorker (10m Hold Release)"]
            DisputeWorker["DisputeHarvestWorker (Gate Log Snapshot & Recommendation)"]
            StuckWorker["StuckSettlementWorker (Payout Recovery)"]
        end
    end

    subgraph AIService ["AIEngine Service (Python FastAPI :8000)"]
        FastAPI_App["FastAPI Server (:8000)"]
        ML_Model["ML Inference Engine (Random Forest / XGBoost)"]
    end

    subgraph MockOrganizerService ["MockOrganizer.API (.NET 8 Web API :5001 / :5002)"]
        OrgGrpc["gRPC OrganizerResaleService (organizer_resale.proto)"]
        OrgControllers["Organizer Gate API (gate_access_logs)"]
        TicketLifecycleMgr["Ticket Issue / Cancel / Transfer Engine"]
        GateSimulator["Gate Access & Scanner Simulator"]
    end

    subgraph DataStorage ["Lớp Dữ Liệu & Hạ Tầng (PostgreSQL Cluster)"]
        CoreDB[("ticketshield_db<br/>- resale_listings<br/>- core_resale_records (bundles)<br/>- escrow_transactions<br/>- disputes<br/>- outbox_messages")]
        OrgDB[("organizer_db<br/>- mock_tickets<br/>- organizer_resale_records<br/>- gate_access_logs")]
    end

    subgraph ExternalIntegrations ["Dịch Vụ Tích Hợp Ngoại Vi"]
        VietQR["Cổng Thanh Toán VietQR / SePay Webhook"]
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
    Workers --> CoreDB

    InfraPersist -->|gRPC (:5002)| OrgGrpc
    DisputeWorker -->|HTTP GET /gate/access-logs| OrgControllers
    OrgGrpc --> TicketLifecycleMgr
    OrgControllers --> GateSimulator
    TicketLifecycleMgr -->|Npgsql| OrgDB
    GateSimulator -->|Log Scans| OrgDB

    InfraPersist -->|Generate Dynamic VietQR| VietQR
    OrgGrpc -->|Send OTP Code| SMTP
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
        C_WorkerScan["Worker nội bộ quét release_at <= now()"]
        C_CAS{"Thực thi Atomic CAS: Locked -> RELEASING"}
        C_Release["Đổi Escrow -> RELEASED, Gỡ IN_SETTLEMENT_BUFFER"]
        C_Refund["Trích két Escrow: Đổi -> REFUNDED, Hủy vé"]
    end

    subgraph LaneSTM ["4. Settlement Service (STM) & Banking"]
        STM_Receive["Nhận Payout Command (payout_id, amount, bank_info)"]
        STM_Transfer["Chuyển khoản NAPAS 247 (Idempotency Key)"]
        STM_Ack["Bắn Event: PayoutCompleted về Core"]
    end

    subgraph LaneAdmin ["5. Quản trị viên đối soát (Admin Review)"]
        A_Audit["Xem khiếu nại, snapshot log cổng & khuyến nghị tự động (DisputeHarvestWorker)"]
        A_Verdict{"Phán quyết của Admin (theo đề xuất)?"}
        A_Approve["Duyệt Hoàn tiền (Lỗi BTC / Vé bị chiếm dụng trước)"]
        A_Reject["Bác bỏ khiếu nại (Buyer gian dối đã vào cổng)"]
    end

    subgraph LaneBank ["6. Cổng thanh toán & Hoàn tiền"]
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
    B_HoldDone --> C_WorkerScan
    B_VerifyResult -->|"Phát hiện sai lệch"| B_FileDisputeA

    %% Nhánh Kịch bản B: Cận giờ G (<= 24h)
    B_CheckType -->|"Cận giờ G (<= 24h)"| B_GoGate
    B_GoGate --> G_Scan
    G_Scan --> G_Success
    G_Scan --> G_Fail

    G_Success --> B_GateResult
    G_Fail --> B_GateResult

    B_GateResult -->|"Thành công (VALID_ENTRY)"| B_Enjoy
    B_Enjoy --> C_CAS

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

    A_Reject --> C_CAS

    %% Luồng giải ngân CAS và STM
    C_WorkerScan --> C_CAS
    C_CAS -->|"CAS thành công (1 row)"| STM_Receive
    C_CAS -->|"CAS thất bại (0 row - Có dispute)"| C_Freeze
    STM_Receive --> STM_Transfer
    STM_Transfer --> S_Receive
    STM_Transfer --> STM_Ack
    STM_Ack --> C_Release
```

