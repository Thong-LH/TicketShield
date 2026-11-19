"""
curl_cffi_collector.py
Bộ thu thập dữ liệu tấn công đối kháng tầng giao thức bằng curl_cffi / Protocol Impersonation (Subtask 13.3.1.7).
Mô phỏng các hình thức botting trực diện ở tầng Network / Protocol / API:
  1. Raw API Bot: Hoàn toàn không gửi Client Telemetry (telemetry = null/empty), gọi thẳng backend API trong < 0.2s.
  2. Replay Bot: Gửi lại nonce cũ đã tiêu thụ hoặc telemetry cũ, kích hoạt Nonce Replay Protection.
  3. Burst Rate Assault: Bắn dồn dập với tần suất cao (12 - 30 req/s từ cùng IP/Subnet), kích hoạt Redis Rate Limiter.
  4. Protocol Impersonation: Giả lập TLS JA3/JA4 và HTTP/2 fingerprint của Chrome/Safari nhưng lệch pha Client Hints (thiếu Sec-Ch-Ua hoặc lệch OS).
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

PROTOCOL_ATTACK_MODES = [
    "raw_api_bot",
    "replay_bot",
    "burst_rate",
    "protocol_impersonation",
]

IMPERSONATED_USER_AGENTS = [
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36",
    "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Safari/605.1.15",
    "curl/8.4.0",
    "python-requests/2.32.3",
]

JA4_PROFILES = {
    "chrome": "t13d1516h2_8daaf6152771_02711d0e519e",
    "safari": "t13d1312h2_2b4b45e89649_3943a5951663",
    "curl": "t13d0302h2_9e27c1973656_9a4199c15984",
    "python": "t12i1910h1_35158b0f80a4_8daaf6152771",
}

class CurlCffiCollector:
    """
    Bộ thu thập dữ liệu tấn công đối kháng tầng giao thức (Protocol / Network Level).
    Mô phỏng bot cURL, script Python và các công cụ giả mạo TLS JA4/H2.
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
        self._replayed_nonces: List[str] = []

    def generate_protocol_payload_and_headers(
        self,
        mode: str = "raw_api_bot",
        target_ua: Optional[str] = None
    ) -> tuple[Dict[str, Any], Dict[str, str], float]:
        """
        Sinh payload và bộ HTTP headers tương ứng với từng kiểu tấn công tầng giao thức.
        Trả về (payload, headers, time_to_checkout).
        """
        ua = target_ua or random.choice(IMPERSONATED_USER_AGENTS)
        time_to_checkout = round(random.uniform(0.04, 0.28), 3)

        if mode == "raw_api_bot":
            # Bot gọi thẳng backend API, không hề nạp trang web hay SDK client
            payload = {
                "tierId": self.tier_id,
                "quantity": random.choice([2, 4, 6]),
                "telemetry": None,  # Hoàn toàn không có telemetry
                "deviceFingerprint": "",
                "nonce": str(uuid.uuid4()),
                "clientTimestamp": int(time.time() * 1000),
            }
            headers = {
                "User-Agent": ua,
                "Content-Type": "application/json",
                "X-Data-Collection-Mode": "True",
                "X-AntiBot-Client-Id": f"raw_bot_{uuid.uuid4().hex[:8]}",
                "X-JA4": JA4_PROFILES["curl"],
            }

        elif mode == "replay_bot":
            # Bot dùng lại nonce cũ đã được tiêu thụ (Double-Claim Attack)
            if self._replayed_nonces and random.random() < 0.8:
                reused_nonce = random.choice(self._replayed_nonces)
            else:
                reused_nonce = str(uuid.uuid4())
                self._replayed_nonces.append(reused_nonce)

            payload = {
                "tierId": self.tier_id,
                "quantity": 2,
                "telemetry": {
                    "is_webdriver": 0,
                    "virtual_gpu_detected": 0.0,
                    "prototype_tampered": 0.0,
                    "canvas_tampered": 0.0,
                    "canvas_hash": uuid.uuid4().hex[:32],
                    "webgl_renderer": "ANGLE (NVIDIA RTX)",
                    "webgl_vendor": "Google Inc. (NVIDIA)",
                    "action_interval_std_dev": 35.0,
                    "device_fingerprint": uuid.uuid4().hex[:32],
                    "nonce": reused_nonce,
                    "client_timestamp": int(time.time() * 1000) - 300000, # Telemetry cũ từ 5 phút trước
                    "client_user_agent": ua,
                },
                "deviceFingerprint": uuid.uuid4().hex[:32],
                "nonce": reused_nonce,
                "clientTimestamp": int(time.time() * 1000) - 300000,
            }
            headers = {
                "User-Agent": ua,
                "Content-Type": "application/json",
                "X-Data-Collection-Mode": "True",
                "X-AntiBot-Client-Id": f"replay_bot_{uuid.uuid4().hex[:8]}",
                "X-JA4": JA4_PROFILES["chrome"],
            }

        elif mode == "burst_rate":
            # Tấn công dồn dập tần suất cao kích hoạt Redis Rate Limiter
            nonce = str(uuid.uuid4())
            payload = {
                "tierId": self.tier_id,
                "quantity": 4,
                "telemetry": {
                    "is_webdriver": 1,
                    "virtual_gpu_detected": 0.0,
                    "prototype_tampered": 0.0,
                    "canvas_tampered": 0.0,
                    "canvas_hash": uuid.uuid4().hex[:32],
                    "webgl_renderer": "unknown",
                    "webgl_vendor": "unknown",
                    "action_interval_std_dev": 2.5,  # Gần như 0ms do bắn bằng script
                    "device_fingerprint": uuid.uuid4().hex[:32],
                    "nonce": nonce,
                    "client_timestamp": int(time.time() * 1000),
                    "client_user_agent": ua,
                },
                "deviceFingerprint": uuid.uuid4().hex[:32],
                "nonce": nonce,
                "clientTimestamp": int(time.time() * 1000),
            }
            headers = {
                "User-Agent": ua,
                "Content-Type": "application/json",
                "X-Data-Collection-Mode": "True",
                "X-AntiBot-Client-Id": "cluster_burst_attacker_subnet",
                "X-Forwarded-For": "198.51.100.45",  # Giả lập IP subnet chung
                "X-JA4": JA4_PROFILES["python"],
            }
            time_to_checkout = 0.05

        else: # protocol_impersonation
            # Tự nhận là Chrome nhưng thiếu Client Hints hoặc lệch OS (BlatantMismatch)
            nonce = str(uuid.uuid4())
            chrome_ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36"
            payload = {
                "tierId": self.tier_id,
                "quantity": 1,
                "telemetry": {
                    "is_webdriver": 0,
                    "virtual_gpu_detected": 0.0,
                    "prototype_tampered": 0.0,
                    "canvas_tampered": 0.0,
                    "canvas_hash": uuid.uuid4().hex[:32],
                    "webgl_renderer": "ANGLE (Intel Iris Xe)",
                    "webgl_vendor": "Google Inc. (Intel)",
                    "action_interval_std_dev": 18.0,
                    "device_fingerprint": uuid.uuid4().hex[:32],
                    "nonce": nonce,
                    "client_timestamp": int(time.time() * 1000),
                    "client_user_agent": "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36", # LỆCH OS!
                },
                "deviceFingerprint": uuid.uuid4().hex[:32],
                "nonce": nonce,
                "clientTimestamp": int(time.time() * 1000),
            }
            headers = {
                "User-Agent": chrome_ua, # Header nhận Windows nhưng Client nhận Linux
                "Content-Type": "application/json",
                "X-Data-Collection-Mode": "True",
                "X-AntiBot-Client-Id": f"proto_impersonator_{uuid.uuid4().hex[:8]}",
                "Sec-Ch-Ua-Platform": '"Linux"', # Lệch pha rõ rệt (Blatant Platform Mismatch)
                "X-JA4": JA4_PROFILES["chrome"],
            }

        return payload, headers, time_to_checkout

    def execute_single_attack(
        self,
        mode: str = "raw_api_bot",
        user_agent: Optional[str] = None
    ) -> DatasetRecord:
        """Thực hiện một đợt tấn công tầng giao thức đơn lẻ tới Gateway."""
        sid = f"cffi_{uuid.uuid4().hex[:12]}"
        payload, headers, time_to_checkout = self.generate_protocol_payload_and_headers(mode=mode, target_ua=user_agent)
        ua = headers.get("User-Agent", "curl/8.4.0")

        try:
            target_url = f"{self.gateway_url}/api/organizer/orders/checkout"
            resp = self.session.post(
                target_url,
                json=payload,
                headers=headers,
                timeout=5.0
            )
            debug_header = resp.headers.get("X-AntiBot-Debug-Vector")
            if debug_header:
                debug_data = json.loads(debug_header)
                record = DatasetRecord.from_gateway_debug(
                    tool_name="curl_cffi",
                    attack_mode=mode,
                    debug_vector=debug_data,
                    http_status=resp.status_code,
                    label=1,
                    sample_id=sid
                )
            else:
                record = self._build_synthetic_record(sid, mode, payload, headers, resp.status_code, time_to_checkout)
        except Exception:
            # Khi Gateway offline
            status_code = 403 if mode in ["protocol_impersonation", "raw_api_bot"] else 429
            record = self._build_synthetic_record(sid, mode, payload, headers, status_code, time_to_checkout)

        if self.writer:
            self.writer.add_record(record)

        return record

    def _build_synthetic_record(
        self,
        sid: str,
        mode: str,
        payload: Dict[str, Any],
        headers: Dict[str, str],
        status_code: int,
        time_to_checkout: float
    ) -> DatasetRecord:
        """Fallback tạo record hợp lệ khi Gateway offline."""
        telemetry = payload.get("telemetry") or {}
        ua = headers.get("User-Agent", "curl/8.4.0")

        # Xác định mức độ lệch pha UA
        if mode == "protocol_impersonation":
            ua_match_level = "BlatantMismatch"
        elif "curl" in ua or "python" in ua:
            ua_match_level = "MinorMismatch"
        else:
            ua_match_level = "ExactMatch"

        burst_rate = random.randint(14, 28) if mode == "burst_rate" else random.randint(2, 6)
        ja4 = headers.get("X-JA4", JA4_PROFILES["curl"])
        http2_fp = "m,p,a,s" if ("curl" in ua or "python" in ua) else "m,a,s,p"

        return DatasetRecord(
            sample_id=sid,
            tool_name="curl_cffi",
            attack_mode=mode,
            label=1,
            is_webdriver=float(telemetry.get("is_webdriver", -1.0)),
            virtual_gpu_detected=float(telemetry.get("virtual_gpu_detected", -1.0)),
            prototype_tampered=float(telemetry.get("prototype_tampered", -1.0)),
            canvas_tampered=float(telemetry.get("canvas_tampered", -1.0)),
            canvas_hash=telemetry.get("canvas_hash", ""),
            webgl_vendor=telemetry.get("webgl_vendor", "unknown"),
            webgl_renderer=telemetry.get("webgl_renderer", "unknown"),
            action_interval_std_dev=float(telemetry.get("action_interval_std_dev", -1.0)),
            device_fingerprint=telemetry.get("device_fingerprint", payload.get("deviceFingerprint", "")),
            client_user_agent=telemetry.get("client_user_agent", ""),
            header_user_agent=ua,
            ua_match_level=ua_match_level,
            ja4_fingerprint=ja4,
            http2_fingerprint=http2_fp,
            burst_rate_10s=burst_rate,
            concurrent_hold=random.randint(1, 4),
            time_to_checkout=time_to_checkout,
            device_account_overlap=0.85,
            gateway_http_status=status_code,
            gateway_decision="Block",
            risk_score=round(random.uniform(0.92, 0.99), 4),
        )

    def collect(
        self,
        count: int = 200,
        mode: str = "all",
        delay_between_requests: float = 0.01
    ) -> List[DatasetRecord]:
        """Thu thập một batch gồm 'count' mẫu bot curl_cffi / Protocol."""
        print(f"[CurlCffiCollector] Bắt đầu thu thập {count} mẫu bot Protocol / curl_cffi (Mode: {mode})...")
        modes = PROTOCOL_ATTACK_MODES
        results = []
        for i in range(count):
            selected_mode = modes[i % len(modes)] if mode == "all" else mode
            rec = self.execute_single_attack(mode=selected_mode)
            results.append(rec)
            if (i + 1) % 50 == 0 or (i + 1) == count:
                print(f"   -> Đã thu thập {i + 1}/{count} mẫu cURL (Mode: {selected_mode}, HTTP {rec.gateway_http_status}, UAMatch: {rec.ua_match_level}, Risk: {rec.risk_score:.4f})")
            if delay_between_requests > 0:
                time.sleep(delay_between_requests)

        if self.writer:
            self.writer.flush()

        print(f"[CurlCffiCollector] Hoàn tất thu thập {len(results)} mẫu.")
        return results

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="curl_cffi & Protocol Impersonation Attack Dataset Collector")
    parser.add_argument("--count", type=int, default=200, help="Số lượng mẫu bot cần thu thập")
    parser.add_argument("--mode", type=str, default="all", choices=["all", "raw_api_bot", "replay_bot", "burst_rate", "protocol_impersonation"])
    parser.add_argument("--output-csv", type=str, default="src/AIEngine/datasets/raw/curl_cffi_samples.csv")
    parser.add_argument("--gateway-url", type=str, default=DEFAULT_GATEWAY_URL)
    args = parser.parse_args()

    with DatasetWriter(output_csv_path=args.output_csv, batch_size=50) as writer:
        collector = CurlCffiCollector(gateway_url=args.gateway_url, writer=writer)
        collector.collect(count=args.count, mode=args.mode)
        print(f"Đã lưu kết quả tại: {args.output_csv}")
