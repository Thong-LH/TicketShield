# TicketShield - AI-Based Verified P2P Resale Platform

> Capstone Graduation Project (FA26SE261) - FPT University

---

## 🚀 Hướng dẫn Chạy Dự Án (Dành cho Thành viên Nhóm)

Dự án đã được tích hợp **Auto-Migration & Seed Data tự động**. Khi khởi chạy, hệ thống sẽ **tự động tạo Database, sinh 14 bảng và nạp sẵn dữ liệu mẫu**, các bạn **KHÔNG CẦN gõ lệnh CLI hay nạp SQL thủ công**!

---

### 1. Yêu cầu Môi trường
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
* PostgreSQL 16+ (Chạy local trên máy hoặc qua Docker)

---

### 2. Cấu hình Kết nối CSDL (Nếu dùng PostgreSQL Local)
Mở 2 file `appsettings.json` sau và chỉnh lại mật khẩu `postgres` của máy bạn:
1. `src/TicketShield.Core/TicketShield.API/appsettings.json` (Trỏ về `ticketshield_db`)
2. `src/MockOrganizer/MockOrganizer.API/appsettings.json` (Trỏ về `organizer_db`)

```json
"ConnectionStrings": {
  "DefaultConnection": "Host=localhost;Port=5432;Database=...;Username=postgres;Password=<mat_khau_cua_ban>"
}
```

*(Lưu ý: Nếu dùng Docker, chỉ cần gõ `docker compose up -d` rồi dùng mật khẩu mặc định `postgres`).*

---

### 3. Chạy 2 Dịch vụ Backend
Mở 2 terminal riêng biệt:

#### Terminal 1: Chạy Dịch vụ Ban tổ chức (MockOrganizer.API)
```bash
dotnet run --project src/MockOrganizer/MockOrganizer.API
```
👉 Mở Swagger: **http://localhost:5001/swagger**

#### Terminal 2: Chạy Dịch vụ Sàn P2P (TicketShield.Core)
```bash
dotnet run --project src/TicketShield.Core/TicketShield.API
```
👉 Mở Swagger: **http://localhost:5000/swagger**

---

### 4. Dữ liệu Mẫu có sẵn để Test Flow (Seed Data)
Ngay khi ứng dụng khởi chạy lần đầu, CSDL tự động nạp sẵn:
* **Tài khoản Người bán (Seller):** `seller@ticketshield.vn` (Id: `11111111-1111-1111-1111-111111111111`)
* **Tài khoản Người mua (Buyer):** `buyer@ticketshield.vn` (Id: `22222222-2222-2222-2222-222222222222`)
* **Sự kiện mẫu:** `Anh Trai Say Hi Concert 2026` (Hạng VIP Zone A: 2.500.000đ, GA Standing: 1.200.000đ)
* **Vé gốc hợp lệ để test MF-02:**
  * `ATSH-VIP-888` (VIP, giá gốc 2.500.000đ, chủ vé: `seller@ticketshield.vn`)
  * `ATSH-GA-999` (GA, giá gốc 1.200.000đ, chủ vé: `seller@ticketshield.vn`)
  * `ATSH-USED-001` (Vé đã qua sử dụng, dùng để test case lỗi)
