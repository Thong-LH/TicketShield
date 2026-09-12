# PRE-RELEASE & PRODUCTION DEPLOYMENT CLEANUP MANDATE

> [!CRITICAL]
> Khi dự án bước vào giai đoạn chuẩn bị Release, Staging/Production Deployment, hoặc nghiệm thu cuối kỳ, Agent bắt buộc phải thực thi checklist dọn dẹp các thành phần Mock / Dev / Internal Tools dưới đây trước khi bàn giao sản phẩm.

---

## 1. Thành phần Frontend (`FE_CapstoneProject`) cần xóa / ẩn trước khi Release
- [ ] **Xóa Route nội bộ:** Xóa route `<Route path="/organizer" element={<OrganizerPortalPage />} />` khỏi `AppRoutes.tsx`.
- [ ] **Xóa Link trên Navigation:** Xóa nút `MO Portal` khỏi cả 2 menu (Guest nav & Logged-in nav) trong `Navbar.tsx`.
- [ ] **Xóa File Page nội bộ:** Xóa file `apps/web/src/pages/OrganizerPortalPage.tsx`.
- [ ] **Rà soát Hardcoded Test Links:** Đảm bảo không còn URL trỏ trực tiếp đến `http://localhost:5001`.

---

## 2. Thành phần Backend (`Capstone`) cần cô lập / loại bỏ
- [ ] **Mock Organizer Portal Endpoints:** 
  - Đảm bảo `OrganizerPortalController.cs` và `PortalHtml.cs` chỉ chạy trong môi trường `Development`. Tuyệt đối không expose ra môi trường Production.
  - Các endpoint nhạy cảm như `/api/organizer/reset-all`, `/api/organizer/otps` phải bị vô hiệu hóa khi `app.Environment.IsProduction()`.
- [ ] **Mock Credentials:** Thay thế các khóa phát triển cục bộ (`TicketShieldDevelopmentApiKeyForCapstone2026!`, `TicketShieldDevelopmentHmacKeyForCapstone2026!`) bằng bí mật cấu hình từ Doppler / Azure Key Vault thực tế.
- [ ] **Database Seeder:** Đảm bảo `OrganizerDatabaseSeeder.SeedOrganizerAsync()` không tự ý xóa dữ liệu (`RemoveRange`) trên môi trường Production.

---

## 3. Quy trình xác minh trước khi Release
1. Chạy full Unit Tests (`dotnet test`) và Typecheck (`npm run typecheck`).
2. Build bundle production (`npm run build`).
3. Kiểm tra quét toàn bộ codebase (`grep_search`) các từ khóa: `organizer`, `MO Portal`, `localhost:5001` trên FE để đảm bảo không còn vết tích rò rỉ công cụ test nội bộ ra cho người dùng cuối.
