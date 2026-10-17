# Danh Mục Dọn Dẹp Trước Khi Release & Deploy Production (Pre-Release Cleanup)

Tài liệu này ghi nhận các công cụ test nội bộ (Mock Organizer Portal, debug routes) được tạo ra để phục vụ quá trình phát triển cục bộ và kiểm thử đồ án. Khi chuẩn bị Release / Deploy Production, các mục sau đây cần được dọn dẹp hoặc đóng lại.

---

## 1. Phía Frontend (`d:\FE_CapstoneProject`)
| File | Vị trí / Đoạn code | Thao tác cần làm |
| :--- | :--- | :--- |
| `apps/web/src/routes/AppRoutes.tsx` | Route `/organizer` | Xóa route `OrganizerPortalPage` |
| `apps/web/src/components/layout/Navbar.tsx` | Nút `MO Portal` ở Guest Nav & User Nav | Xóa thẻ `<Link to="/organizer">MO Portal</Link>` |
| `apps/web/src/pages/OrganizerPortalPage.tsx` | Toàn bộ file | Xóa file page này khỏi dự án |
| Kiểm tra chung | Tìm kiếm `localhost:5001` | Đảm bảo không còn endpoint test nào của MockOrganizer trong client bundle |

---

## 2. Phía Backend (`d:\Capstone`)
| Thành phần | Vị trí | Thao tác cần làm |
| :--- | :--- | :--- |
| `OrganizerPortalController.cs` | `src/MockOrganizer/MockOrganizer.API/Controllers/` | Chỉ chạy trong Development profile hoặc cô lập sau VPN/Auth nội bộ |
| `PortalHtml.cs` | `src/MockOrganizer/MockOrganizer.API/Portal/` | Không map endpoint public `/` hoặc `/portal` trên Production |
| `OrganizerDatabaseSeeder.cs` | `src/MockOrganizer/MockOrganizer.API/Data/` | Tắt auto-seed reset trên Production database |
| Khóa HMAC & API Key | `appsettings.json` | Thay các key dev bằng secret thật lấy từ Doppler / Vault |

---

## 3. Lời nhắc tự động cho AI Agent (Antigravity)
Chỉ thị dọn dẹp này đã được nạp trực tiếp vào hệ thống Rules tự động của Antigravity tại:
- `d:\Capstone\.agents\rules\pre-release-cleanup.md`
- `d:\FE_CapstoneProject\.agents\rules\pre-release-cleanup.md`

Mỗi khi agent làm việc trong các tác vụ liên quan đến **Release**, **Deploy**, hoặc **Production Preparation**, Antigravity sẽ tự động kích hoạt rule này để nhắc nhở và thực thi dọn dẹp.
