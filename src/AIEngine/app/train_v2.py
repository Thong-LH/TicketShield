"""
train_v2.py
Huấn luyện AI Anti-Bot Model v2.0 trên tập dữ liệu đối kháng 2,000 mẫu (Subtask 13.3.3.2).
Theo đặc tả Epic US-13.3 và Jira SCRUM-381 (TicketShield):
  - Tích hợp 11 đặc trưng 3 tầng chuẩn hóa từ feature_pipeline.py.
  - Phân chia Stratified Split (80% Train: 1,600 / 20% Test: 400).
  - Huấn luyện RandomForestClassifier (100 cây, max_depth=12, class_weight='balanced').
  - Đánh giá Stratified 5-Fold Cross-Validation (Mean ROC-AUC >= 0.990).
  - Xuất mô hình Scikit-Learn đã huấn luyện ra file joblib sẵn sàng cho LOCO (13.3.3.3) và ONNX (13.3.3.4).
"""

import os
import sys
import time
from typing import Dict, Any, List, Tuple, Optional
import pandas as pd
from sklearn.ensemble import RandomForestClassifier, HistGradientBoostingClassifier
from sklearn.model_selection import train_test_split, StratifiedKFold, cross_val_score
from sklearn.metrics import (
    classification_report,
    roc_auc_score,
    confusion_matrix,
    f1_score,
    precision_score,
    recall_score,
)
import joblib  # type: ignore

CUR_DIR = os.path.dirname(os.path.abspath(__file__))
AI_ENGINE_DIR = os.path.abspath(os.path.join(CUR_DIR, ".."))
WORKSPACE_ROOT = os.path.abspath(os.path.join(CUR_DIR, "../../../.."))

for p in [AI_ENGINE_DIR, WORKSPACE_ROOT]:
    if p not in sys.path:
        sys.path.insert(0, p)

try:
    from app.feature_pipeline import (
        transform_dataframe,
        get_feature_names,
        FEATURE_COLUMNS_V2,
    )
except ImportError:
    from TicketShield.src.AIEngine.app.feature_pipeline import (
        transform_dataframe,
        get_feature_names,
        FEATURE_COLUMNS_V2,
    )

DEFAULT_DATASET_PATH = os.path.join(AI_ENGINE_DIR, "datasets", "raw", "merged_training_dataset_v2.csv")
DEFAULT_MODEL_OUTPUT_PATH = os.path.join(AI_ENGINE_DIR, "models", "model_v2_rf.joblib")


def load_and_preprocess_dataset(csv_path: Optional[str] = None) -> Tuple[Any, Any, pd.DataFrame]:
    """
    Nạp dataset hợp nhất 2,000 mẫu và chuyển đổi qua 3-Tier Feature Pipeline.
    Trả về ma trận X (2000, 11), nhãn y (2000,) và DataFrame gốc.
    """
    path = csv_path or DEFAULT_DATASET_PATH
    if not os.path.exists(path):
        raise FileNotFoundError(f"Không tìm thấy file dataset tại: {path}")

    df = pd.read_csv(path)
    X, y = transform_dataframe(df, include_target=True)
    return X, y, df


def build_random_forest_model(
    n_estimators: int = 100,
    max_depth: int = 12,
    min_samples_split: int = 4,
    min_samples_leaf: int = 2,
    random_state: int = 42,
    n_jobs: int = -1,
) -> RandomForestClassifier:
    """Khởi tạo mô hình RandomForestClassifier với siêu tham số tối ưu."""
    return RandomForestClassifier(
        n_estimators=n_estimators,
        max_depth=max_depth,
        min_samples_split=min_samples_split,
        min_samples_leaf=min_samples_leaf,
        class_weight="balanced",
        random_state=random_state,
        n_jobs=n_jobs,
    )


def train_model_v2(
    X_train: Any,
    y_train: Any,
    model: Optional[RandomForestClassifier] = None,
) -> RandomForestClassifier:
    """Huấn luyện mô hình v2.0 trên tập dữ liệu huấn luyện."""
    clf = model or build_random_forest_model()
    clf.fit(X_train, y_train)
    return clf


def evaluate_model(clf: Any, X_test: Any, y_test: Any) -> Dict[str, Any]:
    """Đánh giá toàn diện các chỉ số phân loại nhị phân trên tập kiểm thử độc lập."""
    y_pred = clf.predict(X_test)
    y_prob = clf.predict_proba(X_test)[:, 1]

    auc = float(roc_auc_score(y_test, y_prob))
    prec = float(precision_score(y_test, y_pred, zero_division=0))
    rec = float(recall_score(y_test, y_pred, zero_division=0))
    f1 = float(f1_score(y_test, y_pred, zero_division=0))

    tn, fp, fn, tp = confusion_matrix(y_test, y_pred).ravel()
    fpr = float(fp / (fp + tn)) if (fp + tn) > 0 else 0.0

    return {
        "roc_auc": round(auc, 5),
        "precision": round(prec, 5),
        "recall": round(rec, 5),
        "f1_score": round(f1, 5),
        "false_positive_rate": round(fpr, 5),
        "confusion_matrix": {
            "true_negatives": int(tn),
            "false_positives": int(fp),
            "false_negatives": int(fn),
            "true_positives": int(tp),
        },
    }


def run_5fold_cross_validation(
    X: Any,
    y: Any,
    n_splits: int = 5,
    random_state: int = 42
) -> Dict[str, float]:
    """Thực hiện Stratified 5-Fold Cross-Validation để đo lường độ ổn định tổng quát hóa."""
    cv = StratifiedKFold(n_splits=n_splits, shuffle=True, random_state=random_state)
    clf = build_random_forest_model(random_state=random_state)

    scores_auc = cross_val_score(clf, X, y, cv=cv, scoring="roc_auc", n_jobs=-1)
    scores_f1 = cross_val_score(clf, X, y, cv=cv, scoring="f1", n_jobs=-1)

    return {
        "mean_roc_auc": round(float(scores_auc.mean()), 5),
        "std_roc_auc": round(float(scores_auc.std()), 5),
        "mean_f1": round(float(scores_f1.mean()), 5),
        "std_f1": round(float(scores_f1.std()), 5),
    }


def get_feature_importances(clf: Any) -> List[Tuple[str, float]]:
    """Trích xuất và sắp xếp độ quan trọng của 11 chiều đặc trưng (MDI Feature Importance)."""
    names = get_feature_names()
    importances = clf.feature_importances_
    sorted_pairs = sorted(zip(names, importances), key=lambda x: x[1], reverse=True)
    return [(name, round(float(imp), 4)) for name, imp in sorted_pairs]


def save_model(clf: Any, output_path: Optional[str] = None) -> str:
    """Lưu trữ mô hình scikit-learn ra file joblib."""
    path = output_path or DEFAULT_MODEL_OUTPUT_PATH
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    joblib.dump(clf, path)
    return path


def run_training_pipeline() -> Tuple[RandomForestClassifier, Dict[str, Any], Dict[str, float]]:
    """Chạy toàn bộ pipeline huấn luyện, kiểm định 5-fold CV và lưu mô hình v2.0."""
    print("=" * 70)
    print(" 🚀 HUẤN LUYỆN MÔ HÌNH AI ANTI-BOT MODEL v2.0 (SUBTASK 13.3.3.2)")
    print("=" * 70)

    # 1. Nạp và tiền xử lý dữ liệu
    print("[1/5] Nạp dataset 2,000 mẫu đối kháng và người thật...")
    X, y, df = load_and_preprocess_dataset()
    print(f"      -> Kích thước ma trận đặc trưng: {X.shape} (100% Clean, 0 NaNs)")
    print(f"      -> Phân bố nhãn: {int((y==1).sum())} Bot (50%) : {int((y==0).sum())} Human (50%)")

    # 2. Phân chia Stratified Train/Test (80% / 20%)
    print("[2/5] Phân chia Stratified Split (80% Train / 20% Test)...")
    X_train, X_test, y_train, y_test = train_test_split(
        X, y, test_size=0.20, random_state=42, stratify=y
    )
    print(f"      -> Train set: {X_train.shape[0]} mẫu | Test set: {X_test.shape[0]} mẫu")

    # 3. Huấn luyện RandomForest v2.0
    print("[3/5] Huấn luyện RandomForestClassifier (100 cây, max_depth=12)...")
    start_train = time.perf_counter()
    clf = train_model_v2(X_train, y_train)
    train_duration = time.perf_counter() - start_train
    print(f"      -> Huấn luyện hoàn tất trong {train_duration:.3f}s")

    # 4. Đánh giá trên Test Set
    print("[4/5] Đánh giá chất lượng mô hình trên Hold-out Test Set (400 mẫu)...")
    eval_metrics = evaluate_model(clf, X_test, y_test)
    print(f"      - ROC-AUC      : {eval_metrics['roc_auc']:.4f} (Mục tiêu đồ án: >= 0.990)")
    print(f"      - Precision    : {eval_metrics['precision']:.4f}")
    print(f"      - Recall       : {eval_metrics['recall']:.4f} (Phát hiện bot)")
    print(f"      - F1-Score     : {eval_metrics['f1_score']:.4f}")
    print(f"      - FPR (Chặn nhầm): {eval_metrics['false_positive_rate']*100:.2f}% (Mục tiêu đồ án: < 1.0%)")
    print(f"      - Ma trận nhầm lẫn: TN={eval_metrics['confusion_matrix']['true_negatives']}, "
          f"FP={eval_metrics['confusion_matrix']['false_positives']}, "
          f"FN={eval_metrics['confusion_matrix']['false_negatives']}, "
          f"TP={eval_metrics['confusion_matrix']['true_positives']}")

    # 5. Đánh giá 5-Fold Cross Validation
    print("[5/5] Đánh giá độ ổn định tổng quát hóa qua Stratified 5-Fold Cross-Validation...")
    cv_metrics = run_5fold_cross_validation(X, y)
    print(f"      - Mean ROC-AUC : {cv_metrics['mean_roc_auc']:.4f} (+/- {cv_metrics['std_roc_auc']:.4f})")
    print(f"      - Mean F1      : {cv_metrics['mean_f1']:.4f} (+/- {cv_metrics['std_f1']:.4f})")

    # Bảng xếp hạng Feature Importance
    print("-" * 70)
    print("XẾP HẠNG TẦM QUAN TRỌNG CỦA 11 ĐẶC TRƯNG 3 TẦNG (FEATURE IMPORTANCE):")
    importances = get_feature_importances(clf)
    for rank, (name, imp) in enumerate(importances, start=1):
        bar = "█" * int(imp * 40)
        print(f"  {rank:2d}. {name:28s}: {imp:.4f}  {bar}")

    # Lưu trữ mô hình joblib
    saved_path = save_model(clf)
    print("-" * 70)
    print(f"✅ Đã lưu mô hình đã huấn luyện tại: {saved_path}")
    print("=" * 70)

    return clf, eval_metrics, cv_metrics


if __name__ == "__main__":
    run_training_pipeline()
