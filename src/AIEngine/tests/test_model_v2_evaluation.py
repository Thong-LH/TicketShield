"""
test_model_v2_evaluation.py
Unit tests cho Subtask 13.3.3.3: Scientific Evaluation & Empirical Threshold Sweep.
Kiểm thử tính đúng đắn khoa học của:
  1. LOCO 1: Brave Privacy Farbling Holdout (FPR = 0.0%).
  2. LOCO 2: Stealth Bot Generalization (Recall >= 98.0%).
  3. Empirical Threshold Sweep (19 ngưỡng quyết định, đầy đủ metrics).
  4. JSON Evaluation Report schema & integrity.
"""

import os
import sys
import json
import unittest
import pandas as pd

CUR_DIR = os.path.dirname(os.path.abspath(__file__))
AI_ENGINE_DIR = os.path.abspath(os.path.join(CUR_DIR, ".."))
WORKSPACE_ROOT = os.path.abspath(os.path.join(CUR_DIR, "../../../.."))

for p in [AI_ENGINE_DIR, WORKSPACE_ROOT]:
    if p not in sys.path:
        sys.path.insert(0, p)

try:
    from app.evaluate_v2 import (
        evaluate_loco_brave_farbling,
        evaluate_loco_stealth_bots,
        run_empirical_threshold_sweep,
        generate_comprehensive_evaluation_report,
        DEFAULT_DATASET_PATH,
        DEFAULT_MODEL_JOB_PATH,
        DEFAULT_SWEEP_CSV_PATH,
        DEFAULT_REPORT_JSON_PATH,
    )
    from app.train_v2 import (
        load_and_preprocess_dataset,
        build_random_forest_model,
    )
    from app.feature_pipeline import (
        FEATURE_COLUMNS_V2,
        transform_dataframe,
    )
except ImportError:
    from TicketShield.src.AIEngine.app.evaluate_v2 import (
        evaluate_loco_brave_farbling,
        evaluate_loco_stealth_bots,
        run_empirical_threshold_sweep,
        generate_comprehensive_evaluation_report,
        DEFAULT_DATASET_PATH,
        DEFAULT_MODEL_JOB_PATH,
        DEFAULT_SWEEP_CSV_PATH,
        DEFAULT_REPORT_JSON_PATH,
    )
    from TicketShield.src.AIEngine.app.train_v2 import (
        load_and_preprocess_dataset,
        build_random_forest_model,
    )
    from TicketShield.src.AIEngine.app.feature_pipeline import (
        FEATURE_COLUMNS_V2,
        transform_dataframe,
    )


class TestModelV2Evaluation(unittest.TestCase):
    """Bộ kiểm thử đánh giá mô hình AI v2.0."""

    @classmethod
    def setUpClass(cls):
        """Nạp dataset và huấn luyện nhanh mô hình phục vụ kiểm thử."""
        cls.X, cls.y, cls.df = load_and_preprocess_dataset(DEFAULT_DATASET_PATH)
        # Sử dụng 200 mẫu đại diện phân tầng để chạy test cực nhanh (< 2 giây)
        step = max(1, len(cls.df) // 200)
        cls.df_sample = cls.df.iloc[::step].copy()

    def test_01_loco_brave_farbling_zero_fpr(self):
        """Thí nghiệm LOCO 1: Rút nhóm Brave ra khỏi tập train, FPR tại ngưỡng Block phải là 0%."""
        res = evaluate_loco_brave_farbling(self.df, random_state=42)
        self.assertEqual(res["total_brave_samples"], 200)
        self.assertEqual(res["blocked_count"], 0)
        self.assertEqual(res["false_positive_rate"], 0.0)
        self.assertTrue(res["passed"])
        self.assertEqual(res["allow_rate"], 100.0)

    def test_02_loco_stealth_bots_high_recall(self):
        """Thí nghiệm LOCO 2: Rút từng nhóm stealth bot ra khỏi tập train, Recall phải >= 98%."""
        res = evaluate_loco_stealth_bots(self.df, random_state=42)
        # Kiểm tra LOCO Puppeteer Stealth
        p_stealth = res["puppeteer_stealth"]
        self.assertEqual(p_stealth["total_samples"], 250)
        self.assertGreaterEqual(p_stealth["recall_at_0_50"], 98.0)
        self.assertTrue(p_stealth["passed"])

        # Kiểm tra LOCO Undetected Chromedriver
        uc_stealth = res["undetected_chromedriver"]
        self.assertEqual(uc_stealth["total_samples"], 250)
        self.assertGreaterEqual(uc_stealth["recall_at_0_50"], 98.0)
        self.assertTrue(uc_stealth["passed"])

        # Kiểm tra Joint Holdout (chặn/thách thức >= 98%)
        joint = res["joint_stealth_holdout"]
        self.assertEqual(joint["total_samples"], 500)
        self.assertGreaterEqual(joint["interception_rate"], 98.0)
        self.assertTrue(res["all_passed"])

    def test_03_empirical_threshold_sweep_structure(self):
        """Kiểm tra bảng quét 19 ngưỡng: đủ 19 bước từ 0.05 đến 0.95 với đầy đủ metrics."""
        from sklearn.model_selection import train_test_split
        _, X_test, _, y_test = train_test_split(self.X, self.y, test_size=0.20, random_state=42, stratify=self.y)
        
        clf = build_random_forest_model(random_state=42)
        clf.fit(self.X, self.y)

        df_sweep = run_empirical_threshold_sweep(clf, X_test, y_test)
        self.assertEqual(len(df_sweep), 19)

        expected_columns = [
            "threshold", "total_samples", "true_negatives", "false_positives",
            "false_negatives", "true_positives", "precision", "recall",
            "f1_score", "false_positive_rate", "false_negative_rate", "action_recommendation"
        ]
        for col in expected_columns:
            self.assertIn(col, df_sweep.columns)

        # Ngưỡng đầu tiên 0.05, cuối cùng 0.95
        self.assertAlmostEqual(df_sweep.iloc[0]["threshold"], 0.05)
        self.assertAlmostEqual(df_sweep.iloc[-1]["threshold"], 0.95)

        # File CSV phải được tạo
        self.assertTrue(os.path.exists(DEFAULT_SWEEP_CSV_PATH))

    def test_04_comprehensive_evaluation_report_json(self):
        """Kiểm tra tính toàn vẹn của Báo cáo Khoa học JSON xuất ra."""
        from sklearn.model_selection import train_test_split
        _, X_test, _, y_test = train_test_split(self.X, self.y, test_size=0.20, random_state=42, stratify=self.y)
        
        clf = build_random_forest_model(random_state=42)
        clf.fit(self.X, self.y)

        report = generate_comprehensive_evaluation_report(clf, X_test, y_test, self.df)
        self.assertEqual(report["task_id"], "ML-13.3.3")
        self.assertEqual(report["model_version"], "2.0.0")
        self.assertEqual(report["feature_count"], 11)
        self.assertEqual(report["feature_names"], FEATURE_COLUMNS_V2)

        # Tuân thủ SLA khoa học
        compliance = report["compliance_status"]
        self.assertTrue(compliance["fpr_under_1_percent"])
        self.assertTrue(compliance["recall_over_98_percent"])
        self.assertTrue(compliance["auc_over_0_99"])
        self.assertTrue(compliance["ready_for_onnx_export"])

        # File JSON phải tồn tại trên đĩa
        self.assertTrue(os.path.exists(DEFAULT_REPORT_JSON_PATH))
        with open(DEFAULT_REPORT_JSON_PATH, "r", encoding="utf-8") as f:
            saved_json = json.load(f)
        self.assertEqual(saved_json["task_id"], "ML-13.3.3")


if __name__ == "__main__":
    unittest.main()
