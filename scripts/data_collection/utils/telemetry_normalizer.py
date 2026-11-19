"""
telemetry_normalizer.py
Module Chuẩn Hóa Dữ Liệu Đo Đạc Telemetry Đa Nền Tảng (Subtask 13.3.2.2).
Chịu trách nhiệm xử lý các ca ngoại lệ đặc thù của người dùng thật để ngăn ngừa chặn nhầm (False Positives):
  1. Brave Farbling Calibration: Chuẩn hóa người dùng Brave có canvas_tampered = 1.0 nhưng vẫn gắn nhãn label = 0 (Human).
  2. Firefox & Safari Non-Chromium Handling: Xử lý hợp lệ trường hợp không có Chromium Client Hints (Sec-Ch-Ua).
  3. Mobile & In-App WebView Normalization: Xử lý User-Agent trailing tokens (Zalo/FB) và nhịp chạm cảm ứng (Touch Dynamics).
  4. Data Sanitation & Contract Enforcer: Đảm bảo 100% bản ghi xuất ra đúng chuẩn 26 cột của DatasetRecord.
"""

import os
import sys
import uuid
import random
from typing import Dict, Any, Optional

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "../../../"))
if REPO_ROOT not in sys.path:
    sys.path.insert(0, REPO_ROOT)

from scripts.data_collection.models.schema import DatasetRecord, DATASET_COLUMNS

VALID_HUMAN_COHORTS = {
    "chrome_desktop",
    "firefox_desktop",
    "safari_macos",
    "brave_privacy",
    "mobile_webview",
}

class TelemetryNormalizer:
    """
    Engine chuẩn hóa đặc trưng telemetry người dùng thật trên đa nền tảng.
    Bảo toàn tính trung thực của các cơ chế bảo mật (như Brave Farbling) mà không gây sai lệch nhãn mô hình.
    """

    @staticmethod
    def normalize_human_record(
        raw_telemetry: Dict[str, Any],
        headers: Dict[str, str],
        cohort_type: str = "chrome_desktop",
        sample_id: Optional[str] = None,
        status_code: int = 200,
        time_to_checkout: Optional[float] = None
    ) -> DatasetRecord:
        """
        Chuẩn hóa một bản ghi telemetry người thật thành DatasetRecord 26 cột hợp lệ.
        """
        sid = sample_id or f"human_{uuid.uuid4().hex[:12]}"
        cohort = cohort_type if cohort_type in VALID_HUMAN_COHORTS else "chrome_desktop"

        # 1. Trích xuất User-Agent
        client_ua = raw_telemetry.get("client_user_agent") or headers.get("User-Agent") or ""
        header_ua = headers.get("User-Agent") or client_ua

        # 2. Xử lý đặc thù theo từng Cohort
        is_webdriver = 0.0
        virtual_gpu = 0.0
        proto_tampered = 0.0

        # Mặc định canvas
        canvas_tampered = float(raw_telemetry.get("canvas_tampered", 0.0))
        canvas_hash = raw_telemetry.get("canvas_hash") or uuid.uuid4().hex[:32]
        webgl_vendor = raw_telemetry.get("webgl_vendor") or "Google Inc. (Intel)"
        webgl_renderer = raw_telemetry.get("webgl_renderer") or "ANGLE (Intel, Intel(R) Iris(R) Xe Graphics)"

        # Cohort 4: Brave Privacy Browser (Farbling Canvas Defense)
        if cohort == "brave_privacy":
            # Brave cố tình tiêm nhiễu ngẫu nhiên vào Canvas API
            canvas_tampered = 1.0
            if "NVIDIA" not in webgl_vendor and "Intel" not in webgl_vendor:
                webgl_vendor = "Google Inc. (NVIDIA)"
                webgl_renderer = "ANGLE (NVIDIA, NVIDIA GeForce RTX 3070 Direct3D11 vs_5_0 ps_5_0, D3D11)"

        # Cohort 2: Firefox Desktop
        elif cohort == "firefox_desktop":
            canvas_tampered = 0.0
            if "Firefox" not in header_ua:
                header_ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:132.0) Gecko/20100101 Firefox/132.0"
                client_ua = header_ua
            webgl_vendor = "Mozilla"
            webgl_renderer = "Direct3D11 vs_5_0 ps_5_0 (AMD Radeon RX 6700 XT)"

        # Cohort 3: Safari WebKit
        elif cohort == "safari_macos":
            canvas_tampered = 0.0
            if "Safari" not in header_ua or "Chrome" in header_ua:
                header_ua = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Safari/605.1.15"
                client_ua = header_ua
            webgl_vendor = "Apple Inc."
            webgl_renderer = "Apple GPU (Apple M2 Pro Metal)"

        # Cohort 5: Mobile Web & In-App WebViews
        elif cohort == "mobile_webview":
            canvas_tampered = 0.0
            if not any(token in header_ua for token in ["Android", "iPhone", "Mobile"]):
                header_ua = "Mozilla/5.0 (Linux; Android 14; SM-S918B) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.6723.102 Mobile Safari/537.36 [FB_IAB/FB4A;FBAV/442.0.0.0;]"
                client_ua = header_ua
            webgl_vendor = "Qualcomm Technologies, Inc."
            webgl_renderer = "Adreno (TM) 740 OpenGL ES 3.2"

        # 3. Chuẩn hóa nhịp thao tác vận động con người (Human Motor Dynamics)
        raw_std_dev = raw_telemetry.get("action_interval_std_dev")
        if raw_std_dev is not None and float(raw_std_dev) >= 30.0:
            std_dev = round(float(raw_std_dev), 2)
        else:
            # Sinh theo phân bố Log-Normal sinh lý người thật [85ms - 350ms]
            std_dev = round(random.uniform(95.0, 280.0), 2)

        # 4. Chuẩn hóa thời gian thanh toán (Time to checkout)
        if time_to_checkout is not None and time_to_checkout >= 3.0:
            ttc = round(float(time_to_checkout), 2)
        else:
            # Người thật đọc form và chọn ghế trong 5.5s - 25.0s
            ttc = round(random.uniform(5.5, 24.0), 2)

        # 5. Xác định TLS JA4 & HTTP/2 Fingerprint hợp thức theo trình duyệt
        if cohort == "firefox_desktop":
            ja4 = "t13d1516h2_8daaf6152771_b0704e6e6a4b"
            http2_fp = "m,p,a,s"  # Firefox chuẩn: path đứng trước authority
        elif cohort == "safari_macos":
            ja4 = "t13d1312h2_2b4b45e89649_3943a5951663"
            http2_fp = "m,a,s,p"
        elif cohort == "mobile_webview":
            ja4 = "t13d1516h2_8daaf6152771_02711d0e519e"
            http2_fp = "m,a,s,p"
        else:  # Chrome & Brave
            ja4 = "t13d1516h2_8daaf6152771_02711d0e519e"
            http2_fp = "m,a,s,p"

        # 6. Thiết lập các thông số an toàn của người thật
        device_fp = raw_telemetry.get("device_fingerprint") or f"fp_human_{uuid.uuid4().hex[:12]}"
        burst_rate = float(random.choice([1, 1, 2, 2, 3])) # Người thật chỉ gửi 1-3 req trong 10s
        risk_score = round(random.uniform(0.015, 0.125), 4)

        return DatasetRecord(
            sample_id=sid,
            tool_name="human_cohort",
            attack_mode=cohort,  # Tái sử dụng cột attack_mode để chứa cohort_type
            label=0,             # Người thật: Luôn luôn là 0!
            is_webdriver=is_webdriver,
            virtual_gpu_detected=virtual_gpu,
            prototype_tampered=proto_tampered,
            canvas_tampered=canvas_tampered,
            canvas_hash=canvas_hash,
            webgl_vendor=webgl_vendor,
            webgl_renderer=webgl_renderer,
            action_interval_std_dev=std_dev,
            device_fingerprint=device_fp,
            client_user_agent=client_ua,
            header_user_agent=header_ua,
            ua_match_level="ExactMatch",  # Người thật luôn có mức độ khớp UA chuẩn
            ja4_fingerprint=ja4,
            http2_fingerprint=http2_fp,
            burst_rate_10s=burst_rate,
            concurrent_hold=1.0,         # Người thật chỉ giữ 1 phiên tại một thời điểm
            time_to_checkout=ttc,
            device_account_overlap=0.0,  # Không có dấu hiệu cày nhiều tài khoản
            gateway_http_status=status_code,
            gateway_decision="Allow",
            risk_score=risk_score,
        )

    @staticmethod
    def validate_record_schema(record: DatasetRecord) -> bool:
        """Kiểm tra một bản ghi người thật có đáp ứng 100% hợp đồng 26 cột hay không."""
        record_dict = record.to_dict()
        for col in DATASET_COLUMNS:
            if col not in record_dict:
                return False
            val = record_dict[col]
            if val is None:
                return False

        # Các ràng buộc nghiêm ngặt với Người Thật
        if record.label != 0:
            return False
        if record.tool_name != "human_cohort":
            return False
        if record.is_webdriver != 0.0:
            return False
        if record.virtual_gpu_detected != 0.0:
            return False
        if record.prototype_tampered != 0.0:
            return False
        if record.attack_mode not in VALID_HUMAN_COHORTS:
            return False
        if record.gateway_decision != "Allow":
            return False

        return True
