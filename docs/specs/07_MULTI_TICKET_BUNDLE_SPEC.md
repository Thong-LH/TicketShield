# Đặc Tả Nghiệp Vụ & UX: Bán Nhiều Vé Cùng Lúc & Gói Combo Không Tách Rời (Multi-Ticket & Indivisible Bundle Listing)

Tài liệu này quy định chi tiết về nghiệp vụ, trải nghiệm người dùng (UX/UI) và giải pháp kiến trúc Backend cho tính năng **Đăng bán nhiều vé cùng lúc** và **Bán trọn bộ theo gói (All-or-Nothing Bundle)** trên sàn TicketShield.

---

## 1. Bối Cảnh & Vấn Đề Nghiệp Vụ (Problem Statement)

### 1.1. Thực trạng người bán sở hữu nhiều vé
Trong các sự kiện âm nhạc, thể thao hoặc lễ hội, người dùng thường mua theo nhóm hoặc mua nhiều vé (2 đến 4 vé). Khi phát sinh nhu cầu bán lại do bận đột xuất, người bán gặp 2 trường hợp bố trí ghế:
- **Trường hợp 1 (Vé liền kề - Adjacent Seats):** 2 hoặc nhiều vé cùng hàng, ghế nằm cạnh nhau (ví dụ: Zone A, Hàng G, Ghế 12 & 14).
- **Trường hợp 2 (Vé không liền kề - Non-adjacent Seats):** Các vé ở vị trí cách xa nhau, khác hàng, hoặc thậm chí ở các khán đài/Zone khác nhau (ví dụ: 1 vé Zone A Hàng B Ghế 05 và 1 vé Zone C Hàng K Ghế 22).

### 1.2. Nỗi đau của người bán nếu bị xé lẻ (Orphan Ticket Risk)
Nếu hệ thống chỉ cho phép bán lẻ từng vé đơn độc:
- Người mua sẽ có xu hướng chỉ chọn mua 1 vé tốt hơn (hoặc vị trí đẹp hơn).
- Vé còn lại bị bỏ rơi ("vé mồ côi"). Rất khó tìm được người mua lẻ vé thứ hai cận giờ sự kiện, khiến người bán chịu rủi ro mất trắng 50% hoặc 100% giá trị tiền vé còn lại.
- **Nhu cầu cấp thiết:** Người bán muốn có quyền quyết định bán cả gói **"Được thì lấy hết, không thì thôi" (All-or-Nothing)**, bảo đảm thanh khoản đồng thời cho toàn bộ số vé họ đang sở hữu.

---

## 2. Thiết Kế Trải Nghiệm Người Dùng (UX/UI Specifications)

### 2.1. Luồng Người Bán (Seller Flow - Multi-Ticket Listing)

```
[Chọn danh sách vé sở hữu] (Multi-select Checkbox)
              │
              ▼
[Chọn hình thức đăng bán]
       ├── (A) Bán trọn bộ Combo (All-or-Nothing) ──> Khuyến nghị khi bán >= 2 vé
       └── (B) Bán tách lẻ từng vé (Independent) ──> Tách thành N tin đăng độc lập
              │
              ▼
[Thiết lập giá & Kiểm tra trần giá]
       ├── Kiểm tra trần giá từng vé con (0 < Price <= OriginalPrice * 1.15)
       └── Kiểm tra tổng trần giá gói Combo
              │
              ▼
[Xác thực OTP quyền sở hữu 1 chạm] (Batch Verification)
              │
              ▼
[Niêm yết thành công lên Marketplace]
```

#### Chi tiết giao diện Seller:
1. **Màn hình chọn vé:** Cho phép tích chọn nhiều vé (`Select all` hoặc chọn từng vé). Hiển thị rõ số lượng vé đã chọn (`Đã chọn 2 vé`).
2. **Lựa chọn chế độ bán:**
   - **Option A (Khuyến nghị): "Bán trọn bộ Combo (All-or-Nothing)":**
     - *Mô tả thân thiện:* "Người mua bắt buộc phải mua toàn bộ các vé này trong cùng một đơn hàng. Giúp bạn thanh lý nhanh gọn và không bị tồn đọng vé lẻ."
     - *Nhập giá:* Cho phép nhập giá từng vé (hệ thống tự cộng tổng) hoặc nhập Tổng giá gói (hệ thống tự phân bổ tỷ lệ).
   - **Option B: "Bán tách lẻ từng vé":**
     - *Mô tả thân thiện:* "Mỗi vé sẽ hiển thị thành một tin đăng riêng biệt trên sàn. Người mua có thể chọn mua lẻ từng vé tùy nhu cầu."
3. **Cảnh báo độ liền kề:**
   - Nếu vé liền kề: Hiển thị huy hiệu `[Ghế liền kề]` kèm gợi ý tăng tính thanh khoản.
   - Nếu vé không liền kề: Hiển thị nhãn trung lập `[Vị trí không liền kề]` và nhắc người bán kiểm tra kỹ thông tin vị trí ghế trước khi bấm niêm yết.

---

### 2.2. Luồng Người Mua (Buyer Flow - Marketplace & Checkout)

#### Giao diện thẻ vé trên Marketplace (Listing Card):
- **Thẻ Combo đặc thù:**
  - Huy hiệu góc trên: `Gói [N] vé (Bán trọn bộ)` (Badge tím sang trọng, nổi bật).
  - Huy hiệu vị trí:
    - Nếu ghế cạnh nhau: Nhãn xanh lá `Ghế liền kề (Cùng hàng G)`.
    - Nếu khác vị trí: Nhãn hổ phách `2 vị trí khác nhau (Zone A & Zone B)` nhằm đảm bảo **tính minh bạch 100%**, người mua không bị nhầm lẫn là ghế cạnh nhau.
  - Hiển thị giá: **Tổng giá trọn gói** (VD: `1.800.000 đ cho 2 vé`) kèm giá trung bình mỗi vé (`900.000 đ/vé`).
  - Nút bấm hành động: `Mua trọn gói 2 vé` (Tuyệt đối không có lựa chọn tick chọn 1 trong 2 vé).

#### Giao diện Thanh Toán & Khóa Giữ Chỗ (Checkout Modal):
- **Giữ chỗ toàn bộ gói (Atomic Hold):** Khi người mua bấm "Mua trọn gói 2 vé", hệ thống lập tức khóa tạm giữ (Hold) đồng thời cả 2 vé trong 10 phút. Trạng thái cả cụm vé trên sàn chuyển sang `TRANSACTING` kèm hiệu ứng mờ (blurred overlay).
- **Mã VietQR duy nhất:** Người mua chỉ quét 1 mã VietQR duy nhất với tổng số tiền của toàn bộ gói vé.
- **Cấp vé sau thanh toán:** Ngay khi Webhook thanh toán thành công, hệ thống gRPC sang BTC đổi chủ và cấp đồng thời [N] mã vé mới chính chủ vào mục *Vé đã mua của tôi* và gửi file vé về email người mua.

---

## 3. Kiến Trúc Kỹ Thuật Backend (Backend Architecture & DB Design)

### 3.1. Mô Hình Dữ Liệu (Database Schema)

Để hỗ trợ bán trọn gói Combo mà không phá vỡ cấu trúc bảng `core_resale_records` hiện có, kiến trúc sử dụng định danh `bundle_id`:

```sql
-- Thêm cột bundle_id và cờ is_bundle_all_or_nothing vào bảng core_resale_records
ALTER TABLE core_resale_records
ADD COLUMN IF NOT EXISTS bundle_id UUID NULL,
ADD COLUMN IF NOT EXISTS is_bundle_all_or_nothing BOOLEAN NOT NULL DEFAULT FALSE,
ADD COLUMN IF NOT EXISTS bundle_total_tickets INT NOT NULL DEFAULT 1;

-- Index tăng tốc truy vấn nhóm Bundle
CREATE INDEX IF NOT EXISTS idx_resale_records_bundle_id 
ON core_resale_records (bundle_id) 
WHERE bundle_id IS NOT NULL;
```

- **Vé đơn lẻ (Single Ticket):** `bundle_id = NULL`, `is_bundle_all_or_nothing = false`.
- **Gói Combo (Bundle Tickets):** Các bản ghi vé con thuộc cùng gói có chung một giá trị `bundle_id = <UUID>`. Cột `is_bundle_all_or_nothing = true`.

---

### 3.2. Tính Toàn Vẹn Khóa Giữ Chỗ (Atomic Hold Transaction)

Khi Buyer bấm giữ chỗ gói Combo, Service thực thi khóa giữ chỗ đồng thời cho toàn bộ các vé con trong 1 transaction duy nhất:

```csharp
// Luồng giữ chỗ nguyên khối All-or-Nothing
using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);

var bundleTickets = await _db.ResaleRecords
    .Where(r => r.BundleId == bundleId)
    .ToListAsync(ct);

// Kiểm tra số lượng vé và tính toàn vẹn trạng thái
if (bundleTickets.Count != expectedTicketCount || bundleTickets.Any(t => t.Status != ResaleStatus.VERIFIED))
{
    throw new ConflictException("Một hoặc nhiều vé trong gói Combo đã có người khác đặt mua hoặc không còn hợp lệ.");
}

// Cập nhật trạng thái đồng loạt sang TRANSACTING
var holdDeadline = DateTime.UtcNow.AddMinutes(10);
foreach (var ticket in bundleTickets)
{
    ticket.Status = ResaleStatus.TRANSACTING;
    ticket.BuyerId = currentBuyerId;
    ticket.HoldExpiresAt = holdDeadline;
}

await _db.SaveChangesAsync(ct);
await tx.CommitAsync(ct);
```

---

### 3.3. Xử Lý Escrow Ký Quỹ & Giải Ngân Cho Gói Vé

1. **Một Giao Dịch Ký Quỹ Duy Nhất (Single Escrow Record):**
   - Tạo 1 bản ghi Escrow duy nhất đại diện cho gói vé với `bundle_id = bundleTickets.First().BundleId`.
   - `Amount = Sum(ticket.ResalePrice)`.
   - `BuyerFee = Sum(ticket.BuyerFee)`.
   - `SellerFee = Sum(ticket.SellerFee)`.
2. **Kích hoạt đổi chủ đồng thời qua gRPC:**
   - Hệ thống gọi sang ban tổ chức để chuyển giao từng vé con trong gói.
   - **Nguyên tắc Compensation (Saga Pattern):** Nếu có bất kỳ vé con nào bị lỗi gRPC bên BTC:
     - Hủy toàn bộ giao dịch mua gói.
     - Hoàn tiền 100% cho Người mua.
     - Khôi phục trạng thái vé về an toàn.
3. **Điều kiện Giải ngân (Release Escrow):**
   - Tiền được giải ngân về tài khoản ngân hàng của Seller sau T+24h hoặc khi **toàn bộ các vé trong gói** đã được quét cổng (`ticket_status == 'USED'`).

---

## 4. Bảng So Sánh Hai Hình Thức Đăng Bán

| Tiêu chí so sánh | Bán Tách Lẻ (Independent) | Bán Trọn Bộ Combo (All-or-Nothing) |
| :--- | :--- | :--- |
| **Hành vi người mua** | Được chọn mua lẻ từng vé tùy ý | Bắt buộc mua toàn bộ các vé trong gói |
| **Vị trí ghế ngồi** | Không ảnh hưởng (ai mua vé nào thì lấy vé đó) | Hỗ trợ cả ghế liền kề lẫn khác Zone/hàng ghế |
| **Rủi ro Seller tồn vé** | Cao (dễ bị mua mất 1 vé đẹp, vé xấu bị ế) | **0%** (Được bán là thanh lý toàn bộ) |
| **Thanh toán VietQR** | Thanh toán từng đơn cho từng vé | 1 mã VietQR duy nhất cho toàn bộ gói |
| **Trần giá sàn (Price Cap)** | Đo riêng biệt cho từng vé | Đo từng vé và tổng trần giá toàn gói |
| **Cơ chế Rollback** | Đơn lẻ theo từng vé | Nguyên khối (1 vé lỗi -> Hoàn tiền cả gói) |

---

## 5. Phân Kỳ Triển Khai (Roadmap)

- **Sprint MF-03 & Sprint 2 Hardening (Hiện tại):**
  - Khóa chặt nền tảng Core: Gộp 2 DbContext, xử lý triệt để ChangeTracker leak, đánh index, vá 5 lỗi vòng đời vé và luật BR-L04.
  - Ghi nhận luật nghiệp vụ **BR-G06** vào tài liệu đặc tả hệ thống.
- **Sprint 3 (Future Scope):**
  - Mở rộng Database Migration: Thêm trường `bundle_id` và `is_bundle_all_or_nothing`.
  - Nâng cấp API `POST /api/resale/listings/bundle` và luồng thanh toán Escrow Bundle.
  - Cập nhật UI Marketplace: Hiển thị huy hiệu Combo và chi tiết vị trí ghế.
