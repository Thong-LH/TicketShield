/**
 * Unit Test Suite kiểm tra tính toàn vẹn và độ trễ của Lightweight Telemetry Harness (Subtask 13.3.1.2)
 * Chạy bằng: node scripts/data_collection/tests/test_harness.js
 */

import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const harnessHtmlPath = path.resolve(__dirname, '../harness/index.html');

console.log('[Test Harness] Đọc file:', harnessHtmlPath);
const htmlContent = fs.readFileSync(harnessHtmlPath, 'utf8');

// 1. Kiểm tra cấu trúc HTML
assert.ok(htmlContent.includes('<title>TicketShield Anti-Bot Telemetry Harness</title>'), 'HTML phải có thẻ title');
assert.ok(htmlContent.includes('window.collectTelemetry ='), 'Phải expose window.collectTelemetry');
assert.ok(htmlContent.includes('window.simulateClicks ='), 'Phải expose window.simulateClicks');
assert.ok(htmlContent.includes('window.submitCheckout ='), 'Phải expose window.submitCheckout');

// 2. Trích xuất mã JavaScript trong thẻ <script>
const scriptMatch = htmlContent.match(/<script>([\s\S]*?)<\/script>/);
assert.ok(scriptMatch, 'Không tìm thấy thẻ <script> trong harness.html');
const scriptCode = scriptMatch[1];

// 3. Giả lập môi trường Browser Context trong Node VM
const mockWindow = {
  addEventListener: () => {},
  removeEventListener: () => {},
};

const mockDocument = {
  addEventListener: () => {},
  removeEventListener: () => {},
  createElement: (tag) => {
    if (tag === 'canvas') {
      return {
        width: 180,
        height: 40,
        getContext: (type) => {
          if (type.includes('webgl')) {
            return {
              getParameter: (param) => {
                if (param === 7936) return 'WebKit';
                if (param === 7937) return 'WebKit WebGL';
                return 'Mock GPU Renderer';
              },
              getExtension: (ext) => {
                if (ext === 'WEBGL_debug_renderer_info') {
                  return {
                    UNMASKED_VENDOR_WEBGL: 37445,
                    UNMASKED_RENDERER_WEBGL: 37446,
                  };
                }
                if (ext === 'WEBGL_lose_context') {
                  return { loseContext: () => {} };
                }
                return null;
              },
            };
          }
          if (type === '2d') {
            return {
              createLinearGradient: () => ({ addColorStop: () => {} }),
              fillRect: () => {},
              fillText: () => {},
              beginPath: () => {},
              arc: () => {},
              stroke: () => {},
              getImageData: () => ({ data: new Uint8Array(180 * 40 * 4) }),
            };
          }
          return null;
        },
        toDataURL: () => 'data:image/png;base64,mockImageData123',
      };
    }
    return {};
  },
  getElementById: () => ({ textContent: '' }),
};

const mockNavigator = {
  userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36',
  webdriver: true, // Giả lập bot có cờ webdriver
  language: 'vi-VN',
  platform: 'Win32',
  hardwareConcurrency: 8,
};

const context = {
  window: mockWindow,
  document: mockDocument,
  navigator: mockNavigator,
  screen: { width: 1920, height: 1080, colorDepth: 24 },
  performance: { now: () => Date.now() },
  console,
  setTimeout,
  clearTimeout,
  Date,
  Math,
  TextEncoder,
  Uint8Array,
  Function,
  Object,
  Array,
  Intl,
  crypto: globalThis.crypto,
};

vm.createContext(context);
const startEval = performance.now();
vm.runInContext(scriptCode, context);
const evalTime = performance.now() - startEval;

console.log(`[Test Harness] Nạp và biên dịch JavaScript Harness trong ${evalTime.toFixed(3)}ms`);
assert.ok(evalTime < 100, `Thời gian nạp phải < 100ms (thực tế: ${evalTime.toFixed(3)}ms)`);

// 4. Kiểm tra hàm window.simulateClicks
const stdDev = context.window.simulateClicks(10, 15, 2);
console.log(`[Test Harness] simulateClicks(10, 15, 2) => action_interval_std_dev = ${stdDev}ms`);
assert.equal(typeof stdDev, 'number');
assert.ok(stdDev >= 0, 'Độ lệch chuẩn phải >= 0');

// 5. Kiểm tra hàm window.collectTelemetry
context.window.collectTelemetry().then((telemetry) => {
  console.log('[Test Harness] collectTelemetry() result:', JSON.stringify(telemetry, null, 2));

  // Kiểm tra đầy đủ 11 trường telemetry hợp đồng
  assert.equal(telemetry.is_webdriver, 1, 'Phải nhận diện is_webdriver = 1');
  assert.ok('virtual_gpu_detected' in telemetry, 'Phải có virtual_gpu_detected');
  assert.ok('prototype_tampered' in telemetry, 'Phải có prototype_tampered');
  assert.ok('canvas_tampered' in telemetry, 'Phải có canvas_tampered');
  assert.ok('canvas_hash' in telemetry, 'Phải có canvas_hash');
  assert.ok('webgl_renderer' in telemetry, 'Phải có webgl_renderer');
  assert.ok('webgl_vendor' in telemetry, 'Phải có webgl_vendor');
  assert.ok('device_fingerprint' in telemetry, 'Phải có device_fingerprint');
  assert.ok('nonce' in telemetry, 'Phải có nonce');
  assert.ok('client_timestamp' in telemetry, 'Phải có client_timestamp');
  assert.ok('client_user_agent' in telemetry, 'Phải có client_user_agent');
  assert.equal(telemetry.client_user_agent, mockNavigator.userAgent);

  console.log('\n✅ [SUCCESS] Toàn bộ tiêu chí nghiệm thu của Subtask 13.3.1.2 ĐẠT 100%!');
  process.exit(0);
}).catch((err) => {
  console.error('❌ [ERROR] Lỗi khi collectTelemetry:', err);
  process.exit(1);
});
