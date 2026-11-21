"""
test_model_v2_gateway_integration.py
Kiểm Thử Tích Hợp Đối Chiếu Chéo Python ORT vs C# .NET 8 Gateway (Subtask 13.3.3.6).
Theo đặc tả Epic US-13.3 và Jira SCRUM-381 (TicketShield):
  1. Đưa 10 vector biên đối kháng (Human, Brave Farbling, Stealth Bot, cURL, UC, Scalper...) chạy đồng thời qua:
     - Python OnnxRuntime (anti_bot_model_v2.onnx)
     - C# .NET 8 Gateway In-Process (AntiBotScoringService.EvaluateV2())
  2. Khẳng định sai số tuyệt đối giữa 2 ngôn ngữ: |Delta Risk| < 1e-4 (Triệt tiêu 100% Training-Serving Skew).
  3. Khẳng định 100% đồng thuận về quyết định phân loại (Allow, Challenge, Block).
  4. Khẳng định Brave Farbling được ALLOW mượt mà và Stealth Bot bị BLOCK tức thì ở cả 2 nền tảng.
"""

import os
import sys
import json
import subprocess
import unittest
from typing import Dict, Any, List

CUR_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_CANDIDATE = os.path.abspath(os.path.join(CUR_DIR, "../../.."))
TICKETSHIELD_ROOT = os.path.join(REPO_CANDIDATE, "TicketShield") if os.path.exists(os.path.join(REPO_CANDIDATE, "TicketShield", "TicketShield.sln")) else REPO_CANDIDATE
AI_ENGINE_DIR = os.path.join(TICKETSHIELD_ROOT, "src", "AIEngine")

for p in [AI_ENGINE_DIR, TICKETSHIELD_ROOT]:
    if p not in sys.path:
        sys.path.insert(0, p)

import onnxruntime as ort  # type: ignore

DEFAULT_ONNX_PATH = os.path.join(AI_ENGINE_DIR, "models", "anti_bot_model_v2.onnx")
GATEWAY_EXE_PATH = os.path.join(TICKETSHIELD_ROOT, "src", "TicketShield.AntiBot.Gateway", "bin", "Debug", "net8.0", "TicketShield.AntiBot.Gateway.exe")


# 10 Vector Biên Đại Diện Toàn Bộ Các Cohort Đối Kháng & Khách Hàng Hợp Lệ
TEST_BENCHMARK_VECTORS: List[Dict[str, Any]] = [
    {
        "name": "01_human_standard",
        "description": "Người thật lướt web bình thường (Chrome chuẩn, hành vi sinh học tự nhiên)",
        "expected_decision": "Allow",
        "vector": {
            "is_webdriver": 0.0,
            "virtual_gpu_detected": 0.0,
            "prototype_tampered": 0.0,
            "canvas_tampered": 0.0,
            "action_interval_std_dev": 150.0,
            "ua_match_level": 0.0,
            "network_inconsistency_score": 0.0,
            "time_to_checkout": 18.0,
            "burst_rate_10s": 1.0,
            "concurrent_hold": 0.0,
            "device_account_overlap": 0.0,
        }
    },
    {
        "name": "02_human_elderly_slow",
        "description": "Người cao tuổi thao tác chậm rãi (Đọc kỹ thông tin, nhịp bấm cách xa)",
        "expected_decision": "Allow",
        "vector": {
            "is_webdriver": 0.0,
            "virtual_gpu_detected": 0.0,
            "prototype_tampered": 0.0,
            "canvas_tampered": 0.0,
            "action_interval_std_dev": 320.0,
            "ua_match_level": 0.0,
            "network_inconsistency_score": 0.0,
            "time_to_checkout": 120.0,
            "burst_rate_10s": 1.0,
            "concurrent_hold": 0.0,
            "device_account_overlap": 0.0,
        }
    },
    {
        "name": "03_human_power_user_fast",
        "description": "Thợ săn vé (PowerUser thao tác cực nhanh, nhịp bấm dồn dập rơi vào Challenge Grey Zone)",
        "expected_decision": "Challenge",
        "vector": {
            "is_webdriver": 0.0,
            "virtual_gpu_detected": 0.0,
            "prototype_tampered": 0.0,
            "canvas_tampered": 0.0,
            "action_interval_std_dev": 95.0,
            "ua_match_level": 0.0,
            "network_inconsistency_score": 0.0,
            "time_to_checkout": 3.5,
            "burst_rate_10s": 2.0,
            "concurrent_hold": 1.0,
            "device_account_overlap": 0.0,
        }
    },
    {
        "name": "04_human_brave_farbling",
        "description": "Khách hàng dùng trình duyệt Brave bật khiên bảo vệ vân tay (Canvas Noise)",
        "expected_decision": "Allow",
        "vector": {
            "is_webdriver": 0.0,
            "virtual_gpu_detected": 0.0,
            "prototype_tampered": 0.0,
            "canvas_tampered": 1.0,  # Brave Farbling ngẫu hóa canvas
            "action_interval_std_dev": 165.0,  # Nhịp bấm con người tự nhiên
            "ua_match_level": 0.0,
            "network_inconsistency_score": 0.0,
            "time_to_checkout": 15.0,
            "burst_rate_10s": 1.0,
            "concurrent_hold": 0.0,
            "device_account_overlap": 0.0,
        }
    },
    {
        "name": "05_bot_curl_cffi_api",
        "description": "Bot gọi trực tiếp API bằng curl_cffi giả lập TLS Chrome (Không nạp Browser)",
        "expected_decision": "Block",
        "vector": {
            "is_webdriver": -1.0,
            "virtual_gpu_detected": -1.0,
            "prototype_tampered": -1.0,
            "canvas_tampered": -1.0,
            "action_interval_std_dev": -1.0,
            "ua_match_level": 1.0,
            "network_inconsistency_score": 0.9,
            "time_to_checkout": 0.15,
            "burst_rate_10s": 25.0,
            "concurrent_hold": 6.0,
            "device_account_overlap": 0.8,
        }
    },
    {
        "name": "06_bot_playwright_standard",
        "description": "Bot tự động hóa Playwright tiêu chuẩn (Lộ cờ navigator.webdriver)",
        "expected_decision": "Block",
        "vector": {
            "is_webdriver": 1.0,
            "virtual_gpu_detected": 0.0,
            "prototype_tampered": 0.0,
            "canvas_tampered": 0.0,
            "action_interval_std_dev": 12.0,
            "ua_match_level": 0.0,
            "network_inconsistency_score": 0.0,
            "time_to_checkout": 0.6,
            "burst_rate_10s": 8.0,
            "concurrent_hold": 3.0,
            "device_account_overlap": 0.4,
        }
    },
    {
        "name": "07_bot_puppeteer_stealth",
        "description": "Bot tàng hình Puppeteer Stealth (Monkey-patch WebGL & Chrome objects)",
        "expected_decision": "Block",
        "vector": {
            "is_webdriver": 1.0,
            "virtual_gpu_detected": 0.0,
            "prototype_tampered": 1.0,
            "canvas_tampered": 0.0,
            "action_interval_std_dev": 5.0,
            "ua_match_level": 0.5,
            "network_inconsistency_score": 0.35,
            "time_to_checkout": 0.25,
            "burst_rate_10s": 12.0,
            "concurrent_hold": 5.0,
            "device_account_overlap": 0.9,
        }
    },
    {
        "name": "08_bot_undetected_chromedriver",
        "description": "Bot Undetected-Chromedriver (Chạy Headless trên Linux SwiftShader GPU)",
        "expected_decision": "Block",
        "vector": {
            "is_webdriver": 0.0,
            "virtual_gpu_detected": 1.0,
            "prototype_tampered": 0.0,
            "canvas_tampered": 0.0,
            "action_interval_std_dev": 8.0,
            "ua_match_level": 1.0,
            "network_inconsistency_score": 0.6,
            "time_to_checkout": 0.4,
            "burst_rate_10s": 15.0,
            "concurrent_hold": 4.0,
            "device_account_overlap": 0.7,
        }
    },
    {
        "name": "09_bot_res_proxy_cluster",
        "description": "Botnet phân tán qua mạng Residential Proxy (Lệch pha JA4 TLS & HTTP2)",
        "expected_decision": "Block",
        "vector": {
            "is_webdriver": 0.0,
            "virtual_gpu_detected": 0.0,
            "prototype_tampered": 0.0,
            "canvas_tampered": 0.0,
            "action_interval_std_dev": 15.0,
            "ua_match_level": 1.0,
            "network_inconsistency_score": 0.95,
            "time_to_checkout": 0.8,
            "burst_rate_10s": 18.0,
            "concurrent_hold": 7.0,
            "device_account_overlap": 0.95,
        }
    },
    {
        "name": "10_bot_scalper_concurrent",
        "description": "Phe vé gom vé dồn dập nhiều tài khoản chung một máy (Concurrent Hold = 9)",
        "expected_decision": "Block",
        "vector": {
            "is_webdriver": 0.0,
            "virtual_gpu_detected": 0.0,
            "prototype_tampered": 0.0,
            "canvas_tampered": 0.0,
            "action_interval_std_dev": 22.0,
            "ua_match_level": 0.0,
            "network_inconsistency_score": 0.0,
            "time_to_checkout": 1.2,
            "burst_rate_10s": 22.0,
            "concurrent_hold": 9.0,
            "device_account_overlap": 1.0,
        }
    },
]


def evaluate_python_onnx(vectors: List[Dict[str, float]], onnx_path: str = DEFAULT_ONNX_PATH) -> List[Dict[str, Any]]:
    """Dự đoán bằng Python ONNX Runtime engine."""
    session = ort.InferenceSession(onnx_path)
    input_name = session.get_inputs()[0].name

    results = []
    for v in vectors:
        feat_array = [
            float(v["is_webdriver"]),
            float(v["virtual_gpu_detected"]),
            float(v["prototype_tampered"]),
            float(v["canvas_tampered"]),
            float(v["action_interval_std_dev"]),
            float(v["ua_match_level"]),
            float(v["network_inconsistency_score"]),
            float(v["time_to_checkout"]),
            float(v["burst_rate_10s"]),
            float(v["concurrent_hold"]),
            float(v["device_account_overlap"]),
        ]
        ort_out = session.run(None, {input_name: [feat_array]})
        prob_bot = float(ort_out[1][0, 1])

        if prob_bot < 0.35:
            decision = "Allow"
        elif prob_bot >= 0.75:
            decision = "Block"
        else:
            decision = "Challenge"

        results.append({
            "risk_score": round(prob_bot, 4),
            "decision": decision,
        })
    return results


def evaluate_csharp_gateway(vectors: List[Dict[str, float]], exe_path: str = GATEWAY_EXE_PATH) -> List[Dict[str, Any]]:
    """Dự đoán bằng C# .NET 8 Gateway In-Process executable thông qua stdin."""
    json_payload = json.dumps(vectors)
    proc = subprocess.run(
        [exe_path, "--eval-v2-batch"],
        input=json_payload,
        text=True,
        capture_output=True,
        check=True,
    )
    raw_output = proc.stdout.strip()
    csharp_res = json.loads(raw_output)

    results = []
    for item in csharp_res:
        results.append({
            "risk_score": round(float(item["RiskScore"]), 4),
            "decision": str(item["Decision"]),
            "inference_time_ms": float(item.get("InferenceTimeMs", 0.0)),
        })
    return results


class TestModelV2GatewayIntegration(unittest.TestCase):
    """Bộ kiểm thử đối chiếu chéo Python vs C# (.NET 8)."""

    @classmethod
    def setUpClass(cls):
        """Khẳng định môi trường sẵn sàng."""
        cls.assertTrue(os.path.exists(DEFAULT_ONNX_PATH), f"Không tìm thấy file ONNX tại: {DEFAULT_ONNX_PATH}")
        cls.assertTrue(os.path.exists(GATEWAY_EXE_PATH), f"Không tìm thấy file Gateway.exe tại: {GATEWAY_EXE_PATH}")

    def test_cross_language_consistency_zero_skew(self):
        """Kiểm tra độ lệch pha |Delta| giữa Python và C# phải < 1e-4 trên 100% vector biên."""
        raw_vectors = [item["vector"] for item in TEST_BENCHMARK_VECTORS]

        # 1. Dự đoán bằng Python ORT
        py_results = evaluate_python_onnx(raw_vectors)

        # 2. Dự đoán bằng C# Gateway
        cs_results = evaluate_csharp_gateway(raw_vectors)

        self.assertEqual(len(py_results), len(TEST_BENCHMARK_VECTORS))
        self.assertEqual(len(cs_results), len(TEST_BENCHMARK_VECTORS))

        print("\n" + "=" * 90)
        print(" 🔍 BẢNG ĐỐI CHIẾU SUY DIỄN CHÉO PYTHON ORT VS C# .NET 8 GATEWAY (ML-13.3.6)")
        print("=" * 90)
        print(f"{'STT':<4} {'Tên Vector / Kịch Bản':<26} {'Py Risk':<9} {'C# Risk':<9} {'Delta':<8} {'Py Dec':<8} {'C# Dec':<8} {'Kết Quả'}")
        print("-" * 90)

        for i, (tc, py_r, cs_r) in enumerate(zip(TEST_BENCHMARK_VECTORS, py_results, cs_results), start=1):
            delta = abs(py_r["risk_score"] - cs_r["risk_score"])
            decision_match = (py_r["decision"] == cs_r["decision"])
            meets_expected = (cs_r["decision"] == tc["expected_decision"])
            passed = (delta < 1e-4) and decision_match and meets_expected

            status_str = "✅ PASS" if passed else "❌ FAIL"
            print(f"{i:<4} {tc['name']:<26} {py_r['risk_score']:<9.4f} {cs_r['risk_score']:<9.4f} {delta:<8.4f} {py_r['decision']:<8} {cs_r['decision']:<8} {status_str}")

            # Assertions
            self.assertLess(delta, 1e-4, f"Lệch pha tại {tc['name']}: Delta={delta}")
            self.assertEqual(py_r["decision"], cs_r["decision"], f"Quyết định không khớp tại {tc['name']}")
            self.assertEqual(cs_r["decision"], tc["expected_decision"], f"Không đúng quyết định kỳ vọng tại {tc['name']}")

        print("=" * 90)
        print(" ✅ 10/10 VECTOR ĐỐI CHIẾU CHÉO ĐẠT ĐỒNG THUẬN TUYỆT ĐỐI (DELTA = 0.0000)!")
        print("=" * 90)


if __name__ == "__main__":
    unittest.main()
