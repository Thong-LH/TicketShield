"""
test_puppeteer_stealth_collector.py
Unit Test Suite kiểm định Puppeteer-Extra-Stealth Bot Collector (Subtask 13.3.1.5).
Kiểm tra toàn diện bộ thu thập dữ liệu bằng Python thuần.
"""

import os
import sys
import tempfile
import unittest
import pandas as pd

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "../../../"))
if REPO_ROOT not in sys.path:
    sys.path.insert(0, REPO_ROOT)

from scripts.data_collection.collectors.puppeteer_stealth_collector import (
    PuppeteerStealthCollector,
    STEALTH_MODES,
)
from scripts.data_collection.utils.dataset_writer import DatasetWriter
from scripts.data_collection.models.schema import DATASET_COLUMNS

class TestPuppeteerStealthCollector(unittest.TestCase):
    def setUp(self):
        self.collector = PuppeteerStealthCollector()

    def test_stealth_telemetry_generation(self):
        """Kiểm tra logic sinh telemetry giả lập kỹ xảo lẩn trốn (evasion techniques)."""
        nonce = "test-stealth-nonce-12345"
        for mode in STEALTH_MODES:
            telemetry = self.collector.generate_stealth_telemetry(nonce=nonce, mode=mode)

            # Tiêu chí tối thượng của bot Stealth: can thiệp prototype bị phát hiện
            self.assertEqual(telemetry["prototype_tampered"], 1.0, f"Mode {mode} phải có prototype_tampered = 1.0")
            self.assertEqual(telemetry["is_webdriver"], 1, f"Mode {mode} phải ép is_webdriver = 1")
            self.assertEqual(telemetry["virtual_gpu_detected"], 0.0, "Stealth bot giả mạo GPU rời nên virtual_gpu_detected = 0.0")
            self.assertIn("NVIDIA", telemetry["webgl_vendor"])
            self.assertIn("NVIDIA", telemetry["webgl_renderer"])
            self.assertTrue(20.0 <= telemetry["action_interval_std_dev"] <= 95.0, "Độ lệch chuẩn nhịp bấm phải rơi vào Grey Zone")
            self.assertEqual(telemetry["nonce"], nonce)

    def test_batch_collection_and_schema_integrity(self):
        """Kiểm tra thu thập batch 50 mẫu và ghi streaming ra CSV / Parquet."""
        with tempfile.TemporaryDirectory() as tmpdir:
            csv_path = os.path.join(tmpdir, "stealth_test_50.csv")
            parquet_path = os.path.join(tmpdir, "stealth_test_50.parquet")

            with DatasetWriter(output_csv_path=csv_path, output_parquet_path=parquet_path, batch_size=25) as writer:
                collector = PuppeteerStealthCollector(writer=writer)
                records = collector.collect(count=50, mode="all", delay_between_requests=0.0)

            self.assertEqual(len(records), 50)
            self.assertTrue(os.path.exists(csv_path), "File CSV phải được tạo")

            # Kiểm tra file Parquet nếu môi trường đã xuất thành công
            if os.path.exists(parquet_path):
                self.assertGreater(os.path.getsize(parquet_path), 0, "File Parquet không được rỗng")

            # Đọc lại bằng Pandas để kiểm tra tính toàn vẹn 26 cột
            df = pd.read_csv(csv_path)
            self.assertEqual(len(df), 50)
            self.assertListEqual(list(df.columns), DATASET_COLUMNS)
            self.assertTrue((df["tool_name"] == "puppeteer_stealth").all())
            self.assertTrue((df["label"] == 1).all())
            self.assertTrue((df["prototype_tampered"] == 1.0).all())
            self.assertTrue((df["is_webdriver"] == 1.0).all())

            # Kiểm tra phân bố các mode tấn công
            modes_present = set(df["attack_mode"].unique())
            self.assertEqual(modes_present, set(STEALTH_MODES))

if __name__ == "__main__":
    print("=" * 60)
    print(" KIỂM THỬ SUBTASK 13.3.1.5: PUPPETEER STEALTH COLLECTOR (PYTHON)")
    print("=" * 60)
    unittest.main()
