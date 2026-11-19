"""
test_human_cohort_collector.py
Unit Test Suite kiểm định Human Cohort Collector Module (Subtask 13.3.2.3).
Kiểm tra toàn diện bộ thu thập dữ liệu người dùng thật đa nền tảng bằng Python thuần.
"""

import os
import sys
import tempfile
import unittest
import pandas as pd

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "../../../"))
if REPO_ROOT not in sys.path:
    sys.path.insert(0, REPO_ROOT)

from scripts.data_collection.collectors.human_cohort_collector import (
    HumanCohortCollector,
    COHORT_USER_AGENTS,
)
from scripts.data_collection.utils.telemetry_normalizer import VALID_HUMAN_COHORTS
from scripts.data_collection.utils.dataset_writer import DatasetWriter
from scripts.data_collection.models.schema import DATASET_COLUMNS

class TestHumanCohortCollector(unittest.TestCase):
    def setUp(self):
        self.collector = HumanCohortCollector()

    def test_generate_telemetry_across_all_5_cohorts(self):
        """Kiểm tra đặc tính telemetry sinh ra từ cả 5 cohorts người thật."""
        nonce = "test-human-nonce-12345"
        for cohort in VALID_HUMAN_COHORTS:
            telemetry = self.collector.generate_human_telemetry(cohort=cohort, nonce=nonce)

            # Ràng buộc cơ bản của Người Thật
            self.assertEqual(telemetry["is_webdriver"], 0, f"Cohort {cohort} phải có is_webdriver = 0")
            self.assertEqual(telemetry["virtual_gpu_detected"], 0.0, f"Cohort {cohort} phải dùng GPU thật")
            self.assertEqual(telemetry["prototype_tampered"], 0.0, f"Cohort {cohort} không bị tampered prototype")
            self.assertGreaterEqual(telemetry["action_interval_std_dev"], 85.0, "Nhịp bấm người thật phải >= 85ms")

            # Kiểm tra đặc thù của Brave Farbling
            if cohort == "brave_privacy":
                self.assertEqual(telemetry["canvas_tampered"], 1.0, "Brave phải có canvas_tampered = 1.0 (Farbling)")
            else:
                self.assertEqual(telemetry["canvas_tampered"], 0.0, f"Cohort {cohort} không có noise canvas")

            # Kiểm tra GPU Apple Metal trên Safari
            if cohort == "safari_macos":
                self.assertEqual(telemetry["webgl_vendor"], "Apple Inc.")

    def test_batch_collection_and_schema_integrity(self):
        """Kiểm tra thu thập batch 50 mẫu (10 mẫu/cohort x 5 cohorts) và thẩm định 26 cột bằng Pandas."""
        with tempfile.TemporaryDirectory() as tmpdir:
            csv_path = os.path.join(tmpdir, "human_test_50.csv")
            parquet_path = os.path.join(tmpdir, "human_test_50.parquet")

            with DatasetWriter(output_csv_path=csv_path, output_parquet_path=parquet_path, batch_size=25) as writer:
                collector = HumanCohortCollector(writer=writer)
                for cohort in sorted(list(VALID_HUMAN_COHORTS)):
                    collector.collect_cohort(cohort=cohort, count=10, delay_between_sessions=0.0)

            self.assertTrue(os.path.exists(csv_path), "File CSV phải được tạo")

            # Đọc lại và kiểm tra tính toàn vẹn 26 cột
            df = pd.read_csv(csv_path)
            self.assertEqual(len(df), 50, "Tổng số mẫu phải là 50 (10 mẫu/cohort x 5 cohorts)")
            self.assertListEqual(list(df.columns), DATASET_COLUMNS)
            self.assertTrue((df["tool_name"] == "human_cohort").all())
            self.assertTrue((df["label"] == 0).all(), "100% mẫu người thật phải có label = 0")
            self.assertTrue((df["is_webdriver"] == 0.0).all())
            self.assertTrue((df["virtual_gpu_detected"] == 0.0).all())
            self.assertTrue((df["prototype_tampered"] == 0.0).all())
            self.assertTrue((df["gateway_decision"] == "Allow").all())
            self.assertTrue((df["risk_score"] < 0.20).all())

            # Phân bố đều 10 mẫu cho mỗi cohort trong cột attack_mode
            cohort_counts = df["attack_mode"].value_counts().to_dict()
            for cohort in VALID_HUMAN_COHORTS:
                self.assertEqual(cohort_counts[cohort], 10, f"Cohort {cohort} phải có đúng 10 mẫu")

if __name__ == "__main__":
    print("=" * 60)
    print(" KIỂM THỬ SUBTASK 13.3.2.3: HUMAN COHORT COLLECTOR")
    print("=" * 60)
    unittest.main()
