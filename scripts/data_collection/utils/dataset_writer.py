"""
dataset_writer.py
Module tiện ích ghi dữ liệu đối kháng (Dataset Writer) theo lô (batch streaming),
hỗ trợ xuất ra CSV, JSONL và Parquet chuẩn hóa cho pipeline huấn luyện mô hình v2.0 (ML-13.3.3).
"""

import os
import csv
import json
import threading
from typing import List, Dict, Any, Optional
import pandas as pd

from scripts.data_collection.models.schema import DatasetRecord, DATASET_COLUMNS

class DatasetWriter:
    """
    Writer chuyên dụng cho việc thu thập dữ liệu bot và người thật.
    Hỗ trợ auto-flush, thread-safety và xuất đa định dạng.
    """
    def __init__(
        self,
        output_csv_path: str,
        output_parquet_path: Optional[str] = None,
        batch_size: int = 50,
        append: bool = True
    ):
        self.output_csv_path = os.path.abspath(output_csv_path)
        self.output_parquet_path = os.path.abspath(output_parquet_path) if output_parquet_path else None
        self.batch_size = batch_size
        self.append = append
        self._lock = threading.Lock()
        self._buffer: List[DatasetRecord] = []
        self._total_written = 0

        # Đảm bảo thư mục cha tồn tại
        os.makedirs(os.path.dirname(self.output_csv_path), exist_ok=True)
        if self.output_parquet_path:
            os.makedirs(os.path.dirname(self.output_parquet_path), exist_ok=True)

        self._init_csv()

    def _init_csv(self):
        """Khởi tạo file CSV với header chuẩn nếu file chưa tồn tại hoặc append=False."""
        with self._lock:
            file_exists = os.path.exists(self.output_csv_path) and os.path.getsize(self.output_csv_path) > 0
            if not file_exists or not self.append:
                with open(self.output_csv_path, mode="w", newline="", encoding="utf-8") as f:
                    writer = csv.DictWriter(f, fieldnames=DATASET_COLUMNS)
                    writer.writeheader()

    def add_record(self, record: DatasetRecord):
        """Thêm một bản ghi vào buffer. Tự động flush khi buffer đầy."""
        with self._lock:
            self._buffer.append(record)
            if len(self._buffer) >= self.batch_size:
                self._flush_unlocked()

    def add_records(self, records: List[DatasetRecord]):
        """Thêm một danh sách bản ghi vào buffer."""
        with self._lock:
            self._buffer.extend(records)
            if len(self._buffer) >= self.batch_size:
                self._flush_unlocked()

    def flush(self):
        """Ép xả toàn bộ buffer vào file."""
        with self._lock:
            self._flush_unlocked()

    def _flush_unlocked(self):
        """Xả buffer không lock (gọi bên trong block with self._lock)."""
        if not self._buffer:
            return

        rows = [r.to_dict() for r in self._buffer]
        with open(self.output_csv_path, mode="a", newline="", encoding="utf-8") as f:
            writer = csv.DictWriter(f, fieldnames=DATASET_COLUMNS)
            writer.writerows(rows)
            f.flush()

        self._total_written += len(self._buffer)
        self._buffer.clear()

    def export_parquet(self) -> bool:
        """Đọc file CSV và xuất ra định dạng Parquet để nạp nhanh vào Polars/Pandas."""
        if not self.output_parquet_path:
            return False

        self.flush()

        with self._lock:
            try:
                if not os.path.exists(self.output_csv_path):
                    return False

                df = pd.read_csv(self.output_csv_path)
                try:
                    df.to_parquet(self.output_parquet_path, index=False, engine="pyarrow")
                    print(f"[DatasetWriter] Exported {len(df)} records to Parquet: {self.output_parquet_path}")
                    return True
                except Exception as ex_engine:
                    # Nếu pyarrow chưa cài đặt, thông báo cảnh báo mà không làm gián đoạn pipeline CSV
                    print(f"[DatasetWriter] Notice: Parquet engine not available ({ex_engine}). CSV is fully saved at: {self.output_csv_path}")
                    return False
            except Exception as ex:
                print(f"[DatasetWriter] Error exporting parquet: {ex}")
                return False

    def close(self):
        """Đóng writer, xả buffer còn lại và xuất parquet nếu có cấu hình."""
        self.flush()
        if self.output_parquet_path:
            self.export_parquet()

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc_val, exc_tb):
        self.close()

    @property
    def total_written(self) -> int:
        return self._total_written + len(self._buffer)
