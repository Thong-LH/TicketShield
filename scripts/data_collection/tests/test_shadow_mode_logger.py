#!/usr/bin/env python3
"""
TicketShield Anti-Bot Gateway - Shadow Mode & FPR Metric Logger Test Suite
Epic US-13.3, Jira SCRUM-382 (Subtask 13.3.4.5)

Thẩm định tính toàn vẹn cú pháp JSON Lines (.jsonl), kiểm chứng schema 11 đặc trưng 3 tầng,
và xác thực độ chính xác số học của bộ đếm thống kê đối chứng FPR/Friction/Stealth.
"""

import sys
import os
import json
import tempfile
import unittest
from datetime import datetime, timezone

REQUIRED_ROOT_KEYS = {
    "timestamp_utc",
    "request_id",
    "execution_mode",
    "client_ip",
    "buyer_email",
    "device_fingerprint",
    "primary_model",
    "shadow_model",
    "shadow_cohort",
    "decision_matches",
    "risk_score_delta",
    "is_false_positive_candidate",
    "is_friction_reduction_candidate",
    "is_stealth_catch_candidate",
    "features_v2",
    "latency_ms"
}

REQUIRED_FEATURES_V2_KEYS = {
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
    "device_account_overlap"
}

REQUIRED_MODEL_SUMMARY_KEYS = {"version", "risk_score", "decision"}
REQUIRED_LATENCY_KEYS = {"evaluation_ms", "total_guard_ms"}
VALID_DECISIONS = {"Allow", "Challenge", "Block"}
VALID_EXECUTION_MODES = {"Shadow", "Enforce", "Audit"}


def create_sample_log_entry(
    request_id: str,
    execution_mode: str,
    cohort: str,
    p_dec: str,
    p_risk: float,
    s_dec: str,
    s_risk: float,
    eval_ms: float = 0.035,
    guard_ms: float = 1.25
) -> dict:
    """Tạo 1 log entry mẫu theo đúng schema chuẩn của Gateway (Mục 4 của Plan)."""
    delta = round(abs(p_risk - s_risk), 4)
    matches = (p_dec == s_dec)
    is_fp = (cohort == "v1_block_v2_allow")
    is_friction = (cohort == "v1_challenge_v2_allow")
    is_stealth = (cohort in ("v1_allow_v2_block", "v1_challenge_v2_block"))

    return {
        "timestamp_utc": datetime.now(timezone.utc).isoformat(),
        "request_id": request_id,
        "execution_mode": execution_mode,
        "client_ip": "127.0.0.1",
        "buyer_email": f"{request_id.lower()}@ticketshield.io",
        "device_fingerprint": f"df_{request_id.lower()}_123456",
        "primary_model": {
            "version": "1.0.0",
            "risk_score": p_risk,
            "decision": p_dec
        },
        "shadow_model": {
            "version": "2.0.0",
            "risk_score": s_risk,
            "decision": s_dec
        },
        "shadow_cohort": cohort,
        "decision_matches": matches,
        "risk_score_delta": delta,
        "is_false_positive_candidate": is_fp,
        "is_friction_reduction_candidate": is_friction,
        "is_stealth_catch_candidate": is_stealth,
        "features_v2": {
            "is_webdriver": 0.0,
            "virtual_gpu_detected": 0.0,
            "prototype_tampered": 0.0,
            "canvas_tampered": 1.0 if is_fp else 0.0,
            "action_interval_std_dev": 165.0,
            "ua_match_level": 0.0,
            "network_inconsistency_score": 0.0,
            "time_to_checkout": 15.2,
            "burst_rate_10s": 1.0,
            "concurrent_hold": 0.0,
            "device_account_overlap": 0.0
        },
        "latency_ms": {
            "evaluation_ms": eval_ms,
            "total_guard_ms": guard_ms
        }
    }


def aggregate_shadow_logs(log_entries: list[dict]) -> dict:
    """Mô phỏng bộ tổng hợp số liệu tương đương IShadowMetricsAggregator trong C#."""
    total = len(log_entries)
    if total == 0:
        return {
            "total_evaluations": 0,
            "agreement_count": 0,
            "agreement_rate": 0.0,
            "rescued_false_positives": 0,
            "rescued_false_positive_rate": 0.0,
            "friction_reduction": 0,
            "friction_reduction_rate": 0.0,
            "caught_stealth_bots": 0,
            "caught_stealth_bot_rate": 0.0,
            "captcha_bot_elimination": 0,
            "captcha_bot_elimination_rate": 0.0,
            "average_risk_score_delta": 0.0
        }

    agreements = sum(1 for e in log_entries if e["decision_matches"])
    rescued_fp = sum(1 for e in log_entries if e["is_false_positive_candidate"])
    friction_red = sum(1 for e in log_entries if e["is_friction_reduction_candidate"])
    stealth_caught = sum(1 for e in log_entries if e["shadow_cohort"] == "v1_allow_v2_block")
    captcha_bot = sum(1 for e in log_entries if e["shadow_cohort"] == "v1_challenge_v2_block")
    delta_sum = sum(e["risk_score_delta"] for e in log_entries)

    return {
        "total_evaluations": total,
        "agreement_count": agreements,
        "agreement_rate": round(agreements / total, 4),
        "rescued_false_positives": rescued_fp,
        "rescued_false_positive_rate": round(rescued_fp / total, 4),
        "friction_reduction": friction_red,
        "friction_reduction_rate": round(friction_red / total, 4),
        "caught_stealth_bots": stealth_caught,
        "caught_stealth_bot_rate": round(stealth_caught / total, 4),
        "captcha_bot_elimination": captcha_bot,
        "captcha_bot_elimination_rate": round(captcha_bot / total, 4),
        "average_risk_score_delta": round(delta_sum / total, 4)
    }


class TestShadowModeLogger(unittest.TestCase):
    """Test suite kiểm thử tính toàn vẹn của Structured JSON Lines Logger."""

    def test_01_schema_conformance(self):
        """Xác thực cấu trúc JSON Lines tuân thủ 100% schema mục 4 của plan."""
        entry = create_sample_log_entry(
            "REQ-001", "Shadow", "v1_block_v2_allow",
            "Block", 0.85, "Allow", 0.01
        )

        # Kiểm tra các trường cấp 1
        self.assertEqual(set(entry.keys()), REQUIRED_ROOT_KEYS)
        self.assertIn(entry["execution_mode"], VALID_EXECUTION_MODES)

        # Kiểm tra primary_model
        self.assertEqual(set(entry["primary_model"].keys()), REQUIRED_MODEL_SUMMARY_KEYS)
        self.assertIn(entry["primary_model"]["decision"], VALID_DECISIONS)

        # Kiểm tra shadow_model
        self.assertEqual(set(entry["shadow_model"].keys()), REQUIRED_MODEL_SUMMARY_KEYS)
        self.assertIn(entry["shadow_model"]["decision"], VALID_DECISIONS)

        # Kiểm tra features_v2 đủ 11 chiều đặc trưng 3 tầng
        self.assertEqual(set(entry["features_v2"].keys()), REQUIRED_FEATURES_V2_KEYS)
        self.assertEqual(len(entry["features_v2"]), 11)

        # Kiểm tra latency_ms
        self.assertEqual(set(entry["latency_ms"].keys()), REQUIRED_LATENCY_KEYS)

        # Kiểm tra logic phân loại cờ
        self.assertTrue(entry["is_false_positive_candidate"])
        self.assertFalse(entry["is_friction_reduction_candidate"])
        self.assertFalse(entry["is_stealth_catch_candidate"])
        self.assertFalse(entry["decision_matches"])
        self.assertAlmostEqual(entry["risk_score_delta"], 0.84, places=4)

    def test_02_jsonl_serialization_and_line_parsing(self):
        """Xác thực ghi và đọc file .jsonl (mỗi dòng 1 JSON độc lập, không BOM)."""
        entries = [
            create_sample_log_entry("REQ-A", "Shadow", "v1_allow_v2_allow", "Allow", 0.05, "Allow", 0.02),
            create_sample_log_entry("REQ-B", "Shadow", "v1_block_v2_allow", "Block", 0.88, "Allow", 0.03),
            create_sample_log_entry("REQ-C", "Shadow", "v1_challenge_v2_allow", "Challenge", 0.50, "Allow", 0.10),
            create_sample_log_entry("REQ-D", "Shadow", "v1_allow_v2_block", "Allow", 0.15, "Block", 0.95),
            create_sample_log_entry("REQ-E", "Shadow", "v1_challenge_v2_block", "Challenge", 0.65, "Block", 0.90),
            create_sample_log_entry("REQ-F", "Shadow", "v1_block_v2_block", "Block", 0.90, "Block", 0.98),
        ]

        with tempfile.NamedTemporaryFile("w+", delete=False, suffix=".jsonl", encoding="utf-8") as f:
            temp_path = f.name
            for e in entries:
                f.write(json.dumps(e) + "\n")

        try:
            parsed_entries = []
            with open(temp_path, "r", encoding="utf-8") as f:
                for line_num, line in enumerate(f, 1):
                    line_clean = line.strip()
                    self.assertTrue(line_clean, f"Dòng {line_num} không được rỗng")
                    parsed = json.loads(line_clean)
                    parsed_entries.append(parsed)

            self.assertEqual(len(parsed_entries), 6)
            for i, p in enumerate(parsed_entries):
                self.assertEqual(p["request_id"], entries[i]["request_id"])
                self.assertEqual(p["shadow_cohort"], entries[i]["shadow_cohort"])
        finally:
            if os.path.exists(temp_path):
                os.remove(temp_path)

    def test_03_statistical_metrics_aggregation_precision(self):
        """Xác thực công thức tính toán chỉ số FPR, Friction, Stealth và Agreement."""
        entries = [
            create_sample_log_entry("R1", "Shadow", "v1_allow_v2_allow", "Allow", 0.02, "Allow", 0.02),
            create_sample_log_entry("R2", "Shadow", "v1_allow_v2_allow", "Allow", 0.03, "Allow", 0.04),
            create_sample_log_entry("R3", "Shadow", "v1_block_v2_block", "Block", 0.95, "Block", 0.96),
            create_sample_log_entry("R4", "Shadow", "v1_block_v2_allow", "Block", 0.85, "Allow", 0.05), # Cứu FP (Brave)
            create_sample_log_entry("R5", "Shadow", "v1_challenge_v2_allow", "Challenge", 0.45, "Allow", 0.10), # Giảm ma sát
            create_sample_log_entry("R6", "Shadow", "v1_allow_v2_block", "Allow", 0.10, "Block", 0.85), # Bắt stealth
            create_sample_log_entry("R7", "Shadow", "v1_challenge_v2_block", "Challenge", 0.60, "Block", 0.90), # Bắt bot giải captcha
            create_sample_log_entry("R8", "Shadow", "v1_block_v2_allow", "Block", 0.82, "Allow", 0.02), # Cứu FP (Brave)
            create_sample_log_entry("R9", "Shadow", "v1_allow_v2_allow", "Allow", 0.01, "Allow", 0.01),
            create_sample_log_entry("R10", "Shadow", "v1_block_v2_block", "Block", 0.90, "Block", 0.92),
        ]

        stats = aggregate_shadow_logs(entries)

        self.assertEqual(stats["total_evaluations"], 10)
        self.assertEqual(stats["agreement_count"], 5)  # R1, R2, R3, R9, R10
        self.assertEqual(stats["agreement_rate"], 0.5000)

        # False Positive Rescue: 2 cases (R4, R8) -> 20.0%
        self.assertEqual(stats["rescued_false_positives"], 2)
        self.assertEqual(stats["rescued_false_positive_rate"], 0.2000)

        # Friction Reduction: 1 case (R5) -> 10.0%
        self.assertEqual(stats["friction_reduction"], 1)
        self.assertEqual(stats["friction_reduction_rate"], 0.1000)

        # Stealth Catch: 1 case (R6) -> 10.0%
        self.assertEqual(stats["caught_stealth_bots"], 1)
        self.assertEqual(stats["caught_stealth_bot_rate"], 0.1000)

        # Captcha Bot Elimination: 1 case (R7) -> 10.0%
        self.assertEqual(stats["captcha_bot_elimination"], 1)
        self.assertEqual(stats["captcha_bot_elimination_rate"], 0.1000)

        # Delta trung bình
        self.assertGreater(stats["average_risk_score_delta"], 0.0)

    def test_04_candidate_flag_exclusivity(self):
        """Kiểm tra tính loại trừ lẫn nhau giữa các nhóm candidate."""
        # 1. FP Rescue candidate
        fp_entry = create_sample_log_entry("T1", "Shadow", "v1_block_v2_allow", "Block", 0.85, "Allow", 0.02)
        self.assertTrue(fp_entry["is_false_positive_candidate"])
        self.assertFalse(fp_entry["is_friction_reduction_candidate"])
        self.assertFalse(fp_entry["is_stealth_catch_candidate"])

        # 2. Friction Reduction candidate
        fric_entry = create_sample_log_entry("T2", "Shadow", "v1_challenge_v2_allow", "Challenge", 0.40, "Allow", 0.05)
        self.assertFalse(fric_entry["is_false_positive_candidate"])
        self.assertTrue(fric_entry["is_friction_reduction_candidate"])
        self.assertFalse(fric_entry["is_stealth_catch_candidate"])

        # 3. Stealth catch candidate
        stealth_entry = create_sample_log_entry("T3", "Shadow", "v1_allow_v2_block", "Allow", 0.10, "Block", 0.90)
        self.assertFalse(stealth_entry["is_false_positive_candidate"])
        self.assertFalse(stealth_entry["is_friction_reduction_candidate"])
        self.assertTrue(stealth_entry["is_stealth_catch_candidate"])


if __name__ == "__main__":
    print("=" * 60)
    print(" US-13.3 & ML-13.3.4: SHADOW MODE JSONL LOG VALIDATOR ")
    print("=" * 60)
    suite = unittest.TestLoader().loadTestsFromTestCase(TestShadowModeLogger)
    runner = unittest.TextTestRunner(verbosity=2)
    result = runner.run(suite)
    sys.exit(0 if result.wasSuccessful() else 1)
