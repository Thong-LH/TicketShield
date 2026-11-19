"""
test_onnx_v2_export.py
Unit tests cho Subtask 13.3.3.4: In-Process ONNX Export & Latency Benchmark.
Kiểm thử tính đúng đắn và chuẩn hóa công nghiệp của:
  1. File ONNX v2.0 tồn tại, dung lượng hợp lệ (> 5KB, < 500KB).
  2. Input / Output ONNX contract: float_input [None, 11] -> probabilities [None, 2].
  3. Tính toàn vẹn của mã băm SHA-256 và metadata JSON chống silent staleness.
  4. Cơ chế đồng bộ kép (Dual Sync): AIEngine/models <-> Gateway/models khớp bit-for-bit.
  5. Triệt tiêu lệch pha huấn luyện - triển khai (Training-Serving Skew max |Delta| < 1e-5).
  6. SLA độ trễ suy diễn CPU (Mean < 1.0ms, P99 < 2.5ms).
"""

import os
import sys
import json
import unittest
import pandas as pd
import joblib  # type: ignore
import onnxruntime as ort  # type: ignore

CUR_DIR = os.path.dirname(os.path.abspath(__file__))
AI_ENGINE_DIR = os.path.abspath(os.path.join(CUR_DIR, ".."))
WORKSPACE_ROOT = os.path.abspath(os.path.join(CUR_DIR, "../../../.."))

for p in [AI_ENGINE_DIR, WORKSPACE_ROOT]:
    if p not in sys.path:
        sys.path.insert(0, p)

try:
    from app.export_onnx_v2 import (
        DEFAULT_ONNX_AI_PATH,
        DEFAULT_META_AI_PATH,
        DEFAULT_ONNX_GATEWAY_PATH,
        DEFAULT_META_GATEWAY_PATH,
        compute_sha256,
        verify_cross_inference,
        benchmark_onnx_latency,
    )
    from app.train_v2 import (
        DEFAULT_MODEL_OUTPUT_PATH,
        load_and_preprocess_dataset,
    )
    from app.feature_pipeline import FEATURE_COLUMNS_V2
except ImportError:
    from TicketShield.src.AIEngine.app.export_onnx_v2 import (
        DEFAULT_ONNX_AI_PATH,
        DEFAULT_META_AI_PATH,
        DEFAULT_ONNX_GATEWAY_PATH,
        DEFAULT_META_GATEWAY_PATH,
        compute_sha256,
        verify_cross_inference,
        benchmark_onnx_latency,
    )
    from TicketShield.src.AIEngine.app.train_v2 import (
        DEFAULT_MODEL_OUTPUT_PATH,
        load_and_preprocess_dataset,
    )
    from TicketShield.src.AIEngine.app.feature_pipeline import FEATURE_COLUMNS_V2


class TestOnnxV2Export(unittest.TestCase):
    """Bộ kiểm thử xuất bản ONNX v2.0 và hiệu năng Gateway."""

    @classmethod
    def setUpClass(cls):
        """Khởi tạo session và nạp dataset."""
        cls.assertTrue(os.path.exists(DEFAULT_ONNX_AI_PATH), "File anti_bot_model_v2.onnx chưa được tạo!")
        cls.session = ort.InferenceSession(DEFAULT_ONNX_AI_PATH)
        cls.clf = joblib.load(DEFAULT_MODEL_OUTPUT_PATH)
        cls.X, cls.y, _ = load_and_preprocess_dataset()

    def test_01_onnx_files_dual_sync_and_sha256(self):
        """Kiểm tra file ONNX tồn tại ở cả 2 thư mục và khớp mã băm SHA-256 bit-for-bit."""
        self.assertTrue(os.path.exists(DEFAULT_ONNX_AI_PATH))
        self.assertTrue(os.path.exists(DEFAULT_ONNX_GATEWAY_PATH))

        hash_ai = compute_sha256(DEFAULT_ONNX_AI_PATH)
        hash_gw = compute_sha256(DEFAULT_ONNX_GATEWAY_PATH)
        self.assertEqual(hash_ai, hash_gw, "Mã băm SHA-256 giữa AIEngine và Gateway không khớp nhau!")
        self.assertGreater(os.path.getsize(DEFAULT_ONNX_AI_PATH), 5000)

    def test_02_onnx_contract_input_output_nodes(self):
        """Kiểm tra khế ước ONNX: input [None, 11] và output probabilities [None, 2]."""
        inputs = self.session.get_inputs()
        self.assertEqual(len(inputs), 1)
        self.assertEqual(inputs[0].name, "float_input")
        self.assertEqual(inputs[0].shape[1], 11)

        outputs = self.session.get_outputs()
        out_names = [o.name for o in outputs]
        self.assertIn("probabilities", out_names)
        self.assertIn("label", out_names)

        # Kiểm tra thử nghiệm 1 vector dự đoán
        dummy_input = pd.DataFrame([[0.0] * 11], dtype="float32").values
        res = self.session.run(None, {"float_input": dummy_input})
        probs = res[1]
        self.assertEqual(probs.shape, (1, 2))
        self.assertAlmostEqual(float(probs[0, 0] + probs[0, 1]), 1.0, places=5)

    def test_03_metadata_json_schema_and_integrity(self):
        """Kiểm tra metadata JSON chứa đầy đủ trường xác thực và thông tin ngưỡng."""
        self.assertTrue(os.path.exists(DEFAULT_META_AI_PATH))
        self.assertTrue(os.path.exists(DEFAULT_META_GATEWAY_PATH))

        with open(DEFAULT_META_AI_PATH, "r", encoding="utf-8") as f:
            meta = json.load(f)

        self.assertEqual(meta["model_version"], "2.0.0")
        self.assertEqual(meta["contract"]["feature_count"], 11)
        self.assertEqual(meta["contract"]["feature_names"], FEATURE_COLUMNS_V2)
        self.assertEqual(meta["contract"]["input_node_name"], "float_input")
        self.assertEqual(meta["contract"]["probability_output_name"], "probabilities")
        self.assertEqual(meta["operational_thresholds"]["allow_threshold"], 0.35)
        self.assertEqual(meta["operational_thresholds"]["block_threshold"], 0.75)

        # SHA-256 trong metadata phải khớp với hash file ONNX thực tế
        actual_hash = compute_sha256(DEFAULT_ONNX_AI_PATH)
        self.assertEqual(meta["sha256_hash"], actual_hash)

    def test_04_cross_inference_skew_zero(self):
        """Kiểm định sai số tuyệt đối giữa Scikit-Learn và OnnxRuntime phải < 1e-5."""
        passed, max_diff = verify_cross_inference(self.clf, DEFAULT_ONNX_AI_PATH, self.X)
        self.assertTrue(passed, f"Phát hiện Training-Serving Skew: max_diff = {max_diff}")
        self.assertLess(max_diff, 1e-5)

    def test_05_inference_latency_sla(self):
        """Đo lường độ trễ suy diễn CPU 1,000 lần: Mean < 1.0ms, P99 < 2.5ms."""
        bench = benchmark_onnx_latency(DEFAULT_ONNX_AI_PATH, num_runs=1000, warmup_runs=20)
        self.assertLess(bench["mean_latency_ms"], 1.0)
        self.assertLess(bench["p99_latency_ms"], 2.5)
        self.assertTrue(bench["meets_sla_1ms"])
        self.assertTrue(bench["meets_sla_p99_2_5ms"])


if __name__ == "__main__":
    unittest.main()
