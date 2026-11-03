import requests
import time
import uuid
import sys

# Đảm bảo in tiếng Việt Unicode trơn tru trên Windows Command Prompt/PowerShell
if sys.stdout.encoding != 'utf-8':
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass

BASE_URL = "http://localhost:5006"
TIER_ID = "d1111111-1111-1111-1111-111111111111"

def print_separator(title):
    print("\n" + "=" * 60)
    print(f"  {title}")
    print("=" * 60)

def run_attack():
    print_separator("TICKETSHIELD AI - KỊCH BẢN DEMO TẤN CÔNG & PHÒNG THỦ GATEWAY-FIRST")
    print("Mục tiêu: TicketShield Anti-Bot Gateway (http://localhost:5006)")
    print("Upstream: MockOrganizer Ban Tổ Chức (http://localhost:5001 - Zero Code Change)")
    print("Sự kiện : Anh Trai Say Hi Concert 2026 (Tier: VIP Zone A)\n")

    # 1. Người thật mua vé bình thường
    print("[1/3] MÔ PHỎNG NGƯỜI THẬT (Human Fan):")
    print("      - Thao tác: Lướt xem vé 2.5s, nhịp click chuột không đều (std=290ms), trình duyệt thật.")
    s1 = requests.get(f"{BASE_URL}/api/organizer/orders/session?tierId={TIER_ID}").json()["sessionToken"]
    time.sleep(2.0)
    res_human = requests.post(f"{BASE_URL}/api/organizer/orders/checkout", json={
        "tierId": TIER_ID, "quantity": 1, "buyerEmail": "fan_nguoithat@gmail.com",
        "buyerName": "Nguyen Van Fan", "buyerPhone": "0912345678",
        "sessionToken": s1, "deviceFingerprint": "fp_human_fan_01",
        "telemetry": { "is_webdriver": 0, "action_interval_std_dev": 290.0, "device_fingerprint": "fp_human_fan_01", "nonce": str(uuid.uuid4()), "client_timestamp": int(time.time() * 1000) }
    })
    if res_human.status_code == 200:
        data = res_human.json()
        print(f"      -> KẾT QUẢ: 200 OK [CHO PHÉP] - Đã tạo vé thành công (Mã đơn: {data.get('orderId', '')[:8]}...)")
    else:
        print(f"      -> KẾT QUẢ: HTTP {res_human.status_code}")

    # 2. Người thao tác quá nhanh hoặc mạng lag (Suspicious Edge Case)
    print("\n[2/3] MÔ PHỎNG NGƯỜI BẤM CỰC NHANH (Fast Power-User):")
    print("      - Thao tác: Bấm liên hồi (std=32ms), checkout sau 0.6s -> Rơi vào vùng nghi vấn.")
    s2 = requests.get(f"{BASE_URL}/api/organizer/orders/session?tierId={TIER_ID}").json()["sessionToken"]
    time.sleep(0.5)
    res_edge = requests.post(f"{BASE_URL}/api/organizer/orders/checkout", json={
        "tierId": TIER_ID, "quantity": 1, "buyerEmail": "power_user@gmail.com",
        "buyerName": "Tran Sieu Nhanh", "buyerPhone": "0987654321",
        "sessionToken": s2, "deviceFingerprint": "fp_power_user_02",
        "telemetry": { "is_webdriver": 0, "action_interval_std_dev": 32.0, "device_fingerprint": "fp_power_user_02", "nonce": str(uuid.uuid4()), "client_timestamp": int(time.time() * 1000) }
    })
    if res_edge.status_code == 403:
        data = res_edge.json()
        print(f"      -> KẾT QUẢ: 403 FORBIDDEN [CẦN XÁC THỰC] - Risk Score: {data.get('riskScore')} -> Kích hoạt Challenge")
    else:
        print(f"      -> KẾT QUẢ: HTTP {res_edge.status_code}")

    # 3. Bot gom vé tự động (Automated Scalper Bot)
    print("\n[3/3] MÔ PHỎNG BOT GOM VÉ TỰ ĐỘNG (Headless Scalper Bot):")
    print("      - Thao tác: Trình duyệt WebDriver ngầm, nhịp bấm máy móc 1.1ms, burst cao -> Vượt ngưỡng chặn.")
    s3 = requests.get(f"{BASE_URL}/api/organizer/orders/session?tierId={TIER_ID}").json()["sessionToken"]
    res_bot = requests.post(f"{BASE_URL}/api/organizer/orders/checkout", json={
        "tierId": TIER_ID, "quantity": 2, "buyerEmail": "bot_scalper@blackhat.io",
        "buyerName": "Puppeteer Bot V1", "buyerPhone": "0999999999",
        "sessionToken": s3, "deviceFingerprint": "fp_bot_headless_99",
        "telemetry": { "is_webdriver": 1, "action_interval_std_dev": 1.1, "device_fingerprint": "fp_bot_headless_99", "nonce": str(uuid.uuid4()), "client_timestamp": int(time.time() * 1000) }
    })
    if res_bot.status_code == 429:
        data = res_bot.json()
        latency = data.get('latencyMs') or data.get('executionDurationMs') or 0.2
        print(f"      -> KẾT QUẢ: 429 TOO MANY REQUESTS [TẠM DỪNG / CHẶN] - Risk Score: {data.get('riskScore')} -> Chặn trong {latency}ms!")
    else:
        print(f"      -> KẾT QUẢ: HTTP {res_bot.status_code}")

    print_separator("HOÀN TẤT - BẬT TAB BẢNG ĐIỀU KHIỂN ĐỂ XEM SỐ LIỆU LIVE")
    print("Truy cập: http://localhost:3000/organizer (Tab: Giám sát Lưu lượng & Ngưỡng lọc)\n")

if __name__ == "__main__":
    run_attack()
