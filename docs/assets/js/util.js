// Shop-local time zone. Must match public.shop_tz() in supabase/schema.sql.
export const TZ = 'Asia/Manila';
export const TZ_OFFSET = '+08:00';

// Programs that mean "nobody is actively using the PC".
export const IDLE_PROGRAMS = new Set(['Chromatic Menu', 'Unknown']);
// Left out of "top program" lists: not a meaningful program for insights.
export const NON_PROGRAMS = new Set(['Chromatic Menu', 'Unknown', 'Windows']);

export function formatProgramName(program, isOnline = true) {
  if (!program || program === 'Unknown') {
    return isOnline ? 'Idle (No app reported)' : 'Unknown';
  }
  if (program === 'Chromatic Menu') {
    return 'Idle in menu';
  }
  return program;
}

export const store = {
  get(key, fallback = null) {
    try {
      const v = localStorage.getItem('cm.' + key);
      return v === null ? fallback : v;
    } catch { return fallback; }
  },
  set(key, value) {
    try { localStorage.setItem('cm.' + key, value); } catch { /* storage unavailable */ }
  },
  remove(key) {
    try { localStorage.removeItem('cm.' + key); } catch { /* storage unavailable */ }
  }
};

// All text that came from PCs or customers goes through this before innerHTML.
export function esc(value) {
  return String(value ?? '')
    .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

export function todayStr() {
  return new Intl.DateTimeFormat('en-CA', { timeZone: TZ }).format(new Date());
}

export function addDays(day, n) {
  const d = new Date(day + 'T00:00:00Z');
  d.setUTCDate(d.getUTCDate() + n);
  return d.toISOString().slice(0, 10);
}

export function daysInclusive(from, to) {
  return Math.round((Date.parse(to + 'T00:00:00Z') - Date.parse(from + 'T00:00:00Z')) / 86400000) + 1;
}

export function dayStartMs(day) {
  return Date.parse(day + 'T00:00:00' + TZ_OFFSET);
}

export function fmtDay(day, opts = { month: 'short', day: 'numeric' }) {
  return new Intl.DateTimeFormat('en-US', { ...opts, timeZone: 'UTC' }).format(new Date(day + 'T00:00:00Z'));
}

export function fmtTime(iso) {
  return new Intl.DateTimeFormat('en-GB', { hour: '2-digit', minute: '2-digit', timeZone: TZ }).format(new Date(iso));
}

export function fmtDateTime(iso) {
  return new Intl.DateTimeFormat('en-US', {
    month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit', timeZone: TZ
  }).format(new Date(iso));
}

export function fmtMinutes(minutes) {
  const m = Math.max(0, Math.round(Number(minutes) || 0));
  if (m < 60) return `${m}m`;
  const h = Math.floor(m / 60);
  const r = m % 60;
  const hStr = h.toLocaleString('en-US');
  return r ? `${hStr}h ${r}m` : `${hStr}h`;
}

export function fmtAgo(iso) {
  const s = Math.max(0, (Date.now() - Date.parse(iso)) / 1000);
  if (s < 90) return 'just now';
  const m = Math.round(s / 60);
  if (m < 60) return `${m} min ago`;
  const h = Math.round(m / 60);
  if (h < 48) return `${h} h ago`;
  return `${Math.round(h / 24)} days ago`;
}

// Currency used for every revenue figure; chosen in Settings.
export const CURRENCIES = ['PHP', 'USD', 'EUR', 'GBP', 'IDR', 'MYR', 'SGD', 'THB', 'VND', 'INR', 'JPY', 'KRW', 'AUD', 'CAD', 'BRL', 'MXN'];

export function currency() {
  const code = store.get('currency', 'PHP');
  return CURRENCIES.includes(code) ? code : 'PHP';
}

export function currencySymbol(code = currency()) {
  try {
    const part = new Intl.NumberFormat('en-US', { style: 'currency', currency: code, currencyDisplay: 'narrowSymbol' }).formatToParts(0).find(p => p.type === 'currency');
    return part ? part.value : code;
  } catch { return code; }
}

export function fmtMoney(value, opts = {}) {
  const n = Math.max(0, Number(value) || 0);
  const defaultFrac = Number.isInteger(n) ? 0 : 2;
  const minFrac = opts.minimumFractionDigits ?? defaultFrac;
  const maxFrac = opts.maximumFractionDigits ?? defaultFrac;
  try {
    const formatted = new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: currency(),
      currencyDisplay: 'narrowSymbol',
      minimumFractionDigits: minFrac,
      maximumFractionDigits: maxFrac,
      ...opts
    }).format(n);
    return formatted
      .replace(/^([^\d\s\-+]+)(\d)/, '$1\u00A0$2')
      .replace(/(\d)([^\d\s\-+]+)$/, '$1\u00A0$2');
  } catch {
    const sym = currencySymbol();
    const num = (maxFrac > 0 ? n.toFixed(maxFrac) : Math.round(n)).toLocaleString('en-US');
    return `${sym}\u00A0${num}`;
  }
}

export function fmtPct(value) {
  return `${Math.round((Number(value) || 0) * 100)}%`;
}

// Minutes of active use that earn one unit of the chosen currency.
export function minutesPerUnit() {
  const v = Number(store.get('minutesPerUnit', '9'));
  return v > 0 ? v : 9;
}

export function moneyFromMinutes(minutes) {
  return Math.max(0, Number(minutes) || 0) / minutesPerUnit();
}

export function rateText() {
  return `${fmtMoney(1)} per ${minutesPerUnit()} active min`;
}

export function cssVar(name) {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
}

// Categorical colors for PCs in stacked charts; fixed order keeps a PC's color stable.
const SERIES = ['#0ea5e9', '#f59e0b', '#10b981', '#8b5cf6', '#ef4444', '#14b8a6', '#ec4899', '#84cc16', '#6366f1', '#f97316'];
export function seriesColor(index) {
  return SERIES[index % SERIES.length];
}

export function groupBy(rows, key) {
  const map = new Map();
  for (const row of rows) {
    const k = typeof key === 'function' ? key(row) : row[key];
    if (!map.has(k)) map.set(k, []);
    map.get(k).push(row);
  }
  return map;
}

export function sum(rows, key) {
  return rows.reduce((acc, r) => acc + (Number(typeof key === 'function' ? key(r) : r[key]) || 0), 0);
}

// Global registry of PC name -> Shop (menu_name) mapping
export const pcShopMap = new Map();

export function recordPcShops(rows) {
  if (!rows || !Array.isArray(rows)) return;
  for (const r of rows) {
    if (r && r.pc_name && r.menu_name) {
      pcShopMap.set(r.pc_name, r.menu_name);
    }
  }
}

export function getShopForPc(pcName) {
  return pcShopMap.get(pcName) || '';
}

export function getAllShops() {
  return [...new Set([...pcShopMap.values()].filter(Boolean))];
}

