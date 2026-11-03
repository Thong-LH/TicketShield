#!/usr/bin/env python3
"""
Benchmark Baseline Passthrough Latency: Direct vs YARP AntiBot Gateway
So sánh độ trễ giữa:
- Direct MockOrganizer (http://localhost:5001)
- Passthrough qua TicketShield.AntiBot.Gateway (http://localhost:5006)
Tính toán overhead (delta p50, p95, p99, mean) để chứng minh YARP overhead < 1ms.
"""

import asyncio
import time
import json
import statistics
from pathlib import Path
import httpx

DIRECT_BASE_URL = "http://localhost:5001"
GATEWAY_BASE_URL = "http://localhost:5006"
CONCURRENCY = 10
NUM_REQUESTS = 300
WARMUP_REQUESTS = 20

async def measure_request(client: httpx.AsyncClient, method: str, url: str, json_data: dict = None) -> float:
    t0 = time.perf_counter()
    if method == "GET":
        resp = await client.get(url)
    else:
        resp = await client.post(url, json=json_data)
    t1 = time.perf_counter()
    if resp.status_code >= 500:
        print(f"[WARN] Status {resp.status_code} for {url}")
    return (t1 - t0) * 1000.0  # ms

async def run_worker(client: httpx.AsyncClient, queue: asyncio.Queue, results: list, method: str, path: str, payload: dict = None):
    while not queue.empty():
        base_url = await queue.get()
        url = f"{base_url}{path}"
        try:
            latency = await measure_request(client, method, url, payload)
            results.append(latency)
        except Exception as e:
            print(f"[ERR] Request to {url} failed: {e}")
        finally:
            queue.task_done()

async def benchmark_endpoint(base_url: str, method: str, path: str, payload: dict = None, num_req: int = NUM_REQUESTS) -> list:
    # Warmup
    async with httpx.AsyncClient(timeout=10.0) as client:
        for _ in range(WARMUP_REQUESTS):
            try:
                await measure_request(client, method, f"{base_url}{path}", payload)
            except Exception:
                pass

        # Measurement
        queue = asyncio.Queue()
        for _ in range(num_req):
            queue.put_nowait(base_url)

        results = []
        tasks = [
            asyncio.create_task(run_worker(client, queue, results, method, path, payload))
            for _ in range(CONCURRENCY)
        ]
        await queue.join()
        for t in tasks:
            t.cancel()
        return results

def compute_stats(latencies: list) -> dict:
    if not latencies:
        return {}
    sorted_l = sorted(latencies)
    n = len(sorted_l)
    return {
        "count": n,
        "min_ms": round(sorted_l[0], 3),
        "median_p50_ms": round(statistics.median(sorted_l), 3),
        "p95_ms": round(sorted_l[int(n * 0.95)], 3),
        "p99_ms": round(sorted_l[min(int(n * 0.99), n - 1)], 3),
        "max_ms": round(sorted_l[-1], 3),
        "mean_ms": round(statistics.mean(sorted_l), 3),
        "stdev_ms": round(statistics.stdev(sorted_l) if n > 1 else 0.0, 3)
    }

async def main():
    print("=" * 70)
    print("TICKETSHIELD ANTI-BOT GATEWAY — BASELINE LATENCY BENCHMARK")
    print(f"Direct: {DIRECT_BASE_URL} | Gateway Passthrough: {GATEWAY_BASE_URL}")
    print(f"Requests per test: {NUM_REQUESTS} | Concurrency: {CONCURRENCY}")
    print("=" * 70)

    # 1. Health check Gateway
    async with httpx.AsyncClient(timeout=5.0) as client:
        try:
            gw_health = await client.get(f"{GATEWAY_BASE_URL}/health")
            print(f"[OK] Gateway /health: {gw_health.status_code} -> {gw_health.json()}")
        except Exception as e:
            print(f"[ERROR] Cannot reach Gateway {GATEWAY_BASE_URL}: {e}")
            return

        try:
            direct_events = await client.get(f"{DIRECT_BASE_URL}/api/organizer/events")
            print(f"[OK] Direct MockOrganizer /api/organizer/events: {direct_events.status_code}")
        except Exception as e:
            print(f"[ERROR] Cannot reach MockOrganizer {DIRECT_BASE_URL}: {e}")
            return

    # Benchmark GET /api/organizer/events
    print("\n--- 1. Benchmarking GET /api/organizer/events ---")
    print("Running Direct...")
    direct_get = await benchmark_endpoint(DIRECT_BASE_URL, "GET", "/api/organizer/events")
    print(f"Direct finished ({len(direct_get)} samples)")

    print("Running Passthrough via Gateway...")
    gw_get = await benchmark_endpoint(GATEWAY_BASE_URL, "GET", "/api/organizer/events")
    print(f"Gateway finished ({len(gw_get)} samples)")

    stats_direct_get = compute_stats(direct_get)
    stats_gw_get = compute_stats(gw_get)

    delta_get_p50 = round(stats_gw_get["median_p50_ms"] - stats_direct_get["median_p50_ms"], 3)
    delta_get_mean = round(stats_gw_get["mean_ms"] - stats_direct_get["mean_ms"], 3)
    delta_get_p95 = round(stats_gw_get["p95_ms"] - stats_direct_get["p95_ms"], 3)

    print("\n[RESULT] GET /api/organizer/events:")
    print(f"  Direct:      p50={stats_direct_get['median_p50_ms']}ms | p95={stats_direct_get['p95_ms']}ms | mean={stats_direct_get['mean_ms']}ms")
    print(f"  Gateway:     p50={stats_gw_get['median_p50_ms']}ms | p95={stats_gw_get['p95_ms']}ms | mean={stats_gw_get['mean_ms']}ms")
    print(f"  Overhead:    Delta p50={delta_get_p50}ms | Delta p95={delta_get_p95}ms | Delta mean={delta_get_mean}ms")

    # Output JSON summary
    report = {
        "timestamp": time.strftime("%Y-%m-%dT%H:%M:%S%z"),
        "concurrency": CONCURRENCY,
        "requests_per_target": NUM_REQUESTS,
        "direct_base_url": DIRECT_BASE_URL,
        "gateway_base_url": GATEWAY_BASE_URL,
        "results": {
            "get_events": {
                "direct": stats_direct_get,
                "gateway_passthrough": stats_gw_get,
                "overhead_delta": {
                    "p50_ms": delta_get_p50,
                    "p95_ms": delta_get_p95,
                    "mean_ms": delta_get_mean,
                    "overhead_under_1ms": delta_get_p50 < 1.0 and delta_get_mean < 1.0
                }
            }
        }
    }

    out_path = Path("d:/Capstone/local_docs/ai_research/baseline_passthrough_benchmark.json")
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"\n[SAVED] Benchmark report saved to: {out_path}")

if __name__ == "__main__":
    asyncio.run(main())
