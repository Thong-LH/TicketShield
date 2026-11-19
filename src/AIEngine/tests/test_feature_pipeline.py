"""
test_feature_pipeline.py
Unit Test Suite kiểm định 3-Tier Feature Transformation Pipeline (Subtask 13.3.3.1).
Kiểm tra khả năng trích xuất và chuẩn hóa 11 chiều đặc trưng 3 tầng cho AI Anti-Bot Model v2.0.
"""

import os
import sys
import time
import math
import unittest
import pandas as pd

CUR_DIR = os.path.dirname(os.path.abspath(__file__))
AI_ENGINE_DIR = os.path.abspath(os.path.join(CUR_DIR, ".."))
WORKSPACE_ROOT = os.path.abspath(os.path.join(CUR_DIR, "../../../.."))

for p in [AI_ENGINE_DIR, WORKSPACE_ROOT]:
    if p not in sys.path:
        sys.path.insert(0, p)

try:
    from app.feature_pipeline import (
        FEATURE_COLUMNS_V2,
        encode_ua_match_level,
        compute_network_inconsistency_score,
        extract_features_from_dict,
        transform_single_record,
        transform_dataframe,
        get_feature_names,
        validate_feature_matrix,
    )
except ImportError:
    from TicketShield.src.AIEngine.app.feature_pipeline import (
        FEATURE_COLUMNS_V2,
        encode_ua_match_level,
        compute_network_inconsistency_score,
        extract_features_from_dict,
        transform_single_record,
        transform_dataframe,
        get_feature_names,
        validate_feature_matrix,
    )

MERGED_CSV_PATH = os.path.join(AI_ENGINE_DIR, "datasets", "raw", "merged_training_dataset_v2.csv")


class TestFeaturePipelineV2(unittest.TestCase):
    """Test Suite cho module feature_pipeline.py."""

    def test_01_feature_columns_count_and_order(self):
        """TC-FEAT-01: Kiểm tra đúng 11 đặc trưng 3 tầng theo thứ tự cố định."""
        names = get_feature_names()
        self.assertEqual(len(names), 11, "Model v2.0 phải có đúng 11 chiều đặc trưng")
        expected_cols = [
            "is_webdriver",
            "virtual_gpu_detected",
            "prototype_tampered",
            "canvas_tampered",
            "action_interval_std_dev",
            "ua_match_level",
            "network_inconsistency_score",
            "time_to_checkout",
            "burst_rate_10s",
            "concurrent_hold",
            "device_account_overlap",
        ]
        self.assertListEqual(names, expected_cols)

    def test_02_ua_match_level_encoding(self):
        """TC-FEAT-02: Kiểm tra bảng mã hóa số chuẩn xác cho ua_match_level."""
        self.assertEqual(encode_ua_match_level("ExactMatch"), 0.0)
        self.assertEqual(encode_ua_match_level("MinorMismatch"), 0.5)
        self.assertEqual(encode_ua_match_level("BlatantMismatch"), 1.0)
        self.assertEqual(encode_ua_match_level("Unknown"), -1.0)
        self.assertEqual(encode_ua_match_level(""), -1.0)
        self.assertEqual(encode_ua_match_level(None), -1.0)
        self.assertEqual(encode_ua_match_level(0.5), 0.5)

    def test_03_network_inconsistency_score(self):
        """TC-FEAT-03: Kiểm tra tính toán điểm bất nhất mạng (Protocol Inconsistency)."""
        # 1. Ca người thật sạch
        clean_row = {
            "ua_match_level": "ExactMatch",
            "ja4_fingerprint": "t13d1516h2_8daaf6152771_0271dda329",
            "header_user_agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
            "client_user_agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
        }
        score_clean = compute_network_inconsistency_score(clean_row)
        self.assertEqual(score_clean, 0.0, "Người thật duyệt web chuẩn phải có inconsistency = 0.0")

        # 2. Ca lệch pha nghiêm trọng Blatant Mismatch
        mismatch_row = {
            "ua_match_level": "BlatantMismatch",
            "ja4_fingerprint": "t13d1516h2_8daaf6152771_0271dda329",
        }
        score_mismatch = compute_network_inconsistency_score(mismatch_row)
        self.assertGreaterEqual(score_mismatch, 0.70)

        # 3. Ca bot cURL cffi
        curl_row = {
            "ua_match_level": "MinorMismatch",
            "ja4_fingerprint": "t13d0302h2_9e27c1973656_9a4199c15984",
            "tool_name": "curl_cffi",
        }
        score_curl = compute_network_inconsistency_score(curl_row)
        self.assertGreaterEqual(score_curl, 0.80)

    def test_04_missing_values_safe_fallback(self):
        """TC-FEAT-04: Kiểm tra khả năng xử lý an toàn các giá trị missing / None / NaN."""
        empty_row = {}
        feats = extract_features_from_dict(empty_row)
        self.assertEqual(len(feats), 11)

        # Các trường thiếu phải là -1.0 (hoặc default phù hợp)
        self.assertEqual(feats[0], -1.0)  # is_webdriver
        self.assertEqual(feats[1], -1.0)  # virtual_gpu_detected
        self.assertEqual(feats[2], -1.0)  # prototype_tampered
        self.assertEqual(feats[3], -1.0)  # canvas_tampered
        self.assertEqual(feats[4], -1.0)  # action_interval_std_dev
        self.assertEqual(feats[5], -1.0)  # ua_match_level
        self.assertEqual(feats[7], -1.0)  # time_to_checkout
        self.assertEqual(feats[8], 1.0)   # burst_rate_10s default
        self.assertEqual(feats[9], 0.0)   # concurrent_hold default
        self.assertEqual(feats[10], -1.0) # device_account_overlap

        # Khẳng định không có phần tử nào là None hay NaN
        for val in feats:
            self.assertIsInstance(val, float)
            self.assertFalse(math.isnan(val))

    def test_05_transform_single_record(self):
        """TC-FEAT-05: Kiểm tra chuyển đổi 1 record đơn lẻ phục vụ inference thời gian thực."""
        sample = {
            "is_webdriver": 0.0,
            "virtual_gpu_detected": 0.0,
            "prototype_tampered": 0.0,
            "canvas_tampered": 1.0,  # Brave
            "action_interval_std_dev": 185.5,
            "ua_match_level": "ExactMatch",
            "network_inconsistency_score": 0.0,
            "time_to_checkout": 14.2,
            "burst_rate_10s": 2.0,
            "concurrent_hold": 1.0,
            "device_account_overlap": 0.0,
        }
        tensor = transform_single_record(sample)
        self.assertEqual(tensor.shape, (1, 11))
        self.assertEqual(str(tensor.dtype), "float32")
        self.assertEqual(tensor[0, 3], 1.0)  # canvas_tampered
        self.assertAlmostEqual(tensor[0, 4], 185.5, places=1)

    def test_06_transform_dataframe_on_real_dataset(self):
        """TC-FEAT-06: Chuyển đổi toàn bộ 2,000 mẫu dataset thực tế và đo tốc độ thực thi."""
        self.assertTrue(os.path.exists(MERGED_CSV_PATH), f"File dataset thực tế phải tồn tại: {MERGED_CSV_PATH}")
        df = pd.read_csv(MERGED_CSV_PATH)
        self.assertEqual(len(df), 2000, "Dataset thực tế phải có đúng 2,000 mẫu")

        start_time = time.perf_counter()
        X, y = transform_dataframe(df, include_target=True)
        duration = time.perf_counter() - start_time

        # Kiểm tra shape và tính hợp lệ
        self.assertEqual(X.shape, (2000, 11))
        self.assertEqual(len(y), 2000)
        self.assertEqual(str(X.dtype), "float32")
        self.assertEqual(str(y.dtype), "int64")

        self.assertTrue(validate_feature_matrix(X), "Ma trận đặc trưng X phải hợp lệ 100%")
        self.assertFalse(pd.isna(X).any(), "Ma trận X không được chứa bất kỳ giá trị NaN nào")
        self.assertFalse(pd.isna(y).any(), "Mảng y không được chứa bất kỳ giá trị NaN nào")

        # SLA tốc độ: chuyển đổi 2,000 mẫu phải < 0.2s
        self.assertLess(duration, 0.20, f"Thời gian xử lý quá lâu: {duration:.4f}s")
        print(f"\n[TestFeaturePipeline] Chuyển đổi thành công {len(df)} mẫu -> X {X.shape} trong {duration*1000:.2f}ms")

    def test_07_cohort_feature_separation(self):
        """TC-FEAT-07: Thẩm định tính phân tách đặc trưng giữa các nhóm quan trọng (Brave vs Bot)."""
        df = pd.read_csv(MERGED_CSV_PATH)
        X, y = transform_dataframe(df, include_target=True)

        # 1. Kiểm tra nhóm Brave Privacy (200 mẫu)
        brave_indices = df[df["attack_mode"] == "brave_privacy"].index
        self.assertEqual(len(brave_indices), 200)

        X_brave = X[brave_indices]
        y_brave = y[brave_indices]

        # Brave có canvas_tampered (cột index 3) = 1.0, is_webdriver (cột index 0) = 0.0, y = 0
        self.assertTrue((X_brave[:, 3] == 1.0).all(), "Toàn bộ mẫu Brave phải có canvas_tampered = 1.0")
        self.assertTrue((X_brave[:, 0] == 0.0).all(), "Toàn bộ mẫu Brave phải có is_webdriver = 0.0")
        self.assertTrue((y_brave == 0).all(), "Toàn bộ mẫu Brave phải có label = 0 (Human)")

        # 2. Kiểm tra nhóm Bot Playwright (250 mẫu)
        pw_indices = df[df["tool_name"] == "playwright"].index
        self.assertEqual(len(pw_indices), 250)
        y_pw = y[pw_indices]
        self.assertTrue((y_pw == 1).all(), "Toàn bộ mẫu Playwright phải có label = 1 (Bot)")


if __name__ == "__main__":
    print("=" * 60)
    print(" KIỂM THỬ SUBTASK 13.3.3.1: 3-TIER FEATURE PIPELINE (V2)")
    print("=" * 60)
    unittest.main()
