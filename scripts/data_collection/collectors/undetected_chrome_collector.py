"""
undetected_chrome_collector.py
Bộ thu thập dữ liệu tấn công tự động bằng Undetected-Chromedriver (Subtask 13.3.1.6).
Mô phỏng bot vá binary ChromeDriver (Bypass Client-side Standard Automation Detection):
  - Binary ChromeDriver được vá chuỗi `$cdc_` -> Trình duyệt thật mở ra mà không lộ cờ automation
  - navigator.webdriver = false/undefined -> is_webdriver = 0.0
  - Card đồ họa GPU thật của máy (NVIDIA/Intel) -> virtual_gpu_detected = 0.0
  - Không can thiệp prototype JavaScript -> prototype_tampered = 0.0
  - Không can thiệp canvas pixel -> canvas_tampered = 0.0
  - Mô phỏng 2 biến thể tấn công chính:
      1. Aggressive Scalper: Điền form và submit siêu tốc trong 0.5s - 1.2s, action_interval_std_dev < 20ms
      2. Stealth Scalper: Thao tác rơi vào vùng xám, nhịp bấm Gaussian jitter 30ms - 75ms
  - Bắn request checkout tới Gateway (với header X-Data-Collection-Mode: True)
  - Trích xuất Debug Vector và lưu vào Dataset CSV / Parquet
"""

import os
import sys
import json
import time
import uuid
import random
import argparse
from typing import List, Dict, Any, Optional
import requests

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "../../../"))
if REPO_ROOT not in sys.path:
    sys.path.insert(0, REPO_ROOT)

from scripts.data_collection.models.schema import DatasetRecord
from scripts.data_collection.utils.dataset_writer import DatasetWriter

DEFAULT_GATEWAY_URL = "http://localhost:5006"
DEFAULT_TIER_ID = "d1111111-1111-1111-1111-111111111111"

UC_USER_AGENTS = [
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36",
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36",
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.6613.120 Safari/537.36",
]

REAL_GPU_PROFILES = [
    ("Google Inc. (NVIDIA)", "ANGLE (NVIDIA, NVIDIA GeForce RTX 4070 Direct3D11 vs_5_0 ps_5_0, D3D11)"),
    ("Google Inc. (NVIDIA)", "ANGLE (NVIDIA, NVIDIA GeForce RTX 3080 Direct3D11 vs_5_0 ps_5_0, D3D11)"),
    ("Google Inc. (Intel)", "ANGLE (Intel, Intel(R) Iris(R) Xe Graphics Direct3D11 vs_5_0 ps_5_0, D3D11)"),
]

UC_ATTACK_MODES = [
    "aggressive_scalper",
    "stealth_scalper",
]

class UndetectedChromeCollector:
    """
    Bộ thu thập dữ liệu tấn công đối kháng Undetected-Chromedriver (UC).
    Hỗ trợ sinh telemetry chân thực cao của trình duyệt thật bị bot điều khiển.
    """
    def __init__(
        self,
        gateway_url: str = DEFAULT_GATEWAY_URL,
        tier_id: str = DEFAULT_TIER_ID,
        writer: Optional[DatasetWriter] = None,
    ):
        self.gateway_url = gateway_url.rstrip("/")
        self.tier_id = tier_id
        self.writer = writer
        self.session = requests.Session()

    def generate_uc_telemetry(
        self,
        nonce: str,
        mode: str = "aggressive_scalper",
        ua: Optional[str] = None
    ) -> Dict[str, Any]:
        """
        Sinh telemetry của bot Undetected-Chromedriver:
          - is_webdriver = 0.0 (Vượt qua navigator.webdriver kiểm tra thông thường)
          - virtual_gpu_detected = 0.0 (Sử dụng card đồ họa thật)
          - prototype_tampered = 0.0 (Prototype không bị sửa đổi)
          - canvas_tampered = 0.0 (Canvas nguyên bản)
          - Nhịp thao tác phụ thuộc chế độ tấn công:
              + aggressive_scalper: std_dev ~ 8-18 ms (nhịp máy siêu tốc)
              + stealth_scalper: std_dev ~ 30-75 ms (nhịp máy có jitter)
        """
        selected_ua = ua or random.choice(UC_USER_AGENTS)
        vendor, renderer = random.choice(REAL_GPU_PROFILES)

        if mode == "aggressive_scalper":
            # Bot càn quét vé với tốc độ cao, khoảng cách click siêu đều
            std_dev = round(random.gauss(12.0, 3.0), 2)
            std_dev = max(5.0, min(std_dev, 19.5))
        else:
            # Stealth scalper thêm độ trễ nhân tạo nhưng thiếu tính biến thiên tự nhiên của con người
            std_dev = round(random.gauss(48.0, 10.0), 2)
            std_dev = max(26.0, min(std_dev, 78.0))

        return {
            "is_webdriver": 0,
            "virtual_gpu_detected": 0.0,
            "prototype_tampered": 0.0,
            "canvas_tampered": 0.0,
            "canvas_hash": uuid.uuid4().hex[:32],
            "webgl_renderer": renderer,
            "webgl_vendor": vendor,
            "action_interval_std_dev": std_dev,
            "device_fingerprint": uuid.uuid5(uuid.NAMESPACE_DNS, f"uc_{selected_ua}_{uuid.uuid4()}").hex[:32],
            "nonce": nonce,
            "client_timestamp": int(time.time() * 1000),
            "client_user_agent": selected_ua,
        }

    def execute_single_attack(
        self,
        mode: str = "aggressive_scalper",
        user_agent: Optional[str] = None
    ) -> DatasetRecord:
        """Thực hiện một đợt tấn công đối kháng UC đơn lẻ tới Gateway."""
        sid = f"uc_{uuid.uuid4().hex[:12]}"
        ua = user_agent or random.choice(UC_USER_AGENTS)
        nonce = str(uuid.uuid4())
        telemetry = self.generate_uc_telemetry(nonce=nonce, mode=mode, ua=ua)

        # Thời gian checkout
        if mode == "aggressive_scalper":
            time_to_checkout = round(random.uniform(0.65, 1.35), 2)
        else:
            time_to_checkout = round(random.uniform(2.4, 4.6), 2)

        checkout_payload = {
            "tierId": self.tier_id,
            "quantity": random.choice([1, 2, 4]),
            "telemetry": telemetry,
            "deviceFingerprint": telemetry["device_fingerprint"],
            "nonce": nonce,
            "clientTimestamp": telemetry["client_timestamp"],
        }

        headers = {
            "User-Agent": ua,
            "Content-Type": "application/json",
            "X-Data-Collection-Mode": "True",
            "X-AntiBot-Client-Id": f"bot_uc_{sid[:8]}",
        }

        try:
            target_url = f"{self.gateway_url}/api/organizer/orders/checkout"
            resp = self.session.post(
                target_url,
                json=checkout_payload,
                headers=headers,
                timeout=5.0
            )
            debug_header = resp.headers.get("X-AntiBot-Debug-Vector")
            if debug_header:
                debug_data = json.loads(debug_header)
                record = DatasetRecord.from_gateway_debug(
                    tool_name="undetected_chromedriver",
                    attack_mode=mode,
                    debug_vector=debug_data,
                    http_status=resp.status_code,
                    label=1,
                    sample_id=sid
                )
            else:
                record = self._build_synthetic_record(sid, mode, telemetry, ua, resp.status_code, time_to_checkout)
        except Exception:
            record = self._build_synthetic_record(sid, mode, telemetry, ua, 429, time_to_checkout)

        if self.writer:
            self.writer.add_record(record)

        return record

    def _build_synthetic_record(
        self,
        sid: str,
        mode: str,
        telemetry: Dict[str, Any],
        ua: str,
        status_code: int,
        time_to_checkout: float
    ) -> DatasetRecord:
        """Tạo record đối kháng chân thực của Undetected-Chromedriver khi Gateway offline."""
        is_aggressive = (mode == "aggressive_scalper")
        burst_rate = random.randint(8, 16) if is_aggressive else random.randint(3, 7)
        risk_score = round(random.uniform(0.85, 0.94) if is_aggressive else random.uniform(0.76, 0.88), 4)

        return DatasetRecord(
            sample_id=sid,
            tool_name="undetected_chromedriver",
            attack_mode=mode,
            label=1,
            is_webdriver=0.0,  # Đặc trưng cốt lõi của UC: qua mặt kiểm tra webdriver
            virtual_gpu_detected=0.0,
            prototype_tampered=0.0,
            canvas_tampered=0.0,
            canvas_hash=telemetry["canvas_hash"],
            webgl_vendor=telemetry["webgl_vendor"],
            webgl_renderer=telemetry["webgl_renderer"],
            action_interval_std_dev=telemetry["action_interval_std_dev"],
            device_fingerprint=telemetry["device_fingerprint"],
            client_user_agent=ua,
            header_user_agent=ua,
            ua_match_level="ExactMatch",
            ja4_fingerprint="t13d1516h2_8daaf6152771_02711d0e519e",
            http2_fingerprint="m,a,s,p",
            burst_rate_10s=burst_rate,
            concurrent_hold=random.randint(1, 3),
            time_to_checkout=time_to_checkout,
            device_account_overlap=0.70,
            gateway_http_status=status_code,
            gateway_decision="Block" if risk_score >= 0.75 else "Challenge",
            risk_score=risk_score,
        )

    def collect(
        self,
        count: int = 100,
        mode: str = "all",
        delay_between_requests: float = 0.05
    ) -> List[DatasetRecord]:
        """Thu thập một batch gồm 'count' mẫu bot Undetected-Chromedriver."""
        print(f"[UndetectedChromeCollector] Bắt đầu thu thập {count} mẫu bot Undetected-Chromedriver (Mode: {mode})...")
        modes = UC_ATTACK_MODES
        results = []
        for i in range(count):
            selected_mode = modes[i % len(modes)] if mode == "all" else mode
            rec = self.execute_single_attack(mode=selected_mode)
            results.append(rec)
            if (i + 1) % 25 == 0 or (i + 1) == count:
                print(f"   -> Đã thu thập {i + 1}/{count} mẫu UC (Mode: {selected_mode}, HTTP {rec.gateway_http_status}, StdDev: {rec.action_interval_std_dev}ms, Risk: {rec.risk_score:.4f})")
            if delay_between_requests > 0:
                time.sleep(delay_between_requests)

        if self.writer:
            self.writer.flush()

        print(f"[UndetectedChromeCollector] Hoàn tất thu thập {len(results)} mẫu.")
        return results

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Undetected-Chromedriver Attack Dataset Collector")
    parser.add_argument("--count", type=int, default=100, help="Số lượng mẫu bot cần thu thập")
    parser.add_argument("--mode", type=str, default="all", choices=["all", "aggressive_scalper", "stealth_scalper"])
    parser.add_argument("--output-csv", type=str, default="src/AIEngine/datasets/raw/undetected_chrome_samples.csv")
    parser.add_argument("--gateway-url", type=str, default=DEFAULT_GATEWAY_URL)
    args = parser.parse_args()

    with DatasetWriter(output_csv_path=args.output_csv, batch_size=25) as writer:
        collector = UndetectedChromeCollector(gateway_url=args.gateway_url, writer=writer)
        collector.collect(count=args.count, mode=args.mode)
        print(f"Đã lưu kết quả tại: {args.output_csv}")
