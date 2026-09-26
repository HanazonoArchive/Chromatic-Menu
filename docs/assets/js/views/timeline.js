import { api } from '../api.js';
import { icon } from '../icons.js';
import {
  esc, todayStr, dayStartMs, fmtDay, fmtTime, fmtDateTime, fmtMinutes, fmtMoney, fmtPct,
  moneyFromMinutes, rateText, cssVar, TZ, IDLE_PROGRAMS, recordPcShops, isStale
} from '../util.js';
import {
  clampSegments, concurrencySteps, concurrencySlots, peakOf, minutesAtLeast, perPcDay
} from '../analytics.js';
import { makeChart, withAlpha, errorBox, empty, kpi, card, shopTag } from './common.js';

function segLabel(s) {
  if (s.kind === 'active') return s.program === 'Windows' ? 'Windows app' : s.program;
  return s.program === 'Chromatic Menu' ? 'Idle in menu' : 'Idle (no app reported)';
}

export async function renderTimeline(root, day) {
  let rows, status, incidents;
  try {
    [rows, status, incidents] = await Promise.all([api.timeline(day), api.pcStatus(), api.networkIncidents(day)]);
  } catch (e) {
    root.innerHTML = errorBox(e);
    return;
  }
  if (!root.isConnected) return;
  recordPcShops(status);

  const start = dayStartMs(day);
  const isToday = day === todayStr();
  const pct = (ms) => ((ms - start) / 86400000) * 100;
  const statusByPc = new Map(status.map(s => [s.pc_name, s]));
  const online = (pc) => isToday && !!statusByPc.get(pc)?.is_online;

  const segs = clampSegments(rows, day);
  const byPc = perPcDay(segs, online);
  // Show every PC that reported this day, plus current PCs that stayed off.
  const pcs = [...new Set([...byPc.keys(), ...status.filter(s => !isStale(s.last_seen)).map(s => s.pc_name)])]
    .sort((x, y) => x.localeCompare(y, undefined, { numeric: true }));

  let dayOn = 0, dayActive = 0, boots = 0, stops = 0;
  for (const p of byPc.values()) { dayOn += p.onMin; dayActive += p.activeMin; boots += p.boots; stops += p.midGameStops; }
  const pcsUsed = byPc.size;
  const steps = concurrencySteps(segs);
  const peak = peakOf(steps);
  const fleet = Math.max(1, pcsUsed);
  const fullHouse = minutesAtLeast(steps, fleet);
  const busy = minutesAtLeast(steps, Math.ceil(fleet * 0.75));
  const firstOn = segs.length ? Math.min(...segs.map(s => s.a)) : null;
  const lastOff = segs.length ? Math.max(...segs.map(s => s.b)) : null;
  const stillOpen = isToday && pcs.some(online);
  const openMin = firstOn ? (lastOff - firstOn) / 60000 : 0;

  const kpis = `<div class="kpis">
    ${kpi({ label: 'Estimated revenue', iconName: 'wallet', tone: 'primary', value: fmtMoney(moneyFromMinutes(dayActive)), foot: `<span><b>${fmtMinutes(dayActive)}</b> active &middot; ${fmtPct(dayOn ? dayActive / dayOn : 0)} of on-time</span>` })}
    ${kpi({ label: 'Shop hours', iconName: 'clock', value: firstOn ? `${fmtTime(firstOn)}<span class="of"> &ndash; ${stillOpen ? 'now' : fmtTime(lastOff)}</span>` : '&mdash;', foot: firstOn ? `<span>Open ${fmtMinutes(openMin)} &middot; first PC on to last PC off</span>` : '<span class="subtle">No PC was turned on</span>' })}
    ${kpi({ label: 'Peak', iconName: 'users', value: peak ? `${peak.count}<span class="of">/${fleet}</span>` : '&mdash;', foot: peak ? `<span>at ${fmtTime(peak.a)} &middot; full house ${fmtMinutes(fullHouse)}</span>` : '<span class="subtle">No active play</span>' })}
    ${kpi({ label: 'Power-ons', iconName: 'power', value: String(boots), foot: stops ? `<span class="text-warn">${icon('alert-triangle')}${stops} turned off mid-game</span>` : `<span>${pcsUsed} of ${pcs.length} PCs used</span>` })}
  </div>`;

  const ticks = [0, 3, 6, 9, 12, 15, 18, 21, 24];
  const gridLines = ticks.slice(1, -1).map(h => `<div class="tl-grid" style="left:${(h / 24) * 100}%"></div>`).join('');
  const nowMarker = isToday ? `<div class="tl-now" style="left:${pct(Date.now())}%" data-tip="Now ${fmtTime(Date.now())}"></div>` : '';

  const pcRows = pcs.map(pc => {
    const p = byPc.get(pc);
    const on = online(pc);
    const st = !on ? 'off' : IDLE_PROGRAMS.has(statusByPc.get(pc)?.last_program) ? 'idle' : 'use';
    const pill = isToday ? `<span class="state ${st}"><span class="dot"></span>${{ use: 'In use', idle: 'Idle', off: 'Off' }[st]}</span>` : '';
    if (!p) {
      return `<div class="tl-row off">
        <div class="tl-station"><div class="tl-station-top"><span class="tl-station-name">${esc(pc)}</span>${pill}</div>${shopTag(pc, statusByPc.get(pc)?.menu_name)}</div>
        <div class="tl-bar">${gridLines}${nowMarker}</div>
        <div class="tl-stats"><span>Off all day</span></div>
      </div>`;
    }
    const bars = p.segs.map(s => {
      const tip = `${segLabel(s)}\n${fmtTime(s.a)} - ${fmtTime(s.b)}  (${fmtMinutes((s.b - s.a) / 60000)})`;
      return `<div class="tl-seg ${s.kind}" style="left:${pct(s.a)}%;width:${Math.max(0.15, pct(s.b) - pct(s.a))}%" data-tip="${esc(tip)}"></div>`;
    }).join('');
    return `<div class="tl-row">
      <div class="tl-station">
        <div class="tl-station-top"><span class="tl-station-name" title="${esc(pc)}">${esc(pc)}</span>${pill}</div>
        ${shopTag(pc, statusByPc.get(pc)?.menu_name)}
        ${p.midGameStops ? `<span class="issue-tag" title="The PC stopped sending heartbeats while a game was open: power cut, crash, or forced shutdown.">${icon('alert-triangle')}${p.midGameStops} off mid-game</span>` : ''}
      </div>
      <div class="tl-bar">${gridLines}${bars}${nowMarker}</div>
      <div class="tl-stats">
        <span class="hl">Active<b>${fmtMinutes(p.activeMin)}</b></span>
        <span>Est.<b>${fmtMoney(moneyFromMinutes(p.activeMin))}</b></span>
        <span>On<b>${fmtMinutes(p.onMin)}</b></span>
        <span>${fmtTime(p.first)} &ndash; ${on ? 'now' : fmtTime(p.last)}</span>
        <span>Power-ons<b${p.boots >= 5 ? ' class="warn-val"' : ''}>${p.boots}</b></span>
        <span>Longest on<b>${fmtMinutes(p.longest)}</b></span>
      </div>
    </div>`;
  }).join('');

  const incidentList = Array.isArray(incidents) ? incidents.filter(i => (i.delay_seconds || 0) >= 120) : null;
  const networkHtml = !incidentList ? '' : incidentList.length === 0
    ? `<div class="alert ok section">${icon('wifi')}<div><b>Connection stable.</b> No PC had to hold back heartbeats because of a network drop.</div></div>`
    : card({
      title: 'Network drops',
      meta: `${incidentList.length} delayed upload${incidentList.length === 1 ? '' : 's'} &middot; the PC kept counting and sent the data once back online`,
      cls: 'section',
      flush: true,
      body: `<div class="table-wrap"><table class="table">
        <thead><tr><th>Computer</th><th>When</th><th class="num">Offline for about</th><th>Program</th></tr></thead>
        <tbody>${incidentList.slice(0, 12).map(i => `<tr>
          <td class="strong">${esc(i.pc_name)}</td>
          <td>${esc(fmtDateTime(i.incident_time))}</td>
          <td class="num">${fmtMinutes(Math.max(1, (i.delay_seconds || 0) / 60))}</td>
          <td class="muted">${esc(i.program || '')}</td>
        </tr>`).join('')}</tbody></table></div>`
    });

  root.innerHTML = `${kpis}
    ${card({
      title: 'PCs in use through the day',
      meta: peak ? `Busy (&ge;75% of PCs) for ${fmtMinutes(busy)} &middot; every PC in use for ${fmtMinutes(fullHouse)}` : 'Average PCs in active use per 15 minutes',
      cls: 'section',
      body: segs.length ? '<div class="chart-box sm"><canvas id="concurrencyChart"></canvas></div>' : empty('No activity on this day.', 'activity')
    })}
    ${card({
      title: esc(fmtDay(day, { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' })),
      actions: `<div class="legend">
        <span><i style="background:var(--accent)"></i>In use</span>
        <span><i style="background:var(--idle)"></i>Idle in menu</span>
        <span><i style="background:var(--surface-2);border:1px solid var(--border-strong)"></i>Off</span>
      </div>`,
      cls: 'section',
      flush: true,
      body: pcs.length === 0 ? empty('No data for this day.', 'clock') : `<div class="tl">
        <div class="tl-axis"><div class="label-col"></div><div class="ticks">${ticks.map(h => `<span style="left:${(h / 24) * 100}%">${String(h).padStart(2, '0')}:00</span>`).join('')}</div></div>
        ${pcRows}
      </div>`
    })}
    ${networkHtml}
    <p class="footnote">Times in ${esc(TZ)}. A gap longer than two heartbeat intervals counts as the PC being off. Revenue at ${esc(rateText())}. Hover or tap a bar for details.</p>`;

  const canvas = root.querySelector('#concurrencyChart');
  if (canvas) {
    const slots = concurrencySlots(steps, day);
    const nowSlot = isToday ? Math.floor((Date.now() - start) / (15 * 60000)) : 96;
    makeChart(canvas, {
      type: 'bar',
      data: {
        labels: slots.map((_, i) => fmtTime(start + i * 15 * 60000)),
        datasets: [{
          label: 'PCs in use',
          data: slots.map((v, i) => (i <= nowSlot ? Math.round(v * 10) / 10 : null)),
          backgroundColor: slots.map(v => (v >= fleet * 0.75 ? cssVar('--accent') : withAlpha(cssVar('--accent'), 0.45))),
          borderRadius: 2,
          categoryPercentage: 1,
          barPercentage: 0.85
        }]
      },
      options: {
        plugins: {
          legend: { display: false },
          tooltip: { callbacks: { label: (c) => ` ${c.raw ?? 0} of ${fleet} PCs in use (${Math.round(((c.raw || 0) / fleet) * 100)}%)` } }
        },
        scales: {
          x: { grid: { display: false }, ticks: { autoSkip: false, maxRotation: 0, callback: (v, i) => (i % 12 === 0 ? `${String(i / 4).padStart(2, '0')}:00` : '') } },
          y: { beginAtZero: true, suggestedMax: fleet, ticks: { precision: 0 } }
        }
      }
    });
  }
}
