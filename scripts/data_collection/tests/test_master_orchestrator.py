"""
test_master_orchestrator.py
Unit Test Suite kiểm định Master Orchestrator CLI Runner (Subtask 13.3.1.8).
Kiểm tra toàn diện quy trình điều phối đa công cụ và xuất dataset.
"""

import os
import sys
import tempfile
import unittest
import pandas as pd

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "../../../"))
if REPO_ROOT not in sys.path:
    sys.path.insert(0, REPO_ROOT)

from scripts.data_collection.run_adversarial_bot_collection import (
    run_collection,
    preflight_check,
)
from scripts.data_collection.models.schema import DATASET_COLUMNS

class TestMasterOrchestrator(unittest.TestCase):
    def test_preflight_check_safe(self):
        """Kiểm tra preflight_check chạy an toàn và trả về bool."""
        res = preflight_check("http://localhost:59999") # Port không tồn tại
        self.assertFalse(res)

    def test_end_to_end_orchestration(self):
        """Kiểm tra toàn trình thu thập dữ liệu với 4 công cụ (10 mẫu/tool = 40 mẫu)."""
        with tempfile.TemporaryDirectory() as tmpdir:
            csv_path = os.path.join(tmpdir, "test_orchestrator_40.csv")
            parquet_path = os.path.join(tmpdir, "test_orchestrator_40.parquet")

            run_collection(
                tools="all",
                samples_per_tool=10,
                output_csv=csv_path,
                output_parquet=parquet_path,
                batch_size=10,
                delay=0.0
            )

            self.assertTrue(os.path.exists(csv_path), "File CSV tổng hợp phải tồn tại")

            df = pd.read_csv(csv_path)
            self.assertEqual(len(df), 40, "Phải có đúng 40 mẫu thu thập được")
            self.assertListEqual(list(df.columns), DATASET_COLUMNS)
            self.assertTrue((df["label"] == 1).all())

            # Kiểm tra phân bổ đủ 4 công cụ
            tools_present = set(df["tool_name"].unique())
            expected_tools = {"playwright", "puppeteer_stealth", "undetected_chromedriver", "curl_cffi"}
            self.assertEqual(tools_present, expected_tools)

            for t in expected_tools:
                count_t = (df["tool_name"] == t).sum()
                self.assertEqual(count_t, 10, f"Công cụ {t} phải có đúng 10 mẫu")

if __name__ == "__main__":
    print("=" * 60)
    print(" KIỂM THỬ SUBTASK 13.3.1.8: MASTER ORCHESTRATOR")
    print("=" * 60)
    unittest.main()
