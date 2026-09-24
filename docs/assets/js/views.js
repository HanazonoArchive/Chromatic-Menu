import { Chart, registerables } from 'https://cdn.jsdelivr.net/npm/chart.js@4.4.4/+esm';
import { api } from './api.js';
import { icon } from './icons.js';
import {
  esc, todayStr, addDays, daysInclusive, dayStartMs, fmtDay, fmtTime, fmtDateTime, fmtMinutes,
  fmtAgo, fmtPeso, fmtPct, minutesPerPeso, pesoFromMinutes, cssVar, seriesColor, groupBy, sum,
  IDLE_PROGRAMS, NON_PROGRAMS, store, TZ
} from './util.js';

Chart.register(...registerables);

const charts = [];

export function destroyCharts() {
  while (charts.length) charts.pop().destroy();
}

function makeChart(canvas, config) {
  const text = cssVar('--text-2');
  const grid = cssVar('--border');
  Chart.defaults.font.family = getComputedStyle(document.body).fontFamily;
  Chart.defaults.color = text;
  config.options = config.options || {};
  config.options.maintainAspectRatio = false;
  config.options.animation = { duration: 150 };
  const scales = config.options.scales || {};
  for (const axis of Object.values(scales)) {
    axis.grid = { color: grid, drawTicks: false, ...(axis.grid || {}) };
    axis.border = { display: false };
    axis.ticks = { padding: 8, ...(axis.ticks || {}) };
  }
  charts.push(new Chart(canvas, config));
}

// Axis step in minutes that lands on whole quarter-hours / hours and keeps about 6 ticks.
function minuteStep(maxMinutes) {
  const steps = [15, 30, 60, 120, 180, 240, 360, 480, 720, 1440, 2880, 5760];
  return steps.find(s => maxMinutes / s <= 6) || steps[steps.length - 1];
}

function kpi(label, value, foot = '', iconName = null) {
  return `<div class="card kpi">
    <div class="label">${iconName ? icon(iconName) : ''}${esc(label)}</div>
    <div class="value">${value}</div>
    ${foot ? `<div class="foot">${foot}</div>` : ''}
  </div>`;
}

function errorBox(error) {
  return `<div class="alert error">${icon('alert-triangle')}<div>${esc(error.message || String(error))}</div></div>`;
}

export function loading() {
  return '<div class="empty">Loading...</div>';
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
  let status, daily, newRequests;
  try {
    [status, daily, newRequests] = await Promise.all([api.pcStatus(), api.daily(today, today), api.requests('new')]);
  } catch (e) {
    root.innerHTML = errorBox(e);
    return;
  }

  const byPc = new Map(daily.map(d => [d.pc_name, d]));
  const online = status.filter(s => s.is_online).length;
  const onTotal = sum(daily, 'minutes_on');
  const activeTotal = sum(daily, 'minutes_active');

  const kpis = `<div class="grid kpis">
    ${kpi('PCs online now', `${online}<span class="subtle" style="font-size:16px"> / ${status.length}</span>`, 'Heartbeat in the last few minutes', 'monitor')}
    ${kpi('On time today', fmtMinutes(onTotal), 'All PCs combined', 'power')}
    ${kpi('Active time today', fmtMinutes(activeTotal), `${fmtPct(onTotal ? activeTotal / onTotal : 0)} of on time`, 'activity')}
    ${kpi('Estimated revenue today', fmtPeso(pesoFromMinutes(activeTotal)), `₱1 per ${minutesPerPeso()} active minutes`, 'wallet')}
  </div>`;

  const cards = status.length === 0
    ? `<div class="card"><div class="empty">No heartbeats yet. Once a PC with telemetry enabled starts, it appears here within a minute.</div></div>`
    : `<div class="grid pcs">${status.map(s => {
        const d = byPc.get(s.pc_name) || { minutes_on: 0, minutes_active: 0 };
        const programLabel = s.is_online ? 'Using' : 'Last';
        return `<div class="card pc">
          <div class="pc-top">
            <span class="name" title="${esc(s.pc_name)}">${esc(s.pc_name)}</span>
            <span class="badge ${s.is_online ? 'online' : 'offline'}"><span class="dot"></span>${s.is_online ? 'Online' : 'Offline'}</span>
          </div>
          <div class="pc-program"><span class="subtle">${programLabel}:</span><strong title="${esc(s.last_program)}">${esc(s.last_program)}</strong></div>
          <div class="subtle" style="font-size:12px;margin:-6px 0 12px">Last seen ${esc(fmtAgo(s.last_seen))}</div>
          <div class="pc-stats">
            <div><div class="k">On today</div><div class="v">${fmtMinutes(d.minutes_on)}</div></div>
            <div><div class="k">Active</div><div class="v">${fmtMinutes(d.minutes_active)}</div></div>
            <div><div class="k">Est.</div><div class="v">${fmtPeso(pesoFromMinutes(d.minutes_active))}</div></div>
          </div>
        </div>`;
      }).join('')}</div>`;

  const requests = `<div class="card section">
    <div class="card-head"><h2>New game requests</h2><a class="btn sm" href="#requests">View all</a></div>
    ${newRequests.length === 0 ? '<div class="empty">No new requests.</div>' : `<div class="table-wrap"><table class="table"><tbody>
      ${newRequests.slice(0, 5).map(r => `<tr>
        <td style="width:140px" class="muted">${esc(fmtDateTime(r.created_at))}</td>
        <td style="width:120px">${esc(r.pc_name)}</td>
        <td><div class="req-title">${esc(r.title)}</div>${r.description ? `<div class="req-desc">${esc(r.description)}</div>` : ''}</td>
      </tr>`).join('')}
    </tbody></table></div>`}
  </div>`;

  root.innerHTML = kpis + cards + requests;
}

// ---------------------------------------------------------------------------
// Timeline
// ---------------------------------------------------------------------------

export async function renderTimeline(root, day) {
  let rows, status;
  try {
    [rows, status] = await Promise.all([api.timeline(day), api.pcStatus()]);
  } catch (e) {
    root.innerHTML = errorBox(e);
    return;
  }

  const start = dayStartMs(day);
  const end = start + 86400000;
  const isToday = day === todayStr();
  const clamp = (ms) => Math.min(end, Math.max(start, ms));
  const pct = (ms) => ((clamp(ms) - start) / 86400000) * 100;

  const segsByPc = groupBy(rows, 'pc_name');
  const statusByPc = new Map(status.map(s => [s.pc_name, s]));
  const pcs = [...new Set([...status.map(s => s.pc_name), ...segsByPc.keys()])].sort();

  const ticks = [0, 3, 6, 9, 12, 15, 18, 21, 24];
  const gridLines = ticks.slice(1, -1).map(h => `<div class="tl-grid" style="left:${(h / 24) * 100}%"></div>`).join('');
  const nowMarker = isToday ? `<div class="tl-now" style="left:${pct(Date.now())}%" data-tip="Now"></div>` : '';

  const pcRows = pcs.map(pc => {
    const segs = (segsByPc.get(pc) || []).map(s => ({ ...s, a: clamp(Date.parse(s.seg_start)), b: clamp(Date.parse(s.seg_end)) }));
    const onMin = segs.reduce((m, s) => m + (s.b - s.a) / 60000, 0);
    const activeMin = segs.filter(s => s.kind === 'active').reduce((m, s) => m + (s.b - s.a) / 60000, 0);
    const boots = groupBy(segs, 'power_on');
    let longest = 0;
    for (const group of boots.values()) {
      longest = Math.max(longest, (Math.max(...group.map(s => s.b)) - Math.min(...group.map(s => s.a))) / 60000);
    }
    const first = segs.length ? Math.min(...segs.map(s => s.a)) : null;
    const last = segs.length ? Math.max(...segs.map(s => s.b)) : null;
    const online = isToday && statusByPc.get(pc)?.is_online;

    const bars = segs.map(s => {
      const tip = `${s.program}\n${fmtTime(s.a)} - ${fmtTime(s.b)} (${fmtMinutes((s.b - s.a) / 60000)})`;
      return `<div class="tl-seg ${s.kind}" style="left:${pct(s.a)}%;width:${Math.max(0.15, pct(s.b) - pct(s.a))}%" data-tip="${esc(tip)}"></div>`;
    }).join('');

    const stats = segs.length ? `<div class="tl-stats">
        <span>First on <b>${fmtTime(first)}</b></span>
        <span>${online ? 'Still on' : `Last off <b>${fmtTime(last)}</b>`}</span>
        <span>Power-ons <b>${boots.size}</b></span>
        <span>Longest session <b>${fmtMinutes(longest)}</b></span>
        <span>On <b>${fmtMinutes(onMin)}</b></span>
        <span>Active <b>${fmtMinutes(activeMin)}</b></span>
      </div>` : `<div class="tl-stats"><span class="subtle">Off all day</span></div>`;

    return `<div class="tl-row">
      <div class="tl-name">${esc(pc)}<span class="subtle">${online ? 'Online now' : ''}</span></div>
      <div class="tl-bar">${gridLines}${bars}${nowMarker}</div>
      ${stats}
    </div>`;
  }).join('');

  root.innerHTML = `<div class="card">
    <div class="card-head">
      <h2>${esc(fmtDay(day, { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' }))}</h2>
      <div class="legend">
        <span><i style="background:var(--accent)"></i>Active (a program in use)</span>
        <span><i style="background:var(--idle)"></i>Idle (Chromatic Menu)</span>
        <span><i style="background:var(--surface-2);border:1px solid var(--border)"></i>Off</span>
      </div>
    </div>
    ${pcs.length === 0 ? '<div class="empty">No data for this day.</div>' : `<div class="timeline">
      <div class="tl-axis"><div class="label-col"></div><div class="ticks">${ticks.map(h => `<span style="left:${(h / 24) * 100}%">${String(h).padStart(2, '0')}:00</span>`).join('')}</div></div>
      ${pcRows}
    </div>`}
  </div>
  <p class="subtle" style="font-size:12px;margin-top:10px">Times are shown in ${esc(TZ)}. A gap longer than two heartbeat intervals is treated as the PC being off.</p>`;
}

// ---------------------------------------------------------------------------
// Programs
// ---------------------------------------------------------------------------

export async function renderPrograms(root, range) {
  let usage;
  try {
    usage = await api.usage(range.from, range.to);
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
    .map(([pc, rows]) => ({ pc, top: rows.sort((a, b) => b.minutes - a.minutes).slice(0, 3) }))
    .sort((a, b) => a.pc.localeCompare(b.pc));

  root.innerHTML = `<div class="grid kpis">
      ${kpi('Programs used', String(programs.length), 'Excluding Windows and the menu', 'app-window')}
      ${kpi('Active time', fmtMinutes(activeTotal), 'A program in the foreground', 'activity')}
      ${kpi('Idle in menu', fmtMinutes(idleTotal), 'Chromatic Menu in the foreground', 'layout-grid')}
      ${kpi('Windows tools', fmtMinutes(windowsTotal), 'Explorer, Settings and similar', 'monitor')}
    </div>
    <div class="grid cols-2">
      <div class="card">
        <div class="card-head"><h2>Top programs by time</h2></div>
        <div class="card-body">${top.length ? '<div class="chart-box"><canvas id="programsChart"></canvas></div>' : '<div class="empty">No program usage in this range.</div>'}</div>
      </div>
      <div class="card">
        <div class="card-head"><h2>Top programs per PC</h2></div>
        <div class="card-body flush">${perPc.length ? `<div class="table-wrap"><table class="table"><tbody>
          ${perPc.map(p => `<tr><td style="width:40%"><b>${esc(p.pc)}</b></td><td>${p.top.map(t => `<div>${esc(t.program)} <span class="subtle">${fmtMinutes(t.minutes)}</span></div>`).join('')}</td></tr>`).join('')}
        </tbody></table></div>` : '<div class="empty">No data.</div>'}</div>
      </div>
    </div>
    <div class="card section">
      <div class="card-head"><h2>All programs</h2></div>
      <div class="card-body flush">${programs.length ? `<div class="table-wrap"><table class="table">
        <thead><tr><th>#</th><th>Program</th><th class="num">Time</th><th>Share of active time</th><th class="num">PCs</th></tr></thead>
        <tbody>${programs.map((p, i) => `<tr>
          <td class="subtle">${i + 1}</td>
          <td>${esc(p.program)}</td>
          <td class="num">${fmtMinutes(p.minutes)}</td>
          <td style="width:32%"><div style="display:flex;align-items:center;gap:10px"><div class="bar-track" style="flex:1"><div class="bar-fill" style="width:${activeTotal ? (p.minutes / activeTotal) * 100 : 0}%"></div></div><span class="subtle" style="width:40px;text-align:right">${fmtPct(activeTotal ? p.minutes / activeTotal : 0)}</span></div></td>
          <td class="num">${p.pcs}</td>
        </tr>`).join('')}</tbody>
      </table></div>` : '<div class="empty">No program usage in this range.</div>'}</div>
    </div>`;

  if (top.length) {
    makeChart(root.querySelector('#programsChart'), {
      type: 'bar',
      data: {
        labels: top.map(p => p.program),
        datasets: [{ data: top.map(p => Math.round(p.minutes)), backgroundColor: cssVar('--accent'), borderRadius: 4, maxBarThickness: 22 }]
      },
      options: {
        indexAxis: 'y',
        plugins: { legend: { display: false }, tooltip: { callbacks: { label: (c) => ' ' + fmtMinutes(c.raw) } } },
        scales: {
          x: { beginAtZero: true, ticks: { stepSize: minuteStep(maxMin), callback: (v) => fmtMinutes(v) } },
          y: { grid: { display: false } }
        }
      }
    });
  }
}

// ---------------------------------------------------------------------------
// Revenue
// ---------------------------------------------------------------------------

export async function renderRevenue(root, range) {
  const days = daysInclusive(range.from, range.to);
  const prevTo = addDays(range.from, -1);
  const prevFrom = addDays(prevTo, -(days - 1));
  let daily, prev, heat;
  try {
    [daily, prev, heat] = await Promise.all([
      api.daily(range.from, range.to), api.daily(prevFrom, prevTo), api.heatmap(range.from, range.to)
    ]);
  } catch (e) {
    root.innerHTML = errorBox(e);
    return;
  }

  const activeTotal = sum(daily, 'minutes_active');
  const onTotal = sum(daily, 'minutes_on');
  const revenue = pesoFromMinutes(activeTotal);
  const prevRevenue = pesoFromMinutes(sum(prev, 'minutes_active'));
  const change = prevRevenue > 0 ? (revenue - prevRevenue) / prevRevenue : null;

  const deltaHtml = change === null
    ? `No data for ${esc(fmtDay(prevFrom))} - ${esc(fmtDay(prevTo))}`
    : `<span class="delta ${change >= 0 ? 'up' : 'down'}">${icon(change >= 0 ? 'trending-up' : 'trending-down')} ${change >= 0 ? '+' : ''}${Math.round(change * 100)}%</span> vs previous ${days} day${days === 1 ? '' : 's'}`;

  const pcs = [...new Set(daily.map(d => d.pc_name))].sort();
  const dayList = Array.from({ length: days }, (_, i) => addDays(range.from, i));
  const byDayPc = new Map(daily.map(d => [`${d.day}|${d.pc_name}`, d]));

  const perPc = pcs.map(pc => {
    const rows = daily.filter(d => d.pc_name === pc);
    const on = sum(rows, 'minutes_on');
    const active = sum(rows, 'minutes_active');
    return { pc, on, active, revenue: pesoFromMinutes(active) };
  }).sort((a, b) => b.revenue - a.revenue);

  const maxHeat = Math.max(0.0001, ...heat.map(h => Number(h.avg_active_pcs)));
  const heatByKey = new Map(heat.map(h => [`${h.weekday}|${h.hour}`, Number(h.avg_active_pcs)]));
  const weekdays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
  const pesoPerPcHour = 60 / minutesPerPeso();
  const heatmap = `<div class="heatmap">
    <div></div>${Array.from({ length: 24 }, (_, h) => `<div class="hm-hour">${h % 3 === 0 ? h : ''}</div>`).join('')}
    ${weekdays.map((name, i) => `<div class="hm-label">${name}</div>${Array.from({ length: 24 }, (_, h) => {
      const v = heatByKey.get(`${i + 1}|${h}`) || 0;
      const alpha = v > 0 ? 0.12 + 0.88 * (v / maxHeat) : 0;
      const tip = `${name} ${String(h).padStart(2, '0')}:00\n${v.toFixed(2)} PCs active on average\nabout ${fmtPeso(v * pesoPerPcHour)} per hour`;
      return `<div class="hm-cell" style="${v > 0 ? `background:color-mix(in srgb, var(--accent) ${Math.round(alpha * 100)}%, transparent)` : ''}" data-tip="${esc(tip)}"></div>`;
    }).join('')}`).join('')}
  </div>`;

  root.innerHTML = `<div class="alert" style="margin-bottom:16px">${icon('info')}<div>Estimate based on minutes a program other than Chromatic Menu was in the foreground, at ₱1 per ${minutesPerPeso()} minutes. The timer only turns off the monitor, so a game left open after time runs out still counts.</div></div>
    <div class="grid kpis">
      ${kpi('Estimated revenue', fmtPeso(revenue), deltaHtml, 'wallet')}
      ${kpi('Average per day', fmtPeso(revenue / days), `${days} day${days === 1 ? '' : 's'} in range`, 'activity')}
      ${kpi('Active time', fmtMinutes(activeTotal), `${fmtPct(onTotal ? activeTotal / onTotal : 0)} utilisation of on time`, 'clock')}
      ${kpi('Previous period', fmtPeso(prevRevenue), `${esc(fmtDay(prevFrom))} - ${esc(fmtDay(prevTo))}`, 'trending-up')}
    </div>
    <div class="card">
      <div class="card-head"><h2>Estimated revenue per day</h2></div>
      <div class="card-body">${daily.length ? '<div class="chart-box"><canvas id="revenueChart"></canvas></div>' : '<div class="empty">No data in this range.</div>'}</div>
    </div>
    <div class="grid cols-even section">
      <div class="card">
        <div class="card-head"><h2>Busy hours</h2><span class="subtle" style="font-size:12px">Average PCs in active use</span></div>
        <div class="card-body">${heatmap}</div>
      </div>
      <div class="card">
        <div class="card-head"><h2>By PC</h2></div>
        <div class="card-body flush">${perPc.length ? `<div class="table-wrap"><table class="table">
          <thead><tr><th>PC</th><th class="num">On</th><th class="num">Active</th><th class="num">Utilisation</th><th class="num">Estimate</th></tr></thead>
          <tbody>${perPc.map(p => `<tr>
            <td>${esc(p.pc)}</td><td class="num">${fmtMinutes(p.on)}</td><td class="num">${fmtMinutes(p.active)}</td>
            <td class="num">${fmtPct(p.on ? p.active / p.on : 0)}</td><td class="num"><b>${fmtPeso(p.revenue)}</b></td>
          </tr>`).join('')}</tbody>
        </table></div>` : '<div class="empty">No data.</div>'}</div>
      </div>
    </div>`;

  if (daily.length) {
    makeChart(root.querySelector('#revenueChart'), {
      type: 'bar',
      data: {
        labels: dayList.map(d => fmtDay(d)),
        datasets: pcs.map((pc, i) => ({
          label: pc,
          data: dayList.map(d => Math.round(pesoFromMinutes(byDayPc.get(`${d}|${pc}`)?.minutes_active || 0))),
          backgroundColor: seriesColor(i),
          borderRadius: 3,
          maxBarThickness: 36
        }))
      },
      options: {
        plugins: {
          legend: { position: 'bottom', labels: { boxWidth: 10, boxHeight: 10 } },
          tooltip: { callbacks: { label: (c) => ` ${c.dataset.label}: ${fmtPeso(c.raw)}` } }
        },
        scales: {
          x: { stacked: true, grid: { display: false } },
          y: { stacked: true, beginAtZero: true, ticks: { callback: (v) => fmtPeso(v) } }
        }
      }
    });
  }
}

// ---------------------------------------------------------------------------
// Game requests
// ---------------------------------------------------------------------------

export async function renderRequests(root, filter, onChanged) {
  let rows;
  try {
    rows = await api.requests(filter);
  } catch (e) {
    root.innerHTML = errorBox(e);
    return;
  }

  const filters = [['new', 'New'], ['added', 'Added'], ['rejected', 'Rejected'], ['all', 'All']];
  const actions = (r) => {
    const btn = (status, label) => `<button class="btn sm" data-id="${esc(r.id)}" data-status="${status}">${label}</button>`;
    if (r.status === 'new') return btn('added', 'Mark added') + btn('rejected', 'Reject');
    return btn('new', 'Move to new');
  };

  root.innerHTML = `<div style="margin-bottom:14px" class="seg" id="reqFilter">
      ${filters.map(([k, label]) => `<button data-filter="${k}" class="${k === filter ? 'active' : ''}">${label}</button>`).join('')}
    </div>
    <div class="card">
      ${rows.length === 0 ? '<div class="empty">No requests here.</div>' : `<div class="table-wrap"><table class="table">
        <thead><tr><th>Received</th><th>PC</th><th>Request</th><th>Status</th><th></th></tr></thead>
        <tbody>${rows.map(r => `<tr>
          <td class="muted" style="white-space:nowrap">${esc(fmtDateTime(r.created_at))}</td>
          <td style="white-space:nowrap">${esc(r.pc_name)}<div class="subtle" style="font-size:12px">${esc(r.menu_name)}</div></td>
          <td><div class="req-title">${esc(r.title)}</div>${r.description ? `<div class="req-desc">${esc(r.description)}</div>` : ''}</td>
          <td><span class="badge ${esc(r.status)}">${esc(r.status[0].toUpperCase() + r.status.slice(1))}</span></td>
          <td><div class="req-actions">${actions(r)}</div></td>
        </tr>`).join('')}</tbody>
      </table></div>`}
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
        <label class="field"><span>Minutes of use per ₱1</span>
          <input class="input" id="mpp" type="number" min="1" max="120" step="1" value="${minutesPerPeso()}" style="max-width:160px">
        </label>
        <div class="hint">Default is 9 minutes per peso. Used for every revenue figure in the dashboard.</div>
        <div style="margin-top:14px"><button class="btn primary" id="saveMpp">Save</button> <span id="mppMsg" class="subtle" style="margin-left:8px"></span></div>
      </div>
    </div>
    <div class="card">
      <div class="card-head"><h2>Appearance</h2></div>
      <div class="card-body">
        <div class="seg" id="themeSeg">
          <button data-theme="dark" class="${ctx.theme === 'dark' ? 'active' : ''}">Dark</button>
          <button data-theme="light" class="${ctx.theme === 'light' ? 'active' : ''}">Light</button>
        </div>
      </div>
    </div>
    <div class="card">
      <div class="card-head"><h2>Connection</h2></div>
      <div class="card-body">
        <div class="subtle" style="font-size:12px">Supabase project</div>
        <div class="mono" style="margin-bottom:12px;word-break:break-all">${esc(ctx.url)}</div>
        <div class="subtle" style="font-size:12px">Signed in as</div>
        <div style="margin-bottom:16px">${esc(ctx.email || '')}</div>
        <div style="display:flex;gap:8px;flex-wrap:wrap">
          <button class="btn" id="logoutBtn">${icon('log-out')}Log out</button>
          <button class="btn danger" id="changeProjectBtn">${icon('database')}Change project</button>
        </div>
        <div class="hint">You stay signed in on this browser until you log out.</div>
      </div>
    </div>
    <div class="card">
      <div class="card-head"><h2>Help</h2></div>
      <div class="card-body">
        <p class="muted" style="margin-top:0">Setup steps, the database schema and troubleshooting are in the setup guide.</p>
        <a class="btn" href="setup.html">${icon('book-open')}Open setup guide</a>
      </div>
    </div>
  </div>`;

  root.querySelector('#saveMpp').addEventListener('click', () => {
    const v = Number(root.querySelector('#mpp').value);
    const msg = root.querySelector('#mppMsg');
    if (!(v >= 1 && v <= 120)) { msg.textContent = 'Enter a number from 1 to 120.'; return; }
    store.set('minutesPerPeso', String(v));
    msg.textContent = 'Saved.';
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
