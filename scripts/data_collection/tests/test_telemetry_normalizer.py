"""
test_telemetry_normalizer.py
Unit Test Suite kiểm định Telemetry Normalizer Engine (Subtask 13.3.2.2).
Kiểm tra toàn diện các ca chuẩn hóa đặc thù của 5 Cohorts người thật bằng Python thuần.
"""

import os
import sys
import unittest

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "../../../"))
if REPO_ROOT not in sys.path:
    sys.path.insert(0, REPO_ROOT)

from scripts.data_collection.utils.telemetry_normalizer import (
    TelemetryNormalizer,
    VALID_HUMAN_COHORTS,
)
from scripts.data_collection.models.schema import DATASET_COLUMNS

class TestTelemetryNormalizer(unittest.TestCase):
    def test_brave_farbling_normalization(self):
        """Kiểm tra xử lý ngoại lệ Brave: canvas_tampered = 1 nhưng vẫn gán nhãn người thật (label = 0)."""
        raw_telemetry = {
            "canvas_tampered": 1,
            "canvas_hash": "c1a2b3d4",
            "action_interval_std_dev": 145.2,
            "client_user_agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0.0.0",
        }
        headers = {"User-Agent": raw_telemetry["client_user_agent"]}

        rec = TelemetryNormalizer.normalize_human_record(
            raw_telemetry=raw_telemetry,
            headers=headers,
            cohort_type="brave_privacy",
            time_to_checkout=12.5
        )

        self.assertEqual(rec.label, 0, "Brave user phải luôn có label = 0 (Human)")
        self.assertEqual(rec.canvas_tampered, 1.0, "Phải bảo toàn cờ canvas_tampered = 1.0 của Brave Farbling")
        self.assertEqual(rec.is_webdriver, 0.0)
        self.assertEqual(rec.virtual_gpu_detected, 0.0)
        self.assertEqual(rec.prototype_tampered, 0.0)
        self.assertEqual(rec.gateway_decision, "Allow")
        self.assertLess(rec.risk_score, 0.15)
        self.assertTrue(TelemetryNormalizer.validate_record_schema(rec))

    def test_firefox_desktop_normalization(self):
        """Kiểm tra xử lý Firefox: không có Client Hints Chromium nhưng H2 pseudo-header đúng chuẩn m,p,a,s."""
        raw_telemetry = {
            "canvas_tampered": 0,
            "action_interval_std_dev": 182.0,
            "client_user_agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:132.0) Gecko/20100101 Firefox/132.0",
        }
        headers = {"User-Agent": raw_telemetry["client_user_agent"]}

        rec = TelemetryNormalizer.normalize_human_record(
            raw_telemetry=raw_telemetry,
            headers=headers,
            cohort_type="firefox_desktop",
            time_to_checkout=15.0
        )

        self.assertEqual(rec.label, 0)
        self.assertEqual(rec.http2_fingerprint, "m,p,a,s", "Firefox phải có thứ tự H2 là m,p,a,s")
        self.assertEqual(rec.webgl_vendor, "Mozilla")
        self.assertEqual(rec.ua_match_level, "ExactMatch")
        self.assertEqual(rec.gateway_decision, "Allow")
        self.assertTrue(TelemetryNormalizer.validate_record_schema(rec))

    def test_safari_macos_normalization(self):
        """Kiểm tra xử lý Safari WebKit: sử dụng nhân GPU Apple Metal thật."""
        raw_telemetry = {
            "canvas_tampered": 0,
            "action_interval_std_dev": 210.5,
            "client_user_agent": "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 Version/17.5 Safari/605.1.15",
        }
        headers = {"User-Agent": raw_telemetry["client_user_agent"]}

        rec = TelemetryNormalizer.normalize_human_record(
            raw_telemetry=raw_telemetry,
            headers=headers,
            cohort_type="safari_macos",
            time_to_checkout=9.8
        )

        self.assertEqual(rec.label, 0)
        self.assertEqual(rec.webgl_vendor, "Apple Inc.")
        self.assertIn("Apple", rec.webgl_renderer)
        self.assertEqual(rec.virtual_gpu_detected, 0.0)
        self.assertEqual(rec.gateway_decision, "Allow")
        self.assertTrue(TelemetryNormalizer.validate_record_schema(rec))

    def test_mobile_webview_normalization(self):
        """Kiểm tra xử lý Mobile: nhịp chạm cảm ứng tự nhiên và UA in-app tokens."""
        raw_telemetry = {
            "canvas_tampered": 0,
            "action_interval_std_dev": 120.0,
            "client_user_agent": "Mozilla/5.0 (Linux; Android 14; SM-S918B) AppleWebKit/537.36 Chrome/130.0.0.0 Mobile [FB_IAB/FB4A;]",
        }
        headers = {"User-Agent": raw_telemetry["client_user_agent"]}

        rec = TelemetryNormalizer.normalize_human_record(
            raw_telemetry=raw_telemetry,
            headers=headers,
            cohort_type="mobile_webview",
            time_to_checkout=18.2
        )

        self.assertEqual(rec.label, 0)
        self.assertEqual(rec.webgl_vendor, "Qualcomm Technologies, Inc.")
        self.assertGreaterEqual(rec.action_interval_std_dev, 85.0)
        self.assertEqual(rec.gateway_decision, "Allow")
        self.assertTrue(TelemetryNormalizer.validate_record_schema(rec))

    def test_schema_integrity_and_validation(self):
        """Kiểm tra việc từ chối các bản ghi sai lệch (ví dụ cờ is_webdriver = 1 hoặc label = 1)."""
        valid_rec = TelemetryNormalizer.normalize_human_record(
            raw_telemetry={},
            headers={"User-Agent": "Mozilla/5.0 Chrome"},
            cohort_type="chrome_desktop"
        )
        self.assertTrue(TelemetryNormalizer.validate_record_schema(valid_rec))

        # Kiểm tra nếu cờ webdriver bị bật -> phải bị từ chối khỏi tập người thật
        valid_rec.is_webdriver = 1.0
        self.assertFalse(TelemetryNormalizer.validate_record_schema(valid_rec))

        # Kiểm tra nếu nhãn label bị gán sai thành bot -> từ chối
        valid_rec.is_webdriver = 0.0
        valid_rec.label = 1
        self.assertFalse(TelemetryNormalizer.validate_record_schema(valid_rec))

if __name__ == "__main__":
    print("=" * 60)
    print(" KIỂM THỬ SUBTASK 13.3.2.2: TELEMETRY NORMALIZER")
    print("=" * 60)
    unittest.main()
