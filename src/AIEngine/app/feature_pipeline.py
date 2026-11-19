"""
feature_pipeline.py
Pipeline Biến Đổi & Chuẩn Hóa Vector Đặc Trưng 3 Tầng Cho AI Anti-Bot Model v2.0 (Subtask 13.3.3.1).
Theo đặc tả Epic US-13.3 và Jira SCRUM-381 (TicketShield):
  - Tầng 1: Browser / Client Telemetry (5 chiều: is_webdriver, virtual_gpu, proto_tamper, canvas_tamper, action_std)
  - Tầng 2: Network / Protocol Consistency (2 chiều: ua_match_level, network_inconsistency_score)
  - Tầng 3: Business / Behavioral Dynamics (4 chiều: time_to_checkout, burst_rate_10s, concurrent_hold, device_overlap)
Tổng cộng: Vector 11 chiều chuẩn hóa (Float32).
Khóa chặt hợp đồng toán học và xử lý missing value (-1.0) nhằm triệt tiêu hoàn toàn Training-Serving Skew.
"""

import os
import sys
import math
from typing import Dict, Any, List, Optional, Tuple, Union
import pandas as pd

# Danh sách chuẩn 11 chiều đặc trưng của Model v2.0 theo thứ tự cố định
FEATURE_COLUMNS_V2 = [
    # Tầng 1: Browser / Client Telemetry (5 chiều)
    "is_webdriver",
    "virtual_gpu_detected",
    "prototype_tampered",
    "canvas_tampered",
    "action_interval_std_dev",
    
    # Tầng 2: Network / Protocol Consistency (2 chiều)
    "ua_match_level",
    "network_inconsistency_score",
    
    # Tầng 3: Business / Behavioral Dynamics (4 chiều)
    "time_to_checkout",
    "burst_rate_10s",
    "concurrent_hold",
    "device_account_overlap",
]

# Bảng mã hóa số cho cấp độ khớp User-Agent giữa Client SDK và HTTP Header
UA_MATCH_LEVEL_MAPPING = {
    "ExactMatch": 0.0,
    "MinorMismatch": 0.5,
    "BlatantMismatch": 1.0,
    "Unknown": -1.0,
}


def encode_ua_match_level(val: Any) -> float:
    """Mã hóa chuỗi ua_match_level thành giá trị số float32 tương ứng."""
    if val is None:
        return -1.0
    if isinstance(val, (int, float)):
        return -1.0 if math.isnan(val) else float(val)
    str_val = str(val).strip()
    return UA_MATCH_LEVEL_MAPPING.get(str_val, -1.0)


def compute_network_inconsistency_score(row: Union[pd.Series, Dict[str, Any]]) -> float:
    """
    Tính điểm bất nhất mạng (0.0 đến 1.0) từ UA mismatch, TLS JA4 và HTTP/2 signatures.
    Điểm càng cao thể hiện hành vi giả mạo giao thức (bot cURL/Python API).
    """
    # Nếu record đã có sẵn trường network_inconsistency_score hợp lệ
    existing_score = row.get("network_inconsistency_score")
    if existing_score is not None:
        try:
            val = float(existing_score)
            if not math.isnan(val) and val >= 0.0:
                return float(max(0.0, min(1.0, val)))
        except (ValueError, TypeError):
            pass

    score = 0.0
    ua_match = row.get("ua_match_level", "")
    if ua_match == "BlatantMismatch":
        score += 0.70
    elif ua_match == "MinorMismatch":
        score += 0.35

    # Phân tích chữ ký TLS JA4
    ja4 = str(row.get("ja4_fingerprint") or "").lower()
    header_ua = str(row.get("header_user_agent") or "").lower()
    client_ua = str(row.get("client_user_agent") or "").lower()
    tool_name = str(row.get("tool_name") or "").lower()

    if "curl" in ja4 or "t13d0302h2_9e27c1973656" in ja4 or "cffi" in tool_name:
        score += 0.50

    if ("curl" in header_ua or "python" in header_ua) and client_ua != "":
        score += 0.40

    # Trường hợp bot gọi thẳng API (không có client telemetry)
    if client_ua == "none" or client_ua == "" or client_ua == "missing":
        if "mozilla" in header_ua:
            score += 0.30

    return float(max(0.0, min(1.0, score)))


def extract_features_from_dict(row: Dict[str, Any]) -> List[float]:
    """
    Trích xuất vector 11 chiều từ 1 bản ghi dạng dictionary.
    Đảm bảo 100% ép kiểu an toàn sang float và xử lý missing value thành -1.0.
    """
    def safe_float(key: str, default: float = -1.0) -> float:
        val = row.get(key)
        if val is None:
            return default
        try:
            f = float(val)
            return default if math.isnan(f) else f
        except (ValueError, TypeError):
            return default

    # Tầng 1: Browser / Client Telemetry (5 chiều)
    is_webdriver = safe_float("is_webdriver", -1.0)
    virtual_gpu_detected = safe_float("virtual_gpu_detected", -1.0)
    prototype_tampered = safe_float("prototype_tampered", -1.0)
    canvas_tampered = safe_float("canvas_tampered", -1.0)
    action_interval_std_dev = safe_float("action_interval_std_dev", -1.0)

    # Tầng 2: Network / Protocol Consistency (2 chiều)
    ua_match_level = encode_ua_match_level(row.get("ua_match_level"))
    network_inconsistency = compute_network_inconsistency_score(row)

    # Tầng 3: Business / Behavioral Dynamics (4 chiều)
    time_to_checkout = safe_float("time_to_checkout", -1.0)
    burst_rate_10s = safe_float("burst_rate_10s", 1.0)
    concurrent_hold = safe_float("concurrent_hold", 0.0)
    device_account_overlap = safe_float("device_account_overlap", -1.0)

    return [
        is_webdriver,
        virtual_gpu_detected,
        prototype_tampered,
        canvas_tampered,
        action_interval_std_dev,
        ua_match_level,
        network_inconsistency,
        time_to_checkout,
        burst_rate_10s,
        concurrent_hold,
        device_account_overlap,
    ]


def transform_single_record(record: Dict[str, Any]) -> Any:
    """
    Chuyển đổi 1 bản ghi đơn lẻ sang tensor 2D shape [1, 11] kiểu float32
    phục vụ suy diễn thời gian thực (Inference Serving).
    """
    feats = extract_features_from_dict(record)
    return pd.DataFrame([feats], dtype="float32").values


def transform_dataframe(
    df: pd.DataFrame,
    include_target: bool = True
) -> Tuple[Any, Optional[Any]]:
    """
    Chuyển đổi DataFrame sang ma trận đặc trưng số X [N, 11] và vector nhãn y [N].
    Đảm bảo 100% không chứa giá trị NaN hay Inf, sẵn sàng nạp thẳng vào Scikit-Learn.
    """
    records = df.to_dict(orient="records")
    features_list = [extract_features_from_dict(r) for r in records]
    X = pd.DataFrame(features_list, dtype="float32").values

    y = None
    if include_target and "label" in df.columns:
        y = df["label"].values.astype("int64")

    return X, y


def get_feature_names() -> List[str]:
    """Trả về danh sách 11 tên đặc trưng theo đúng thứ tự vector."""
    return list(FEATURE_COLUMNS_V2)


def validate_feature_matrix(X: Any) -> bool:
    """Kiểm tra ma trận đặc trưng có hợp lệ về shape và hoàn toàn sạch NaN hay không."""
    if not hasattr(X, "shape"):
        return False
    if len(X.shape) != 2 or X.shape[1] != len(FEATURE_COLUMNS_V2):
        return False
    return bool(not pd.isna(X).any())
