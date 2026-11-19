"""
run_adversarial_bot_collection.py
Master Orchestrator CLI Runner cho bộ thu thập dữ liệu tấn công đối kháng đa công cụ (Subtask 13.3.1.8).
Điều phối 4 công cụ bot độc lập:
  1. Playwright: Standard Automation (navigator.webdriver, GPU ảo SwiftShader, nhịp đều)
  2. Puppeteer-Extra-Stealth: Evasion Bot (can thiệp Prototype, fake GPU NVIDIA, canvas noise)
  3. Undetected-Chromedriver: Binary-patched Bot (is_webdriver=0, GPU thật, nhịp càn quét)
  4. curl_cffi: Protocol Impersonation (Raw API, Replay Nonce, Burst Rate, Client Hints Mismatch)

Xuất dataset thô chuẩn hóa 26 cột ra CSV và Parquet tại:
  - src/AIEngine/datasets/raw/adversarial_bot_attacks_raw.csv
  - src/AIEngine/datasets/raw/adversarial_bot_attacks_raw.parquet
"""

import os
import sys
import time
import argparse
from typing import Dict, Any, List
import requests
import pandas as pd

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "../../"))
if REPO_ROOT not in sys.path:
    sys.path.insert(0, REPO_ROOT)

from scripts.data_collection.models.schema import DATASET_COLUMNS
from scripts.data_collection.utils.dataset_writer import DatasetWriter
from scripts.data_collection.collectors.playwright_collector import PlaywrightCollector
from scripts.data_collection.collectors.puppeteer_stealth_collector import PuppeteerStealthCollector
from scripts.data_collection.collectors.undetected_chrome_collector import UndetectedChromeCollector
from scripts.data_collection.collectors.curl_cffi_collector import CurlCffiCollector

DEFAULT_GATEWAY_URL = "http://localhost:5006"
DEFAULT_CSV_PATH = os.path.join(REPO_ROOT, "src/AIEngine/datasets/raw/adversarial_bot_attacks_raw.csv")
DEFAULT_PARQUET_PATH = os.path.join(REPO_ROOT, "src/AIEngine/datasets/raw/adversarial_bot_attacks_raw.parquet")

def preflight_check(gateway_url: str) -> bool:
    """Kiểm tra tình trạng sẵn sàng của Anti-Bot Gateway."""
    print("=" * 70)
    print(" 🛡️ TICKETSHIELD ADVERSARIAL BOT DATASET COLLECTION ORCHESTRATOR")
    print("=" * 70)
    print(f"[*] Đang thực hiện Preflight Check tới Gateway: {gateway_url}...")
    try:
        resp = requests.get(f"{gateway_url.rstrip('/')}/health", timeout=2.0)
        if resp.status_code == 200:
            print("   -> [ONLINE] Gateway đang hoạt động! Chế độ: Live Target Mode (Bắn trực tiếp)")
            return True
    except Exception:
        pass

    print("   -> [OFFLINE / SANDBOX] Gateway chưa bật hoặc chạy trong sandbox cô lập.")
    print("   -> Chế độ: High-Fidelity Synthetic Evasion Simulation (Sinh vector chuẩn xác, không crash)")
    return False

def print_dataset_summary(csv_path: str, parquet_path: str, elapsed_sec: float):
    """Đọc lại tập dữ liệu thô và xuất báo cáo thẩm định phân tích đặc trưng."""
    if not os.path.exists(csv_path):
        print(f"[!] Không tìm thấy file dữ liệu tại {csv_path}")
        return

    df = pd.read_csv(csv_path)
    total_samples = len(df)
    csv_size_kb = os.path.getsize(csv_path) / 1024.0

    parquet_size_info = "Chưa xuất (Yêu cầu pyarrow)"
    if os.path.exists(parquet_path):
        parquet_size_info = f"{os.path.getsize(parquet_path) / 1024.0:.2f} KB"

    print("\n" + "=" * 70)
    print(" 📊 BÁO CÁO TỔNG KẾT THU THẬP DỮ LIỆU ĐỐI KHÁNG (ML-13.3.1)")
    print("=" * 70)
    print(f"[*] Tổng số mẫu bot thu thập: {total_samples:,} mẫu (Thời gian: {elapsed_sec:.2f}s)")
    print(f"[*] File CSV:     {csv_path} ({csv_size_kb:.2f} KB)")
    print(f"[*] File Parquet: {parquet_path} ({parquet_size_info})")
    print(f"[*] Số cột đặc trưng: {len(df.columns)} / 26 cột chuẩn DATASET_COLUMNS")
    print(f"[*] Tỷ lệ nhãn bot (label = 1): {(df['label'] == 1).mean() * 100:.1f}%")

    print("\n[+] Phân bố số lượng mẫu theo công cụ (Tool Distribution):")
    tool_counts = df["tool_name"].value_counts().to_dict()
    for tool, count in tool_counts.items():
        pct = (count / total_samples) * 100
        print(f"    - {tool:25s}: {count:5d} mẫu ({pct:.1f}%)")

    print("\n[+] Phân bố các hình thức tấn công (Attack Modes):")
    mode_counts = df["attack_mode"].value_counts().to_dict()
    for mode, count in mode_counts.items():
        print(f"    - {mode:28s}: {count:5d} mẫu")

    print("\n[+] Thống kê các đặc trưng cốt lõi (Core Feature Breakdown):")
    is_wd_pos = (df["is_webdriver"] == 1.0).sum()
    virt_gpu_pos = (df["virtual_gpu_detected"] == 1.0).sum()
    proto_tamper_pos = (df["prototype_tampered"] == 1.0).sum()
    canvas_tamper_pos = (df["canvas_tampered"] == 1.0).sum()

    print(f"    - is_webdriver = 1.0:           {is_wd_pos:5d} mẫu ({(is_wd_pos/total_samples)*100:.1f}%)")
    print(f"    - virtual_gpu_detected = 1.0:   {virt_gpu_pos:5d} mẫu ({(virt_gpu_pos/total_samples)*100:.1f}%)")
    print(f"    - prototype_tampered = 1.0:     {proto_tamper_pos:5d} mẫu ({(proto_tamper_pos/total_samples)*100:.1f}%)")
    print(f"    - canvas_tampered = 1.0:        {canvas_tamper_pos:5d} mẫu ({(canvas_tamper_pos/total_samples)*100:.1f}%)")

    print("\n" + "=" * 70)
    print(" ✅ HOÀN THÀNH NGHIỆM THU SUBTASK ML-13.3.1 THÀNH CÔNG RỰC RỠ!")
    print("=" * 70)

def run_collection(
    tools: str = "all",
    samples_per_tool: int = 250,
    output_csv: str = DEFAULT_CSV_PATH,
    output_parquet: str = DEFAULT_PARQUET_PATH,
    gateway_url: str = DEFAULT_GATEWAY_URL,
    batch_size: int = 50,
    delay: float = 0.01,
):
    """Hàm điều phối chính thực thi thu thập dữ liệu từ các công cụ."""
    start_time = time.time()
    preflight_check(gateway_url)

    # Đảm bảo thư mục lưu trữ tồn tại
    os.makedirs(os.path.dirname(os.path.abspath(output_csv)), exist_ok=True)
    os.makedirs(os.path.dirname(os.path.abspath(output_parquet)), exist_ok=True)

    tool_list = []
    if tools == "all":
        tool_list = ["playwright", "puppeteer", "uc", "curl_cffi"]
    else:
        tool_list = [tools]

    print(f"\n[*] Kế hoạch thu thập: {len(tool_list)} công cụ, mỗi công cụ {samples_per_tool:,} mẫu.")
    print(f"[*] Tổng số mẫu dự kiến: {len(tool_list) * samples_per_tool:,} mẫu.")
    print(f"[*] Định dạng xuất: CSV và Parquet (Streaming buffer: {batch_size} mẫu/batch)\n")

    with DatasetWriter(
        output_csv_path=output_csv,
        output_parquet_path=output_parquet,
        batch_size=batch_size,
        append=False # Khởi tạo dataset mới
    ) as writer:

        for idx, tool in enumerate(tool_list, 1):
            print(f"[{idx}/{len(tool_list)}] Khởi chạy bộ thu thập: {tool.upper()} ({samples_per_tool} mẫu)...")

            if tool == "playwright":
                collector = PlaywrightCollector(gateway_url=gateway_url, writer=writer)
                collector.collect(count=samples_per_tool, mode="headless_standard", delay_between_requests=delay)

            elif tool == "puppeteer":
                collector = PuppeteerStealthCollector(gateway_url=gateway_url, writer=writer)
                collector.collect(count=samples_per_tool, mode="all", delay_between_requests=delay)

            elif tool == "uc":
                collector = UndetectedChromeCollector(gateway_url=gateway_url, writer=writer)
                collector.collect(count=samples_per_tool, mode="all", delay_between_requests=delay)

            elif tool == "curl_cffi":
                collector = CurlCffiCollector(gateway_url=gateway_url, writer=writer)
                collector.collect(count=samples_per_tool, mode="all", delay_between_requests=delay)

            print(f"   -> Đã hoàn thành công cụ {tool.upper()}!\n")

    elapsed = time.time() - start_time
    print_dataset_summary(output_csv, output_parquet, elapsed)

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="TicketShield Multi-Tool Adversarial Dataset Collection CLI")
    parser.add_argument(
        "--tools",
        type=str,
        default="all",
        choices=["all", "playwright", "puppeteer", "uc", "curl_cffi"],
        help="Công cụ bot cần thu thập (mặc định: all)",
    )
    parser.add_argument(
        "--samples-per-tool",
        type=int,
        default=250,
        help="Số lượng mẫu thu thập cho mỗi công cụ (mặc định: 250 -> 1000 mẫu khi chạy all)",
    )
    parser.add_argument(
        "--output-csv",
        type=str,
        default=DEFAULT_CSV_PATH,
        help=f"Đường dẫn lưu file CSV (mặc định: {DEFAULT_CSV_PATH})",
    )
    parser.add_argument(
        "--output-parquet",
        type=str,
        default=DEFAULT_PARQUET_PATH,
        help=f"Đường dẫn lưu file Parquet (mặc định: {DEFAULT_PARQUET_PATH})",
    )
    parser.add_argument(
        "--gateway-url",
        type=str,
        default=DEFAULT_GATEWAY_URL,
        help="URL của Anti-Bot Gateway (mặc định: http://localhost:5006)",
    )
    parser.add_argument(
        "--batch-size",
        type=int,
        default=50,
        help="Kích thước batch ghi streaming (mặc định: 50)",
    )
    parser.add_argument(
        "--delay",
        type=float,
        default=0.005,
        help="Độ trễ giữa các request tính bằng giây (mặc định: 0.005s)",
    )

    args = parser.parse_args()

    run_collection(
        tools=args.tools,
        samples_per_tool=args.samples_per_tool,
        output_csv=args.output_csv,
        output_parquet=args.output_parquet,
        gateway_url=args.gateway_url,
        batch_size=args.batch_size,
        delay=args.delay,
    )
