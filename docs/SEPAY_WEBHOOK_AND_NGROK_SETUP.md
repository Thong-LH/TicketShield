# 🌐 HƯỚNG DẪN CẤU HÌNH NGROK & SEPAY WEBHOOK DÙNG CHUNG (TICKETSHIELD TEAM)

> **Dành cho**: Tất cả thành viên trong nhóm phát triển dự án **TicketShield**  
> **Mục đích**: Nhận Webhook thanh toán VietQR từ SePAY về máy cá nhân (`localhost:5000`) sử dụng **1 Domain cố định duy nhất của team**, không cần tạo tài khoản riêng hay đổi URL trên SePAY.

---

## 🏛️ 1. Kiến Trúc Luồng Webhook Thanh Toán

```
┌──────────────┐     Internet      ┌───────────────┐     Static Tunnel   ┌─────────────────────────────────────────┐     YARP Route     ┌────────────────────────┐
│  Ngân Hàng   │ ───────────────>  │     SePAY     │ ──────────────────> │ Ngrok Static URL                        │ ─────────────────> │ TicketShield.Gateway   │
│   (MBBank)   │   (Chuyển tiền)   │   (Webhook)   │                     │ (https://chest-huddling-asleep.ngrok...)│   (Port 5000)      │ (Forward /api/webhooks)│
└──────────────┘                   └───────────────┘                     └─────────────────────────────────────────┘                    └───────────┬────────────┘
                                                                                                                                                    │
                                                                                                                                        Forward Port 5003
                                                                                                                                                    ▼
                                                                                                                                        ┌────────────────────────┐
                                                                                                                                        │ TicketShield.Core API  │
                                                                                                                                        │ (Xác thực API Key &    │
                                                                                                                                        │  Khóa Escrow mua vé)   │
                                                                                                                                        └────────────────────────┘
```

---

## 🔑 2. Cấu Hình Webhook Trên Dashboard SePAY (Cố Định Cho Toàn Team)

Webhook trên [Dashboard SePAY](https://my.sepay.vn/webhooks) đã được cấu hình cố định:

| Thông Số | Giá Trị Cố Định | Ghi Chú |
| :--- | :--- | :--- |
| **URL Nhận Webhook** | `https://chest-huddling-asleep.ngrok-free.dev/api/webhooks/sepay` | **Cố định cho cả team**, không đổi |
| **Header Xác Thực** | `Authorization` | Tiêu chuẩn SePAY |
| **Giá Trị Secret API Key** | `Apikey TicketShieldWebhookKey2026` | Đúng chính xác có tiền tố `Apikey ` |
| **Sự Kiện** | Tiền vào tài khoản (Inbound Transfer) | MBBank |

---

## 🚀 3. Hướng Dẫn Khởi Chạy Cho Đồng Nghiệp

### 🖥️ Cách 1: Chạy 1-Click (Dành Cho Windows - Tự động tải nếu chưa có Ngrok)
1. Kéo code mới nhất về máy (`git pull`).
2. Nhấp đúp chuột vào file:
   ```text
   TicketShield/scripts/run-ngrok.bat
   ```
3. **Cơ chế tự động của script**:
   - Nếu máy đồng nghiệp **chưa có `ngrok`**, script sẽ tự động tải bản zip chính thức của Ngrok về và giải nén.
   - Tự động nạp AuthToken dùng chung: `3JPOHWF0ec1eSouECUMVeiWXD4C_38MZP8E7mPAHSkfmq1gjt`.
   - Tự động mở đúng URL cố định: `https://chest-huddling-asleep.ngrok-free.dev` trỏ về `localhost:5000`.
4. Giữ cửa sổ terminal mở trong suốt quá trình test thanh toán.

---

### 💻 Cách 2: Chạy Bằng Lệnh Terminal Trực Tiếp (Windows / MacOS / Linux)

Nếu đồng nghiệp muốn tự gõ lệnh hoặc dùng hệ điều hành khác:

#### Bước 2.1: Cài đặt Ngrok (chọn 1 lệnh tùy môi trường)
* **Windows (PowerShell/CMD)**:
  ```powershell
  winget install ngrok
  # Hoặc cài qua choco: choco install ngrok
  # Hoặc qua npm: npm install -g ngrok
  ```
* **MacOS**:
  ```bash
  brew install ngrok
  ```
* **Linux (Ubuntu/Debian)**:
  ```bash
  curl -s https://ngrok-agent.s3.amazonaws.com/ngrok.asc | sudo tee /etc/apt/trusted.gpg.d/ngrok.asc >/dev/null && echo "deb https://ngrok-agent.s3.amazonaws.com buster main" | sudo tee /etc/apt/sources.list.d/ngrok.list && sudo apt update && sudo apt install ngrok
  ```

#### Bước 2.2: Nạp AuthToken của team (chỉ làm 1 lần)
```bash
ngrok config add-authtoken 3JPOHWF0ec1eSouECUMVeiWXD4C_38MZP8E7mPAHSkfmq1gjt
```

#### Bước 2.3: Chạy tunnel tới Gateway
```bash
ngrok http 5000 --url=https://chest-huddling-asleep.ngrok-free.dev
```

---

## 🧪 4. Quy Trình Test Thanh Toán E2E

1. Khởi chạy Backend (`.\start-dev.bat`) và Frontend (`npm run dev`).
2. Mở Ngrok (`scripts/run-ngrok.bat` hoặc lệnh ở Mục 3).
3. Truy cập [http://localhost:5173/marketplace](http://localhost:5173/marketplace) -> Bấm **Mua vé** -> Nhập thông tin -> Nhận mã VietQR.
4. Chuyển khoản hoặc vào SePay Dashboard bấm **Test gửi Webhook**.
5. Giao diện Web lập tức phát hiện đã thanh toán thành công và hiển thị vé chính chủ có mã QR trong **"Vé Của Tôi"**.

---

## ⚠️ 5. Lưu Ý Khi Sử Dụng Chung 1 Domain Cố Định
- Vì cả team dùng chung 1 Domain `https://chest-huddling-asleep.ngrok-free.dev`, tại một thời điểm **chỉ 1 bạn mở Ngrok** để test webhook.
- Sau khi test xong, hãy đóng cửa sổ terminal Ngrok để nhường lượt cho bạn khác trong team test.
