"""
test_undetected_chrome_collector.py
Unit Test Suite kiểm định Undetected-Chromedriver Collector (Subtask 13.3.1.6).
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

from scripts.data_collection.collectors.undetected_chrome_collector import (
    UndetectedChromeCollector,
    UC_ATTACK_MODES,
)
from scripts.data_collection.utils.dataset_writer import DatasetWriter
from scripts.data_collection.models.schema import DATASET_COLUMNS

class TestUndetectedChromeCollector(unittest.TestCase):
    def setUp(self):
        self.collector = UndetectedChromeCollector()

    def test_uc_telemetry_signature(self):
        """Kiểm tra đặc trưng dấu vết đối kháng của Undetected-Chromedriver."""
        nonce = "test-uc-nonce-12345"
        for mode in UC_ATTACK_MODES:
            telemetry = self.collector.generate_uc_telemetry(nonce=nonce, mode=mode)

            # Đặc trưng cốt lõi của UC: qua mặt kiểm tra navigator.webdriver
            self.assertEqual(telemetry["is_webdriver"], 0, f"Mode {mode} phải có is_webdriver = 0")
            self.assertEqual(telemetry["virtual_gpu_detected"], 0.0, "UC sử dụng GPU thật nên virtual_gpu_detected = 0.0")
            self.assertEqual(telemetry["prototype_tampered"], 0.0, "UC không inject monkey-patch nên prototype_tampered = 0.0")
            self.assertEqual(telemetry["canvas_tampered"], 0.0, "Canvas nguyên bản không bị can thiệp")

            # GPU thật (NVIDIA / Intel)
            self.assertTrue(
                "NVIDIA" in telemetry["webgl_vendor"] or "Intel" in telemetry["webgl_vendor"],
                "WebGL Vendor phải là card đồ họa thật"
            )
            self.assertEqual(telemetry["nonce"], nonce)

    def test_attack_modes_timing_variance(self):
        """Kiểm tra phân bố nhịp bấm giữa Aggressive Scalper và Stealth Scalper."""
        # 1. Aggressive Scalper: nhịp bấm siêu tốc, độ lệch chuẩn rất thấp
        agg_telemetry = self.collector.generate_uc_telemetry(nonce="test-agg", mode="aggressive_scalper")
        self.assertLess(agg_telemetry["action_interval_std_dev"], 20.0, "Aggressive Scalper phải có std_dev < 20ms")

        # 2. Stealth Scalper: nhịp bấm rơi vào vùng xám (25ms - 80ms)
        stealth_telemetry = self.collector.generate_uc_telemetry(nonce="test-stealth", mode="stealth_scalper")
        self.assertGreaterEqual(stealth_telemetry["action_interval_std_dev"], 25.0)
        self.assertLessEqual(stealth_telemetry["action_interval_std_dev"], 80.0)

    def test_batch_collection_and_schema_integrity(self):
        """Kiểm tra thu thập batch 50 mẫu và ghi streaming ra CSV / Parquet."""
        with tempfile.TemporaryDirectory() as tmpdir:
            csv_path = os.path.join(tmpdir, "uc_test_50.csv")
            parquet_path = os.path.join(tmpdir, "uc_test_50.parquet")

            with DatasetWriter(output_csv_path=csv_path, output_parquet_path=parquet_path, batch_size=25) as writer:
                collector = UndetectedChromeCollector(writer=writer)
                records = collector.collect(count=50, mode="all", delay_between_requests=0.0)

            self.assertEqual(len(records), 50)
            self.assertTrue(os.path.exists(csv_path), "File CSV phải được tạo")

            # Kiểm tra file Parquet nếu môi trường đã xuất thành công (không dùng import tĩnh)
            if os.path.exists(parquet_path):
                self.assertGreater(os.path.getsize(parquet_path), 0, "File Parquet không được rỗng")

            # Đọc lại bằng Pandas để kiểm tra tính toàn vẹn 26 cột
            df = pd.read_csv(csv_path)
            self.assertEqual(len(df), 50)
            self.assertListEqual(list(df.columns), DATASET_COLUMNS)
            self.assertTrue((df["tool_name"] == "undetected_chromedriver").all())
            self.assertTrue((df["label"] == 1).all())
            self.assertTrue((df["is_webdriver"] == 0.0).all(), "UC bot phải có is_webdriver = 0.0")
            self.assertTrue((df["prototype_tampered"] == 0.0).all())
            self.assertTrue((df["virtual_gpu_detected"] == 0.0).all())

            # Kiểm tra phân bổ cả 2 modes tấn công
            modes_present = set(df["attack_mode"].unique())
            self.assertEqual(modes_present, set(UC_ATTACK_MODES))

if __name__ == "__main__":
    print("=" * 60)
    print(" KIỂM THỬ SUBTASK 13.3.1.6: UNDETECTED-CHROMEDRIVER COLLECTOR")
    print("=" * 60)
    unittest.main()
