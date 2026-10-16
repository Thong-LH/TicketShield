"""
synthetic_data_generator.py
Tạo tập dữ liệu 6D cho Anti-Bot ML Model theo đặc tả US-8.3 (TicketShield).
Bao gồm các nhóm đối tượng mô phỏng theo chuẩn nghiên cứu:
  - StandardUser: Người dùng thông thường (label 0)
  - PowerUser: Người dùng tay nhanh / game thủ (label 0, dùng cho Leave-One-Cohort-Out)
  - SimpleBot: Script Python/Go bắn thẳng API (label 1)
  - StealthBot / GreyZone: Phe vé tinh vi có delay giả lập (label 1 & edge-case)
"""

import numpy as np
import pandas as pd

FEATURE_COLUMNS = [
    "action_interval_std_dev",
    "is_webdriver",
    "device_hash_entropy",
    "time_to_checkout",
    "burst_rate_10s",
    "device_account_overlap",
]

def generate_standard_user(n: int = 2500, seed: int = 42) -> pd.DataFrame:
    """Cohort 1: StandardUser — Người mua vé bình thường."""
    rng = np.random.default_rng(seed)
    return pd.DataFrame({
        "action_interval_std_dev": np.clip(rng.normal(loc=190.0, scale=40.0, size=n), 60.0, 400.0),
        "is_webdriver": rng.binomial(n=1, p=0.005, size=n).astype(float),
        "device_hash_entropy": np.clip(rng.normal(loc=0.85, scale=0.07, size=n), 0.55, 1.00),
        "time_to_checkout": np.clip(rng.normal(loc=15.0, scale=4.0, size=n), 6.0, 45.0),
        "burst_rate_10s": rng.poisson(lam=1.2, size=n).astype(float),
        "device_account_overlap": np.clip(rng.beta(a=1.0, b=15.0, size=n), 0.0, 1.0),
        "cohort": "StandardUser",
        "label": np.zeros(n, dtype=int),
    })

def generate_power_user(n: int = 1000, seed: int = 43) -> pd.DataFrame:
    """Cohort 2: PowerUser — Game thủ / người dùng thành thạo, mua rất nhanh nhưng là người thật."""
    rng = np.random.default_rng(seed)
    return pd.DataFrame({
        "action_interval_std_dev": np.clip(rng.normal(loc=90.0, scale=18.0, size=n), 60.0, 160.0),
        "is_webdriver": np.zeros(n, dtype=float), # Người thật không có cờ webdriver
        "device_hash_entropy": np.clip(rng.normal(loc=0.78, scale=0.08, size=n), 0.50, 0.95),
        "time_to_checkout": np.clip(rng.normal(loc=3.2, scale=0.8, size=n), 2.0, 5.0),
        "burst_rate_10s": np.clip(rng.poisson(lam=2.5, size=n), 1, 6).astype(float),
        "device_account_overlap": np.clip(rng.beta(a=1.0, b=10.0, size=n), 0.0, 0.3),
        "cohort": "PowerUser",
        "label": np.zeros(n, dtype=int),
    })

def generate_simple_bot(n: int = 2500, seed: int = 44) -> pd.DataFrame:
    """Cohort 3: SimpleBot — Bot tự động hóa cào vé, lộ cờ webdriver và tốc độ máy tính."""
    rng = np.random.default_rng(seed)
    return pd.DataFrame({
        "action_interval_std_dev": np.clip(rng.normal(loc=12.0, scale=5.0, size=n), 1.0, 25.0),
        "is_webdriver": rng.binomial(n=1, p=0.96, size=n).astype(float),
        "device_hash_entropy": np.clip(rng.normal(loc=0.15, scale=0.07, size=n), 0.00, 0.35),
        "time_to_checkout": np.clip(rng.exponential(scale=0.8, size=n), 0.2, 2.0),
        "burst_rate_10s": np.clip(rng.poisson(lam=12.0, size=n), 6, 35).astype(float),
        "device_account_overlap": np.clip(rng.beta(a=9.0, b=1.5, size=n), 0.5, 1.0),
        "cohort": "SimpleBot",
        "label": np.ones(n, dtype=int),
    })

def generate_stealth_bot(n: int = 1500, seed: int = 45) -> pd.DataFrame:
    """Cohort 4: StealthBot / Grey Zone — Bot phe vé tinh vi cố tình chèn delay ngụy trang."""
    rng = np.random.default_rng(seed)
    return pd.DataFrame({
        "action_interval_std_dev": np.clip(rng.normal(loc=55.0, scale=15.0, size=n), 25.0, 95.0),
        "is_webdriver": rng.binomial(n=1, p=0.20, size=n).astype(float),
        "device_hash_entropy": np.clip(rng.normal(loc=0.48, scale=0.12, size=n), 0.20, 0.70),
        "time_to_checkout": np.clip(rng.normal(loc=4.2, scale=1.2, size=n), 1.8, 7.5),
        "burst_rate_10s": np.clip(rng.poisson(lam=4.5, size=n), 2, 12).astype(float),
        "device_account_overlap": np.clip(rng.beta(a=4.0, b=2.5, size=n), 0.1, 0.9),
        "cohort": "StealthBot",
        "label": rng.binomial(n=1, p=0.65, size=n), # 65% bot, 35% human edge-cases
    })

def generate_dataset(
    n_standard: int = 2500,
    n_power: int = 1000,
    n_simple: int = 2500,
    n_stealth: int = 1500,
    seed: int = 42,
    output_path: str = None,
) -> pd.DataFrame:
    """Tạo hợp nhất 4 cohort, xáo trộn dữ liệu và lưu CSV nếu có output_path."""
    df_std = generate_standard_user(n=n_standard, seed=seed)
    df_pwr = generate_power_user(n=n_power, seed=seed + 1)
    df_smp = generate_simple_bot(n=n_simple, seed=seed + 2)
    df_stl = generate_stealth_bot(n=n_stealth, seed=seed + 3)

    df = pd.concat([df_std, df_pwr, df_smp, df_stl], ignore_index=True)
    df = df.sample(frac=1.0, random_state=seed).reset_index(drop=True)

    if output_path:
        df.to_csv(output_path, index=False)
        print(f"[Dataset] Đã xuất {len(df)} dòng dữ liệu ra: {output_path}")

    return df

if __name__ == "__main__":
    df = generate_dataset(output_path="dataset.csv")
    print(df.groupby("cohort")[FEATURE_COLUMNS].mean())
    print("\nPhân bố nhãn:")
    print(df["label"].value_counts(normalize=True))
