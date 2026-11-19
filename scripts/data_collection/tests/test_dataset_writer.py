"""
test_dataset_writer.py
Unit Test Suite kiểm định tính toàn vẹn của Data Contract Schema & Batch Dataset Writer (Subtask 13.3.1.3).
Kiểm thử tạo, ghi lô (batch write) và đọc lại 1,000 bản ghi dữ liệu đối kháng.
"""

import os
import sys
import tempfile
import uuid
import pandas as pd

# Đảm bảo import được module từ root repo
REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "../../../"))
if REPO_ROOT not in sys.path:
    sys.path.insert(0, REPO_ROOT)

from scripts.data_collection.models.schema import DatasetRecord, DATASET_COLUMNS
from scripts.data_collection.utils.dataset_writer import DatasetWriter

def run_tests():
    print("=" * 60)
    print(" KIỂM THỬ SUBTASK 13.3.1.3: SCHEMA & DATASET WRITER")
    print("=" * 60)

    # 1. Test from_gateway_debug mapping
    print("[1/4] Kiểm tra mapping từ Gateway debugVector:")
    mock_debug_vector = {
        "time_to_checkout": 0.42,
        "action_interval_std_dev": 11.5,
        "burst_rate_10s": 14.0,
        "concurrent_hold": 3.0,
        "is_webdriver": 1.0,
        "device_account_overlap": 0.85,
        "virtual_gpu_detected": 1.0,
        "prototype_tampered": 0.0,
        "canvas_tampered": 0.0,
        "canvas_hash": "c1a2b3d4",
        "webgl_renderer": "ANGLE (SwiftShader)",
        "webgl_vendor": "Google Inc.",
        "client_user_agent": "Mozilla/5.0 Playwright",
        "header_user_agent": "Mozilla/5.0 Playwright",
        "ua_match_level": "ExactMatch",
        "ja4_fingerprint": "t13d1516h2_8daaf6152771_02711d0e519e",
        "http2_fingerprint": "m,a,s,p",
        "risk_score": 0.985,
        "decision": "Block",
        "status_code": 429,
        "reason": "Perimeter Defense Blocked",
    }

    record = DatasetRecord.from_gateway_debug(
        tool_name="playwright",
        attack_mode="headless_standard",
        debug_vector=mock_debug_vector,
        http_status=429,
        label=1
    )

    d = record.to_dict()
    assert d["tool_name"] == "playwright", "tool_name phải là playwright"
    assert d["is_webdriver"] == 1.0, "is_webdriver phải là 1.0"
    assert d["virtual_gpu_detected"] == 1.0, "virtual_gpu_detected phải là 1.0"
    assert d["gateway_http_status"] == 429, "status_code phải là 429"
    assert d["gateway_decision"] == "Block", "decision phải là Block"
    assert d["label"] == 1, "label phải là 1"
    print("   -> [PASS] Mapping hoàn hảo từ Gateway response!")

    # 2. Tạo 1,000 mẫu đại diện cho 4 công cụ bot
    print("\n[2/4] Khởi tạo 1,000 mẫu bot đối kháng (250 mẫu/tool):")
    records = []
    tools = [
        ("playwright", "headless_standard", {"is_webdriver": 1.0, "virtual_gpu_detected": 1.0}),
        ("puppeteer_stealth", "evasion_stealth", {"prototype_tampered": 1.0, "canvas_tampered": 1.0}),
        ("undetected_chromedriver", "stealth_delay", {"is_webdriver": 0.0, "time_to_checkout": 2.5}),
        ("curl_cffi", "burst_assault", {"burst_rate_10s": 22.0, "client_user_agent": "", "is_webdriver": -1.0}),
    ]

    for tool_name, attack_mode, custom_fields in tools:
        for i in range(250):
            base_debug = mock_debug_vector.copy()
            base_debug.update(custom_fields)
            r = DatasetRecord.from_gateway_debug(
                tool_name=tool_name,
                attack_mode=attack_mode,
                debug_vector=base_debug,
                http_status=429 if "burst" in attack_mode or "headless" in attack_mode else 403,
                label=1,
                sample_id=str(uuid.uuid4())
            )
            records.append(r)

    assert len(records) == 1000, "Phải có đúng 1000 records"
    print("   -> [PASS] Đã tạo thành công 1,000 mẫu đại diện cho 4 công cụ!")

    # 3. Ghi lô qua DatasetWriter vào file tạm
    print("\n[3/4] Ghi 1,000 mẫu theo batch vào file CSV tạm thời:")
    with tempfile.TemporaryDirectory() as tmpdir:
        csv_path = os.path.join(tmpdir, "test_dataset_1000.csv")
        parquet_path = os.path.join(tmpdir, "test_dataset_1000.parquet")

        with DatasetWriter(output_csv_path=csv_path, output_parquet_path=parquet_path, batch_size=50) as writer:
            for r in records:
                writer.add_record(r)

        assert os.path.exists(csv_path), "File CSV phải tồn tại sau khi close"
        file_size_kb = os.path.getsize(csv_path) / 1024
        print(f"   -> [PASS] Ghi thành công CSV: {file_size_kb:.2f} KB")

        # 4. Đọc lại và kiểm tra tính toàn vẹn (Integrity Check)
        print("\n[4/4] Kiểm tra tính toàn vẹn dữ liệu bằng Pandas:")
        df = pd.read_csv(csv_path)
        assert len(df) == 1000, f"Số dòng phải là 1000, thực tế: {len(df)}"
        assert list(df.columns) == DATASET_COLUMNS, "Các cột phải khớp 100% DATASET_COLUMNS"

        # Kiểm tra phân bổ 4 tools
        tool_counts = df["tool_name"].value_counts().to_dict()
        print("   Phân bổ công cụ:", tool_counts)
        for t, _, _ in tools:
            assert tool_counts[t] == 250, f"Tool {t} phải có đúng 250 mẫu"

        # Kiểm tra nhãn label
        assert (df["label"] == 1).all(), "100% nhãn phải là 1 (Bot)"

        print("   -> [PASS] Dữ liệu hoàn hảo! 1000/1000 dòng chuẩn format.")

    print("\n" + "=" * 60)
    print(" ✅ TẤT CẢ TEST CASES SUBTASK 13.3.1.3 ĐÃ PASS 100%!")
    print("=" * 60)

if __name__ == "__main__":
    run_tests()
