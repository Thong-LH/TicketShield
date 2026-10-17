# TICKETSHIELD - SPECIFICATION: 09_B2B_PARTNER_GATEWAY_SPEC
## Dynamic B2B Partner Onboarding, gRPC Gateway Resolver & Catalog Sync Engine (US-7.5)

| Thuộc tính | Chi tiết |
| :--- | :--- |
| **Mã nghiệp vụ** | `US-7.5` (`SCRUM-239` / `BE-CORE-7.5.1` -> `7.5.4`) |
| **Tên tính năng** | Dynamic B2B Partner Gateway Resolver, Lifecycle Management & Catalog Sync |
| **Target Service** | `TicketShield.Core` (Application, Domain, Infrastructure, API) |
| **Trạng thái** | Hoàn tất triển khai & Đã kiểm thử 100% (Passed 9/9 Unit Tests) |

---

## 1. TỔNG QUAN & BỐI CẢNH NGHIỆP VỤ (OVERVIEW & RATIONALE)

### 1.1. Vấn Đề Thực Tế (Problem Statement)
Trong các giai đoạn phát triển ban đầu (MF-01 đến MF-04), địa chỉ kết nối gRPC tới Ban tổ chức (`GrpcEndpoint`) bị gắn cứng (hardcode) vào `appsettings.json` chỉ trỏ tới duy nhất 1 instance MockOrganizer (`http://localhost:5004`).
Hạn chế:
1. **Thiếu tính đa đối tác (Multi-tenant B2B):** Không thể mở rộng tích hợp đồng thời với nhiều Ban tổ chức khác nhau (ví dụ: Ticketbox, CTCP Đua Thuyền, Nhà hát Lớn).
2. **Không hỗ trợ Dynamic Discovery:** Khi Ban tổ chức đổi domain hoặc IP gateway, hệ thống Core bắt buộc phải sửa cấu hình file tĩnh và redeploy lại toàn bộ backend.
3. **Nguy cơ nghẽn kết nối & SSRF:** Khởi tạo `GrpcChannel` ad-hoc trên mỗi request gây cạn kiệt socket (Socket Exhaustion), đồng thời thiếu cơ chế kiểm duyệt IP nội bộ (SSRF).

### 1.2. Giải Pháp Chuẩn Hóa
Triển khai hệ thống B2B Partner Dynamic Topology chuẩn doanh nghiệp:
1. **Cấu hình Động trong Database:** Toàn bộ thông tin đối tác (`ApiBaseUrl`, `GrpcEndpoint`, `WebhookSecretKey`, `ConnectionStatus`) được quản lý tập trung trong bảng `organizers`.
2. **Dynamic Channel Pool:** `OrganizerGatewayResolver` quản lý cache pool `ConcurrentDictionary<Guid, Lazy<GrpcChannel>>`, tận dụng kết nối đa kênh HTTP/2 multiplexing.
3. **Phòng vệ SSRF Perimeter:** Kiểm duyệt địa chỉ IP và hostname của đối tác, từ chối kết nối tới các dải IP nội bộ, loopback nhạy cảm hoặc subnet riêng tư không hợp lệ.
4. **Vòng Đời Đối Tác 4 Bước (Partner Lifecycle):** Quản lý trạng thái đối tác minh bạch theo quy trình: `Draft` -> `Connected` -> `CatalogSynced` -> `Active`.
5. **Catalog Sync Engine:** Tự động đồng bộ danh mục sự kiện và hạng vé từ API sơ cấp của Ban tổ chức sang bảng `events` và `ticket_tiers` của sàn thứ cấp.

---

## 2. VÒNG ĐỜI ĐỐI TÁC (PARTNER LIFECYCLE STATE MACHINE)

```
 [ Khởi tạo ]
      │
      ▼
   ┌───────┐   Probe gRPC Test thành công
   │ DRAFT ├─────────────────────────────────►┌───────────┐
   └───┬───┘                                  │ CONNECTED │
       │                                      └─────┬─────┘
       │                                            │
       │                                            │ Chạy Sync Catalog Sự Kiện
       │                                            ▼
       │                                     ┌───────────────┐
       │                                     │ CATALOGSYNCED │
       │                                     └──────┬────────┘
       │                                            │
       │ Phê duyệt vận hành chính thức              │ Kích hoạt
       └────────────────────────────────────────────►│ ACTIVE │
                                                     └────────┘
```

1. **`Draft` (Khởi tạo):** Đối tác mới được tạo hồ sơ, chưa kết nối mạng hoặc đang cấu hình tham số ban đầu.
2. **`Connected` (Đã thông mạng):** Quản trị viên thực hiện lệnh Probe kiểm tra thành công, kết nối gRPC/HTTP phản hồi dưới 2000ms.
3. **`CatalogSynced` (Đã đồng bộ):** Hệ thống đã kéo toàn bộ danh mục concert, festival và các hạng vé từ BTC về CSDL TicketShield.
4. **`Active` (Đang hoạt động):** Cho phép người bán bắt đầu nhập mã vé của đối tác này để xác thực OTP và niêm yết chuyển nhượng.

---

## 3. THIẾT KẾ KIẾN TRÚC & HẠ TẦNG (ARCHITECTURE & INFRASTRUCTURE)

### 3.1. Mô Hình Thực Thể (Domain Entities)

#### Thực thể `Organizer` (`src/TicketShield.Core/TicketShield.Domain/Entities/Organizer.cs`):
```csharp
public class Organizer : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? ContactEmail { get; set; }
    public string? ApiBaseUrl { get; set; }
    public string? GrpcEndpoint { get; set; }
    public string? WebhookSecretKey { get; set; }
    public OrganizerConnectionStatus ConnectionStatus { get; set; } = OrganizerConnectionStatus.Draft;
    public OrganizerLifecycleStatus LifecycleStatus { get; set; } = OrganizerLifecycleStatus.Draft;
    public DateTimeOffset? LastConnectedAt { get; set; }
    public ICollection<Event> Events { get; set; } = new List<Event>();
}
```

#### Thực thể `Event` bổ sung định danh ngoài:
* Bổ sung thuộc tính `ExternalEventId`: Mã sự kiện gốc bên hệ thống Ban tổ chức.
* Composite Unique Index: `(OrganizerId, ExternalEventId)` ngăn chặn trùng lặp khi chạy Catalog Sync nhiều lần.

### 3.2. Cơ Chế Dynamic Resolver (`OrganizerGatewayResolver.cs`)
* **Thread-Safe Initialization:** Sử dụng `Lazy<GrpcChannel>` trong `ConcurrentDictionary` để triệt tiêu race condition khi hàng trăm luồng cùng resolve 1 đối tác cùng lúc.
* **HTTP/2 Connection Pooling:**
  ```csharp
  var httpHandler = new SocketsHttpHandler
  {
      PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
      KeepAlivePingDelay = TimeSpan.FromSeconds(60),
      KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
      EnableMultipleHttp2Connections = true
  };
  ```
* **SSRF Protection:** Kiểm tra URL trước khi tạo socket; ném ngoại lệ nếu endpoint trỏ vào loopback hoặc dải IP cấm trong môi trường Production.
* **Cache Invalidation:** Tự động gọi `InvalidateChannel(organizerId)` khi Admin cập nhật GrpcEndpoint để hủy socket cũ và tái tạo channel mới.

---

## 4. DANH MỤC API QUẢN TRỊ (`/api/v1/admin/organizers`)

| HTTP Method | Endpoint | Mô tả chức năng | Quyền hạn |
| :--- | :--- | :--- | :--- |
| `GET` | `/api/v1/admin/organizers` | Danh sách đối tác kèm trạng thái kết nối & số sự kiện | Admin |
| `GET` | `/api/v1/admin/organizers/{id}` | Chi tiết đối tác, bao gồm danh mục concert & hạng vé | Admin |
| `POST` | `/api/v1/admin/organizers` | Tạo hồ sơ Ban tổ chức mới | Admin |
| `PUT` | `/api/v1/admin/organizers/{id}/connection` | Cập nhật cấu hình gRPC, BaseUrl & Secret Key | Admin |
| `DELETE`| `/api/v1/admin/organizers/{id}` | Xóa đối tác (chặn nếu đang có vé niêm yết) | Admin |
| `POST` | `/api/v1/admin/organizers/{id}/test-connection` | Probe kiểm tra độ trễ mạng và trạng thái gRPC | Admin |
| `POST` | `/api/v1/admin/organizers/{id}/sync-events` | Kéo danh mục sự kiện và vé gốc từ BTC về Core | Admin |

---

## 5. KIỂM THỬ XÁC MINH (VERIFICATION)
* **Unit Tests:** [AdminOrganizersTests.cs](file:///d:/Capstone/tests/TicketShield.UnitTests/Features/Organizers/AdminOrganizersTests.cs) (9/9 pass 100%).
* **Frontend Component:** [AdminOrganizersPage.tsx](file:///d:/FE_CapstoneProject/apps/web/src/pages/AdminOrganizersPage.tsx) hiển thị Node-Based Topology Graph với cáp mạng SVG Bezier và Drawer danh mục sự kiện.
