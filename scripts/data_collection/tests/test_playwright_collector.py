"""
test_playwright_collector.py
Unit Test Suite kiểm thử Playwright Bot Dataset Collector (Subtask 13.3.1.4).
"""

import os
import sys
import tempfile
import pandas as pd

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "../../../"))
if REPO_ROOT not in sys.path:
    sys.path.insert(0, REPO_ROOT)

from scripts.data_collection.collectors.playwright_collector import PlaywrightCollector
from scripts.data_collection.utils.dataset_writer import DatasetWriter
from scripts.data_collection.models.schema import DATASET_COLUMNS

def run_tests():
    print("=" * 60)
    print(" KIỂM THỬ SUBTASK 13.3.1.4: PLAYWRIGHT BOT COLLECTOR")
    print("=" * 60)

    # 1. Kiểm tra cấu trúc telemetry payload sinh ra
    print("[1/3] Kiểm tra dấu vết đối kháng trong Telemetry của Playwright:")
    collector = PlaywrightCollector()
    telemetry = collector.generate_telemetry_payload(nonce="test_nonce_123", mode="headless_standard")

    assert telemetry["is_webdriver"] == 1, "Playwright phải để lộ is_webdriver = 1"
    assert telemetry["virtual_gpu_detected"] == 1.0, "Headless Playwright phải kích hoạt virtual_gpu_detected = 1.0"
    assert telemetry["prototype_tampered"] == 0, "Playwright chuẩn không hook prototype"
    assert 1.0 <= telemetry["action_interval_std_dev"] <= 15.0, "Nhịp bấm máy móc phải < 15ms"
    assert "HeadlessChrome" in telemetry["client_user_agent"] or "Playwright" in telemetry["client_user_agent"]
    print("   -> [PASS] Telemetry phản ánh chính xác 100% hành vi Playwright Bot!")

    # 2. Thu thập 100 mẫu bot Playwright vào file CSV tạm thời
    print("\n[2/3] Chạy thu thập 100 mẫu bot Playwright:")
    with tempfile.TemporaryDirectory() as tmpdir:
        csv_path = os.path.join(tmpdir, "playwright_100.csv")

        with DatasetWriter(output_csv_path=csv_path, batch_size=25) as writer:
            col = PlaywrightCollector(writer=writer)
            samples = col.collect(count=100, mode="headless_standard", delay_between_requests=0.0)

        assert len(samples) == 100, f"Phải thu thập đúng 100 mẫu, thực tế: {len(samples)}"
        assert os.path.exists(csv_path), "File CSV phải được lưu trên đĩa"
        print(f"   -> [PASS] Đã ghi thành công 100 mẫu ({os.path.getsize(csv_path) / 1024:.2f} KB)!")

        # 3. Thẩm định dữ liệu bằng Pandas
        print("\n[3/3] Thẩm định tập dữ liệu bằng Pandas:")
        df = pd.read_csv(csv_path)
        assert len(df) == 100, f"Dataset phải có đúng 100 dòng, thực tế: {len(df)}"
        assert list(df.columns) == DATASET_COLUMNS, "Các cột phải khớp 100% DATASET_COLUMNS"
        assert (df["tool_name"] == "playwright").all(), "100% tool_name phải là 'playwright'"
        assert (df["label"] == 1).all(), "100% nhãn phải là 1 (Bot)"
        assert (df["is_webdriver"] == 1.0).all(), "100% is_webdriver phải là 1.0"
        assert (df["virtual_gpu_detected"] == 1.0).all(), "100% virtual_gpu_detected trong headless mode phải là 1.0"
        assert (df["action_interval_std_dev"] < 16.0).all(), "100% nhịp bấm phải < 16ms"
        print("   -> [PASS] Thẩm định dữ liệu thành công! 100/100 bản ghi đạt chuẩn chất lượng cao.")

    print("\n" + "=" * 60)
    print(" ✅ TẤT CẢ TEST CASES SUBTASK 13.3.1.4 ĐÃ PASS 100%!")
    print("=" * 60)

if __name__ == "__main__":
    run_tests()
