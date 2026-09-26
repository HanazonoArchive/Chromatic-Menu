import { api } from '../api.js';
import { icon } from '../icons.js';
import {
  esc, todayStr, addDays, dayStartMs, fmtTime, fmtDateTime, fmtMinutes, fmtAgo, fmtMoney, fmtPct, fmtHour,
  moneyFromMinutes, cssVar, IDLE_PROGRAMS, formatProgramName, recordPcShops, isStale, staleDays
} from '../util.js';
import {
  clampSegments, activeMinutesBefore, concurrencySteps, concurrencySlots, peakOf, programMinutes, perPcDay, minutesOf
} from '../analytics.js';
import { makeChart, errorBox, empty, delta, kpi, meter, card, shopTag } from './common.js';

// Survive the one-minute auto refresh.
const view = { filter: 'all', showStale: false };

function pcState(s) {
  if (!s.is_online) return 'off';
  return IDLE_PROGRAMS.has(s.last_program) ? 'idle' : 'use';
}

const STATE_LABEL = { use: 'In use', idle: 'Idle', off: 'Offline' };

// Yesterday's timeline only feeds the dashed chart line and barely changes,
// so it is fetched once every 30 minutes instead of on every refresh.
const yesterdayCache = { day: null, at: 0, rows: null };
async function yesterdayTimeline(day) {
  if (yesterdayCache.day !== day || Date.now() - yesterdayCache.at > 30 * 60000) {
    yesterdayCache.rows = await api.timeline(day);
    yesterdayCache.day = day;
    yesterdayCache.at = Date.now();
  }
  return yesterdayCache.rows;
}

export async function renderOverview(root) {
  const today = todayStr();
  const yesterday = addDays(today, -1);
  let status, daily, tlToday, tlYesterday, soFar, newRequests, newCount;
  try {
    [status, daily, tlToday, tlYesterday, soFar, newRequests, newCount] = await Promise.all([
      api.pcStatus(), api.daily(today, today), api.timeline(today), yesterdayTimeline(yesterday),
      api.activeSoFar(), api.requests('new', 3), api.newRequestCount()
    ]);
  } catch (e) {
    root.innerHTML = errorBox(e);
    return;
  }
  if (!root.isConnected) return;

  recordPcShops(status);
  recordPcShops(daily);

  const now = Date.now();
  const onlineSet = new Set(status.filter(s => s.is_online).map(s => s.pc_name));
  const segsToday = clampSegments(tlToday, today);
  const segsYesterday = clampSegments(tlYesterday, yesterday);
  const pcDay = perPcDay(segsToday, pc => onlineSet.has(pc));
  const byPcDaily = new Map(daily.map(d => [d.pc_name, d]));

  const current = status.filter(s => !isStale(s.last_seen));
  const stale = status.filter(s => isStale(s.last_seen));
  const counts = { use: 0, idle: 0, off: 0 };
  for (const s of current) counts[pcState(s)]++;

  // Money uses the stored daily totals (same numbers as the Revenue page).
  // "Same time yesterday" comes from get_active_so_far; older schemas without
  // it fall back to cutting both timelines at the same clock time.
  const onTotal = daily.reduce((m, d) => m + Number(d.minutes_on || 0), 0);
  const activeTotal = daily.reduce((m, d) => m + Number(d.minutes_active || 0), 0);
  const revenue = moneyFromMinutes(activeTotal);
  const elapsed = now - dayStartMs(today);
  const compare = soFar
    ? { today: Number(soFar.today_minutes), sameTime: Number(soFar.yesterday_same_time_minutes), total: Number(soFar.yesterday_minutes) }
    : {
      today: minutesOf(segsToday, s => s.kind === 'active'),
      sameTime: activeMinutesBefore(segsYesterday, dayStartMs(yesterday) + elapsed),
      total: minutesOf(segsYesterday, s => s.kind === 'active')
    };
  const change = compare.sameTime > 0 ? compare.today / compare.sameTime - 1 : null;
  const yesterdayTotal = moneyFromMinutes(compare.total);

  const stepsToday = concurrencySteps(segsToday);
  const peak = peakOf(stepsToday);
  const fleet = Math.max(1, current.length);

  // ---- Attention strip: only things worth acting on ----
  // A pisonet PC is switched off when the coin time runs out, usually with the
  // game still open, so going offline is normal and shown neutrally.
  const attention = [];
  const offline = current.filter(s => !s.is_online);
  if (counts.use + counts.idle > 0 && offline.length) {
    const list = offline.map(s => `${esc(s.pc_name)} (${esc(fmtAgo(s.last_seen))})`).join(', ');
    attention.push(`<a class="chip muted" href="#timeline">${icon('power')}<span><b>${offline.length}</b> off while the shop is open: ${list}</span></a>`);
  } else if (current.length && counts.use + counts.idle === 0) {
    const lastSeen = Math.max(...current.map(s => Date.parse(s.last_seen)));
    attention.push(`<span class="chip muted">${icon('moon')}<span>All PCs are off &middot; last activity ${esc(fmtAgo(new Date(lastSeen).toISOString()))}</span></span>`);
  }
  if (newCount) {
    attention.push(`<a class="chip info" href="#requests">${icon('message-square')}<span><b>${newCount}</b> new game request${newCount === 1 ? '' : 's'}</span>${icon('arrow-right')}</a>`);
  }
  if (!attention.length && current.length) {
    attention.push(`<span class="chip ok">${icon('check')}<span>All ${current.length} PCs online, nothing waiting</span></span>`);
  }

  // ---- KPIs ----
  const kpis = `<div class="kpis">
    ${kpi({
      label: 'Revenue today', iconName: 'wallet', tone: 'primary',
      value: fmtMoney(revenue),
      foot: `${delta(change, 'vs same time yesterday') || '<span class="subtle">No data for yesterday</span>'}<span class="subtle">Yesterday total ${fmtMoney(yesterdayTotal)}</span>`
    })}
    ${kpi({
      label: 'In use now', iconName: 'monitor',
      value: `${counts.use}<span class="of">/${current.length}</span>`,
      foot: `<span class="legend-dot idle"></span>${counts.idle} idle<span class="legend-dot off"></span>${counts.off} offline`
    })}
    ${kpi({
      label: 'Utilisation today', iconName: 'activity',
      value: fmtPct(onTotal ? activeTotal / onTotal : 0),
      foot: `<div class="kpi-meter">${meter(activeTotal, onTotal)}<span><b>${fmtMinutes(activeTotal)}</b> active of ${fmtMinutes(onTotal)} on</span></div>`
    })}
    ${kpi({
      label: 'Peak today', iconName: 'users',
      value: peak ? `${peak.count}<span class="of">/${fleet}</span>` : '&mdash;',
      foot: peak ? `<span>PCs in use at ${esc(fmtTime(peak.a))}</span>` : '<span class="subtle">Nobody has played yet</span>'
    })}
  </div>`;

  // ---- Floor ----
  const visible = (view.showStale ? status : current)
    .filter(s => view.filter === 'all' || pcState(s) === view.filter)
    .sort((x, y) => {
      const order = { use: 0, idle: 1, off: 2 };
      return order[pcState(x)] - order[pcState(y)] || x.pc_name.localeCompare(y.pc_name, undefined, { numeric: true });
    });

  const tiles = visible.map(s => {
    const st = pcState(s);
    const d = byPcDaily.get(s.pc_name) || { minutes_on: 0, minutes_active: 0 };
    const cur = pcDay.get(s.pc_name)?.current;
    const since = cur && cur.program === s.last_program ? fmtMinutes((now - cur.a) / 60000) : null;
    let sub;
    if (st === 'use') sub = since ? `Playing for <b>${since}</b>` : 'In use';
    else if (st === 'idle') sub = since ? `Idle for <b>${since}</b>` : 'Waiting in menu';
    else sub = `Last seen ${esc(fmtAgo(s.last_seen))}`;
    const staleCls = isStale(s.last_seen) ? ' stale' : '';
    return `<div class="tile ${st}${staleCls}">
      <div class="tile-top">
        <span class="tile-name" title="${esc(s.pc_name)}">${esc(s.pc_name)}</span>
        <span class="state ${st}"><span class="dot"></span>${STATE_LABEL[st]}</span>
      </div>
      ${shopTag(s.pc_name, s.menu_name)}
      <div class="tile-program" title="${esc(s.last_program)}">${esc(st === 'off' ? (s.last_program && !IDLE_PROGRAMS.has(s.last_program) ? s.last_program : 'Off') : formatProgramName(s.last_program, true))}</div>
      <div class="tile-sub">${sub}</div>
      <div class="tile-foot">
        ${meter(Number(d.minutes_active), Number(d.minutes_on))}
        <div class="tile-nums"><span>${fmtMinutes(d.minutes_active)} active today</span><b>${fmtMoney(moneyFromMinutes(d.minutes_active))}</b></div>
      </div>
    </div>`;
  }).join('');

  const filters = [['all', 'All', current.length], ['use', 'In use', counts.use], ['idle', 'Idle', counts.idle], ['off', 'Offline', counts.off]];
  const floorActions = `<div class="seg sm" id="floorFilter" role="tablist">${filters.map(([k, l, n]) =>
    `<button data-f="${k}" class="${view.filter === k ? 'active' : ''}" role="tab" aria-selected="${view.filter === k}">${l}<span class="count">${n}</span></button>`).join('')}</div>`;

  const floorBody = status.length === 0
    ? empty('No heartbeats yet. Once a PC with telemetry turned on starts, it appears here within a minute.', 'monitor')
    : `${visible.length ? `<div class="floor">${tiles}</div>` : empty('No PCs in this state right now.', 'monitor')}
      ${stale.length ? `<button class="link-btn" id="staleToggle">${icon(view.showStale ? 'chevron-up' : 'chevron-down')}${view.showStale ? 'Hide' : 'Show'} ${stale.length} inactive PC${stale.length === 1 ? '' : 's'} (not seen in ${staleDays()}+ days)</button>` : ''}`;

  const floor = card({
    title: 'Floor',
    meta: 'Refreshes every minute',
    actions: floorActions,
    body: floorBody,
    cls: 'section floor-card'
  });

  // ---- Activity chart + top programs ----
  const top = programMinutes(segsToday).slice(0, 6);
  const topMax = top.length ? top[0].minutes : 1;
  const topList = top.length ? `<ol class="rank-list">${top.map((p, i) => `<li>
      <span class="rank">${i + 1}</span>
      <div><div class="name" title="${esc(p.program)}">${esc(p.program)}</div><div class="bar-track"><div class="bar-fill" style="width:${(p.minutes / topMax) * 100}%"></div></div></div>
      <div class="val">${fmtMinutes(p.minutes)}<span class="meta">${fmtMoney(moneyFromMinutes(p.minutes))}</span></div>
    </li>`).join('')}</ol>` : empty('No games played yet today.', 'gamepad-2');

  const requestsHtml = newRequests.length === 0 ? '' : card({
    title: 'Waiting for review',
    meta: `${newCount} new`,
    actions: `<a class="btn sm" href="#requests">Review ${icon('arrow-right')}</a>`,
    flush: true,
    cls: 'section',
    body: `<ul class="feed">${newRequests.map(r => `<li class="is-new"><div>
        <div class="title">${esc(r.title)}</div>
        ${r.description ? `<div class="desc">${esc(r.description)}</div>` : ''}
        <div class="meta"><span class="strong">${esc(r.pc_name)}</span><span>${esc(fmtDateTime(r.created_at))}</span></div>
      </div></li>`).join('')}</ul>`
  });

  root.innerHTML = `<div class="attention">${attention.join('')}</div>
    ${kpis}
    ${floor}
    <div class="grid cols-2 section">
      ${card({
        title: 'Activity today',
        meta: 'Average PCs in use, every 15 minutes',
        actions: `<div class="legend"><span><i style="background:var(--accent)"></i>Today</span><span><i class="dashed"></i>Yesterday</span></div>`,
        body: stepsToday.length || segsYesterday.length ? '<div class="chart-box sm"><canvas id="activityChart"></canvas></div>' : empty('No activity yet today.', 'activity')
      })}
      ${card({ title: 'Top games today', meta: 'By active time', body: topList, flush: true })}
    </div>
    ${requestsHtml}`;

  root.querySelector('#floorFilter')?.addEventListener('click', (e) => {
    const b = e.target.closest('button[data-f]');
    if (!b) return;
    view.filter = b.dataset.f;
    renderOverview(root);
  });
  root.querySelector('#staleToggle')?.addEventListener('click', () => {
    view.showStale = !view.showStale;
    renderOverview(root);
  });

  const canvas = root.querySelector('#activityChart');
  if (canvas) {
    const slotsToday = concurrencySlots(stepsToday, today);
    const slotsYesterday = concurrencySlots(concurrencySteps(segsYesterday), yesterday);
    const nowSlot = Math.floor(elapsed / (15 * 60000));
    const labels = slotsToday.map((_, i) => fmtTime(dayStartMs(today) + i * 15 * 60000));
    makeChart(canvas, {
      type: 'line',
      data: {
        labels,
        datasets: [
          {
            label: 'Today', data: slotsToday.map((v, i) => (i <= nowSlot ? Math.round(v * 10) / 10 : null)),
            borderColor: cssVar('--accent'), backgroundColor: cssVar('--accent-soft'), fill: true,
            tension: 0.3, pointRadius: 0, pointHoverRadius: 4, borderWidth: 2
          },
          {
            label: 'Yesterday', data: slotsYesterday.map(v => Math.round(v * 10) / 10),
            borderColor: cssVar('--text-3'), borderDash: [4, 4], fill: false,
            tension: 0.3, pointRadius: 0, pointHoverRadius: 3, borderWidth: 1.5
          }
        ]
      },
      options: {
        plugins: {
          legend: { display: false },
          tooltip: { callbacks: { label: (c) => ` ${c.dataset.label}: ${c.raw ?? 0} of ${fleet} PCs` } }
        },
        scales: {
          x: { grid: { display: false }, ticks: { autoSkip: false, maxRotation: 0, callback: (v, i) => (i % 12 === 0 ? fmtHour(i / 4) : '') } },
          y: { beginAtZero: true, suggestedMax: fleet, ticks: { precision: 0 } }
        }
      }
    });
  }
}
