# SPECIFICATION: BE-CORE-2.6.2
## Uniform Fee Model and Admin Dynamic Fee Management for Private Resale

| Metadata | Detail |
| :--- | :--- |
| **Task Code** | `BE-CORE-2.6.2` |
| **Task Name** | Uniform Fee Model & Dynamic Admin Fee Management for Private/Public Resale |
| **Target Service** | `TicketShield.Core` / `TicketShield.TradingCore` |
| **Priority** | High |
| **Assignee** | Backend Development Team |
| **Status** | Approved & Ready for Implementation (Production-Grade Standard) |

---

## 1. TỔNG QUAN & LÝ DO NGHIỆP VỤ (OVERVIEW & BUSINESS RATIONALE)

### 1.1. Mục tiêu Cốt lõi
- **Đồng nhất Biểu phí (Uniform Fee Model):** Áp dụng cùng 1 tỷ lệ/mức phí dịch vụ (Buyer Fee & Seller Fee) cho cả **Public Resale** (đăng bán chợ công khai) và **Private Resale** (bán riêng tư qua Private Token/Link).
- **Cấu hình Động bởi Admin (Admin Dynamic Fee Management):** **TUYỆT ĐỐI KHÔNG HARDCODE** biểu phí trong source code hoặc file config tĩnh (`appsettings.json`). Biểu phí được quản lý trong cơ sở dữ liệu và cho phép Admin chỉnh sửa runtime.
- **Bảo toàn Kế toán & Ký quỹ Escrow (Snapshot & Escrow Validation):** Đóng băng (Snapshot) số tiền phí ngay khi đơn hàng khởi tạo, đồng thời xác thực chuyển đổi quyền sở hữu vé 100% chính chủ qua gRPC tới Ban Tổ Chức (BTC).

---

### 1.2. Phân Tích Nghiệp Vụ Thực Tế (Why This Design Is Industry Best Practice)

> 💡 **Giải thích luận điểm nghiệp vụ chuẩn doanh nghiệp (StubHub, Ticketmaster, E-commerce Standard):**

1. **Linh hoạt Chiến dịch Marketing & Kinh doanh (Business Agility):**
   - Cho phép Đội ngũ Kinh doanh / Admin thực hiện các chiến dịch khuyến mãi (ví dụ: *"Miễn 100% Phí Người Mua vào dịp lễ Tết"* hay *"Ưu đãi giảm Phí Người Bán cho Sự kiện X"*) trực tiếp trên Admin Dashboard **mà không cần sửa code hay redeploy backend**.
2. **Đảm bảo Nguyên tắc Kế toán & Minh bạch Tài chính (Financial Snapshot Rule):**
   - Khi Buyer bấm Checkout thanh toán, số tiền `BuyerFee` và `SellerFee` phải được **LƯU SNAPSHOT CHẾT** vào bản ghi `EscrowTransaction`.
   - Nếu Admin thay đổi biểu phí hệ thống sau đó, các đơn hàng đang ở trạng thái `Locked` / `Pending` **giữ nguyên 100% số tiền snapshot ban đầu**, triệt tiêu hoàn toàn rủi ro lệch sổ sách kế toán (Accounting Reconciliation Discrepancy).
3. **Thích ứng Chi phí Hạ tầng & Cổng thanh toán (Risk & Cost Adaptation):**
   - Giúp Admin chủ động điều chỉnh mức phí để bù đắp biến động phí cổng thanh toán (Payment Gateway fee) hoặc chi phí vận hành server gRPC.
4. **Tính Kiểm toán & Dấu vết Hành vi (Auditability & Traceability):**
   - Mọi thao tác đổi phí của Admin đều lưu `UpdatedBy` và `UpdatedAt` trong DB để phục vụ kiểm toán nội bộ.

---

## 2. CHI TIẾT YÊU CẦU KỸ THUẬT (DETAILED TECHNICAL REQUIREMENTS)

### 2.1. Cơ sở Dữ liệu (Database Schema for Fee Management)

Bảng `SystemSettings` trong Database lưu trữ cấu hình biểu phí động:

```sql
CREATE TABLE SystemSettings (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    SettingKey NVARCHAR(100) NOT NULL UNIQUE, 
    SettingValue NVARCHAR(500) NOT NULL,       
    DataType NVARCHAR(50) NOT NULL,            -- 'Decimal', 'Money'
    Description NVARCHAR(500) NULL,
    UpdatedBy UNIQUEIDENTIFIER NULL,
    UpdatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
```

#### Dữ liệu khởi tạo (Seed Data):
| SettingKey | SettingValue | DataType | Description |
| :--- | :--- | :--- | :--- |
| `ResaleFee_BuyerPercentage` | `0.05` | `Decimal` | Tỷ lệ phí người mua (5%) |
| `ResaleFee_SellerPercentage` | `0.03` | `Decimal` | Tỷ lệ phí người bán (3%) |
| `ResaleFee_MinBuyerFee` | `10000` | `Money` | Phí tối thiểu người mua (10,000 VNĐ) |
| `ResaleFee_MinSellerFee` | `5000` | `Money` | Phí tối thiểu người bán (5,000 VNĐ) |

---

### 2.2. Admin Management APIs & Ràng Buộc Kiểm Soát (Validation Constraints)

Backend triển khai các API quản lý dành cho Admin với các quy tắc kiểm soát đầu vào (Boundary Validation) nghiêm ngặt:

#### 1. API Lấy Biểu phí Hiện tại
`GET /api/v1/admin/fee-settings` (Authorize: `Role = Admin`)

#### 2. API Cập nhật Biểu phí Động
`PUT /api/v1/admin/fee-settings` (Authorize: `Role = Admin`)

**Request Body:**
```json
{
  "buyerFeePercentage": 0.04,  // 4%
  "sellerFeePercentage": 0.02, // 2%
  "minimumBuyerFee": 10000,
  "minimumSellerFee": 5000
}
```

#### ⚠️ Quy Tắc Validation Đầu Vào (Input Boundary Validation):
- `0.00 <= buyerFeePercentage <= 0.15` (Phí người mua từ 0% đến tối đa 15%).
- `0.00 <= sellerFeePercentage <= 0.10` (Phí người bán từ 0% đến tối đa 10%).
- `minimumBuyerFee >= 0` và `minimumSellerFee >= 0`.
- Nếu vi phạm boundary, trả về `400 Bad Request` kèm chi tiết lỗi business rule.

---

### 2.3. Caching Strategy & Dynamic Fee Calculation Engine

Do việc tính phí diễn ra liên tục ở luồng Checkout, Backend áp dụng **Cache Strategy (IMemoryCache / Redis)** để tránh bottleneck DB:

```csharp
public class DynamicResaleFeeCalculator : IResaleFeeCalculator
{
    private readonly ISystemSettingRepository _settingRepository;
    private readonly IMemoryCache _cache;
    private const string FeeConfigCacheKey = "CACHE_RESALE_FEE_CONFIG";

    public async Task<ResaleFeeCalculationResult> CalculateFeeAsync(decimal resalePrice, bool isPrivate, CancellationToken ct = default)
    {
        // 1. Lấy Config từ Cache (Tự động fetch DB nếu cache chưa tồn tại)
        var feeConfig = await _cache.GetOrCreateAsync(FeeConfigCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            return await _settingRepository.GetResaleFeeConfigAsync(ct);
        });

        // 2. CÔNG THỨC TÍNH PHÍ ĐỒNG NHẤT (Áp dụng chung cho cả Public & Private Resale)
        decimal buyerFee = Math.Max(resalePrice * feeConfig.BuyerFeePercentage, feeConfig.MinimumBuyerFee);
        decimal sellerFee = Math.Max(resalePrice * feeConfig.SellerFeePercentage, feeConfig.MinimumSellerFee);

        return new ResaleFeeCalculationResult
        {
            OriginalPrice = resalePrice,
            BuyerFee = Math.Round(buyerFee, 0),
            SellerFee = Math.Round(sellerFee, 0),
            TotalBuyerPaid = Math.Round(resalePrice + buyerFee, 0),
            NetSellerPayout = Math.Round(resalePrice - sellerFee, 0)
        };
    }
}
```

#### Cache Invalidation Rule:
Khi Admin thực hiện `PUT /api/v1/admin/fee-settings` thành công, Handler bắt buộc phải thực hiện xóa cache key `CACHE_RESALE_FEE_CONFIG`.

---

### 2.4. Quy trình Escrow State Machine & Fee Snapshot Law

Khi Buyer bấm Thanh toán, quy trình xử lý giao dịch đảm bảo nguyên tắc **Fee Snapshot & gRPC Verification**:

```
[Buyer Checkout] ──► Read Dynamic Fee Config ──► Create EscrowTransaction (Snapshot Fees & Status: LOCKED)
                                                              │
                                            (Gọi gRPC tới Ban Tổ Chức)
                                                              │
                    ┌─────────────────────────────────────────┴─────────────────────────────────────────┐
                    ▼                                                                                   ▼
       [gRPC Transfer SUCCESS]                                                             [gRPC Transfer FAILED]
                    │                                                                                   │
     - Update Status: RELEASED                                                          - Update Status: REFUNDED
     - Create Payout Record (NetSellerPayout)                                           - Refund TotalBuyerPaid to Buyer
```

---

## 3. CHECKLIST KIỂM THỬ (TESTING & VERIFICATION CHECKLIST)

Backend Dev đảm bảo pass toàn bộ các Test Cases kiểm thử:

### 3.1. Admin Dynamic Config & Boundary Tests
- [ ] **`Admin_UpdateFeeConfig_ValidData_UpdatesDatabaseAndInvalidatesCache`**: Admin cập nhật hợp lệ -> DB thay đổi + Cache bị xóa.
- [ ] **`Admin_UpdateFeeConfig_OutOfBounds_ReturnsBadRequest`**: Thử nhập phí âm (-1%) hoặc quá trần (25%) -> Hệ thống chặn và trả lỗi HTTP 400.
- [ ] **`UnauthorizedUser_UpdateFeeConfig_Returns403Forbidden`**: User không phải Admin bị chặn 100%.

### 3.2. Uniform Fee & Financial Snapshot Tests
- [ ] **`UniformFee_PrivateAndPublic_CalculatesIdenticalAmount`**: Với cùng mức giá vé, Private Resale và Public Resale tính ra `BuyerFee` và `SellerFee` giống hệt nhau.
- [ ] **`FeeSnapshot_PreservesOriginalAmountOnAdminUpdate`**: Giao dịch đã tạo `EscrowTransaction` giữ nguyên phí snapshot ban đầu ngay cả khi Admin vừa đổi phí hệ thống.

---

## 4. MA TRẬN TÓM TẮT THAY ĐỔI CODE & DB

| Thành phần | File / Module | Nội dung |
| :--- | :--- | :--- |
| **Database** | `SystemSettings` table | Lưu trữ cấu hình biểu phí tập trung |
| **Admin API** | `Admin/FeeSettingsController.cs` | Thêm API GET/PUT biểu phí kèm Input Validation Boundary |
| **Caching** | `Services/DynamicResaleFeeCalculator.cs` | Cache biểu phí động + Invalidate cache khi Admin PUT |
| **Escrow Core** | `Entities/EscrowTransaction.cs` | Snapshot số tiền `BuyerFee`, `SellerFee` cố định |

---
*Văn bản này là đặc tả kỹ thuật chính thức và đầy đủ cho nhiệm vụ BE-CORE-2.6.2.*
