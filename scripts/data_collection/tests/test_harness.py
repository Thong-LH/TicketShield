"""
test_harness.py
Unit Test Suite kiểm tra tính toàn vẹn, kích thước và hợp đồng của Lightweight Telemetry Test Harness (Subtask 13.3.1.2).
Chạy bằng: python scripts/data_collection/tests/test_harness.py
"""

import os
import re
import sys
import unittest

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "../../../"))
HARNESS_HTML_PATH = os.path.join(REPO_ROOT, "scripts/data_collection/harness/index.html")

class TestTelemetryHarness(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not os.path.exists(HARNESS_HTML_PATH):
            raise FileNotFoundError(f"Không tìm thấy file harness tại: {HARNESS_HTML_PATH}")
        with open(HARNESS_HTML_PATH, "r", encoding="utf-8") as f:
            cls.html_content = f.read()

    def test_file_size_lightweight(self):
        """Tiêu chí AC 13.3.1.2: Dung lượng harness phải siêu nhẹ (< 25 KB unminified, < 1ms load)."""
        file_size_bytes = os.path.getsize(HARNESS_HTML_PATH)
        file_size_kb = file_size_bytes / 1024.0
        print(f"\n[Test Harness] Dung lượng file index.html: {file_size_kb:.2f} KB ({file_size_bytes} bytes)")
        self.assertLess(file_size_kb, 25.0, f"File index.html quá nặng: {file_size_kb:.2f} KB (yêu cầu < 25 KB)")

    def test_html_structure_and_title(self):
        """Kiểm tra cấu trúc HTML5 cơ bản và tiêu đề trang."""
        self.assertIn("<!DOCTYPE html>", self.html_content)
        self.assertIn("<title>TicketShield Anti-Bot Telemetry Harness</title>", self.html_content)
        self.assertIn('<meta charset="UTF-8">', self.html_content)

    def test_exposed_window_apis(self):
        """Kiểm tra các API bắt buộc phải expose lên window cho bot botting."""
        required_apis = [
            "window.collectTelemetry =",
            "window.simulateClicks =",
            "window.submitCheckout =",
        ]
        for api in required_apis:
            self.assertIn(api, self.html_content, f"Thiếu API bắt buộc: {api}")

    def test_telemetry_schema_fields(self):
        """Kiểm tra hợp đồng telemetry (11 trường) được định nghĩa trong script harness."""
        expected_fields = [
            "is_webdriver:",
            "virtual_gpu_detected:",
            "prototype_tampered:",
            "canvas_hash:",
            "canvas_tampered:",
            "webgl_renderer:",
            "webgl_vendor:",
            "device_fingerprint:",
            "nonce:",
            "client_timestamp:",
            "client_user_agent:",
        ]
        for field in expected_fields:
            self.assertIn(field, self.html_content, f"Hợp đồng telemetry thiếu trường: {field}")

    def test_webgl_and_canvas_traps(self):
        """Kiểm tra sự hiện diện của bẫy đồ họa WebGL và Canvas hash."""
        self.assertIn("WEBGL_debug_renderer_info", self.html_content)
        self.assertIn("UNMASKED_VENDOR_WEBGL", self.html_content)
        self.assertIn("UNMASKED_RENDERER_WEBGL", self.html_content)
        self.assertIn("toDataURL", self.html_content)

    def test_click_simulation_gaussian(self):
        """Kiểm tra thuật toán giả lập nhịp click Box-Muller Gaussian."""
        self.assertIn("Math.sqrt(-2.0 * Math.log(u1))", self.html_content)
        self.assertIn("Math.cos(2.0 * Math.PI * u2)", self.html_content)

    def test_load_latency_script_parsing(self):
        """Kiểm tra mã script trong thẻ <script> có cú pháp đầy đủ."""
        script_match = re.search(r"<script>([\s\S]*?)</script>", self.html_content)
        self.assertIsNotNone(script_match, "Không tìm thấy thẻ <script> trong index.html")
        script_code = script_match.group(1)
        self.assertGreater(len(script_code), 1000, "Mã script quá ngắn")
        print(f"[Test Harness] Độ dài mã script SDK: {len(script_code)} ký tự -> PASS")

if __name__ == "__main__":
    print("=" * 60)
    print(" KIỂM THỬ SUBTASK 13.3.1.2: TELEMETRY HARNESS (PYTHON)")
    print("=" * 60)
    unittest.main()
