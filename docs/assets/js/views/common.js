import { Chart, registerables } from 'https://cdn.jsdelivr.net/npm/chart.js@4.4.4/+esm';
import { icon } from '../icons.js';
import { esc, cssVar, getAllShops, getShopForPc } from '../util.js';

Chart.register(...registerables);

const charts = [];

export function destroyCharts() {
  while (charts.length) charts.pop().destroy();
}

export function makeChart(canvas, config) {
  // Charts whose canvas was replaced by a refresh are no longer needed.
  for (let i = charts.length - 1; i >= 0; i--) {
    if (!charts[i].canvas.isConnected) charts.splice(i, 1)[0].destroy();
  }
  Chart.defaults.font.family = getComputedStyle(document.body).fontFamily;
  Chart.defaults.font.size = 12;
  Chart.defaults.color = cssVar('--text-3');
  config.options = config.options || {};
  config.options.maintainAspectRatio = false;
  config.options.animation = { duration: 150 };
  config.options.interaction = config.options.interaction || { mode: 'index', intersect: false };
  const tooltip = config.options.plugins?.tooltip || {};
  config.options.plugins = {
    ...(config.options.plugins || {}),
    tooltip: {
      backgroundColor: cssVar('--text'), titleColor: cssVar('--bg'), bodyColor: cssVar('--bg'),
      padding: 10, cornerRadius: 8, boxPadding: 4, ...tooltip
    }
  };
  for (const axis of Object.values(config.options.scales || {})) {
    axis.grid = { color: cssVar('--border'), drawTicks: false, ...(axis.grid || {}) };
    axis.border = { display: false };
    axis.ticks = { padding: 10, ...(axis.ticks || {}) };
  }
  const chart = new Chart(canvas, config);
  charts.push(chart);
  return chart;
}

// '#4ea1ff' + 0.45 -> 'rgba(78, 161, 255, 0.45)' (canvas needs a plain color).
export function withAlpha(hex, alpha) {
  const m = /^#?([0-9a-f]{6})$/i.exec(hex.trim());
  if (!m) return hex;
  const n = parseInt(m[1], 16);
  return `rgba(${(n >> 16) & 255}, ${(n >> 8) & 255}, ${n & 255}, ${alpha})`;
}

export function errorBox(error) {
  return `<div class="alert error">${icon('alert-triangle')}<div>${esc(error.message || String(error))}</div></div>`;
}

export function empty(text, iconName = 'info') {
  return `<div class="empty">${icon(iconName)}<div>${esc(text)}</div></div>`;
}

// Grey placeholder blocks shown while a page loads.
export function loading() {
  return `<div class="skeleton">
    <div class="kpis">${'<div class="card sk-block" style="height:112px"></div>'.repeat(4)}</div>
    <div class="card sk-block" style="height:260px;margin-top:16px"></div>
    <div class="card sk-block" style="height:180px;margin-top:16px"></div>
  </div>`;
}

export function delta(change, suffix = '') {
  if (change === null || change === undefined || !isFinite(change)) return '';
  const pct = Math.round(change * 100);
  const cls = pct > 0 ? 'up' : pct < 0 ? 'down' : 'flat';
  const ic = pct >= 0 ? 'trending-up' : 'trending-down';
  return `<span class="delta ${cls}">${icon(ic)}${pct > 0 ? '+' : ''}${pct}%</span>${suffix ? `<span>${esc(suffix)}</span>` : ''}`;
}

// One headline number. `tone` = 'primary' highlights the most important tile.
export function kpi({ label, value, foot = '', iconName = null, tone = '' }) {
  return `<div class="card kpi ${tone}">
    <div class="kpi-label">${iconName ? icon(iconName) : ''}<span>${esc(label)}</span></div>
    <div class="kpi-value">${value}</div>
    ${foot ? `<div class="kpi-foot">${foot}</div>` : ''}
  </div>`;
}

export function meter(active, on) {
  const pct = on > 0 ? Math.min(100, (active / on) * 100) : 0;
  return `<div class="meter" role="img" aria-label="${Math.round(pct)}% active"><i style="width:${pct}%"></i><i class="idle" style="width:${on > 0 ? 100 - pct : 0}%"></i></div>`;
}

export function card({ title, meta = '', actions = '', body, flush = false, cls = '' }) {
  return `<section class="card ${cls}">
    <div class="card-head">
      <div><h2>${title}</h2>${meta ? `<span class="meta">${meta}</span>` : ''}</div>
      ${actions}
    </div>
    ${flush ? body : `<div class="card-body">${body}</div>`}
  </section>`;
}

// Shop label next to a PC name, only when more than one shop reports in.
export function shopTag(pc, shop = '') {
  const name = shop || getShopForPc(pc);
  if (getAllShops().length < 2 || !name) return '';
  return `<span class="shop-tag" title="Shop: ${esc(name)}">${icon('store')}<span>${esc(name)}</span></span>`;
}

// Tooltip shared by timeline bars and heatmap cells. Hover on desktop, tap on touch.
let tooltipEl = null;
export function bindTooltips(root) {
  if (!tooltipEl) {
    tooltipEl = document.createElement('div');
    tooltipEl.className = 'tooltip hidden';
    tooltipEl.setAttribute('role', 'tooltip');
    document.body.appendChild(tooltipEl);
    window.addEventListener('scroll', hideTooltip, { passive: true });
  }
  const show = (target, x, y) => {
    tooltipEl.textContent = target.dataset.tip;
    tooltipEl.classList.remove('hidden');
    const left = Math.max(8, Math.min(x + 14, window.innerWidth - tooltipEl.offsetWidth - 8));
    const top = y + 14 + tooltipEl.offsetHeight > window.innerHeight ? y - tooltipEl.offsetHeight - 10 : y + 14;
    tooltipEl.style.left = left + 'px';
    tooltipEl.style.top = top + 'px';
  };
  root.addEventListener('mousemove', (e) => {
    const target = e.target.closest('[data-tip]');
    if (!target) { hideTooltip(); return; }
    show(target, e.clientX, e.clientY);
  });
  root.addEventListener('mouseleave', hideTooltip);
  root.addEventListener('click', (e) => {
    if (!window.matchMedia('(hover: none)').matches) return;
    const target = e.target.closest('[data-tip]');
    if (!target) { hideTooltip(); return; }
    const r = target.getBoundingClientRect();
    show(target, Math.min(e.clientX || r.left, window.innerWidth - 20), r.bottom);
  });
}

export function hideTooltip() {
  if (tooltipEl) tooltipEl.classList.add('hidden');
}
