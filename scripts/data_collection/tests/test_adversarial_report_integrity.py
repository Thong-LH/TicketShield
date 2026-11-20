"""
test_adversarial_report_integrity.py
Unit Test Suite & Verification: Thẩm Định Toàn Vẹn Số Liệu Đối Kháng & Báo Cáo Chương 4-5 (Subtask 13.3.5.4).
Kiểm chứng tự động:
  1. Sự tồn tại và tính sẵn sàng của tất cả báo cáo và tệp dữ liệu nguồn JSON/CSV.
  2. Tính nhất quán toán học (Mathematical Consistency): TP + FN = 1000, TN + FP = 1000, tổng tỷ lệ = 100%.
  3. Tính chuẩn xác của 3 tầng đánh giá: Hold-out 400 mẫu, LOCO Generalization, Full 2,000 mẫu.
  4. Đối soát chéo giữa các số liệu trong văn bản Markdown (Chương 4 & 5) với file JSON Single Source of Truth.
  5. Sẵn sàng 100% để nghiệm thu đóng Task ML-13.3.5 và chính thức hoàn tất toàn bộ Epic US-13.3 (SCRUM-378).
"""

import os
import sys
import json
import unittest
import pandas as pd  # type: ignore

CUR_DIR = os.path.dirname(os.path.abspath(__file__))
DATA_COLLECTION_DIR = os.path.abspath(os.path.join(CUR_DIR, ".."))
WORKSPACE_ROOT = os.path.abspath(os.path.join(CUR_DIR, "../../../.."))
TICKETSHIELD_ROOT = os.path.join(WORKSPACE_ROOT, "TicketShield") if os.path.exists(os.path.join(WORKSPACE_ROOT, "TicketShield")) else WORKSPACE_ROOT

# Fallback path checking
AI_ENGINE_DIR = os.path.join(TICKETSHIELD_ROOT, "src", "AIEngine")
DOCS_DIR = os.path.join(TICKETSHIELD_ROOT, "docs")

# File Paths
CHAPTER_4_PATH = os.path.join(DOCS_DIR, "reports", "ADVERSARIAL_TESTING_REPORT_CHAPTER_4.md")
CHAPTER_5_PATH = os.path.join(DOCS_DIR, "reports", "EVASION_ROOT_CAUSE_ANALYSIS_CHAPTER_5.md")

JSON_EVAL_PATH = os.path.join(AI_ENGINE_DIR, "models", "adversarial_empirical_evaluation_v1_vs_v2.json")
CSV_TOOL_PATH = os.path.join(AI_ENGINE_DIR, "models", "adversarial_per_tool_breakdown.csv")
CSV_COHORT_PATH = os.path.join(AI_ENGINE_DIR, "models", "adversarial_per_cohort_breakdown.csv")


class TestAdversarialReportIntegrity(unittest.TestCase):
    """Test suite kiểm chứng toàn vẹn số liệu đối kháng và tài liệu báo cáo luận văn."""

    @classmethod
    def setUpClass(cls):
        """Nạp dữ liệu JSON nguồn và nội dung báo cáo Markdown."""
        for path in [CHAPTER_4_PATH, CHAPTER_5_PATH, JSON_EVAL_PATH, CSV_TOOL_PATH, CSV_COHORT_PATH]:
            if not os.path.exists(path):
                raise FileNotFoundError(f"Không tìm thấy tệp bắt buộc tại: {path}")

        with open(JSON_EVAL_PATH, "r", encoding="utf-8") as f:
            cls.eval_data = json.load(f)

        cls.df_tools = pd.read_csv(CSV_TOOL_PATH)
        cls.df_cohorts = pd.read_csv(CSV_COHORT_PATH)

        with open(CHAPTER_4_PATH, "r", encoding="utf-8") as f:
            cls.c4_text = f.read()

        with open(CHAPTER_5_PATH, "r", encoding="utf-8") as f:
            cls.c5_text = f.read()

    def test_01_all_required_artifacts_exist(self):
        """TC-01: Kiểm tra sự tồn tại của đầy đủ 5 artifacts đầu ra của ML-13.3.5."""
        self.assertTrue(os.path.exists(CHAPTER_4_PATH), "Thiếu file Báo cáo Chương 4")
        self.assertTrue(os.path.exists(CHAPTER_5_PATH), "Thiếu file Báo cáo Chương 5")
        self.assertTrue(os.path.exists(JSON_EVAL_PATH), "Thiếu file JSON đánh giá thực nghiệm")
        self.assertTrue(os.path.exists(CSV_TOOL_PATH), "Thiếu file CSV phân rã công cụ bot")
        self.assertTrue(os.path.exists(CSV_COHORT_PATH), "Thiếu file CSV phân rã nhóm human")

    def test_02_json_metadata_and_structure_integrity(self):
        """TC-02: Kiểm tra cấu trúc metadata và các khóa bắt buộc của JSON nguồn."""
        meta = self.eval_data.get("report_metadata", {})
        self.assertEqual(meta.get("task_id"), "ML-13.3.5")
        self.assertEqual(meta.get("epic_id"), "US-13.3")
        self.assertIn("SCRUM-383", meta.get("jira_keys", []))
        self.assertEqual(meta.get("total_dataset_samples"), 2000)

        # Kiểm tra 3 tầng đánh giá và phần takeaways
        self.assertIn("tier1_holdout_test_evaluation", self.eval_data)
        self.assertIn("tier2_loco_experiments", self.eval_data)
        self.assertIn("tier3_full_adversarial_testbed", self.eval_data)
        self.assertIn("key_takeaways_for_thesis", self.eval_data)

    def test_03_tier1_holdout_mathematical_consistency(self):
        """TC-03: Thẩm định tính nhất quán toán học trên tập kiểm thử Hold-out (400 mẫu)."""
        t1 = self.eval_data["tier1_holdout_test_evaluation"]
        m1 = t1["model_v1"]["operational_3zone_metrics"]
        m2 = t1["model_v2"]["operational_3zone_metrics"]

        # Tổng số mẫu hold-out phải đúng 400 (200 bot, 200 human)
        self.assertEqual(m1["bot_performance"]["total_bots"], 200)
        self.assertEqual(m1["human_performance"]["total_humans"], 200)

        # Model v1: Hard Block 58.0% (116), Challenge 42.0% (84), Evasion 0.0% (0)
        self.assertEqual(m1["bot_performance"]["hard_blocked_count"], 116)
        self.assertEqual(m1["bot_performance"]["challenged_count"], 84)
        self.assertEqual(m1["bot_performance"]["evaded_count"], 0)
        self.assertEqual(m1["bot_performance"]["hard_block_rate_pct"], 58.0)
        self.assertEqual(m1["bot_performance"]["challenge_rate_pct"], 42.0)

        # Model v2: Hard Block 100.0% (200), Challenge 0.0% (0), Evasion 0.0% (0)
        self.assertEqual(m2["bot_performance"]["hard_blocked_count"], 200)
        self.assertEqual(m2["bot_performance"]["challenged_count"], 0)
        self.assertEqual(m2["bot_performance"]["hard_block_rate_pct"], 100.0)

        # Human trong cả 2 model phải Allow 100% (200 mẫu), False Positive Rate = 0.0%
        self.assertEqual(m1["human_performance"]["smooth_allowed_count"], 200)
        self.assertEqual(m1["human_performance"]["false_positive_rate_pct"], 0.0)
        self.assertEqual(m2["human_performance"]["smooth_allowed_count"], 200)
        self.assertEqual(m2["human_performance"]["false_positive_rate_pct"], 0.0)

    def test_04_tier2_loco_experiments_integrity(self):
        """TC-04: Thẩm định kết quả thí nghiệm rút nhóm LOCO (Brave Farbling & Stealth Bots)."""
        loco = self.eval_data["tier2_loco_experiments"]["loco_results"]

        # LOCO 1: Brave Farbling Holdout
        brave = loco["brave_farbling_holdout"]
        self.assertEqual(brave["total_brave_samples"], 200)
        self.assertEqual(brave["allowed_count"], 200)
        self.assertEqual(brave["blocked_count"], 0)
        self.assertEqual(brave["false_positive_rate"], 0.0)
        self.assertTrue(brave["passed"])

        # LOCO 2: Stealth Bot Generalization
        stealth = loco["stealth_bot_holdout"]["joint_stealth_holdout"]
        self.assertEqual(stealth["total_samples"], 500)
        self.assertGreaterEqual(stealth["interception_rate"], 99.0)  # 99.2%
        self.assertGreaterEqual(stealth["recall_at_0_50"], 97.0)     # 97.2%
        self.assertTrue(stealth["passed"])

    def test_05_tier3_full_dataset_mathematical_consistency(self):
        """TC-05: Thẩm định tính nhất quán toán học trên toàn bộ 2,000 mẫu thực nghiệm."""
        t3 = self.eval_data["tier3_full_adversarial_testbed"]
        v1_op = t3["overall_v1"]["operational_3zone_metrics"]
        v2_op = t3["overall_v2"]["operational_3zone_metrics"]

        # Tổng bot và human
        self.assertEqual(v1_op["bot_performance"]["total_bots"], 1000)
        self.assertEqual(v1_op["human_performance"]["total_humans"], 1000)

        # Model v1 toàn hệ thống: Block 558 (55.8%), Challenge 442 (44.2%), Evasion 0
        self.assertEqual(v1_op["bot_performance"]["hard_blocked_count"], 558)
        self.assertEqual(v1_op["bot_performance"]["challenged_count"], 442)
        self.assertEqual(v1_op["bot_performance"]["hard_block_rate_pct"], 55.8)
        self.assertEqual(v1_op["bot_performance"]["challenge_rate_pct"], 44.2)
        self.assertEqual(
            v1_op["bot_performance"]["hard_blocked_count"] + v1_op["bot_performance"]["challenged_count"],
            1000,
            "Tổng số bot bị chặn hoặc challenge trong v1 phải đúng 1000"
        )

        # Model v2 toàn hệ thống: Block 1000 (100.0%), Challenge 0, Evasion 0
        self.assertEqual(v2_op["bot_performance"]["hard_blocked_count"], 1000)
        self.assertEqual(v2_op["bot_performance"]["challenged_count"], 0)
        self.assertEqual(v2_op["bot_performance"]["hard_block_rate_pct"], 100.0)

        # Phân phối Shadow Mode Cohorts: tổng phải đúng 2,000
        cohort_counts = t3["shadow_mode_cohort_distribution"]["counts"]
        self.assertEqual(cohort_counts["v1_allow_v2_allow"], 1000)
        self.assertEqual(cohort_counts["v1_block_v2_block"], 558)
        self.assertEqual(cohort_counts["v1_challenge_v2_block"], 442)
        total_cohort_samples = sum(cohort_counts.values())
        self.assertEqual(total_cohort_samples, 2000, "Tổng số mẫu các nhóm đối chứng Shadow Mode phải đúng 2000")

    def test_06_csv_per_tool_and_cohort_breakdowns(self):
        """TC-06: Thẩm định tính toàn vẹn của các file CSV bóc tách công cụ và nhóm người dùng."""
        # Kiểm tra Bot CSV
        self.assertEqual(self.df_tools["sample_count"].sum(), 1000, "Tổng mẫu bot trong CSV phải đúng 1000")
        for _, row in self.df_tools.iterrows():
            total = row["sample_count"]
            v1_sum = row["v1_block_count"] + row["v1_challenge_count"] + row["v1_allow_count"]
            v2_sum = row["v2_block_count"] + row["v2_challenge_count"] + row["v2_allow_count"]
            self.assertEqual(v1_sum, total, f"Hàng bot {row['tool_name']}.{row['attack_mode']} v1 count không khớp")
            self.assertEqual(v2_sum, total, f"Hàng bot {row['tool_name']}.{row['attack_mode']} v2 count không khớp")
            self.assertEqual(row["v2_hard_block_rate_pct"], 100.0, f"Model v2 phải block 100% {row['tool_name']}")

        # Kiểm tra Human CSV
        self.assertEqual(self.df_cohorts["sample_count"].sum(), 1000, "Tổng mẫu human trong CSV phải đúng 1000")
        for _, row in self.df_cohorts.iterrows():
            total = row["sample_count"]
            self.assertEqual(total, 200, f"Cohort {row['cohort_name']} phải có đúng 200 mẫu")
            self.assertEqual(row["v2_allow_rate_pct"], 100.0, f"Model v2 phải allow 100% {row['cohort_name']}")
            self.assertEqual(row["v2_false_positive_rate_pct"], 0.0, f"Model v2 FPR phải là 0.0% {row['cohort_name']}")

    def test_07_markdown_chapter4_structure_and_metrics_cross_check(self):
        """TC-07: Đối soát các số liệu và đề mục trong văn bản Báo cáo Chương 4."""
        # Kiểm tra đầy đủ 7 đề mục (4.1 -> 4.7)
        for i in range(1, 8):
            heading = f"4.{i}."
            self.assertIn(heading, self.c4_text, f"Báo cáo Chương 4 thiếu đề mục {heading}")

        # Kiểm tra các số liệu chủ chốt xuất hiện chính xác trong văn bản
        key_numbers = [
            "2,000",
            "1,000",
            "14.8",
            "85.2",
            "55.8",
            "100.0",
            "0.00",
            "7.84",
            "99.2",
        ]
        for num in key_numbers:
            self.assertIn(num, self.c4_text, f"Báo cáo Chương 4 thiếu số liệu kiểm chứng: {num}")

    def test_08_markdown_chapter5_structure_and_technical_rigor(self):
        """TC-08: Đối soát các thuật ngữ và đề mục kỹ thuật chuyên sâu trong Báo cáo Chương 5."""
        # Kiểm tra đầy đủ đề mục (5.1 -> 5.5)
        for i in range(1, 6):
            heading = f"5.{i}."
            self.assertIn(heading, self.c5_text, f"Báo cáo Chương 5 thiếu đề mục {heading}")

        # Kiểm tra các thuật ngữ kỹ thuật an toàn thông tin cốt lõi
        tech_terms = [
            "navigator.webdriver",
            "cdc_",
            "SwiftShader",
            "llvmpipe",
            "Function.prototype.toString",
            "JA4",
            "concurrent_hold",
            "burst_rate_10s",
            "Proof-of-Work",
            "Shadow Mode",
        ]
        for term in tech_terms:
            self.assertIn(term, self.c5_text, f"Báo cáo Chương 5 thiếu thuật ngữ an toàn thông tin: {term}")


if __name__ == "__main__":
    unittest.main(verbosity=2)
