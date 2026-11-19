"""
test_human_capture_page.py
Unit Test Suite kiểm định Human Telemetry Capture Page & Touch/Mouse Streamer (Subtask 13.3.2.1).
Kiểm tra toàn diện tính toàn vẹn của giao diện thu thập và logic bẫy người thật bằng Python thuần.
"""

import os
import re
import sys
import unittest

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "../../../"))
CAPTURE_HTML_PATH = os.path.join(REPO_ROOT, "scripts/data_collection/harness/human_capture_page.html")

class TestHumanCapturePage(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not os.path.exists(CAPTURE_HTML_PATH):
            raise FileNotFoundError(f"Không tìm thấy file human_capture_page.html tại: {CAPTURE_HTML_PATH}")
        with open(CAPTURE_HTML_PATH, "r", encoding="utf-8") as f:
            cls.html_content = f.read()

    def test_file_size_lightweight(self):
        """Tiêu chí AC 13.3.2.1: Dung lượng trang thu thập phải siêu nhẹ (< 25 KB)."""
        file_size_bytes = os.path.getsize(CAPTURE_HTML_PATH)
        file_size_kb = file_size_bytes / 1024.0
        print(f"\n[Test Human Capture] Dung lượng file human_capture_page.html: {file_size_kb:.2f} KB ({file_size_bytes} bytes)")
        self.assertLess(file_size_kb, 25.0, f"File capture page quá nặng: {file_size_kb:.2f} KB (yêu cầu < 25 KB)")

    def test_html_structure_and_form_elements(self):
        """Kiểm tra cấu trúc HTML5, responsive viewport và các thành phần giao diện mua vé."""
        self.assertIn("<!DOCTYPE html>", self.html_content)
        self.assertIn("<title>TicketShield 🎟️ Human Cohort Telemetry Capture</title>", self.html_content)
        self.assertIn('name="viewport"', self.html_content)
        self.assertIn('id="ticketTier"', self.html_content)
        self.assertIn('id="fullName"', self.html_content)
        self.assertIn('id="email"', self.html_content)
        self.assertIn('id="btnCheckout"', self.html_content)

    def test_exposed_human_apis(self):
        """Kiểm tra các API bắt buộc phải expose lên window phục vụ test automation & profiling."""
        required_apis = [
            "window.collectTelemetry =",
            "window.simulateHumanInteraction =",
            "window.submitCheckout =",
        ]
        for api in required_apis:
            self.assertIn(api, self.html_content, f"Thiếu API bắt buộc: {api}")

    def test_cohort_detection_logic(self):
        """Kiểm tra logic nhận diện 5 cohorts người dùng (Brave, Firefox, Safari, Mobile, Chrome)."""
        expected_cohorts = [
            "brave_privacy",
            "mobile_webview",
            "firefox_desktop",
            "safari_macos",
            "chrome_desktop",
        ]
        for cohort in expected_cohorts:
            self.assertIn(f"'{cohort}'", self.html_content, f"Thiếu định nghĩa cohort: {cohort}")

        # Kiểm tra nhận diện Brave Farbling
        self.assertIn("navigator.brave", self.html_content)
        self.assertIn("isBrave", self.html_content)

    def test_touch_and_pointer_listeners(self):
        """Kiểm tra sự hiện diện của bộ lắng nghe sự kiện chuột và cảm ứng di động."""
        self.assertIn("pointerdown", self.html_content)
        self.assertIn("touchstart", self.html_content)
        self.assertIn("touchCount", self.html_content)
        self.assertIn("mouseCount", self.html_content)

    def test_human_motor_distribution_box_muller(self):
        """Kiểm tra mô hình phân bố thời gian tự nhiên của con người (Log-Normal motor dynamics)."""
        self.assertIn("Math.sqrt(-2.0 * Math.log(u1))", self.html_content)
        self.assertIn("Math.cos(2.0 * Math.PI * u2)", self.html_content)
        self.assertIn("time_to_checkout", self.html_content)

    def test_script_syntax_and_length(self):
        """Kiểm tra tính toàn vẹn của mã JavaScript SDK trong trang."""
        script_match = re.search(r"<script>([\s\S]*?)</script>", self.html_content)
        self.assertIsNotNone(script_match, "Không tìm thấy thẻ <script> trong human_capture_page.html")
        script_code = script_match.group(1)
        self.assertGreater(len(script_code), 2000, "Mã script quá ngắn")
        print(f"[Test Human Capture] Độ dài mã script telemetry: {len(script_code)} ký tự -> PASS")

if __name__ == "__main__":
    print("=" * 60)
    print(" KIỂM THỬ SUBTASK 13.3.2.1: HUMAN CAPTURE PAGE")
    print("=" * 60)
    unittest.main()
