"""
test_human_orchestrator.py
Unit Test Suite kiểm định Cross-Platform Human Orchestrator Runner (Subtask 13.3.2.4).
Kiểm tra toàn diện quy trình điều phối thu thập 5 cohorts và xuất dataset người thật.
"""

import os
import sys
import tempfile
import unittest
import pandas as pd

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "../../../"))
if REPO_ROOT not in sys.path:
    sys.path.insert(0, REPO_ROOT)

from scripts.data_collection.run_human_cohort_collection import (
    run_human_collection,
    preflight_check,
)
from scripts.data_collection.utils.telemetry_normalizer import VALID_HUMAN_COHORTS
from scripts.data_collection.models.schema import DATASET_COLUMNS

class TestHumanOrchestrator(unittest.TestCase):
    def test_preflight_check_safe(self):
        """Kiểm tra preflight_check chạy an toàn và trả về bool."""
        res = preflight_check("http://localhost:59998")
        self.assertFalse(res)

    def test_end_to_end_human_orchestration(self):
        """Kiểm tra toàn trình thu thập với cả 5 cohorts người thật (5 mẫu/cohort = 25 mẫu)."""
        with tempfile.TemporaryDirectory() as tmpdir:
            csv_path = os.path.join(tmpdir, "test_human_orchestrator_25.csv")
            parquet_path = os.path.join(tmpdir, "test_human_orchestrator_25.parquet")

            run_human_collection(
                cohorts="all",
                samples_per_cohort=5,
                output_csv=csv_path,
                output_parquet=parquet_path,
                batch_size=10,
                delay=0.0
            )

            self.assertTrue(os.path.exists(csv_path), "File CSV tổng hợp người thật phải tồn tại")

            df = pd.read_csv(csv_path)
            self.assertEqual(len(df), 25, "Phải có đúng 25 mẫu thu thập được (5 mẫu x 5 cohorts)")
            self.assertListEqual(list(df.columns), DATASET_COLUMNS)
            self.assertTrue((df["label"] == 0).all(), "100% mẫu người thật phải có label = 0")
            self.assertTrue((df["tool_name"] == "human_cohort").all())
            self.assertTrue((df["is_webdriver"] == 0.0).all())
            self.assertTrue((df["virtual_gpu_detected"] == 0.0).all())
            self.assertTrue((df["prototype_tampered"] == 0.0).all())
            self.assertTrue((df["gateway_decision"] == "Allow").all())

            # Kiểm tra phân bổ đủ 5 cohorts
            cohorts_present = set(df["attack_mode"].unique())
            self.assertEqual(cohorts_present, VALID_HUMAN_COHORTS)

            for c in VALID_HUMAN_COHORTS:
                count_c = (df["attack_mode"] == c).sum()
                self.assertEqual(count_c, 5, f"Cohort {c} phải có đúng 5 mẫu")

if __name__ == "__main__":
    print("=" * 60)
    print(" KIỂM THỬ SUBTASK 13.3.2.4: HUMAN ORCHESTRATOR")
    print("=" * 60)
    unittest.main()
