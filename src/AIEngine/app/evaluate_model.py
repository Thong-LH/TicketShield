"""
evaluate_model.py
Script kiểm tra tính toàn vẹn của mô hình ONNX và độ chuẩn xác của contract dữ liệu.
"""

import os
import pytest
import numpy as np
import onnxruntime as ort

MODEL_PATH = "models/anti_bot_model.onnx"

def test_model_exists():
    assert os.path.exists(MODEL_PATH), f"Không tìm thấy file model ONNX tại {MODEL_PATH}"

def test_onnx_contract():
    if not os.path.exists(MODEL_PATH):
        pytest.skip("Model chưa được train, bỏ qua test contract")
        
    session = ort.InferenceSession(MODEL_PATH)
    inputs = session.get_inputs()
    outputs = session.get_outputs()

    # Input kiểm tra: Tên là 'float_input', shape chiều thứ hai là 6
    assert inputs[0].name == "float_input"
    assert inputs[0].shape[1] == 6

    # Output kiểm tra: Có output probabilities với type tensor(float)
    prob_output = [o for o in outputs if o.name == "probabilities"][0]
    assert prob_output is not None
    assert prob_output.type == "tensor(float)"

def test_inference_boundary():
    if not os.path.exists(MODEL_PATH):
        pytest.skip("Model chưa được train, bỏ qua test suy luận")
        
    session = ort.InferenceSession(MODEL_PATH)
    # Test sample: High bot signature
    high_bot = np.array([[5.0, 1.0, 0.05, 0.5, 20.0, 0.9]], dtype=np.float32)
    res = session.run(["probabilities"], {"float_input": high_bot})
    bot_prob = res[0][0][1]
    assert bot_prob > 0.70, f"Bot rõ ràng nhưng xác suất chỉ là {bot_prob}"

if __name__ == "__main__":
    pytest.main(["-v", __file__])
