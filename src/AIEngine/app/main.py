"""
main.py
Huấn luyện mô hình Random Forest phát hiện Bot và xuất file ONNX chuẩn hóa cho C# .NET 8 ONNX Runtime.
Theo đặc tả AI-CORE-8.3.1 (TicketShield) bao gồm:
  - Huấn luyện 100 cây trên tập synthetic data có vùng xám (Grey Zone).
  - Đánh giá nghiêm ngặt bằng phương pháp Leave-One-Cohort-Out (giữ lại PowerUser để đo FPR).
  - Xuất mô hình chuẩn ONNX phục vụ nhúng trực tiếp In-Process C#.
"""

import os
import sys
import numpy as np
import pandas as pd
from sklearn.ensemble import RandomForestClassifier
from sklearn.model_selection import train_test_split
from sklearn.metrics import classification_report, roc_auc_score, confusion_matrix
from skl2onnx import convert_sklearn
from skl2onnx.common.data_types import FloatTensorType
import onnxruntime as ort

from app.synthetic_data_generator import (
    generate_dataset,
    generate_standard_user,
    generate_power_user,
    generate_simple_bot,
    generate_stealth_bot,
    FEATURE_COLUMNS,
)

def evaluate_leave_one_cohort_out(n_estimators: int = 100, max_depth: int = 12, random_state: int = 42):
    """
    Đánh giá Leave-One-Cohort-Out (Vòng 3 Phản biện Hội đồng):
    Huấn luyện mô hình mà HOÀN TOÀN KHÔNG cho thấy nhóm PowerUser (người dùng tay nhanh / game thủ).
    Sau đó kiểm thử trực tiếp trên nhóm PowerUser để đo chính xác tỷ lệ chặn nhầm (False Positive Rate - FPR).
    Mục tiêu khoa học: FPR < 2% (đáp ứng mục 6.1 của đề cương nghiên cứu).
    """
    print("\n" + "-" * 60)
    print(" ĐÁNH GIÁ KHOA HỌC: LEAVE-ONE-COHORT-OUT (LOCO)")
    print("-" * 60)

    # 1. Tập Train: Chỉ chứa StandardUser, SimpleBot, StealthBot (KHÔNG CÓ PowerUser)
    df_std = generate_standard_user(n=2500, seed=random_state)
    df_smp = generate_simple_bot(n=2500, seed=random_state + 1)
    df_stl = generate_stealth_bot(n=1500, seed=random_state + 2)

    df_train = pd.concat([df_std, df_smp, df_stl], ignore_index=True)
    X_train = df_train[FEATURE_COLUMNS].values.astype(np.float32)
    y_train = df_train["label"].values.astype(int)

    # 2. Tập Test Hold-Out: Chỉ chứa PowerUser (Người thật nhãn 0, thao tác cực nhanh)
    df_power = generate_power_user(n=1000, seed=random_state + 3)
    X_power = df_power[FEATURE_COLUMNS].values.astype(np.float32)
    y_power = df_power["label"].values.astype(int)

    clf_loco = RandomForestClassifier(
        n_estimators=n_estimators,
        max_depth=max_depth,
        min_samples_split=5,
        min_samples_leaf=2,
        class_weight="balanced",
        random_state=random_state,
        n_jobs=-1,
    )
    clf_loco.fit(X_train, y_train)

    # Dự đoán trên nhóm PowerUser chưa từng thấy trong lúc học
    probs_power = clf_loco.predict_proba(X_power)[:, 1]

    # Tính False Positive Rate tại ngưỡng BLOCK (>= 0.75) và CHALLENGE (>= 0.35)
    falsely_blocked = np.sum(probs_power >= 0.75)
    fpr_block = (falsely_blocked / len(df_power)) * 100

    falsely_challenged = np.sum((probs_power >= 0.35) & (probs_power < 0.75))
    challenge_rate = (falsely_challenged / len(df_power)) * 100

    correctly_allowed = np.sum(probs_power < 0.35)
    allow_rate = (correctly_allowed / len(df_power)) * 100

    print(f"Tổng số mẫu PowerUser kiểm thử độc lập : {len(df_power)}")
    print(f" - Cho phép mượt mà (ALLOW < 0.35)      : {correctly_allowed}/{len(df_power)} ({allow_rate:.2f}%)")
    print(f" - Yêu cầu xác thực (CHALLENGE 0.35-0.75): {falsely_challenged}/{len(df_power)} ({challenge_rate:.2f}%)")
    print(f" - Bị chặn nhầm    (BLOCK >= 0.75)       : {falsely_blocked}/{len(df_power)} ({fpr_block:.2f}%)")
    print(f"\n=> TỶ LỆ CHẶN NHẦM (FPR tại ngưỡng BLOCK): {fpr_block:.2f}% (Mục tiêu đồ án: < 2.0%)")
    if fpr_block < 2.0:
        print(" [ĐẠT] Mô hình vượt qua kiểm định Leave-One-Cohort-Out, không chặn nhầm Power-User!")
    else:
        print(" [CẢNH BÁO] FPR vượt ngưỡng 2.0%!")
    print("-" * 60 + "\n")

def train_and_export_onnx(
    output_onnx_path: str = "models/anti_bot_model.onnx",
    n_estimators: int = 100,
    max_depth: int = 12,
    random_state: int = 42,
):
    print("=" * 60)
    print(" BẮT ĐẦU HUẤN LUYỆN ANTI-BOT MODEL (AI-CORE-8.3.1)")
    print("=" * 60)

    # 1. Đánh giá Leave-One-Cohort-Out trước khi huấn luyện toàn bộ
    evaluate_leave_one_cohort_out(n_estimators=n_estimators, max_depth=max_depth, random_state=random_state)

    # 2. Sinh tập dữ liệu tổng hợp đầy đủ 4 cohort (Standard, PowerUser, SimpleBot, StealthBot)
    print("[1/5] Sinh tập dữ liệu tổng hợp đầy đủ 4 cohorts (Tổng 7,500 mẫu)...")
    df = generate_dataset(
        n_standard=2500,
        n_power=1000,
        n_simple=2500,
        n_stealth=1500,
        seed=random_state
    )
    X = df[FEATURE_COLUMNS].values.astype(np.float32)
    y = df["label"].values.astype(int)

    X_train, X_test, y_train, y_test = train_test_split(
        X, y, test_size=0.20, random_state=random_state, stratify=y
    )
    print(f"      Train set: {X_train.shape[0]} mẫu | Test set: {X_test.shape[0]} mẫu")

    # 3. Huấn luyện Random Forest 100 cây
    print(f"[2/5] Huấn luyện RandomForestClassifier(n_estimators={n_estimators}, max_depth={max_depth})...")
    clf = RandomForestClassifier(
        n_estimators=n_estimators,
        max_depth=max_depth,
        min_samples_split=5,
        min_samples_leaf=2,
        class_weight="balanced",
        random_state=random_state,
        n_jobs=-1,
    )
    clf.fit(X_train, y_train)

    # 4. Đánh giá chất lượng mô hình trên tập Test
    print("[3/5] Đánh giá mô hình trên tập Test:")
    y_pred = clf.predict(X_test)
    y_prob = clf.predict_proba(X_test)[:, 1]

    auc = roc_auc_score(y_test, y_prob)
    print(f"      ROC-AUC Score: {auc:.4f}")
    print("\nClassification Report:")
    print(classification_report(y_test, y_pred, target_names=["Human (0)", "Bot (1)"]))

    cm = confusion_matrix(y_test, y_pred)
    print(f"      Confusion Matrix:\n{cm}\n")

    print("Feature Importances:")
    for col, imp in zip(FEATURE_COLUMNS, clf.feature_importances_):
        print(f"  - {col:25s}: {imp:.4f}")

    # 5. Xuất file ONNX
    print(f"\n[4/5] Chuyển đổi mô hình sang ONNX format (opset=17, zipmap=False)...")
    os.makedirs(os.path.dirname(output_onnx_path), exist_ok=True)

    initial_type = [("float_input", FloatTensorType([None, 6]))]

    onnx_model = convert_sklearn(
        clf,
        initial_types=initial_type,
        options={id(clf): {"zipmap": False}},
        target_opset=17,
    )

    with open(output_onnx_path, "wb") as f:
        f.write(onnx_model.SerializeToString())
    print(f"      Đã lưu mô hình ONNX thành công: {output_onnx_path} ({os.path.getsize(output_onnx_path)} bytes)")

    # 6. Kiểm tra suy luận với ONNX Runtime
    print("[5/5] Kiểm tra suy luận bằng ONNX Runtime...")
    session = ort.InferenceSession(output_onnx_path)
    input_name = session.get_inputs()[0].name

    sample_human = np.array([[190.0, 0.0, 0.85, 12.5, 1.0, 0.02]], dtype=np.float32)
    sample_power = np.array([[95.0, 0.0, 0.80, 3.0, 2.0, 0.05]], dtype=np.float32)
    sample_bot = np.array([[10.0, 1.0, 0.15, 0.8, 15.0, 0.95]], dtype=np.float32)
    sample_grey = np.array([[55.0, 0.0, 0.48, 4.0, 4.0, 0.50]], dtype=np.float32)

    samples = np.vstack([sample_human, sample_power, sample_bot, sample_grey])
    raw_outputs = session.run(None, {input_name: samples})
    bot_scores = raw_outputs[1][:, 1]

    print("\nKết quả suy luận thử nghiệm:")
    print(f"  - Người thật thường (Kỳ vọng Allow < 0.35)    : RiskScore = {bot_scores[0]:.4f}")
    print(f"  - Người thật nhanh (PowerUser - Allow/Chall) : RiskScore = {bot_scores[1]:.4f}")
    print(f"  - Bot tự động     (Kỳ vọng Block >= 0.75)   : RiskScore = {bot_scores[2]:.4f}")
    print(f"  - Grey Zone/Phe vé (Kỳ vọng Challenge)       : RiskScore = {bot_scores[3]:.4f}")

    print("\n HOÀN THÀNH HUẤN LUYỆN VÀ XUẤT ONNX MODEL (AI-CORE-8.3.1)!")
    print("=" * 60)

if __name__ == "__main__":
    train_and_export_onnx()
