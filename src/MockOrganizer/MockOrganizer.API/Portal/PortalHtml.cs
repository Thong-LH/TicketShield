namespace MockOrganizer.API.Portal;

public static class PortalHtml
{
    public const string Html = @"<!DOCTYPE html>
<html lang=""vi"">
<head>
  <meta charset=""UTF-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
  <title>Mock Organizer Operator Portal</title>
  <script src=""https://cdn.tailwindcss.com""></script>
  <link href=""https://fonts.googleapis.com/css2?family=JetBrains+Mono:wght@400;600;800&family=Inter:wght@400;500;700;900&display=swap"" rel=""stylesheet"">
  <style>
    body { font-family: 'Inter', sans-serif; background-color: #06090e; color: #e2e8f0; }
    .font-mono { font-family: 'JetBrains Mono', monospace; }
  </style>
</head>
<body class=""min-h-screen p-4 sm:p-8 selection:bg-orange-500 selection:text-white"">
  <div class=""max-w-6xl mx-auto space-y-6"">
    
    <!-- Top Header -->
    <header class=""flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-5 bg-[#0d131d] border border-white/10 rounded-2xl shadow-xl"">
      <div class=""flex items-center gap-3"">
        <div class=""w-10 h-10 rounded-xl bg-gradient-to-br from-orange-500 to-amber-600 flex items-center justify-center font-black text-white text-lg shadow-lg shadow-orange-500/20"">
          MO
        </div>
        <div>
          <div class=""flex items-center gap-2"">
            <h1 class=""text-lg font-extrabold text-white tracking-wide"">MOCK ORGANIZER PORTAL</h1>
            <span class=""flex items-center gap-1 text-[10px] font-mono px-2 py-0.5 rounded-full bg-emerald-500/20 text-emerald-400 border border-emerald-500/30"">
              <span class=""w-1.5 h-1.5 rounded-full bg-emerald-400 animate-pulse""></span> ONLINE
            </span>
          </div>
          <p class=""text-xs text-slate-400"">Cổng quản trị nội bộ cho ban tổ chức • REST :5001 / gRPC :5002</p>
        </div>
      </div>

      <div class=""flex flex-wrap items-center gap-2"">
        <button onclick=""generateTicket()"" class=""px-4 py-2 bg-orange-500 hover:bg-orange-600 text-white text-xs font-bold rounded-xl shadow-lg shadow-orange-500/20 transition-all flex items-center gap-1.5"">
          <span>+</span> Sinh Vé Random
        </button>
        <button onclick=""fetchData()"" class=""px-3 py-2 bg-white/10 hover:bg-white/15 text-white text-xs font-medium rounded-xl transition-all"">
          🔄 Làm Mới
        </button>
        <button onclick=""resetAllData()"" class=""px-3 py-2 bg-rose-500/20 hover:bg-rose-500/30 text-rose-300 border border-rose-500/30 text-xs font-medium rounded-xl transition-all"">
          ⚡ Reset Seed
        </button>
      </div>
    </header>

    <!-- LIVE OTP MONITOR -->
    <section class=""p-6 bg-gradient-to-b from-[#111927] to-[#0d131d] border border-orange-500/30 rounded-2xl shadow-2xl space-y-4"">
      <div class=""flex items-center justify-between"">
        <div class=""flex items-center gap-2"">
          <span class=""text-orange-500 text-lg"">🔑</span>
          <div>
            <h2 class=""text-sm font-bold text-white uppercase tracking-wider"">Live OTP Monitor (Real-time)</h2>
            <p class=""text-xs text-slate-400"">Mã OTP tự động xuất hiện tại đây khi có yêu cầu xác thực từ TicketShield</p>
          </div>
        </div>
        <span class=""text-[11px] text-slate-400 font-mono"">Auto-refresh: 3s</span>
      </div>

      <div id=""otp-container"" class=""grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 gap-3"">
        <div class=""p-4 bg-[#080c14] border border-white/5 rounded-xl text-center text-xs text-slate-500 animate-pulse"">
          Đang lắng nghe mã OTP...
        </div>
      </div>
    </section>

    <!-- TICKET INVENTORY TABLE -->
    <section class=""p-6 bg-[#0d131d] border border-white/10 rounded-2xl shadow-xl space-y-4"">
      <div class=""flex flex-col sm:flex-row sm:items-center justify-between gap-2"">
        <div class=""flex items-center gap-2"">
          <span class=""text-cyan-400 text-lg"">🎫</span>
          <h2 class=""text-sm font-bold text-white uppercase tracking-wider"">Kho Vé Ban Tổ Chức (Mock Tickets)</h2>
        </div>
        <div class=""text-xs text-slate-400"">
          Tổng số vé: <span id=""ticket-count"" class=""font-bold text-white font-mono"">0</span>
        </div>
      </div>

      <div class=""overflow-x-auto"">
        <table class=""w-full text-left text-xs"">
          <thead>
            <tr class=""border-b border-white/10 text-slate-400 font-mono uppercase text-[10px]"">
              <th class=""pb-3 pl-2"">Mã Vé (Ticket Code)</th>
              <th class=""pb-3"">Sự Kiện</th>
              <th class=""pb-3"">Khu Vực Ghế</th>
              <th class=""pb-3"">Giá Gốc</th>
              <th class=""pb-3"">Email Chủ Vé</th>
              <th class=""pb-3"">Trạng Thái</th>
              <th class=""pb-3 text-right pr-2"">Thao Tác</th>
            </tr>
          </thead>
          <tbody id=""ticket-tbody"" class=""divide-y divide-white/5"">
            <tr>
              <td colspan=""7"" class=""py-6 text-center text-slate-500"">Đang tải dữ liệu vé...</td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

  </div>

  <!-- Toast Notification -->
  <div id=""toast"" class=""fixed bottom-6 right-6 px-4 py-2.5 bg-slate-900 border border-white/20 text-white text-xs rounded-xl shadow-2xl opacity-0 pointer-events-none transition-all duration-200 z-50 flex items-center gap-2"">
    <span id=""toast-msg""></span>
  </div>

  <script>
    const API = '/api/organizer';

    function showToast(msg, isErr = false) {
      const el = document.getElementById('toast');
      const msgEl = document.getElementById('toast-msg');
      msgEl.textContent = msg;
      el.className = `fixed bottom-6 right-6 px-4 py-2.5 ${isErr ? 'bg-rose-950 border-rose-500 text-rose-200' : 'bg-slate-900 border-white/20 text-white'} text-xs rounded-xl shadow-2xl opacity-100 transition-all duration-200 z-50 flex items-center gap-2`;
      setTimeout(() => {
        el.className = 'fixed bottom-6 right-6 px-4 py-2.5 bg-slate-900 border border-white/20 text-white text-xs rounded-xl shadow-2xl opacity-0 pointer-events-none transition-all duration-200 z-50 flex items-center gap-2';
      }, 2500);
    }

    function copyText(text, label = 'mã') {
      navigator.clipboard.writeText(text).then(() => {
        showToast(`Đã sao chép ${label}: ${text}`);
      }).catch(() => {
        showToast('Không thể sao chép!', true);
      });
    }

    async function fetchData() {
      try {
        const [ticketsRes, otpsRes] = await Promise.all([
          fetch(`${API}/tickets`),
          fetch(`${API}/otps`)
        ]);
        if (ticketsRes.ok) renderTickets(await ticketsRes.json());
        if (otpsRes.ok) renderOtps(await otpsRes.json());
      } catch (err) {
        console.error('Fetch error', err);
      }
    }

    function renderOtps(otps) {
      const container = document.getElementById('otp-container');
      if (!otps || otps.length === 0) {
        container.innerHTML = '<div class=""col-span-full py-6 text-center text-xs text-slate-500"">Chưa có mã OTP nào được yêu cầu.</div>';
        return;
      }
      container.innerHTML = otps.slice(0, 6).map(o => {
        const created = new Date(o.createdAt).toLocaleTimeString('vi-VN');
        const isUsed = o.isUsed;
        return `
          <div class=""p-4 bg-[#080c14] border ${isUsed ? 'border-white/5 opacity-60' : 'border-orange-500/40 bg-orange-500/5 shadow-lg shadow-orange-500/5'} rounded-xl space-y-2 relative group"">
            <div class=""flex items-center justify-between text-[10px] font-mono text-slate-400"">
              <span>Mã vé: <strong class=""text-white"">${o.ticketCode}</strong></span>
              <span>${created}</span>
            </div>
            <div class=""flex items-center justify-between pt-1"">
              <span class=""text-2xl font-black font-mono tracking-widest text-orange-400"">${o.otpCode}</span>
              <button onclick=""copyText('${o.otpCode}', 'OTP')"" class=""px-3 py-1 bg-white/10 hover:bg-orange-500 text-white text-[11px] font-bold rounded-lg transition-all"">
                Copy OTP
              </button>
            </div>
            <div class=""flex items-center justify-between text-[10px] text-slate-400 pt-1"">
              <span class=""truncate max-w-[150px]"">${o.ownerEmail}</span>
              <span class=""px-1.5 py-0.5 rounded font-mono ${isUsed ? 'bg-white/5 text-slate-400' : 'bg-emerald-500/20 text-emerald-400'}"">${isUsed ? 'ĐÃ DÙNG' : 'SẴN SÀNG'}</span>
            </div>
          </div>
        `;
      }).join('');
    }

    function renderTickets(tickets) {
      document.getElementById('ticket-count').textContent = tickets.length;
      const tbody = document.getElementById('ticket-tbody');
      if (!tickets || tickets.length === 0) {
        tbody.innerHTML = '<tr><td colspan=""7"" class=""py-6 text-center text-slate-500"">Không có vé nào trong kho.</td></tr>';
        return;
      }
      tbody.innerHTML = tickets.map(t => {
        let statusBadge = 'bg-emerald-500/20 text-emerald-300 border border-emerald-500/30';
        if (t.status === 'LOCKED_FOR_RESALE') statusBadge = 'bg-purple-500/20 text-purple-300 border border-purple-500/30';
        if (t.status === 'USED') statusBadge = 'bg-slate-500/20 text-slate-400 border border-slate-500/30';

        return `
          <tr class=""hover:bg-white/[0.02] transition-colors"">
            <td class=""py-3 pl-2"">
              <div class=""flex items-center gap-1.5 font-mono font-bold text-white"">
                <span>${t.ticketCode}</span>
                <button onclick=""copyText('${t.ticketCode}', 'mã vé')"" title=""Copy mã vé"" class=""text-slate-400 hover:text-orange-400 transition-colors"">
                  📋
                </button>
              </div>
            </td>
            <td class=""py-3 text-slate-300"">${t.eventName}</td>
            <td class=""py-3 text-slate-400 font-mono text-[11px]"">${t.seatZone}</td>
            <td class=""py-3 font-mono font-bold text-orange-400"">${Number(t.originalPrice).toLocaleString('vi-VN')} đ</td>
            <td class=""py-3 text-slate-400 truncate max-w-[150px]"">${t.ownerEmail}</td>
            <td class=""py-3"">
              <span class=""px-2 py-0.5 rounded-full text-[10px] font-mono font-bold ${statusBadge}"">${t.status}</span>
            </td>
            <td class=""py-3 text-right pr-2"">
              ${t.status !== 'VALID' ? `
                <button onclick=""resetTicket('${t.ticketCode}')"" class=""px-2.5 py-1 bg-white/5 hover:bg-emerald-500/20 text-slate-300 hover:text-emerald-300 border border-white/10 hover:border-emerald-500/30 rounded-lg text-[10px] font-mono transition-all"">
                  Reset VALID
                </button>
              ` : '<span class=""text-[10px] text-slate-600 font-mono"">-</span>'}
            </td>
          </tr>
        `;
      }).join('');
    }

    async function generateTicket() {
      try {
        const res = await fetch(`${API}/tickets/generate`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({})
        });
        if (res.ok) {
          const ticket = await res.json();
          showToast(`Đã sinh vé mới: ${ticket.ticketCode}`);
          fetchData();
        } else {
          showToast('Lỗi khi sinh vé!', true);
        }
      } catch (err) {
        showToast('Không thể kết nối đến máy chủ!', true);
      }
    }

    async function resetTicket(code) {
      try {
        const res = await fetch(`${API}/tickets/${encodeURIComponent(code)}/reset`, { method: 'POST' });
        if (res.ok) {
          showToast(`Đã mở lại trạng thái VALID cho vé ${code}`);
          fetchData();
        } else {
          showToast('Lỗi khi reset vé!', true);
        }
      } catch (err) {
        showToast('Không thể kết nối đến máy chủ!', true);
      }
    }

    async function resetAllData() {
      if (!confirm('Bạn có chắc chắn muốn reset toàn bộ CSDL về 3 vé seed ban đầu?')) return;
      try {
        const res = await fetch(`${API}/reset-all`, { method: 'POST' });
        if (res.ok) {
          showToast('Đã reset CSDL về dữ liệu gốc thành công!');
          fetchData();
        }
      } catch (err) {
        showToast('Lỗi kết nối!', true);
      }
    }

    // Initial load + interval
    fetchData();
    setInterval(fetchData, 3000);
  </script>
</body>
</html>";
}
