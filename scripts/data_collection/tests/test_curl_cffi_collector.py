"""
test_curl_cffi_collector.py
Unit Test Suite kiểm định curl_cffi & Protocol Impersonation Collector (Subtask 13.3.1.7).
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

from scripts.data_collection.collectors.curl_cffi_collector import (
    CurlCffiCollector,
    PROTOCOL_ATTACK_MODES,
    JA4_PROFILES,
)
from scripts.data_collection.utils.dataset_writer import DatasetWriter
from scripts.data_collection.models.schema import DATASET_COLUMNS

class TestCurlCffiCollector(unittest.TestCase):
    def setUp(self):
        self.collector = CurlCffiCollector()

    def test_attack_modes_payload_and_headers(self):
        """Kiểm tra logic sinh payload và headers cho từng hình thức tấn công tầng giao thức."""
        # 1. Raw API Bot: Không có telemetry, gọi thẳng endpoint
        p_raw, h_raw, t_raw = self.collector.generate_protocol_payload_and_headers(mode="raw_api_bot")
        self.assertIsNone(p_raw["telemetry"], "Raw API Bot phải có telemetry = None")
        self.assertLess(t_raw, 0.35, "Raw API Bot checkout siêu tốc < 0.35s")
        self.assertIn("X-JA4", h_raw)

        # 2. Replay Bot: Sử dụng lại nonce cũ
        p_rep, h_rep, _ = self.collector.generate_protocol_payload_and_headers(mode="replay_bot")
        self.assertIsNotNone(p_rep["telemetry"])
        self.assertIn("X-JA4", h_rep)

        # 3. Burst Rate: Nhịp gửi siêu tốc, IP chung cụm
        p_burst, h_burst, _ = self.collector.generate_protocol_payload_and_headers(mode="burst_rate")
        self.assertIn("X-Forwarded-For", h_burst)
        self.assertLessEqual(p_burst["telemetry"]["action_interval_std_dev"], 5.0)

        # 4. Protocol Impersonation: Lệch pha User-Agent giữa Client và Header
        p_imp, h_imp, _ = self.collector.generate_protocol_payload_and_headers(mode="protocol_impersonation")
        self.assertIn("Windows", h_imp["User-Agent"])
        self.assertIn("Linux", p_imp["telemetry"]["client_user_agent"])
        self.assertEqual(h_imp.get("Sec-Ch-Ua-Platform"), '"Linux"')

    def test_batch_collection_and_schema_integrity(self):
        """Kiểm tra thu thập batch 60 mẫu và ghi streaming ra CSV / Parquet."""
        with tempfile.TemporaryDirectory() as tmpdir:
            csv_path = os.path.join(tmpdir, "cffi_test_60.csv")
            parquet_path = os.path.join(tmpdir, "cffi_test_60.parquet")

            with DatasetWriter(output_csv_path=csv_path, output_parquet_path=parquet_path, batch_size=30) as writer:
                collector = CurlCffiCollector(writer=writer)
                records = collector.collect(count=60, mode="all", delay_between_requests=0.0)

            self.assertEqual(len(records), 60)
            self.assertTrue(os.path.exists(csv_path), "File CSV phải được tạo")

            # Kiểm tra file Parquet an toàn nếu môi trường đã xuất thành công
            if os.path.exists(parquet_path):
                self.assertGreater(os.path.getsize(parquet_path), 0, "File Parquet không được rỗng")

            # Đọc lại bằng Pandas để kiểm tra tính toàn vẹn 26 cột
            df = pd.read_csv(csv_path)
            self.assertEqual(len(df), 60)
            self.assertListEqual(list(df.columns), DATASET_COLUMNS)
            self.assertTrue((df["tool_name"] == "curl_cffi").all())
            self.assertTrue((df["label"] == 1).all())

            # Kiểm tra phân bổ cả 4 modes tấn công
            modes_present = set(df["attack_mode"].unique())
            self.assertEqual(modes_present, set(PROTOCOL_ATTACK_MODES))

            # Kiểm tra trường hợp raw_api_bot không có telemetry
            raw_rows = df[df["attack_mode"] == "raw_api_bot"]
            self.assertTrue((raw_rows["is_webdriver"] == -1.0).all())
            self.assertTrue((raw_rows["action_interval_std_dev"] == -1.0).all())

            # Kiểm tra trường hợp protocol_impersonation bị nhận diện lệch pha
            imp_rows = df[df["attack_mode"] == "protocol_impersonation"]
            self.assertTrue((imp_rows["ua_match_level"] == "BlatantMismatch").all())

if __name__ == "__main__":
    print("=" * 60)
    print(" KIỂM THỬ SUBTASK 13.3.1.7: CURL_CFFI / PROTOCOL COLLECTOR")
    print("=" * 60)
    unittest.main()
