"""
schema.py
Định nghĩa Data Contract và Feature Schema cho tập dữ liệu đối kháng (Adversarial Dataset).
Theo đặc tả US-13.3 và ML-13.3.1 của TicketShield.
"""

from dataclasses import dataclass, asdict, field
from datetime import datetime, timezone
from typing import Optional, Dict, Any
import uuid

# Danh sách đầy đủ các cột đặc trưng của Dataset v2.0
DATASET_COLUMNS = [
    "sample_id",
    "tool_name",
    "attack_mode",
    "label",
    "is_webdriver",
    "virtual_gpu_detected",
    "prototype_tampered",
    "canvas_tampered",
    "canvas_hash",
    "webgl_vendor",
    "webgl_renderer",
    "action_interval_std_dev",
    "device_fingerprint",
    "client_user_agent",
    "header_user_agent",
    "ua_match_level",
    "ja4_fingerprint",
    "http2_fingerprint",
    "burst_rate_10s",
    "concurrent_hold",
    "time_to_checkout",
    "device_account_overlap",
    "gateway_http_status",
    "gateway_decision",
    "risk_score",
    "timestamp_utc",
]

@dataclass
class DatasetRecord:
    # 1. Định danh & Metadata
    sample_id: str = field(default_factory=lambda: str(uuid.uuid4()))
    tool_name: str = "unknown"  # playwright, puppeteer_stealth, undetected_chromedriver, curl_cffi
    attack_mode: str = "standard"  # aggressive_burst, stealth_delay, impersonation_replay, headless_standard
    label: int = 1  # 1 = Bot, 0 = Human

    # 2. Client Telemetry Signals (US-13.2)
    is_webdriver: float = -1.0
    virtual_gpu_detected: float = -1.0
    prototype_tampered: float = -1.0
    canvas_tampered: float = -1.0
    canvas_hash: str = "unavailable"
    webgl_vendor: str = "unknown"
    webgl_renderer: str = "unknown"
    action_interval_std_dev: float = -1.0
    device_fingerprint: str = "unknown"
    client_user_agent: str = ""

    # 3. Server & Network Signals (US-13.1, 13.2.4)
    header_user_agent: str = ""
    ua_match_level: str = "Unknown"
    ja4_fingerprint: str = ""
    http2_fingerprint: str = ""
    burst_rate_10s: float = 1.0
    concurrent_hold: float = 0.0
    time_to_checkout: float = -1.0
    device_account_overlap: float = -1.0

    # 4. Gateway Decision & Evaluation
    gateway_http_status: int = 200
    gateway_decision: str = "Allow"  # Allow, Block, Challenge
    risk_score: float = 0.0
    timestamp_utc: str = field(default_factory=lambda: datetime.now(timezone.utc).isoformat())

    def to_dict(self) -> Dict[str, Any]:
        """Chuyển đổi record sang dictionary khớp 100% với DATASET_COLUMNS."""
        res = asdict(self)
        # Đảm bảo các giá trị None được chuẩn hóa
        for key in ["virtual_gpu_detected", "prototype_tampered", "canvas_tampered", "is_webdriver", "action_interval_std_dev", "time_to_checkout", "device_account_overlap"]:
            if res.get(key) is None:
                res[key] = -1.0
            else:
                res[key] = float(res[key])

        for key in ["webgl_vendor", "webgl_renderer", "canvas_hash", "device_fingerprint", "client_user_agent", "header_user_agent", "ua_match_level", "ja4_fingerprint", "http2_fingerprint"]:
            if res.get(key) is None:
                res[key] = ""
            else:
                res[key] = str(res[key])

        return res

    @classmethod
    def from_gateway_debug(
        cls,
        tool_name: str,
        attack_mode: str,
        debug_vector: Dict[str, Any],
        http_status: int = 200,
        label: int = 1,
        sample_id: Optional[str] = None,
    ) -> "DatasetRecord":
        """
        Khởi tạo một DatasetRecord từ kết quả phân tích 'debugVector' do Gateway trả về.
        Tự động mapping các trường camelCase / snake_case.
        """
        sid = sample_id or str(uuid.uuid4())

        def get_val(key_snake: str, key_camel: str, default=None):
            return debug_vector.get(key_snake, debug_vector.get(key_camel, default))

        return cls(
            sample_id=sid,
            tool_name=tool_name,
            attack_mode=attack_mode,
            label=label,
            # Telemetry
            is_webdriver=float(get_val("is_webdriver", "isWebDriver", -1.0) or -1.0),
            virtual_gpu_detected=float(get_val("virtual_gpu_detected", "virtualGpuDetected", -1.0) or -1.0),
            prototype_tampered=float(get_val("prototype_tampered", "prototypeTampered", -1.0) or -1.0),
            canvas_tampered=float(get_val("canvas_tampered", "canvasTampered", -1.0) or -1.0),
            canvas_hash=str(get_val("canvas_hash", "canvasHash", "") or ""),
            webgl_vendor=str(get_val("webgl_vendor", "webglVendor", "unknown") or "unknown"),
            webgl_renderer=str(get_val("webgl_renderer", "webglRenderer", "unknown") or "unknown"),
            action_interval_std_dev=float(get_val("action_interval_std_dev", "actionIntervalStdDev", -1.0) or -1.0),
            device_fingerprint=str(get_val("device_fingerprint", "deviceFingerprint", "") or ""),
            client_user_agent=str(get_val("client_user_agent", "clientUserAgent", "") or ""),
            # Network & Server
            header_user_agent=str(get_val("header_user_agent", "headerUserAgent", "") or ""),
            ua_match_level=str(get_val("ua_match_level", "uaMatchLevel", "Unknown") or "Unknown"),
            ja4_fingerprint=str(get_val("ja4_fingerprint", "ja4Fingerprint", "") or ""),
            http2_fingerprint=str(get_val("http2_fingerprint", "http2Fingerprint", "") or ""),
            burst_rate_10s=float(get_val("burst_rate_10s", "burstRate10s", 1.0) or 1.0),
            concurrent_hold=float(get_val("concurrent_hold", "concurrentHold", 0.0) or 0.0),
            time_to_checkout=float(get_val("time_to_checkout", "timeToCheckout", -1.0) or -1.0),
            device_account_overlap=float(get_val("device_account_overlap", "deviceAccountOverlap", -1.0) or -1.0),
            # Decision
            gateway_http_status=int(get_val("status_code", "statusCode", http_status) or http_status),
            gateway_decision=str(get_val("decision", "decision", "Allow") or "Allow"),
            risk_score=float(get_val("risk_score", "riskScore", 0.0) or 0.0),
            timestamp_utc=datetime.now(timezone.utc).isoformat(),
        )
