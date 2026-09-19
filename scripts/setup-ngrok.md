# 🚀 Hướng Dẫn Cấu Hình Ngrok Webhook Tunnel (TicketShield AI)

Tài liệu này hướng dẫn cách cấu hình **Ngrok** để nhận Webhook thanh toán SePAY VietQR trực tiếp về máy local (`localhost:5000`) khi phát triển và kiểm thử tính năng.

---

## 📌 Bước 1: Ngrok Đã Được Cài Đặt Sẵn
Tệp `ngrok.exe` (v3.39+) đã có sẵn trong thư mục:
```text
scripts/ngrok.exe
```
*(Bạn không cần phải tải hay cài đặt thêm phần mềm gì khác).*

---

## 📌 Bước 2: Đăng Ký & Nhập Authtoken (Chỉ cần làm 1 lần)

1. Đăng ký/Đăng nhập tài khoản miễn phí tại: [https://dashboard.ngrok.com/get-started/your-authtoken](https://dashboard.ngrok.com/get-started/your-authtoken)
2. Copy đoạn mã **Your Authtoken** (gồm chuỗi ký tự ngẫu nhiên).
3. Thực hiện **1 trong 2 cách** sau:
   - **Cách 1**: Nhấp đúp vào file [scripts/set-authtoken.bat](file:///d:/Mon_hoc/Capstone_Project/TicketShield/scripts/set-authtoken.bat), dán token vào rồi nhấn **Enter**.
   - **Cách 2**: Chạy lệnh trong terminal:
     ```powershell
     .\scripts\ngrok.exe config add-authtoken <TOKEN_CUA_BAN>
     ```

---

## 📌 Bước 3: Khởi Chạy Tunnel Cho TicketShield Gateway (Port 5000)

👉 Click đúp vào tệp [scripts/run-ngrok.bat](file:///d:/Mon_hoc/Capstone_Project/TicketShield/scripts/run-ngrok.bat).

Màn hình Ngrok Terminal sẽ xuất hiện dải địa chỉ HTTPS public, ví dụ:
```text
Forwarding   https://abc1-234-567.ngrok-free.app -> http://localhost:5000
```
> ⚠️ **Lưu ý**: Hãy giữ cửa sổ Ngrok luôn mở trong suốt quá trình test thanh toán.

---

## 📌 Bước 4: Cập Nhật Webhook URL Trên SePAY Dashboard

1. Truy cập vào trang quản trị SePAY Webhooks: [https://my.sepay.vn/webhooks](https://my.sepay.vn/webhooks)
2. Nhấn nút **Tạo Webhook** hoặc **Chỉnh sửa** Webhook hiện có.
3. Điền các thông số như sau:
   - **Địa chỉ URL nhận Webhook**:
     ```http
     https://<ten-subdomain-cua-ban>.ngrok-free.app/api/webhooks/sepay
     ```
     *(Thay `<ten-subdomain-cua-ban>.ngrok-free.app` bằng URL thực tế Ngrok vừa cấp ở Bước 3)*
   - **Xác thực / Secret Header**:
     ```text
     Apikey TicketShieldWebhookKey2026
     ```
     *(Hoặc nhập Header `Authorization: Apikey TicketShieldWebhookKey2026`)*
4. Nhấn **Lưu cấu hình**.

---

## 🧪 Bước 5: Kiểm Thử Webhook

1. Đảm bảo toàn bộ hệ thống TicketShield (`localhost:5000`) đang chạy.
2. Trên trang SePAY Dashboard, nhấn nút **Gửi thử dữ liệu (Test Webhook)**.
3. Quan sát:
   - Terminal Ngrok: Sẽ hiển thị `POST /api/webhooks/sepay 200 OK`.
   - Frontend (`http://localhost:5173`): Khi mua vé và thanh toán chuyển khoản, modal sẽ tự động phát hiện thanh toán thành công và đóng modal, cập nhật danh sách vé ngay lập tức!
