import { Chart, registerables } from 'https://cdn.jsdelivr.net/npm/chart.js@4.4.4/+esm';
import { api } from './api.js';
import { icon } from './icons.js';
import {
  esc, todayStr, addDays, daysInclusive, dayStartMs, fmtDay, fmtTime, fmtDateTime, fmtMinutes,
  fmtAgo, fmtMoney, fmtPct, minutesPerUnit, moneyFromMinutes, rateText, currency, currencySymbol, CURRENCIES, cssVar, seriesColor, groupBy, sum,
  IDLE_PROGRAMS, NON_PROGRAMS, formatProgramName, store, TZ,
  recordPcShops, getShopForPc, getAllShops
} from './util.js';

Chart.register(...registerables);

const charts = [];

export function destroyCharts() {
  while (charts.length) charts.pop().destroy();
}

function makeChart(canvas, config) {
  Chart.defaults.font.family = getComputedStyle(document.body).fontFamily;
  Chart.defaults.font.size = 12;
  Chart.defaults.color = cssVar('--text-3');
  config.options = config.options || {};
  config.options.maintainAspectRatio = false;
  config.options.animation = { duration: 150 };
  for (const axis of Object.values(config.options.scales || {})) {
    axis.grid = { color: cssVar('--border'), drawTicks: false, ...(axis.grid || {}) };
    axis.border = { display: false };
    axis.ticks = { padding: 10, ...(axis.ticks || {}) };
  }
  charts.push(new Chart(canvas, config));
}

function errorBox(error) {
  return `<div class="alert error">${icon('alert-triangle')}<div>${esc(error.message || String(error))}</div></div>`;
}

function empty(text, iconName = 'info') {
  return `<div class="empty">${icon(iconName)}${esc(text)}</div>`;
}

export function loading() {
  return '<div class="empty">Loading...</div>';
}

function delta(change, suffix) {
  if (change === null || !isFinite(change)) return '';
  const pct = Math.round(change * 100);
  const cls = pct > 0 ? 'up' : pct < 0 ? 'down' : 'flat';
  const ic = pct >= 0 ? 'trending-up' : 'trending-down';
  return `<span class="delta ${cls}">${icon(ic)}${pct > 0 ? '+' : ''}${pct}%</span><span>${esc(suffix)}</span>`;
}

function stat(label, value, meta = '', iconName = null) {
  return `<div class="stat">
    <div class="eyebrow">${iconName ? icon(iconName) : ''}${esc(label)}</div>
    <div class="stat-value">${value}</div>
    ${meta ? `<div class="meta">${meta}</div>` : ''}
  </div>`;
}

function meter(active, on) {
  const pct = on > 0 ? Math.min(100, (active / on) * 100) : 0;
  return `<div class="meter"><i style="width:${pct}%"></i><i class="idle" style="width:${on > 0 ? 100 - pct : 0}%"></i></div>`;
}

// The program with the most active time across the given daily rows.
function topProgramOf(rows) {
  const counts = new Map();
  for (const r of rows) {
    if (!r.top_program) continue;
    counts.set(r.top_program, (counts.get(r.top_program) || 0) + Number(r.minutes_active || 0));
  }
  let best = null;
  for (const [name, minutes] of counts) if (!best || minutes > best.minutes) best = { name, minutes };
  return best;
}

// ---------------------------------------------------------------------------
// Tooltip shared by the timeline and heatmap
// ---------------------------------------------------------------------------

let tooltipEl = null;
export function bindTooltips(root) {
  if (!tooltipEl) {
    tooltipEl = document.createElement('div');
    tooltipEl.className = 'tooltip hidden';
    document.body.appendChild(tooltipEl);
  }
  root.addEventListener('mousemove', (e) => {
    const target = e.target.closest('[data-tip]');
    if (!target) { tooltipEl.classList.add('hidden'); return; }
    tooltipEl.textContent = target.dataset.tip;
    tooltipEl.classList.remove('hidden');
    const x = Math.min(e.clientX + 14, window.innerWidth - tooltipEl.offsetWidth - 8);
    tooltipEl.style.left = x + 'px';
    tooltipEl.style.top = (e.clientY + 14) + 'px';
  });
  root.addEventListener('mouseleave', () => tooltipEl.classList.add('hidden'));
}

export function hideTooltip() {
  if (tooltipEl) tooltipEl.classList.add('hidden');
}

// ---------------------------------------------------------------------------
// Overview
// ---------------------------------------------------------------------------

export async function renderOverview(root) {
  const today = todayStr();
  const yesterday = addDays(today, -1);
  let status, daily, newRequests;
  try {
    [status, daily, newRequests] = await Promise.all([api.pcStatus(), api.daily(yesterday, today), api.requests('new')]);
  } catch (e) {
    root.innerHTML = errorBox(e);
    return;
  }

  recordPcShops(status);
  recordPcShops(daily);

  const todayRows = daily.filter(d => d.day === today);
  const yesterdayRows = daily.filter(d => d.day === yesterday);
  const byPc = new Map(todayRows.map(d => [d.pc_name, d]));
  const online = status.filter(s => s.is_online);
  const offline = status.filter(s => !s.is_online);
  const onTotal = sum(todayRows, 'minutes_on');
  const activeTotal = sum(todayRows, 'minutes_active');
  const revenue = moneyFromMinutes(activeTotal);
  const yesterdayRevenue = moneyFromMinutes(sum(yesterdayRows, 'minutes_active'));
  const top = topProgramOf(todayRows);

  // What needs a look, in order of importance.
  const attention = [
    ...offline.map(s => `<a class="warn" href="#timeline">${icon('power')}<b>${esc(s.pc_name)}</b> offline<span class="subtle">&middot; ${esc(fmtAgo(s.last_seen))}</span></a>`),
    newRequests.length ? `<a class="info" href="#requests">${icon('message-square')}<b>${newRequests.length}</b> new game request${newRequests.length === 1 ? '' : 's'}${icon('arrow-right')}</a>` : ''
  ].filter(Boolean);
  if (!attention.length && status.length) {
    attention.push(`<span class="chip ok">${icon('check')}All ${status.length} PCs online, nothing waiting</span>`);
  }

  const hero = `<div class="hero">
    <div class="card hero-card primary">
      <div class="top">${icon('wallet')}<span class="eyebrow">Estimated revenue today</span></div>
      <div class="hero-value">${fmtMoney(revenue)}</div>
      <div class="hero-foot"><span>Yesterday <b>${fmtMoney(yesterdayRevenue)}</b></span><span class="subtle">&middot; ${rateText()}</span></div>
    </div>
    <div class="card hero-card">
      <div class="top">${icon('monitor')}<span class="eyebrow">PCs online now</span></div>
      <div class="hero-value">${online.length}<span class="of"> / ${status.length}</span></div>
      <div class="hero-foot"><div class="pc-dots">${status.map(s => `<span class="pc-dot ${s.is_online ? 'on' : ''}" title="${esc(s.pc_name)}${s.menu_name ? ` &middot; ${esc(s.menu_name)}` : ''}"><span class="dot"></span>${esc(s.pc_name)}</span>`).join('')}</div></div>
    </div>
    <div class="card hero-card">
      <div class="top">${icon('activity')}<span class="eyebrow">Utilisation today</span></div>
      <div class="hero-value">${fmtPct(onTotal ? activeTotal / onTotal : 0)}</div>
      <div class="hero-foot" style="display:block">${meter(activeTotal, onTotal)}<div style="margin-top:8px"><b>${fmtMinutes(activeTotal)}</b> active of ${fmtMinutes(onTotal)} on</div></div>
    </div>
  </div>
  <div class="card stats">
    ${stat('On time', fmtMinutes(onTotal), 'All PCs, today', 'power')}
    ${stat('Active time', fmtMinutes(activeTotal), 'A program in use', 'zap')}
    ${stat('Idle in menu', fmtMinutes(Math.max(0, onTotal - activeTotal)), 'On, but nothing open', 'layout-grid')}
    ${stat('Top program', top ? esc(top.name) : '&mdash;', top ? `${fmtMinutes(top.minutes)} active` : 'No usage yet', 'gamepad-2')}
  </div>`;

  const pcs = status.length === 0
    ? `<div class="card">${empty('No heartbeats yet. Once a PC with telemetry set up starts, it appears here within a minute.', 'monitor')}</div>`
    : `<div class="grid pcs">${status.map(s => {
        const d = byPc.get(s.pc_name) || { minutes_on: 0, minutes_active: 0 };
        const idle = IDLE_PROGRAMS.has(s.last_program);
        const shop = s.menu_name || getShopForPc(s.pc_name) || 'PisoNet';
        const expShare = status.length ? (1 / status.length) : 0;
        const actShare = activeTotal > 0 ? (d.minutes_active / activeTotal) : 0;
        const parityRatio = expShare > 0 ? (actShare / expShare) : 1;
        let parityBadge = '';
        if (activeTotal > 0 && status.length > 2) {
          if (parityRatio > 1.4) {
            parityBadge = `<span class="badge heavy" title="High player workload today (+${Math.round((parityRatio - 1) * 100)}% vs avg)">Heavy</span>`;
          } else if (parityRatio < 0.6) {
            parityBadge = `<span class="badge neglected" title="Low player workload today (-${Math.round((1 - parityRatio) * 100)}% vs avg)">Low</span>`;
          }
        }
        return `<div class="card pc ${s.is_online ? 'on' : 'off'}">
          <div class="pc-top">
            <div class="pc-identity">
              <span class="name" title="${esc(s.pc_name)}">${esc(s.pc_name)}</span>
              <span class="pc-shop" title="Shop: ${esc(shop)}">${icon('store')}<span>${esc(shop)}</span></span>
            </div>
            <div style="display:flex;align-items:center;gap:4px">
              ${parityBadge}
              <span class="badge ${s.is_online ? 'online' : 'offline'}"><span class="dot"></span>${s.is_online ? 'Online' : 'Offline'}</span>
            </div>
          </div>
          <div class="pc-now">
            <div class="eyebrow">${s.is_online ? 'Now using' : 'Last used'}</div>
            <div class="program ${idle ? 'idle' : ''}" title="${esc(s.last_program)}">${esc(formatProgramName(s.last_program, s.is_online))}</div>
            <div class="meta">${s.is_online ? 'Updated' : 'Last seen'} ${esc(fmtAgo(s.last_seen))}</div>
          </div>
          <div class="pc-usage">
            <div class="row"><span>Active <b>${fmtMinutes(d.minutes_active)}</b></span><span>On ${fmtMinutes(d.minutes_on)}</span></div>
            ${meter(Number(d.minutes_active), Number(d.minutes_on))}
          </div>
          <div class="pc-foot"><span class="eyebrow">Est. today</span><span class="money">${fmtMoney(moneyFromMinutes(d.minutes_active))}</span></div>
        </div>`;
      }).join('')}</div>`;

  const requests = newRequests.length === 0 ? '' : `
    <div class="section-head"><h2>Waiting for review</h2><a class="meta" href="#requests">View all requests</a></div>
    <div class="card"><ul class="feed">${newRequests.slice(0, 4).map(r => `<li class="is-new">
      <div>
        <div class="title">${esc(r.title)}</div>
        ${r.description ? `<div class="desc">${esc(r.description)}</div>` : ''}
        <div class="meta"><span class="strong">${esc(r.pc_name)}</span>${r.menu_name ? `<span class="table-shop" style="display:inline-flex">${icon('store')}<span>${esc(r.menu_name)}</span></span>` : ''}<span>${esc(fmtDateTime(r.created_at))}</span></div>
      </div>
      <div class="side"><span class="badge new">New</span></div>
    </li>`).join('')}</ul></div>`;

  const shops = getAllShops();
  const shopMeta = shops.length === 1 ? `Shop: <b>${esc(shops[0])}</b>` : (shops.length > 1 ? `Across <b>${shops.length}</b> shops` : '');

  root.innerHTML = `<div class="attention">${attention.join('')}</div>
    ${hero}
    <div class="section-head"><h2>Computers</h2><span class="meta">${shopMeta ? `${shopMeta} &middot; ` : ''}Updated ${esc(fmtTime(Date.now()))} &middot; refreshes every minute</span></div>
    ${pcs}
    ${requests}`;
}

// ---------------------------------------------------------------------------
// Timeline
// ---------------------------------------------------------------------------

export async function renderTimeline(root, day) {
  let rows, status, networkInc;
  try {
    [rows, status, networkInc] = await Promise.all([
      api.timeline(day),
      api.pcStatus(),
      api.networkIncidents ? api.networkIncidents(day) : Promise.resolve([])
    ]);
  } catch (e) {
    root.innerHTML = errorBox(e);
    return;
  }

  recordPcShops(status);

  const start = dayStartMs(day);
  const end = start + 86400000;
  const isToday = day === todayStr();
  const clamp = (ms) => Math.min(end, Math.max(start, ms));
  const pct = (ms) => ((clamp(ms) - start) / 86400000) * 100;

  const segsByPc = groupBy(rows, 'pc_name');
  const statusByPc = new Map(status.map(s => [s.pc_name, s]));
  const pcs = [...new Set([...status.map(s => s.pc_name), ...segsByPc.keys()])].sort();

  let dayOn = 0, dayActive = 0, pcsUsed = 0;
  const ticks = [0, 3, 6, 9, 12, 15, 18, 21, 24];
  const gridLines = ticks.slice(1, -1).map(h => `<div class="tl-grid" style="left:${(h / 24) * 100}%"></div>`).join('');
  const nowMarker = isToday ? `<div class="tl-now" style="left:${pct(Date.now())}%" data-tip="Now ${fmtTime(Date.now())}"></div>` : '';

  // Feature 4: Operating Envelope (First PC boot to final shutdown)
  const allClampedSegs = rows.map(s => ({
    ...s,
    a: clamp(Date.parse(s.seg_start)),
    b: clamp(Date.parse(s.seg_end))
  }));
  const activeOnlySegs = allClampedSegs.filter(s => s.kind === 'active');
  const firstBootMs = allClampedSegs.length ? Math.min(...allClampedSegs.map(s => s.a)) : null;
  const lastShutdownMs = allClampedSegs.length ? Math.max(...allClampedSegs.map(s => s.b)) : null;
  const firstActiveMs = activeOnlySegs.length ? Math.min(...activeOnlySegs.map(s => s.a)) : null;
  const lastActiveMs = activeOnlySegs.length ? Math.max(...activeOnlySegs.map(s => s.b)) : null;
  const operatingSpanMins = (firstBootMs && lastShutdownMs) ? Math.max(0, Math.round((lastShutdownMs - firstBootMs) / 60000)) : 0;

  // Feature 1: Concurrent Fleet Saturation (96 15-min slices across 24 hours)
  const totalFleetSize = Math.max(1, pcs.length);
  const slices = Array.from({ length: 96 }, (_, i) => {
    const t = start + i * 15 * 60000;
    const activeCount = pcs.filter(pc =>
      (segsByPc.get(pc) || []).some(s => s.kind === 'active' && clamp(Date.parse(s.seg_start)) <= t && clamp(Date.parse(s.seg_end)) > t)
    ).length;
    return { t, count: activeCount };
  });

  const maxConcurrent = Math.max(0, ...slices.map(s => s.count));
  const peakSlice = slices.find(s => s.count === maxConcurrent && s.count > 0);
  const peakTimeStr = peakSlice ? fmtTime(peakSlice.t) : (maxConcurrent > 0 ? 'Peak' : 'No active play');
  const fullHouseSlices = slices.filter(s => s.count === totalFleetSize && s.count > 0).length;
  const fullHouseMins = fullHouseSlices * 15;
  const highLoadSlices = slices.filter(s => s.count >= Math.ceil(totalFleetSize * 0.75) && s.count > 0).length;
  const highLoadMins = highLoadSlices * 15;
  const lowDemandSlices = slices.filter(s => s.count <= Math.floor(totalFleetSize * 0.25) && (firstBootMs && s.t >= firstBootMs && s.t <= (lastShutdownMs || end))).length;
  const lowDemandMins = lowDemandSlices * 15;

  const hourlyLabels = Array.from({ length: 24 }, (_, h) => {
    const ampm = h >= 12 ? 'PM' : 'AM';
    const h12 = h % 12 === 0 ? 12 : h % 12;
    return `${h12} ${ampm}`;
  });
  const hourlyMaxActive = Array.from({ length: 24 }, (_, h) => {
    const hStart = start + h * 3600000;
    const hEnd = hStart + 3600000;
    const hSlices = slices.filter(s => s.t >= hStart && s.t < hEnd);
    return hSlices.length ? Math.max(...hSlices.map(s => s.count)) : 0;
  });

  // Feature 5: Power Cycles & Unplanned Mid-Game Stops
  let totalFleetBoots = 0;
  let totalUnplannedStops = 0;

  const pcRows = pcs.map(pc => {
    const segs = (segsByPc.get(pc) || []).map(s => ({ ...s, a: clamp(Date.parse(s.seg_start)), b: clamp(Date.parse(s.seg_end)) }));
    const onMin = segs.reduce((m, s) => m + (s.b - s.a) / 60000, 0);
    const activeMin = segs.filter(s => s.kind === 'active').reduce((m, s) => m + (s.b - s.a) / 60000, 0);
    dayOn += onMin; dayActive += activeMin; if (segs.length) pcsUsed++;
    const boots = groupBy(segs, 'power_on');
    totalFleetBoots += boots.size;

    let pcUnplanned = 0;
    for (const group of boots.values()) {
      const sorted = group.sort((a, b) => a.b - b.b);
      const last = sorted[sorted.length - 1];
      if (last && last.kind === 'active') {
        pcUnplanned++;
      }
    }
    totalUnplannedStops += pcUnplanned;

    let longest = 0;
    for (const group of boots.values()) {
      longest = Math.max(longest, (Math.max(...group.map(s => s.b)) - Math.min(...group.map(s => s.a))) / 60000);
    }
    const first = segs.length ? Math.min(...segs.map(s => s.a)) : null;
    const last = segs.length ? Math.max(...segs.map(s => s.b)) : null;
    const online = isToday && statusByPc.get(pc)?.is_online;

    const bars = segs.map(s => {
      const progLabel = s.kind === 'idle' ? (s.program === 'Chromatic Menu' ? 'Idle in menu' : 'Idle (No app reported)') : s.program;
      const tip = `${progLabel}\n${fmtTime(s.a)} - ${fmtTime(s.b)}  (${fmtMinutes((s.b - s.a) / 60000)})`;
      return `<div class="tl-seg ${s.kind}" style="left:${pct(s.a)}%;width:${Math.max(0.15, pct(s.b) - pct(s.a))}%" data-tip="${esc(tip)}"></div>`;
    }).join('');

    const stats = segs.length ? `<div class="tl-stats">
        <span class="hl">Active<b>${fmtMinutes(activeMin)}</b></span>
        <span>On<b>${fmtMinutes(onMin)}</b></span>
        <span>First on<b>${fmtTime(first)}</b></span>
        <span>${online ? 'Still on' : `Last off<b>${fmtTime(last)}</b>`}</span>
        <span>Power-ons<b>${boots.size}</b></span>
        <span>Longest sitting<b>${fmtMinutes(longest)}</b></span>
      </div>` : `<div class="tl-stats"><span>Off all day</span></div>`;

    const shop = statusByPc.get(pc)?.menu_name || getShopForPc(pc) || '';
    const crashBadge = pcUnplanned > 0
      ? `<span class="badge crash" title="Heartbeat ceased mid-game without exiting to menu">${icon('alert-triangle')}${pcUnplanned} mid-game stop${pcUnplanned > 1 ? 's' : ''}</span>`
      : '';
    const bootBadge = boots.size >= 4
      ? `<span class="badge warn" title="High reboot frequency today">${boots.size} boots</span>`
      : '';

    return `<div class="tl-row">
      <div class="tl-name">
        <div class="n">
          ${esc(pc)}
          ${online ? '<span class="badge online"><span class="dot"></span>Online</span>' : ''}
          ${crashBadge}
          ${bootBadge}
        </div>
        ${shop ? `<div class="tl-shop" title="Shop: ${esc(shop)}">${icon('store')}<span>${esc(shop)}</span></div>` : ''}
      </div>
      <div class="tl-bar">${gridLines}${bars}${nowMarker}</div>
      ${stats}
    </div>`;
  }).join('');

  // Feature 4: Operating Envelope HTML
  const envelopeHtml = firstBootMs ? `
    <div class="card" style="margin-bottom:16px">
      <div class="card-head">
        <h2>Operating envelope & staff schedule</h2>
        <span class="meta">${operatingSpanMins > 0 ? `Open span ${fmtMinutes(operatingSpanMins)} &middot; ` : ''}Earliest power-on to final shutdown</span>
      </div>
      <div class="card-body">
        <div class="envelope-grid">
          <div class="envelope-item">
            <div class="lbl">Store Opened</div>
            <div class="val">${fmtTime(firstBootMs)}</div>
            <div class="sub">First PC powered on</div>
          </div>
          <div class="envelope-item">
            <div class="lbl">First Customer</div>
            <div class="val">${firstActiveMs ? fmtTime(firstActiveMs) : 'No players'}</div>
            <div class="sub">First game launched</div>
          </div>
          <div class="envelope-item">
            <div class="lbl">Last Customer</div>
            <div class="val">${lastActiveMs ? fmtTime(lastActiveMs) : 'None'}</div>
            <div class="sub">Last game ended</div>
          </div>
          <div class="envelope-item">
            <div class="lbl">Store Closed</div>
            <div class="val">${lastShutdownMs ? (isToday && pcs.some(pc => statusByPc.get(pc)?.is_online) ? 'Still open' : fmtTime(lastShutdownMs)) : 'Off'}</div>
            <div class="sub">Final PC shutdown</div>
          </div>
          <div class="envelope-item">
            <div class="lbl">Customer Active Ratio</div>
            <div class="val">${operatingSpanMins > 0 ? fmtPct(dayActive / operatingSpanMins) : '0%'}</div>
            <div class="sub">${fmtMinutes(dayActive)} active / ${fmtMinutes(operatingSpanMins)} open</div>
          </div>
        </div>
      </div>
    </div>` : '';

  // Feature 1: Fleet Saturation HTML
  const saturationHtml = pcs.length ? `
    <div class="card" style="margin-bottom:16px">
      <div class="card-head">
        <h2>Concurrent player saturation</h2>
        <span class="meta">Simultaneous active gaming load across 24 hours</span>
      </div>
      <div class="card-body">
        <div class="envelope-grid" style="margin-bottom:16px">
          <div class="envelope-item">
            <div class="lbl">Peak Concurrent Load</div>
            <div class="val">${maxConcurrent} <span class="subtle" style="font-size:12px">/ ${totalFleetSize} PCs</span></div>
            <div class="sub">${maxConcurrent > 0 ? `${fmtPct(maxConcurrent / totalFleetSize)} peak at ${peakTimeStr}` : 'No active load'}</div>
          </div>
          <div class="envelope-item">
            <div class="lbl">Full House (100% Load)</div>
            <div class="val">${fmtMinutes(fullHouseMins)}</div>
            <div class="sub">Every PC occupied</div>
          </div>
          <div class="envelope-item">
            <div class="lbl">High Demand (&ge;75%)</div>
            <div class="val">${fmtMinutes(highLoadMins)}</div>
            <div class="sub">Rush & peak traffic window</div>
          </div>
          <div class="envelope-item">
            <div class="lbl">Off-Peak Window (&le;25%)</div>
            <div class="val">${fmtMinutes(lowDemandMins)}</div>
            <div class="sub">Quiet hours while open</div>
          </div>
        </div>
        <div class="chart-box sm"><canvas id="concurrencyChart"></canvas></div>
      </div>
    </div>` : '';

  // Feature 6: Network & Outage Buffer HTML
  const incidents = Array.isArray(networkInc) ? networkInc.filter(i => (i.delay_seconds || 0) >= 120) : [];
  let networkHtml = '';
  if (incidents.length === 0) {
    networkHtml = `
      <div class="alert ok section" style="margin-top:16px">
        ${icon('wifi')}
        <div><b>Network 100% Stable:</b> No buffered disconnects or ISP drops recorded for this day across all stations.</div>
      </div>`;
  } else {
    const incItems = incidents.slice(0, 6).map(inc => {
      const mins = Math.max(1, Math.round((inc.delay_seconds || 0) / 60));
      return `
        <div class="incident-row">
          <div>
            <span class="who">${esc(inc.pc_name)}</span>
            <span class="subtle">&middot; buffered for about ${mins} minute${mins === 1 ? '' : 's'}</span>
            ${inc.program ? `<span class="subtle">(in ${esc(inc.program)})</span>` : ''}
          </div>
          <span class="time">${fmtDateTime(inc.incident_time)}</span>
        </div>`;
    }).join('');

    networkHtml = `
      <div class="card section" style="margin-top:16px">
        <div class="card-head">
          <h2>Network & connectivity health</h2>
          <span class="meta">${incidents.length} delayed buffer flush${incidents.length === 1 ? '' : 'es'}</span>
        </div>
        <div class="card-body">
          <div class="hint" style="margin-top:0;margin-bottom:10px">When shop internet or a station cable drops, ChromaticTelemetry buffers heartbeats in memory and flushes them once reconnected.</div>
          <div class="incident-list">${incItems}</div>
        </div>
      </div>`;
  }

  root.innerHTML = `<div class="card stats" style="margin-top:0;margin-bottom:16px">
      ${stat('PCs used', `${pcsUsed}<span class="subtle" style="font-size:15px"> / ${pcs.length}</span>`, 'Turned on at least once', 'monitor')}
      ${stat('Active time', fmtMinutes(dayActive), 'All PCs combined', 'zap')}
      ${stat('Fleet power cycles', `${totalFleetBoots} boots`, `${totalUnplannedStops} mid-game interrupt${totalUnplannedStops === 1 ? '' : 's'}`, 'power')}
      ${stat('Estimated revenue', fmtMoney(moneyFromMinutes(dayActive)), rateText(), 'wallet')}
    </div>
    ${envelopeHtml}
    ${saturationHtml}
    <div class="card">
      <div class="card-head">
        <h2>${esc(fmtDay(day, { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' }))}</h2>
        <div class="legend">
          <span><i style="background:var(--accent)"></i>Active</span>
          <span><i style="background:var(--idle)"></i>Idle in menu</span>
          <span><i style="background:var(--surface-2);border:1px solid var(--border-strong)"></i>Off</span>
        </div>
      </div>
      ${pcs.length === 0 ? empty('No data for this day.', 'clock') : `<div class="tl">
        <div class="tl-axis"><div class="label-col"></div><div class="ticks">${ticks.map(h => `<span style="left:${(h / 24) * 100}%">${String(h).padStart(2, '0')}:00</span>`).join('')}</div></div>
        ${pcRows}
      </div>`}
    </div>
    ${networkHtml}
    <p class="meta" style="margin-top:12px">Times in ${esc(TZ)}. A gap longer than two heartbeat intervals counts as the PC being off. Hover a bar for details.</p>`;

  if (pcs.length && root.querySelector('#concurrencyChart')) {
    makeChart(root.querySelector('#concurrencyChart'), {
      type: 'line',
      data: {
        labels: hourlyLabels,
        datasets: [{
          label: 'Active PCs',
          data: hourlyMaxActive,
          borderColor: cssVar('--accent'),
          backgroundColor: cssVar('--accent-soft'),
          fill: true,
          tension: 0.3,
          pointRadius: 2,
          pointHoverRadius: 5
        }]
      },
      options: {
        plugins: {
          legend: { display: false },
          tooltip: {
            callbacks: {
              label: (c) => ` ${c.raw} of ${totalFleetSize} PCs active (${Math.round((c.raw / totalFleetSize) * 100)}% capacity)`
            }
          }
        },
        scales: {
          x: { grid: { display: false } },
          y: {
            beginAtZero: true,
            suggestedMax: totalFleetSize,
            ticks: {
              precision: 0,
              stepSize: 1
            }
          }
        }
      }
    });
  }
}

// ---------------------------------------------------------------------------
// Programs
// ---------------------------------------------------------------------------

export async function renderPrograms(root, range) {
  let usage, sessions;
  try {
    [usage, sessions] = await Promise.all([
      api.usage(range.from, range.to),
      api.sessionStats ? api.sessionStats(range.from, range.to) : Promise.resolve(null)
    ]);
  } catch (e) {
    root.innerHTML = errorBox(e);
    return;
  }

  const activeTotal = sum(usage.filter(u => !IDLE_PROGRAMS.has(u.program)), 'minutes');
  const idleTotal = sum(usage.filter(u => IDLE_PROGRAMS.has(u.program)), 'minutes');
  const windowsTotal = sum(usage.filter(u => u.program === 'Windows'), 'minutes');

  const programs = [...groupBy(usage.filter(u => !NON_PROGRAMS.has(u.program)), 'program')]
    .map(([program, rows]) => ({ program, minutes: sum(rows, 'minutes'), pcs: new Set(rows.map(r => r.pc_name)).size }))
    .sort((a, b) => b.minutes - a.minutes);
  const top = programs.slice(0, 10);
  const maxMin = top.length ? top[0].minutes : 1;

  const perPc = [...groupBy(usage.filter(u => !NON_PROGRAMS.has(u.program)), 'pc_name')]
    .map(([pc, rows]) => ({ pc, total: sum(rows, 'minutes'), top: rows.sort((a, b) => b.minutes - a.minutes).slice(0, 3) }))
    .sort((a, b) => a.pc.localeCompare(b.pc));

  // Feature 2: Contiguous Session Distributions HTML
  let sessionsHtml = '';
  if (sessions && sessions.length) {
    const totalSessions = sum(sessions, 'session_count');
    const allMinutes = sum(sessions, 'total_minutes');
    const overallAvgMins = totalSessions > 0 ? Math.round(allMinutes / totalSessions) : 0;
    const allQuick = sum(sessions, 'quick_count');
    const allStd = sum(sessions, 'standard_count');
    const allMarathon = sum(sessions, 'marathon_count');
    const quickPct = totalSessions > 0 ? (allQuick / totalSessions) : 0;
    const marathonPct = totalSessions > 0 ? (allMarathon / totalSessions) : 0;
    const stdPct = totalSessions > 0 ? (allStd / totalSessions) : 0;

    sessionsHtml = `
      <div class="card section">
        <div class="card-head">
          <h2>Player session dynamics & retention</h2>
          <span class="meta">Run-length distributions of unbroken game sittings</span>
        </div>
        <div class="card-body">
          <div class="envelope-grid" style="margin-bottom:16px">
            <div class="envelope-item">
              <div class="lbl">Avg Sitting Duration</div>
              <div class="val">${fmtMinutes(overallAvgMins)}</div>
              <div class="sub">Across ${totalSessions} continuous sessions</div>
            </div>
            <div class="envelope-item">
              <div class="lbl">Quick Drop-ins (&lt;20m)</div>
              <div class="val">${fmtPct(quickPct)}</div>
              <div class="sub">${allQuick} short sessions</div>
            </div>
            <div class="envelope-item">
              <div class="lbl">Standard Play (20-60m)</div>
              <div class="val">${fmtPct(stdPct)}</div>
              <div class="sub">${allStd} standard sessions</div>
            </div>
            <div class="envelope-item">
              <div class="lbl">Marathon Sittings (&gt;1h)</div>
              <div class="val">${fmtPct(marathonPct)}</div>
              <div class="sub">${allMarathon} deep gaming sessions</div>
            </div>
          </div>
          <div class="table-wrap">
            <table class="table">
              <thead>
                <tr>
                  <th>Program / Game</th>
                  <th class="num">Sessions</th>
                  <th class="num">Avg Sitting</th>
                  <th class="num">Median</th>
                  <th class="num">Longest Run</th>
                  <th style="width:24%">Duration Mix</th>
                </tr>
              </thead>
              <tbody>
                ${sessions.slice(0, 10).map(s => {
                  const sc = Number(s.session_count || 1);
                  const qP = Math.round((Number(s.quick_count || 0) / sc) * 100);
                  const sP = Math.round((Number(s.standard_count || 0) / sc) * 100);
                  const mP = Math.max(0, 100 - qP - sP);
                  return `<tr>
                    <td class="strong">${esc(s.program)}</td>
                    <td class="num">${s.session_count}</td>
                    <td class="num">${fmtMinutes(Number(s.avg_minutes || 0))}</td>
                    <td class="num subtle">${fmtMinutes(Number(s.median_minutes || 0))}</td>
                    <td class="num strong">${fmtMinutes(Number(s.max_minutes || 0))}</td>
                    <td>
                      <div class="session-mix" title="Quick &lt;20m: ${qP}% | Standard 20-60m: ${sP}% | Marathon &gt;1h: ${mP}%">
                        <i class="quick" style="width:${qP}%"></i>
                        <i class="std" style="width:${sP}%"></i>
                        <i class="marathon" style="width:${mP}%"></i>
                      </div>
                    </td>
                  </tr>`;
                }).join('')}
              </tbody>
            </table>
          </div>
          <div class="hint" style="margin-top:10px">
            <span style="display:inline-flex;align-items:center;gap:4px;margin-right:12px"><i style="display:inline-block;width:8px;height:8px;border-radius:2px;background:var(--success)"></i> &lt;20 min quick</span>
            <span style="display:inline-flex;align-items:center;gap:4px;margin-right:12px"><i style="display:inline-block;width:8px;height:8px;border-radius:2px;background:var(--accent)"></i> 20-60 min standard</span>
            <span style="display:inline-flex;align-items:center;gap:4px"><i style="display:inline-block;width:8px;height:8px;border-radius:2px;background:#8b5cf6"></i> &gt;1 hr marathon</span>
          </div>
        </div>
      </div>`;
  }

  root.innerHTML = `<div class="card stats" style="margin-top:0;margin-bottom:16px">
      ${stat('Most used', programs.length ? esc(programs[0].program) : '&mdash;', programs.length ? `${fmtMinutes(programs[0].minutes)} &middot; ${fmtPct(activeTotal ? programs[0].minutes / activeTotal : 0)} of active time` : 'No usage', 'gamepad-2')}
      ${stat('Programs used', String(programs.length), 'Excluding Windows and the menu', 'app-window')}
      ${stat('Active time', fmtMinutes(activeTotal), 'A program in the foreground', 'zap')}
      ${stat('Idle in menu', fmtMinutes(idleTotal), `Windows tools ${fmtMinutes(windowsTotal)}`, 'layout-grid')}
    </div>
    <div class="grid cols-2">
      <div class="card">
        <div class="card-head"><h2>Top programs</h2><span class="meta">By time in the foreground</span></div>
        ${top.length ? `<ol class="rank-list">${top.map((p, i) => `<li class="${i < 3 ? 'top' : ''}">
          <span class="rank">${i + 1}</span>
          <div><div class="name" title="${esc(p.program)}">${esc(p.program)}</div><div class="bar-track"><div class="bar-fill" style="width:${(p.minutes / maxMin) * 100}%"></div></div></div>
          <div class="val">${fmtMinutes(p.minutes)}<span class="meta">${fmtPct(activeTotal ? p.minutes / activeTotal : 0)}</span></div>
        </li>`).join('')}</ol>` : empty('No program usage in this range.', 'app-window')}
      </div>
      <div class="card">
        <div class="card-head"><h2>By computer</h2><span class="meta">Top 3 each</span></div>
        ${perPc.length ? `<div class="table-wrap"><table class="table"><tbody>
          ${perPc.map(p => {
            const shop = getShopForPc(p.pc);
            return `<tr><td style="width:36%">
              <div class="strong">${esc(p.pc)}</div>
              ${shop ? `<div class="table-shop" title="Shop: ${esc(shop)}">${icon('store')}<span>${esc(shop)}</span></div>` : ''}
              <div class="meta" style="margin-top:2px">${fmtMinutes(p.total)} active</div>
            </td>
            <td>${p.top.map((t, i) => `<div style="display:flex;justify-content:space-between;gap:10px;${i ? 'margin-top:4px' : ''}"><span class="${i === 0 ? 'strong' : 'muted'}">${esc(t.program)}</span><span class="subtle tabular">${fmtMinutes(t.minutes)}</span></div>`).join('')}</td></tr>`;
          }).join('')}
        </tbody></table></div>` : empty('No data.', 'monitor')}
      </div>
    </div>
    ${sessionsHtml}
    <div class="card section">
      <div class="card-head"><h2>All programs</h2><span class="meta">${programs.length} total</span></div>
      ${programs.length ? `<div class="table-wrap"><table class="table">
        <thead><tr><th style="width:48px">#</th><th>Program</th><th class="num">Time</th><th>Share of active time</th><th class="num">PCs</th></tr></thead>
        <tbody>${programs.map((p, i) => `<tr>
          <td class="subtle">${i + 1}</td>
          <td class="${i < 3 ? 'strong' : ''}">${esc(p.program)}</td>
          <td class="num">${fmtMinutes(p.minutes)}</td>
          <td style="width:32%"><div style="display:flex;align-items:center;gap:10px"><div class="bar-track" style="flex:1"><div class="bar-fill" style="width:${activeTotal ? (p.minutes / activeTotal) * 100 : 0}%"></div></div><span class="subtle tabular" style="width:40px;text-align:right">${fmtPct(activeTotal ? p.minutes / activeTotal : 0)}</span></div></td>
          <td class="num">${p.pcs}</td>
        </tr>`).join('')}</tbody>
      </table></div>` : empty('No program usage in this range.', 'app-window')}
    </div>`;
}

// ---------------------------------------------------------------------------
// Revenue
// ---------------------------------------------------------------------------

export async function renderRevenue(root, range) {
  const days = daysInclusive(range.from, range.to);
  const prevTo = addDays(range.from, -1);
  const prevFrom = addDays(prevTo, -(days - 1));
  const isSingleDay = days === 1;

  let daily, prev, heat, usage, timeline = [];
  try {
    const promises = [
      api.daily(range.from, range.to),
      api.daily(prevFrom, prevTo),
      api.heatmap(range.from, range.to),
      api.usage(range.from, range.to)
    ];
    if (isSingleDay) {
      promises.push(api.timeline(range.from));
    }
    const results = await Promise.all(promises);
    daily = results[0] || [];
    prev = results[1] || [];
    heat = results[2] || [];
    usage = results[3] || [];
    if (isSingleDay) {
      timeline = results[4] || [];
    }
  } catch (e) {
    root.innerHTML = errorBox(e);
    return;
  }

  recordPcShops(daily);

  const activeTotal = sum(daily, 'minutes_active');
  const onTotal = sum(daily, 'minutes_on');
  const revenue = moneyFromMinutes(activeTotal);
  const prevRevenue = moneyFromMinutes(sum(prev, 'minutes_active'));
  const change = prevRevenue > 0 ? (revenue - prevRevenue) / prevRevenue : null;

  const pcs = [...new Set(daily.map(d => d.pc_name))].sort();
  const dayList = Array.from({ length: days }, (_, i) => addDays(range.from, i));
  const byDayPc = new Map(daily.map(d => [`${d.day}|${d.pc_name}`, d]));
  const perDay = dayList.map(d => ({ day: d, revenue: moneyFromMinutes(sum(daily.filter(r => r.day === d), 'minutes_active')) }));
  const best = perDay.reduce((a, b) => (b.revenue > (a?.revenue ?? -1) ? b : a), null);

  const perPc = pcs.map(pc => {
    const rows = daily.filter(d => d.pc_name === pc);
    const on = sum(rows, 'minutes_on');
    const active = sum(rows, 'minutes_active');
    const shop = rows[0]?.menu_name || getShopForPc(pc) || '';
    return { pc, shop, on, active, revenue: moneyFromMinutes(active) };
  }).sort((a, b) => b.revenue - a.revenue);

  // Top earning games & applications (excluding system / shell programs)
  const progMap = new Map();
  for (const u of usage) {
    if (!u.program || NON_PROGRAMS.has(u.program)) continue;
    progMap.set(u.program, (progMap.get(u.program) || 0) + Number(u.minutes || 0));
  }
  const topPrograms = [...progMap.entries()]
    .map(([name, mins]) => ({
      name,
      minutes: mins,
      revenue: moneyFromMinutes(mins),
      share: activeTotal > 0 ? (mins / activeTotal) : 0
    }))
    .sort((a, b) => b.minutes - a.minutes)
    .slice(0, 10);

  // Heatmap calculations
  const maxHeat = Math.max(0.0001, ...heat.map(h => Number(h.avg_active_pcs)));
  const heatByKey = new Map(heat.map(h => [`${h.weekday}|${h.hour}`, Number(h.avg_active_pcs)]));
  const busiest = heat.reduce((a, b) => (Number(b.avg_active_pcs) > Number(a?.avg_active_pcs ?? -1) ? b : a), null);
  const weekdays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
  const moneyPerPcHour = 60 / minutesPerUnit();
  const shade = (v) => v > 0 ? `background:color-mix(in srgb, var(--accent) ${Math.round((0.14 + 0.86 * (v / maxHeat)) * 100)}%, var(--track))` : '';
  const heatmap = `<div class="heatmap-wrap"><div class="heatmap">
    <div></div>${Array.from({ length: 24 }, (_, h) => `<div class="hm-hour">${h % 3 === 0 ? h : ''}</div>`).join('')}
    ${weekdays.map((name, i) => `<div class="hm-label">${name}</div>${Array.from({ length: 24 }, (_, h) => {
      const v = heatByKey.get(`${i + 1}|${h}`) || 0;
      const tip = `${name} ${String(h).padStart(2, '0')}:00\n${v.toFixed(1)} PCs in use on average\nabout ${fmtMoney(v * moneyPerPcHour)} per hour`;
      return `<div class="hm-cell" style="${shade(v)}" data-tip="${esc(tip)}"></div>`;
    }).join('')}`).join('')}
  </div></div>
  <div class="hm-scale">Less ${[0.15, 0.4, 0.7, 1].map(f => `<i style="${shade(f * maxHeat)}"></i>`).join('')} More</div>`;

  // Hero cards depending on range (Single Day vs Multi-Day)
  let heroCardsHtml = '';
  if (isSingleDay) {
    const isToday = range.from === todayStr();
    const dayLabel = isToday ? 'today' : fmtDay(range.from);
    heroCardsHtml = `
      <div class="card hero-card primary">
        <div class="top">${icon('wallet')}<span class="eyebrow">Estimated revenue ${esc(dayLabel)}</span></div>
        <div class="hero-value">${fmtMoney(revenue)}</div>
        <div class="hero-foot">${delta(change, `vs yesterday (${fmtMoney(prevRevenue)})`) || `<span class="subtle">Yesterday: ${fmtMoney(prevRevenue)}</span>`}</div>
      </div>
      <div class="card hero-card">
        <div class="top">${icon('zap')}<span class="eyebrow">Paid active time</span></div>
        <div class="hero-value">${fmtMinutes(activeTotal)}</div>
        <div class="hero-foot">of ${fmtMinutes(onTotal)} on &middot; ${fmtMinutes(Math.max(0, onTotal - activeTotal))} idle</div>
      </div>
      <div class="card hero-card">
        <div class="top">${icon('activity')}<span class="eyebrow">Shop utilisation</span></div>
        <div class="hero-value">${fmtPct(onTotal ? activeTotal / onTotal : 0)}</div>
        <div class="hero-foot"><span class="subtle">${rateText()}</span></div>
      </div>`;
  } else {
    heroCardsHtml = `
      <div class="card hero-card primary">
        <div class="top">${icon('wallet')}<span class="eyebrow">Total estimated revenue</span></div>
        <div class="hero-value">${fmtMoney(revenue)}</div>
        <div class="hero-foot">${delta(change, `vs previous ${days} days (${fmtMoney(prevRevenue)})`) || `<span class="subtle">Previous ${days} days: ${fmtMoney(prevRevenue)}</span>`}</div>
      </div>
      <div class="card hero-card">
        <div class="top">${icon('calendar')}<span class="eyebrow">Daily average</span></div>
        <div class="hero-value">${fmtMoney(revenue / days)}</div>
        <div class="hero-foot">${days} days in selected period</div>
      </div>
      <div class="card hero-card">
        <div class="top">${icon('trending-up')}<span class="eyebrow">Best day</span></div>
        <div class="hero-value">${best && best.revenue > 0 ? fmtMoney(best.revenue) : '&mdash;'}</div>
        <div class="hero-foot">${best && best.revenue > 0 ? esc(fmtDay(best.day, { weekday: 'long', month: 'short', day: 'numeric' })) : 'No revenue in range'}</div>
      </div>`;
  }

  // Chart configuration: Hourly breakdown for single day, daily breakdown for multi-day
  let chartTitle = 'Revenue per day';
  let chartMeta = 'Stacked by computer';
  let chartLabels = [];
  let chartDatasets = [];

  if (isSingleDay) {
    const isToday = range.from === todayStr();
    chartTitle = isToday ? "Today's revenue by hour" : `Hourly revenue for ${fmtDay(range.from)}`;
    chartMeta = "Active revenue earned in each hour (by computer)";

    const hours = Array.from({ length: 24 }, (_, h) => h);
    chartLabels = hours.map(h => {
      const ampm = h >= 12 ? 'PM' : 'AM';
      const h12 = h % 12 === 0 ? 12 : h % 12;
      return `${h12} ${ampm}`;
    });

    const startMs = dayStartMs(range.from);
    const endMs = startMs + 86400000;
    const clamp = (ms) => Math.min(endMs, Math.max(startMs, ms));

    const pcHourly = new Map();
    for (const pc of pcs) pcHourly.set(pc, new Array(24).fill(0));

    for (const row of timeline) {
      if (row.kind !== 'active') continue;
      const a = clamp(Date.parse(row.seg_start));
      const b = clamp(Date.parse(row.seg_end));
      if (b <= a) continue;

      const pcArr = pcHourly.get(row.pc_name);
      if (!pcArr) continue;

      for (let h = 0; h < 24; h++) {
        const hStart = startMs + h * 3600000;
        const hEnd = hStart + 3600000;
        const overlap = Math.max(0, Math.min(b, hEnd) - Math.max(a, hStart));
        if (overlap > 0) {
          pcArr[h] += overlap / 60000; // in minutes
        }
      }
    }

    chartDatasets = pcs.map((pc, i) => ({
      label: pc,
      data: hours.map(h => {
        const mins = pcHourly.get(pc)?.[h] || 0;
        return Math.round(moneyFromMinutes(mins) * 100) / 100;
      }),
      backgroundColor: seriesColor(i),
      borderRadius: 3,
      maxBarThickness: 28
    }));
  } else {
    chartTitle = 'Revenue per day';
    chartMeta = 'Stacked by computer';
    chartLabels = dayList.map(d => fmtDay(d));
    chartDatasets = pcs.map((pc, i) => ({
      label: pc,
      data: dayList.map(d => {
        const mins = byDayPc.get(`${d}|${pc}`)?.minutes_active || 0;
        return Math.round(moneyFromMinutes(mins) * 100) / 100;
      }),
      backgroundColor: seriesColor(i),
      borderRadius: 3,
      maxBarThickness: 34
    }));
  }

  // Games table HTML
  const gamesTable = topPrograms.length ? `
    <div class="table-wrap"><table class="table">
      <thead><tr><th>Program / Game</th><th class="num">Time</th><th>Share</th><th class="num">Estimate</th></tr></thead>
      <tbody>${topPrograms.map(p => `<tr>
        <td class="strong">${esc(p.name)}</td>
        <td class="num">${fmtMinutes(p.minutes)}</td>
        <td style="width:28%">
          <div style="display:flex;align-items:center;gap:8px">
            <div class="bar-track" style="flex:1"><div class="bar-fill" style="width:${Math.round(p.share * 100)}%"></div></div>
            <span class="subtle tabular" style="width:36px;text-align:right">${fmtPct(p.share)}</span>
          </div>
        </td>
        <td class="num strong">${fmtMoney(p.revenue)}</td>
      </tr>`).join('')}</tbody>
    </table></div>` : empty('No game or app usage recorded in this period.', 'app-window');

  // Feature 3: Station Load Imbalance & Fleet Workload Parity
  const expectedShare = perPc.length ? (1 / perPc.length) : 0;
  const overworked = perPc.filter(p => expectedShare > 0 && (p.active / (activeTotal || 1)) / expectedShare > 1.35);
  const neglected = perPc.filter(p => expectedShare > 0 && (p.active / (activeTotal || 1)) / expectedShare < 0.65);
  const parityNotice = (perPc.length > 2 && (overworked.length || neglected.length)) ? `
    <div class="alert info" style="margin:14px 18px 0">
      ${icon('scale')}
      <div><b>Fleet Workload Parity:</b> ${overworked.length ? `Station ${overworked.map(o => esc(o.pc)).join(', ')} carries heavy customer wear (>35% above average). ` : ''}${neglected.length ? `Station ${neglected.map(n => esc(n.pc)).join(', ')} has unusually low runtime (<65% of average); inspect screen angle, seat, and mouse/keyboard responsiveness.` : ''}</div>
    </div>` : '';

  // PC Table HTML
  const pcTable = perPc.length ? `
    <div class="table-wrap"><table class="table">
      <thead><tr><th>Computer / Shop</th><th class="num">Active</th><th class="num">On-time</th><th class="num">Utilisation</th><th>Workload</th><th>Share</th><th class="num">Estimate</th></tr></thead>
      <tbody>${perPc.map((p, i) => {
        const actualShare = activeTotal > 0 ? (p.active / activeTotal) : 0;
        const parityRatio = expectedShare > 0 ? (actualShare / expectedShare) : 1;
        let parityBadge = `<span class="badge balanced">Balanced</span>`;
        if (activeTotal > 0 && perPc.length > 1) {
          if (parityRatio > 1.35) {
            parityBadge = `<span class="badge heavy" title="Workload is ${Math.round((parityRatio - 1) * 100)}% above fleet average">+${Math.round((parityRatio - 1) * 100)}% Heavy</span>`;
          } else if (parityRatio < 0.65) {
            parityBadge = `<span class="badge neglected" title="Workload is ${Math.round((1 - parityRatio) * 100)}% below fleet average">-${Math.round((1 - parityRatio) * 100)}% Low</span>`;
          }
        }
        return `<tr>
          <td class="${i === 0 ? 'strong' : ''}">
            <div class="strong">${esc(p.pc)}</div>
            ${p.shop ? `<div class="table-shop" title="Shop: ${esc(p.shop)}">${icon('store')}<span>${esc(p.shop)}</span></div>` : ''}
          </td>
          <td class="num">${fmtMinutes(p.active)}</td>
          <td class="num subtle">${fmtMinutes(p.on)}</td>
          <td class="num">${fmtPct(p.on ? p.active / p.on : 0)}</td>
          <td>${parityBadge}</td>
          <td style="width:16%"><div class="bar-track"><div class="bar-fill" style="width:${revenue ? (p.revenue / revenue) * 100 : 0}%;background:${seriesColor(pcs.indexOf(p.pc))}"></div></div></td>
          <td class="num strong">${fmtMoney(p.revenue)}</td>
        </tr>`;
      }).join('')}</tbody>
    </table></div>${parityNotice}` : empty('No computer data.', 'monitor');

  root.innerHTML = `
    <div class="hero">${heroCardsHtml}</div>
    <div class="card stats">
      ${stat('Active paid time', fmtMinutes(activeTotal), 'Customer gameplay & app time', 'zap')}
      ${stat('Idle in menu', fmtMinutes(Math.max(0, onTotal - activeTotal)), 'PC on, but no customer playing', 'layout-grid')}
      ${stat('Shop utilisation', fmtPct(onTotal ? activeTotal / onTotal : 0), `${fmtMinutes(onTotal)} total power-on`, 'activity')}
      ${stat('Top earner', perPc.length ? esc(perPc[0].pc) : '&mdash;', perPc.length ? `${fmtMoney(perPc[0].revenue)} &middot; ${fmtPct(revenue ? perPc[0].revenue / revenue : 0)}` : '', 'monitor')}
    </div>
    <div class="card section">
      <div class="card-head">
        <h2>${esc(chartTitle)}</h2>
        <span class="meta">${esc(chartMeta)}</span>
      </div>
      <div class="card-body">
        ${daily.length ? '<div class="chart-box"><canvas id="revenueChart"></canvas></div>' : empty('No revenue data in this range.', 'wallet')}
      </div>
    </div>
    <div class="grid cols-even section">
      <div class="card">
        <div class="card-head">
          <h2>Revenue by program / game</h2>
          <span class="meta">What customers are paying to use</span>
        </div>
        ${gamesTable}
      </div>
      <div class="card">
        <div class="card-head">
          <h2>Revenue by computer</h2>
          <span class="meta">Earnings and efficiency per station</span>
        </div>
        ${pcTable}
      </div>
    </div>
    ${days >= 7 ? `
    <div class="card section">
      <div class="card-head">
        <h2>Busy hours heatmap</h2>
        <span class="meta">Average PCs in active use by day and hour</span>
      </div>
      <div class="card-body">${heatmap}</div>
    </div>` : ''}
    <div class="alert section">
      ${icon('info')}
      <div>
        <b>How revenue is estimated:</b> Active minutes with a game or application in the foreground, at <b>${esc(rateText())}</b>.
        Coin timers cut power to monitors only; any active game left open counts towards customer utilisation. Rates and currency can be configured in <b>Settings</b>.
      </div>
    </div>`;

  if (daily.length && chartDatasets.length) {
    makeChart(root.querySelector('#revenueChart'), {
      type: 'bar',
      data: {
        labels: chartLabels,
        datasets: chartDatasets
      },
      options: {
        plugins: {
          legend: { position: 'bottom', labels: { boxWidth: 10, boxHeight: 10, padding: 16, color: cssVar('--text-2') } },
          tooltip: {
            callbacks: {
              label: (c) => ` ${c.dataset.label}: ${fmtMoney(c.raw)}`
            }
          }
        },
        scales: {
          x: { stacked: true, grid: { display: false } },
          y: {
            stacked: true,
            beginAtZero: true,
            suggestedMax: 5,
            ticks: {
              precision: 0,
              callback: (v) => Number.isInteger(v) ? fmtMoney(v, { minimumFractionDigits: 0, maximumFractionDigits: 0 }) : null
            }
          }
        }
      }
    });
  }
}

// ---------------------------------------------------------------------------
// Game requests
// ---------------------------------------------------------------------------

export async function renderRequests(root, filter, onChanged) {
  let all;
  try {
    all = await api.requests('all');
  } catch (e) {
    root.innerHTML = errorBox(e);
    return;
  }

  const counts = { new: 0, added: 0, rejected: 0, all: all.length };
  for (const r of all) counts[r.status] = (counts[r.status] || 0) + 1;
  const rows = filter === 'all' ? all : all.filter(r => r.status === filter);
  const filters = [['new', 'New'], ['added', 'Added'], ['rejected', 'Rejected'], ['all', 'All']];
  const label = (s) => s[0].toUpperCase() + s.slice(1);

  const actions = (r) => r.status === 'new'
    ? `<button class="btn sm success" data-id="${esc(r.id)}" data-status="added">${icon('check')}Mark added</button><button class="btn sm ghost danger" data-id="${esc(r.id)}" data-status="rejected">Reject</button>`
    : `<button class="btn sm ghost" data-id="${esc(r.id)}" data-status="new">Move back to New</button>`;

  root.innerHTML = `<div class="req-header">
      <div class="seg req-seg" id="reqFilter">
        ${filters.map(([k, l]) => `<button data-filter="${k}" class="${k === filter ? 'active' : ''}">${l}<span class="count">${counts[k] || 0}</span></button>`).join('')}
      </div>
      <div class="meta req-hint">Customers send these from the Request a Game button. Each PC can send one every 5 minutes.</div>
    </div>
    <div class="card">
      ${rows.length === 0 ? empty(filter === 'new' ? 'Nothing waiting. New requests appear here.' : 'No requests here.', 'message-square') : `<ul class="feed">
        ${rows.map(r => `<li class="${r.status === 'new' ? 'is-new' : ''}">
          <div>
            <div class="title">${esc(r.title)}</div>
            ${r.description ? `<div class="desc">${esc(r.description)}</div>` : ''}
            <div class="meta"><span class="strong">${esc(r.pc_name)}</span>${r.menu_name ? `<span class="table-shop" style="display:inline-flex">${icon('store')}<span>${esc(r.menu_name)}</span></span>` : ''}<span>${esc(fmtDateTime(r.created_at))}</span></div>
          </div>
          <div class="side"><span class="badge ${esc(r.status)}">${esc(label(r.status))}</span><div class="actions">${actions(r)}</div></div>
        </li>`).join('')}
      </ul>`}
    </div>`;

  root.querySelector('#reqFilter').addEventListener('click', (e) => {
    const b = e.target.closest('button[data-filter]');
    if (b) onChanged({ filter: b.dataset.filter });
  });
  root.querySelectorAll('button[data-status]').forEach(b => b.addEventListener('click', async () => {
    b.disabled = true;
    try {
      await api.setRequestStatus(Number(b.dataset.id), b.dataset.status);
      onChanged({ filter });
    } catch (err) {
      b.disabled = false;
      root.insertAdjacentHTML('afterbegin', errorBox(err));
    }
  }));
}

// ---------------------------------------------------------------------------
// Settings
// ---------------------------------------------------------------------------

export function renderSettings(root, ctx) {
  root.innerHTML = `<div class="grid cols-even">
    <div class="card">
      <div class="card-head"><h2>Revenue estimate</h2></div>
      <div class="card-body">
        <label class="field"><span>Currency</span>
          <select class="input" id="currency" style="max-width:220px">
            ${CURRENCIES.map(c => `<option value="${c}" ${c === currency() ? 'selected' : ''}>${c} (${esc(currencySymbol(c))})</option>`).join('')}
          </select>
        </label>
        <label class="field"><span>Minutes of use per 1 unit of currency</span>
          <input class="input" id="mpp" type="number" min="1" max="120" step="1" value="${minutesPerUnit()}" style="max-width:160px">
        </label>
        <div class="hint" style="margin-top:-8px">Default is 9 minutes. Every revenue figure in the dashboard uses this currency and rate.</div>
        <div style="margin-top:16px;display:flex;align-items:center;gap:10px;flex-wrap:wrap"><button class="btn primary" id="saveMpp">Save</button><span id="mppMsg" class="meta"></span></div>
      </div>
    </div>
    <div class="card">
      <div class="card-head"><h2>Appearance</h2></div>
      <div class="card-body">
        <div class="eyebrow" style="margin-bottom:8px">Theme</div>
        <div class="seg" id="themeSeg">
          <button data-theme="dark" class="${ctx.theme === 'dark' ? 'active' : ''}">${icon('moon')}Dark</button>
          <button data-theme="light" class="${ctx.theme === 'light' ? 'active' : ''}">${icon('sun')}Light</button>
        </div>
      </div>
    </div>
    <div class="card">
      <div class="card-head"><h2>Connection</h2><span class="badge online"><span class="dot"></span>Connected</span></div>
      <div class="card-body">
        <div class="eyebrow">Supabase project</div>
        <div class="mono" style="margin:4px 0 14px;word-break:break-all">${esc(ctx.url)}</div>
        <div class="eyebrow">Connected shop(s)</div>
        <div style="margin:4px 0 14px;font-weight:500;display:flex;align-items:center;gap:6px">
          ${icon('store')}<span>${esc(getAllShops().join(', ') || 'Waiting for computer telemetry...')}</span>
        </div>
        <div class="eyebrow">Signed in as</div>
        <div style="margin:4px 0 18px;font-weight:500">${esc(ctx.email || '')}</div>
        <div style="display:flex;gap:8px;flex-wrap:wrap">
          <button class="btn" id="logoutBtn">${icon('log-out')}Log out</button>
          <button class="btn ghost danger" id="changeProjectBtn">${icon('database')}Use a different project</button>
        </div>
        <div class="hint">You stay signed in on this browser until you log out.</div>
      </div>
    </div>
    <div class="card">
      <div class="card-head"><h2>Help</h2></div>
      <div class="card-body">
        <p class="muted" style="margin-top:0">Setup steps, the database schema, and troubleshooting are in the setup guide.</p>
        <a class="btn" href="setup.html">${icon('book-open')}Open setup guide</a>
      </div>
    </div>
  </div>`;

  root.querySelector('#saveMpp').addEventListener('click', () => {
    const v = Number(root.querySelector('#mpp').value);
    const msg = root.querySelector('#mppMsg');
    if (!(v >= 1 && v <= 120)) { msg.textContent = 'Enter a number from 1 to 120.'; return; }
    store.set('minutesPerUnit', String(v));
    store.set('currency', root.querySelector('#currency').value);
    msg.textContent = `Saved. Rate is now ${rateText()}.`;
  });
  root.querySelector('#themeSeg').addEventListener('click', (e) => {
    const b = e.target.closest('button[data-theme]');
    if (!b) return;
    ctx.setTheme(b.dataset.theme);
    root.querySelectorAll('#themeSeg button').forEach(x => x.classList.toggle('active', x === b));
  });
  root.querySelector('#logoutBtn').addEventListener('click', ctx.logout);
  root.querySelector('#changeProjectBtn').addEventListener('click', ctx.changeProject);
}
