"""
harness_server.py
Máy chủ HTTP tĩnh siêu nhẹ phục vụ trang Lightweight Telemetry Harness cho các Bot Runners
(Playwright, Puppeteer-extra-stealth, Undetected-Chromedriver).
Chạy trên http://127.0.0.1:5007 (Secure Context cho Browser WebGL & subtle crypto).
"""

import os
import sys
import threading
import time
from http.server import HTTPServer, SimpleHTTPRequestHandler

HARNESS_DIR = os.path.dirname(os.path.abspath(__file__))
DEFAULT_PORT = 5007

class HarnessHTTPRequestHandler(SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=HARNESS_DIR, **kwargs)

    def end_headers(self):
        # Bật CORS cho phép fetch qua lại giữa Gateway :5006 và Harness :5007
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "*")
        self.send_header("Cache-Control", "no-cache, no-store, must-revalidate")
        super().end_headers()

    def do_OPTIONS(self):
        self.send_response(200, "OK")
        self.end_headers()

    def log_message(self, format, *args):
        # Tắt bớt log ồn ào khi bot cào dữ liệu với tần suất cao
        if os.environ.get("HARNESS_VERBOSE") == "1":
            super().log_message(format, *args)

class HarnessServer:
    def __init__(self, host: str = "127.0.0.1", port: int = DEFAULT_PORT):
        self.host = host
        self.port = port
        self.server: HTTPServer | None = None
        self.thread: threading.Thread | None = None

    def start(self):
        """Khởi động server trên một daemon thread ngầm."""
        if self.server is not None:
            return

        self.server = HTTPServer((self.host, self.port), HarnessHTTPRequestHandler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        # Chờ 50ms cho socket bind
        time.sleep(0.05)
        print(f"[HarnessServer] Running at http://{self.host}:{self.port}/")

    def stop(self):
        """Dừng server."""
        if self.server:
            self.server.shutdown()
            self.server.server_close()
            self.server = None
            self.thread = None
            print("[HarnessServer] Stopped.")

    @property
    def url(self) -> str:
        return f"http://{self.host}:{self.port}/"

# Singleton instance phục vụ nhanh cho các script
_global_server: HarnessServer | None = None

def get_harness_url(port: int = DEFAULT_PORT) -> str:
    global _global_server
    if _global_server is None:
        _global_server = HarnessServer(port=port)
        _global_server.start()
    return _global_server.url

if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_PORT
    server = HarnessServer(port=port)
    server.start()
    print(f"Harness server started at {server.url}. Press Ctrl+C to terminate.")
    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        server.stop()
