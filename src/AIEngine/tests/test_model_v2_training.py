"""
test_model_v2_training.py
Unit Test Suite kiểm định Model v2.0 Training Engine (Subtask 13.3.3.2).
Kiểm tra toàn diện quy trình huấn luyện, phân chia dữ liệu, 5-Fold Cross-Validation và lưu trữ mô hình.
"""

import os
import sys
import tempfile
import unittest
import pandas as pd

CUR_DIR = os.path.dirname(os.path.abspath(__file__))
AI_ENGINE_DIR = os.path.abspath(os.path.join(CUR_DIR, ".."))
WORKSPACE_ROOT = os.path.abspath(os.path.join(CUR_DIR, "../../../.."))

for p in [AI_ENGINE_DIR, WORKSPACE_ROOT]:
    if p not in sys.path:
        sys.path.insert(0, p)

try:
    from app.train_v2 import (
        load_and_preprocess_dataset,
        build_random_forest_model,
        train_model_v2,
        evaluate_model,
        run_5fold_cross_validation,
        get_feature_importances,
        save_model,
    )
    from app.feature_pipeline import FEATURE_COLUMNS_V2
except ImportError:
    from TicketShield.src.AIEngine.app.train_v2 import (
        load_and_preprocess_dataset,
        build_random_forest_model,
        train_model_v2,
        evaluate_model,
        run_5fold_cross_validation,
        get_feature_importances,
        save_model,
    )
    from TicketShield.src.AIEngine.app.feature_pipeline import FEATURE_COLUMNS_V2

import joblib  # type: ignore


class TestModelV2Training(unittest.TestCase):
    """Test Suite cho quá trình huấn luyện AI Anti-Bot Model v2.0."""

    @classmethod
    def setUpClass(cls):
        """Nạp dữ liệu mẫu một lần để dùng chung cho các test cases."""
        cls.X, cls.y, cls.df = load_and_preprocess_dataset()

    def test_01_dataset_loading_and_shape(self):
        """TC-TRN-01: Kiểm tra nạp dataset đúng kích thước 2,000 mẫu và 11 chiều đặc trưng."""
        self.assertEqual(self.X.shape, (2000, 11), "Ma trận X phải có shape (2000, 11)")
        self.assertEqual(len(self.y), 2000, "Mảng nhãn y phải có độ dài 2,000")
        self.assertFalse(pd.isna(self.X).any(), "Không được có giá trị NaN trong ma trận X")
        self.assertEqual((self.y == 1).sum(), 1000, "Tập dữ liệu phải có đúng 1,000 nhãn Bot")
        self.assertEqual((self.y == 0).sum(), 1000, "Tập dữ liệu phải có đúng 1,000 nhãn Human")

    def test_02_model_architecture_and_params(self):
        """TC-TRN-02: Kiểm tra cấu hình siêu tham số của RandomForest v2.0."""
        clf = build_random_forest_model(n_estimators=100, max_depth=12)
        self.assertEqual(clf.n_estimators, 100)
        self.assertEqual(clf.max_depth, 12)
        self.assertEqual(clf.class_weight, "balanced")
        self.assertEqual(clf.random_state, 42)

    def test_03_training_and_evaluation_metrics(self):
        """TC-TRN-03: Huấn luyện trên 80% và kiểm thử trên 20% holdout test set."""
        from sklearn.model_selection import train_test_split
        X_train, X_test, y_train, y_test = train_test_split(
            self.X, self.y, test_size=0.20, random_state=42, stratify=self.y
        )

        self.assertEqual(len(X_train), 1600)
        self.assertEqual(len(X_test), 400)

        clf = train_model_v2(X_train, y_train)
        metrics = evaluate_model(clf, X_test, y_test)

        self.assertGreaterEqual(metrics["roc_auc"], 0.990, "ROC-AUC phải >= 0.990")
        self.assertGreaterEqual(metrics["f1_score"], 0.980, "F1-Score phải >= 0.980")
        self.assertGreaterEqual(metrics["recall"], 0.980, "Độ nhạy phát hiện bot phải >= 0.980")
        self.assertLessEqual(metrics["false_positive_rate"], 0.010, "Tỷ lệ chặn nhầm FPR phải < 1.0%")

        # Kiểm tra ma trận nhầm lẫn
        cm = metrics["confusion_matrix"]
        self.assertEqual(cm["false_positives"], 0, "Không được có mẫu người thật nào bị phân loại nhầm thành bot")

    def test_04_5fold_cross_validation_stability(self):
        """TC-TRN-04: Đánh giá độ ổn định tổng quát hóa qua Stratified 5-Fold Cross-Validation."""
        cv_res = run_5fold_cross_validation(self.X, self.y, n_splits=5, random_state=42)

        self.assertGreaterEqual(cv_res["mean_roc_auc"], 0.990, "Mean ROC-AUC qua 5 folds phải >= 0.990")
        self.assertLessEqual(cv_res["std_roc_auc"], 0.010, "Độ lệch chuẩn qua 5 folds phải < 0.010 (ổn định)")
        self.assertGreaterEqual(cv_res["mean_f1"], 0.980, "Mean F1-score phải >= 0.980")

    def test_05_feature_importance_contract(self):
        """TC-TRN-05: Kiểm tra xếp hạng tầm quan trọng của 11 đặc trưng."""
        clf = train_model_v2(self.X, self.y)
        importances = get_feature_importances(clf)

        self.assertEqual(len(importances), 11, "Phải có đúng 11 đặc trưng được xếp hạng")
        total_importance = sum(imp for _, imp in importances)
        self.assertAlmostEqual(total_importance, 1.0, places=2, msg="Tổng trọng số phải xấp xỉ 1.0")

        # Top 3 đặc trưng quan trọng nhất phải thuộc về các tín hiệu then chốt (timing / overlap)
        top_feature_names = [name for name, _ in importances[:3]]
        self.assertIn("time_to_checkout", top_feature_names)

    def test_06_model_persistence_and_reload(self):
        """TC-TRN-06: Kiểm tra lưu và nạp lại mô hình joblib bảo toàn kết quả suy diễn."""
        with tempfile.TemporaryDirectory() as tmpdir:
            temp_model_path = os.path.join(tmpdir, "test_model_v2.joblib")
            # Lấy mẫu đều cả 2 lớp Bot (y=1) và Human (y=0)
            X_subset = self.X[::10]
            y_subset = self.y[::10]
            clf = train_model_v2(X_subset, y_subset)

            save_model(clf, temp_model_path)
            self.assertTrue(os.path.exists(temp_model_path), "File joblib phải tồn tại sau khi lưu")

            reloaded_clf = joblib.load(temp_model_path)
            orig_preds = clf.predict_proba(self.X[100:110])
            reloaded_preds = reloaded_clf.predict_proba(self.X[100:110])

            for p_orig, p_rel in zip(orig_preds, reloaded_preds):
                self.assertAlmostEqual(p_orig[1], p_rel[1], places=5, msg="Xác suất suy diễn phải đồng nhất")


if __name__ == "__main__":
    print("=" * 60)
    print(" KIỂM THỬ SUBTASK 13.3.3.2: MODEL v2.0 TRAINING ENGINE")
    print("=" * 60)
    unittest.main()
