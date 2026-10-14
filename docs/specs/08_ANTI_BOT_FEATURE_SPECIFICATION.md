# 08. Đặc Tả Kỹ Thuật 6 Đặc Trưng Anti-Bot (Feature Specification Contract v1.0)

> **Tài liệu:** Hợp đồng đặc tả kỹ thuật thống nhất giữa Frontend, Backend và AI Subsystem.
> **Mã tài liệu:** `docs/specs/08_ANTI_BOT_FEATURE_SPECIFICATION.md`
> **Phiên bản:** `feature_spec_version: "1.0.0"`
> **Mục đích:** Đảm bảo 100% tính nhất quán về tên biến, kiểu dữ liệu, đơn vị đo và cơ chế xử lý dữ liệu thiếu giữa Frontend thu thập, Server đo đếm, Model huấn luyện (Python) và Runtime suy luận (C#).

---

## 1. Bảng Đặc Tả 6 Đặc Trưng Cốt Lõi (Core Features)

| # | Tên chuẩn (JSON / CSV / Python) | Tên thuộc tính C# | Kiểu dữ liệu | Đơn vị tính | Nguồn đo lường | Cơ chế đo lường chi tiết |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **1** | `time_to_checkout` | `TimeToCheckout` | `float` | **Giây (seconds)** | **Server** | `submit_received_utc - issued_at_utc` dựa trên timestamp của session token phát ra khi mở trang chọn vé. |
| **2** | `action_interval_std_dev` | `ActionIntervalStdDev` | `float` | **Mili-giây (ms)** | **Client** | Độ lệch chuẩn giữa các khoảng thời gian click/tap trong phiên. Yêu cầu **tối thiểu 3 thao tác** để tính toán. |
| **3** | `burst_rate_10s` | `BurstRate10s` | `int` | **Số request** | **Server** | Số request giữ chỗ/checkout trong cửa sổ trượt 10 giây gần nhất, **tính cả request hiện tại**. Lấy `Max(rate_by_ip, rate_by_account)`. |
| **4** | `concurrent_hold` | `ConcurrentHold` | `int` | **Số vé** | **Server** | Tổng số vé đang ở trạng thái giữ chỗ (`HELD` / Chưa thanh toán / Chưa hết hạn) gắn với tài khoản của người dùng. |
| **5** | `is_webdriver` | `IsWebdriver` | `int` (0 hoặc 1) | **Boolean flag** | **Client** | Đọc cờ chuẩn `navigator.webdriver`. Giá trị `1` nếu có công cụ tự động hóa (Selenium/Puppeteer/Playwright), `0` nếu là trình duyệt thường. |
| **6** | `device_account_overlap` | `DeviceAccountOverlap` | `int` | **Số tài khoản** | **Server** | Số lượng tài khoản người dùng khác nhau từng đăng nhập/thao tác trên cùng một `device_fingerprint` trong vòng **24 giờ qua**. |

---

## 2. Quy Chuẩn Xử Lý Dữ Liệu Thiếu (Missing / Tampered Data Policy)

> [!IMPORTANT]
> **Nguyên tắc sống còn:** Tuyệt đối KHÔNG gán giá trị mặc định "giống người" khi client không gửi dữ liệu. Mọi trường hợp client thiếu telemetry phải được ghi nhận rõ bằng cờ `telemetry_missing = 1` để tránh việc bot chỉ cần không gửi telemetry là được coi là người thật.

| # | Tên đặc trưng | Xử lý khi thiếu hoặc không hợp lệ | Lý do kỹ thuật |
| :--- | :--- | :--- | :--- |
| **1** | `time_to_checkout` | Không có session token hợp lệ -> **Từ chối ngay lập tức (HTTP 400/403)**, ghi log cảnh báo, không đưa vào mô hình chấm điểm. | Session token do server tự phát ra khi mở trang; request không có token là bất thường rõ ràng của bot gọi thẳng API. |
| **2** | `action_interval_std_dev` | Nếu client không gửi hoặc có dưới 3 thao tác -> Gán `null` (hoặc `-1.0` trong mảng vector), bật `telemetry_missing = 1`. | Không tự bịa ra nhịp bấm sinh học của người khi client không gửi. |
| **3** | `burst_rate_10s` | Luôn có giá trị (server tự đếm qua Redis / MemoryCache). Mặc định tối thiểu là `1`. | Server luôn đếm được request hiện tại. |
| **4** | `concurrent_hold` | Luôn có giá trị (server tự đếm trong DB / Cache). Mặc định là `0`. | Server luôn tra cứu được số vé đang giữ. |
| **5** | `is_webdriver` | Nếu client không gửi trường này -> Gán `null` (hoặc `-1` trong mảng vector), bật `telemetry_missing = 1`. | **Tuyệt đối không mặc định là 0** (vì mặc định 0 sẽ biến bot gọi API thành người dùng hợp lệ). |
| **6** | `device_account_overlap` | Nếu client không gửi `device_fingerprint` -> Gán `null` (hoặc `-1`), bật `telemetry_missing = 1`. | Thiếu dấu vân tay thiết bị là dấu hiệu đáng ngờ. |

---

## 3. Các Cột Phụ Trợ Bắt Buộc Trong Nhật Ký (Telemetry Log & CSV Schema)

Mọi bản ghi dữ liệu (huấn luyện CSV hay nhật ký thực thi server) đều phải chứa đủ các trường:
1. `request_id`: Mã GUID định danh duy nhất cho mỗi giao dịch checkout.
2. `timestamp_utc`: Thời điểm server tiếp nhận request theo chuẩn ISO-8601 (`YYYY-MM-DDTHH:mm:ss.fffZ`).
3. `telemetry_missing`: Cờ `0` (đủ telemetry) hoặc `1` (thiếu ít nhất một telemetry từ client).
4. `label`: `0` (Người thật / Human) hoặc `1` (Bot).
5. `cohort`: Phân loại nhóm hành vi mẫu:
   - `StandardUser`: Người dùng thông thường.
   - `PowerUser`: Người dùng tay nhanh / game thủ.
   - `SimpleBot`: Bot script cộc lốc, tốc độ cao.
   - `StealthBot`: Bot tinh vi lách luật, cố tình delay nhịp bấm.
6. `feature_spec_version`: Cố định giá trị `"1.0.0"`.

---

## 4. Năm Quyết Định Kỹ Thuật Đã Chốt (Architectural Consensus)

1. **Khóa đếm `burst_rate_10s`:**
   - Cơ chế: `Max(count_by_ip, count_by_account)`.
   - Mục đích: Khóa IP bắt mạng lưới bot gom nhiều tài khoản trên 1 máy; Khóa Account bắt bot xoay residential proxy nhưng dùng chung 1 tài khoản.
2. **Khóa đếm `concurrent_hold`:**
   - Đếm theo `user_id` của tài khoản đăng nhập (quy định mỗi tài khoản không được giữ đồng thời quá số vé quy định).
3. **Cửa sổ thời gian của `device_account_overlap`:**
   - Cố định cửa sổ trượt **24 giờ (Sliding Window 24H)**. Đủ để bắt trọn chiến dịch gom vé trong ngày mở bán, không gây phình dung lượng RAM của cache.
4. **Thống nhất Thang điểm & Ngưỡng quyết định 3 tầng:**
   - Chuẩn hóa theo thang xác suất thập phân **`0.0 -> 1.0`** (khớp chuẩn `predict_proba` của Machine Learning và tài liệu dossier):
     - **`RiskScore < 0.35`**: `ALLOW` (Cho phép thanh toán giữ vé bình thường).
     - **`0.35 <= RiskScore < 0.75`**: `CHALLENGE` (Nghi ngờ -> Kích hoạt CAPTCHA hoặc gửi mã xác thực OTP).
     - **`RiskScore >= 0.75`**: `BLOCK` (Rủi ro cao -> Từ chối giao dịch, trả HTTP 429).
5. **Số thao tác tối thiểu cho `action_interval_std_dev`:**
   - Cố định **tối thiểu 3 thao tác** (ví dụ: Chọn vé -> Chọn số lượng -> Bấm nút Thanh toán: tạo ra 2 khoảng cách thời gian giữa 3 lần click để tính độ lệch chuẩn). Nếu nhỏ hơn 3 thao tác, trường này coi là không khả dụng (`null`).

---

## 5. Cấu Trúc Payload Trao Đổi Client-Server (REST API Contract)

```json
{
  "session_token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "ticket_id": "TCK-VIP-2026-001",
  "quantity": 2,
  "telemetry": {
    "is_webdriver": 0,
    "action_interval_std_dev": 285.4,
    "device_fingerprint": "a1b2c3d4e5f67890abcdef1234567890"
  }
}
```
*(Nếu bot gọi thẳng API và không gửi object `telemetry`, server tự động gán `telemetry_missing = 1` và áp dụng chế tài rủi ro).*
