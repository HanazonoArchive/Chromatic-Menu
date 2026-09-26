import { api } from '../api.js';
import { icon } from '../icons.js';
import {
  esc, todayStr, addDays, daysInclusive, dayStartMs, fmtDay, fmtHour, fmtMinutes, fmtMoney, fmtPct,
  minutesPerUnit, moneyFromMinutes, rateText, seriesColor, sum, NON_PROGRAMS, recordPcShops, getShopForPc,
  startOfWeek, endOfWeek, fmtWeekRange, fmtMonth, downloadCsv
} from '../util.js';
import { clampSegments, activeMinutesBefore, minutesOf } from '../analytics.js';
import { makeChart, errorBox, empty, delta, kpi, card, shopTag } from './common.js';

const money2 = (minutes) => Math.round(moneyFromMinutes(minutes) * 100) / 100;

function rollup(daily, keyOf, labelOf) {
  const map = new Map();
  for (const r of daily) {
    const k = keyOf(r.day);
    if (!map.has(k)) map.set(k, { key: k, label: labelOf(r.day), days: new Set(), active: 0, on: 0 });
    const g = map.get(k);
    g.days.add(r.day);
    g.active += Number(r.minutes_active || 0);
    g.on += Number(r.minutes_on || 0);
  }
  const list = [...map.values()].sort((x, y) => x.key.localeCompare(y.key));
  list.forEach((g, i) => {
    g.revenue = moneyFromMinutes(g.active);
    const prev = i > 0 ? list[i - 1].revenue : 0;
    g.growth = prev > 0 ? g.revenue / prev - 1 : null;
  });
  return list;
}

export async function renderRevenue(root, range) {
  const today = todayStr();
  const days = daysInclusive(range.from, range.to);
  const isSingleDay = days === 1;
  const prevTo = addDays(range.from, -1);
  const prevFrom = addDays(prevTo, -(days - 1));
  const includesToday = range.to >= today;

  const isToday = isSingleDay && range.from === today;
  let daily, prev, heat, usage, tl = [], soFar = null;
  try {
    [daily, prev, heat, usage, tl, soFar] = await Promise.all([
      api.daily(range.from, range.to),
      api.daily(prevFrom, prevTo),
      days >= 7 ? api.heatmap(range.from, range.to) : Promise.resolve([]),
      api.usage(range.from, range.to),
      isSingleDay ? api.timeline(range.from) : Promise.resolve([]),
      isToday ? api.activeSoFar() : Promise.resolve(null)
    ]);
  } catch (e) {
    root.innerHTML = errorBox(e);
    return;
  }
  if (!root.isConnected) return;
  recordPcShops(daily);

  const activeTotal = sum(daily, 'minutes_active');
  const onTotal = sum(daily, 'minutes_on');
  const revenue = moneyFromMinutes(activeTotal);
  const prevRevenue = moneyFromMinutes(sum(prev, 'minutes_active'));
  let change = prevRevenue > 0 ? revenue / prevRevenue - 1 : null;
  let changeLabel = isSingleDay ? `vs ${fmtDay(prevTo)} (${fmtMoney(prevRevenue)})` : `vs previous ${days} days (${fmtMoney(prevRevenue)})`;

  // Today is still running: compare with yesterday up to the same clock time.
  // Older schemas without get_active_so_far fetch yesterday's timeline instead.
  if (isToday) {
    let todayMin, sameTime;
    if (soFar) {
      todayMin = Number(soFar.today_minutes);
      sameTime = Number(soFar.yesterday_same_time_minutes);
    } else {
      const segsPrev = clampSegments(await api.timeline(prevTo), prevTo);
      todayMin = minutesOf(clampSegments(tl, today), s => s.kind === 'active');
      sameTime = activeMinutesBefore(segsPrev, dayStartMs(prevTo) + (Date.now() - dayStartMs(today)));
    }
    if (!root.isConnected) return;
    change = sameTime > 0 ? todayMin / sameTime - 1 : null;
    changeLabel = `vs same time yesterday (${fmtMoney(moneyFromMinutes(sameTime))})`;
  }

  const pcs = [...new Set(daily.map(d => d.pc_name))].sort((x, y) => x.localeCompare(y, undefined, { numeric: true }));
  const color = (pc) => seriesColor(pcs.indexOf(pc));
  const dayList = Array.from({ length: days }, (_, i) => addDays(range.from, i));
  const byDayPc = new Map(daily.map(d => [`${d.day}|${d.pc_name}`, d]));
  const perDay = dayList.map(d => ({ day: d, revenue: moneyFromMinutes(sum(daily.filter(r => r.day === d), 'minutes_active')) }));
  const best = perDay.reduce((a, b) => (b.revenue > (a?.revenue ?? 0) ? b : a), null);
  // Days that have finished (or today, if it is the only day) drive averages.
  const completeDays = includesToday && !isSingleDay ? Math.max(1, days - 1) : days;
  const completeRevenue = includesToday && !isSingleDay ? revenue - (perDay.find(d => d.day === today)?.revenue || 0) : revenue;
  const dailyAvg = completeRevenue / completeDays;

  // A range that ends today would compare a partial day with a full one:
  // compare the finished days with the same number of days before them.
  if (includesToday && !isSingleDay) {
    const prevComparable = moneyFromMinutes(sum(prev.filter(r => r.day > prevFrom), 'minutes_active'));
    change = prevComparable > 0 ? completeRevenue / prevComparable - 1 : null;
    changeLabel = `finished days vs the ${completeDays} before (${fmtMoney(prevComparable)})`;
  }

  const perPc = pcs.map(pc => {
    const rows = daily.filter(d => d.pc_name === pc);
    const on = sum(rows, 'minutes_on');
    const active = sum(rows, 'minutes_active');
    return { pc, shop: rows[rows.length - 1]?.menu_name || getShopForPc(pc), on, active, revenue: moneyFromMinutes(active) };
  }).sort((a, b) => b.revenue - a.revenue);

  // ---- KPIs ----
  let kpis;
  if (isSingleDay) {
    kpis = [
      kpi({ label: isToday ? 'Revenue today' : `Revenue ${fmtDay(range.from)}`, iconName: 'wallet', tone: 'primary', value: fmtMoney(revenue), foot: delta(change, changeLabel) || `<span class="subtle">${esc(changeLabel)}</span>` }),
      kpi({ label: 'Paid active time', iconName: 'zap', value: fmtMinutes(activeTotal), foot: `<span>of ${fmtMinutes(onTotal)} on &middot; ${fmtMinutes(Math.max(0, onTotal - activeTotal))} idle</span>` }),
      kpi({ label: 'Utilisation', iconName: 'activity', value: fmtPct(onTotal ? activeTotal / onTotal : 0), foot: '<span>Share of on-time with a program open</span>' }),
      kpi({ label: 'Top earner', iconName: 'monitor', value: perPc.length ? esc(perPc[0].pc) : '&mdash;', foot: perPc.length ? `<span>${fmtMoney(perPc[0].revenue)} &middot; ${fmtPct(revenue ? perPc[0].revenue / revenue : 0)} of total</span>` : '' })
    ];
  } else {
    const monthly = dailyAvg * 30;
    kpis = [
      kpi({ label: 'Total revenue', iconName: 'wallet', tone: 'primary', value: fmtMoney(revenue), foot: delta(change, changeLabel) || `<span class="subtle">${esc(changeLabel)}</span>` }),
      kpi({ label: 'Daily average', iconName: 'calendar', value: fmtMoney(dailyAvg), foot: `<span>&asymp; ${fmtMoney(dailyAvg * 7, { maximumFractionDigits: 0, minimumFractionDigits: 0 })}/week &middot; ${fmtMoney(monthly, { maximumFractionDigits: 0, minimumFractionDigits: 0 })}/month</span>` }),
      kpi({ label: 'Best day', iconName: 'trending-up', value: best ? fmtMoney(best.revenue) : '&mdash;', foot: best ? `<span>${esc(fmtDay(best.day, { weekday: 'long', month: 'short', day: 'numeric' }))}</span>` : '<span class="subtle">No revenue in range</span>' }),
      kpi({ label: 'Utilisation', iconName: 'activity', value: fmtPct(onTotal ? activeTotal / onTotal : 0), foot: `<span>${fmtMinutes(activeTotal)} active of ${fmtMinutes(onTotal)} on</span>` })
    ];
  }

  // ---- Chart datasets ----
  const weekly = rollup(daily, startOfWeek, d => fmtWeekRange(startOfWeek(d), endOfWeek(d)));
  const monthly = rollup(daily, d => d.slice(0, 7), d => fmtMonth(d));
  const stacked = (labels, valueOf, thickness) => ({
    labels,
    datasets: pcs.map(pc => ({ label: pc, data: valueOf(pc), backgroundColor: color(pc), borderRadius: 3, maxBarThickness: thickness }))
  });

  const groupings = {};
  if (isSingleDay) {
    const segs = clampSegments(tl, range.from);
    const start = dayStartMs(range.from);
    const hourly = new Map(pcs.map(pc => [pc, new Array(24).fill(0)]));
    for (const s of segs) {
      if (s.kind !== 'active' || !hourly.has(s.pc_name)) continue;
      for (let h = 0; h < 24; h++) {
        const overlap = Math.min(s.b, start + (h + 1) * 3600000) - Math.max(s.a, start + h * 3600000);
        if (overlap > 0) hourly.get(s.pc_name)[h] += overlap / 60000;
      }
    }
    groupings.hour = { title: 'Revenue by hour', meta: 'Stacked by computer', data: stacked(Array.from({ length: 24 }, (_, h) => fmtHour(h)), pc => hourly.get(pc).map(money2), 28) };
  } else {
    groupings.day = { title: 'Revenue per day', meta: 'Stacked by computer', data: stacked(dayList.map(d => fmtDay(d)), pc => dayList.map(d => money2(byDayPc.get(`${d}|${pc}`)?.minutes_active || 0)), 34) };
    if (days >= 14) {
      groupings.week = { title: 'Revenue per week', meta: 'Monday to Sunday, stacked by computer', data: stacked(weekly.map(w => w.label), pc => weekly.map(w => money2(sum(daily.filter(r => r.pc_name === pc && startOfWeek(r.day) === w.key), 'minutes_active'))), 40) };
    }
    if (monthly.length > 1) {
      groupings.month = { title: 'Revenue per month', meta: 'Stacked by computer', data: stacked(monthly.map(m => m.label), pc => monthly.map(m => money2(sum(daily.filter(r => r.pc_name === pc && r.day.startsWith(m.key)), 'minutes_active'))), 48) };
    }
  }
  const groupKeys = Object.keys(groupings);
  const first = groupings[groupKeys[0]];
  const groupLabels = { hour: 'Hourly', day: 'Daily', week: 'Weekly', month: 'Monthly' };

  // ---- Tables ----
  const expected = perPc.length ? 1 / perPc.length : 0;
  const pcTable = perPc.length ? `<div class="table-wrap"><table class="table">
    <thead><tr><th>Computer</th><th class="num">Active</th><th class="num">Utilisation</th><th class="num" title="Share of active time compared with an even split between PCs">vs average</th><th class="num">Revenue</th></tr></thead>
    <tbody>${perPc.map(p => {
      const ratio = expected && activeTotal ? (p.active / activeTotal) / expected : 1;
      const diff = Math.round((ratio - 1) * 100);
      const cls = perPc.length > 2 && diff <= -35 ? 'text-danger' : perPc.length > 2 && diff >= 35 ? 'text-warn' : 'subtle';
      return `<tr>
        <td><div class="pc-cell"><i class="swatch" style="background:${color(p.pc)}"></i><div><div class="strong">${esc(p.pc)}</div>${shopTag(p.pc, p.shop)}</div></div></td>
        <td class="num">${fmtMinutes(p.active)}</td>
        <td class="num">${fmtPct(p.on ? p.active / p.on : 0)}</td>
        <td class="num ${cls}">${diff > 0 ? '+' : ''}${diff}%</td>
        <td class="num strong">${fmtMoney(p.revenue)}</td>
      </tr>`;
    }).join('')}</tbody></table></div>` : empty('No computer data.', 'monitor');

  const progMap = new Map();
  for (const u of usage) {
    if (!u.program || NON_PROGRAMS.has(u.program)) continue;
    progMap.set(u.program, (progMap.get(u.program) || 0) + Number(u.minutes || 0));
  }
  const topPrograms = [...progMap].map(([name, minutes]) => ({ name, minutes })).sort((a, b) => b.minutes - a.minutes).slice(0, 10);
  const progMax = topPrograms.length ? topPrograms[0].minutes : 1;
  const gamesTable = topPrograms.length ? `<ol class="rank-list">${topPrograms.map((p, i) => `<li>
      <span class="rank">${i + 1}</span>
      <div><div class="name" title="${esc(p.name)}">${esc(p.name)}</div><div class="bar-track"><div class="bar-fill" style="width:${(p.minutes / progMax) * 100}%"></div></div></div>
      <div class="val">${fmtMoney(moneyFromMinutes(p.minutes))}<span class="meta">${fmtMinutes(p.minutes)}</span></div>
    </li>`).join('')}</ol>` : empty('No game or app usage recorded in this period.', 'app-window');

  const rollupTable = (list, isWeek) => `<div class="table-wrap"><table class="table">
    <thead><tr><th>${isWeek ? 'Week' : 'Month'}</th><th class="num">Active</th><th class="num">Utilisation</th><th class="num">Per day</th><th class="num">Revenue</th><th class="num">Change</th></tr></thead>
    <tbody>${list.map(r => `<tr>
      <td><div class="strong">${esc(r.label)}</div><div class="meta">${r.days.size} day${r.days.size === 1 ? '' : 's'} with data</div></td>
      <td class="num">${fmtMinutes(r.active)}</td>
      <td class="num">${fmtPct(r.on ? r.active / r.on : 0)}</td>
      <td class="num">${fmtMoney(r.revenue / r.days.size)}</td>
      <td class="num strong">${fmtMoney(r.revenue)}</td>
      <td class="num">${delta(r.growth) || '<span class="subtle">&mdash;</span>'}</td>
    </tr>`).join('')}</tbody></table></div>`;

  const rollupSection = !isSingleDay && days >= 14 && (weekly.length > 1 || monthly.length > 1) ? card({
    title: 'Weekly and monthly totals',
    meta: 'Partial weeks and months at the edges of the range show only the days inside it',
    actions: `<div class="seg sm" id="rollupSeg"><button class="active" data-tab="week">Weekly</button>${monthly.length > 1 ? '<button data-tab="month">Monthly</button>' : ''}</div>`,
    cls: 'section',
    flush: true,
    body: `<div id="rollupBody">${rollupTable(weekly, true)}</div>`
  }) : '';

  // ---- Heatmap ----
  let heatmapSection = '';
  if (days >= 7 && heat.length) {
    const maxHeat = Math.max(0.0001, ...heat.map(h => Number(h.avg_active_pcs)));
    const heatByKey = new Map(heat.map(h => [`${h.weekday}|${h.hour}`, Number(h.avg_active_pcs)]));
    const weekdays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
    const perPcHour = 60 / minutesPerUnit();
    const shade = (v) => (v > 0 ? `background:color-mix(in srgb, var(--accent) ${Math.round((0.12 + 0.88 * (v / maxHeat)) * 100)}%, var(--track))` : '');
    heatmapSection = card({
      title: 'Busy hours',
      meta: 'Average PCs in use by weekday and hour: plan staff, promos and maintenance around it',
      cls: 'section',
      body: `<div class="heatmap-wrap"><div class="heatmap">
        <div></div>${Array.from({ length: 24 }, (_, h) => `<div class="hm-hour">${h % 3 === 0 ? fmtHour(h).replace(' ', '') : ''}</div>`).join('')}
        ${weekdays.map((name, i) => `<div class="hm-label">${name}</div>${Array.from({ length: 24 }, (_, h) => {
          const v = heatByKey.get(`${i + 1}|${h}`) || 0;
          const tip = `${name} ${fmtHour(h)}\n${v.toFixed(1)} PCs in use on average\nabout ${fmtMoney(v * perPcHour)} per hour`;
          return `<div class="hm-cell" style="${shade(v)}" data-tip="${esc(tip)}"></div>`;
        }).join('')}`).join('')}
      </div></div>
      <div class="hm-scale">Quiet ${[0.15, 0.4, 0.7, 1].map(f => `<i style="${shade(f * maxHeat)}"></i>`).join('')} Busy</div>`
    });
  }

  const notes = [];
  if (includesToday) notes.push(isSingleDay ? 'Today is still running.' : 'Today is still running and is left out of the daily average.');

  root.innerHTML = `<div class="kpis">${kpis.join('')}</div>
    ${card({
      title: `<span id="chartTitle">${esc(first.title)}</span>`,
      meta: `<span id="chartMeta">${esc(first.meta)}</span>`,
      actions: `<div class="card-actions">
        ${groupKeys.length > 1 ? `<div class="seg sm" id="groupSeg">${groupKeys.map((k, i) => `<button data-group="${k}" class="${i === 0 ? 'active' : ''}">${groupLabels[k]}</button>`).join('')}</div>` : ''}
        <button class="btn sm" id="csvBtn" ${daily.length ? '' : 'disabled'} title="Download daily revenue per computer">${icon('download')}<span class="hide-sm">CSV</span></button>
      </div>`,
      cls: 'section',
      body: daily.length ? '<div class="chart-box"><canvas id="revenueChart"></canvas></div>' : empty('No revenue data in this range.', 'wallet')
    })}
    <div class="grid cols-even section">
      ${card({ title: 'By computer', meta: 'PCs far below average may have a hardware or seat problem', body: pcTable, flush: true })}
      ${card({ title: 'By game or app', meta: 'Revenue from time in the foreground', body: gamesTable, flush: true })}
    </div>
    ${rollupSection}
    ${heatmapSection}
    <div class="alert section">${icon('info')}<div>
      <b>How revenue is estimated:</b> minutes with a game or app in the foreground at <b>${esc(rateText())}</b>. Change the rate in Settings.
      ${notes.length ? `<br>${esc(notes.join(' '))}` : ''}
    </div></div>`;

  let chart = null;
  const canvas = root.querySelector('#revenueChart');
  if (canvas) {
    chart = makeChart(canvas, {
      type: 'bar',
      data: first.data,
      options: {
        plugins: {
          legend: { position: 'bottom', labels: { boxWidth: 10, boxHeight: 10, padding: 14 } },
          tooltip: {
            filter: (c) => c.raw > 0,
            itemSort: (x, y) => y.raw - x.raw,
            callbacks: {
              label: (c) => ` ${c.dataset.label}: ${fmtMoney(c.raw)}`,
              footer: (items) => `Total ${fmtMoney(items.reduce((m, c) => m + c.raw, 0))}`
            }
          }
        },
        scales: {
          x: { stacked: true, grid: { display: false } },
          y: { stacked: true, beginAtZero: true, ticks: { callback: (v) => fmtMoney(v, { minimumFractionDigits: 0, maximumFractionDigits: 0 }) } }
        }
      }
    });
  }

  root.querySelector('#groupSeg')?.addEventListener('click', (e) => {
    const b = e.target.closest('button[data-group]');
    if (!b || !chart) return;
    root.querySelectorAll('#groupSeg button').forEach(x => x.classList.toggle('active', x === b));
    const g = groupings[b.dataset.group];
    chart.data = g.data;
    chart.update();
    root.querySelector('#chartTitle').textContent = g.title;
    root.querySelector('#chartMeta').textContent = g.meta;
  });

  root.querySelector('#rollupSeg')?.addEventListener('click', (e) => {
    const b = e.target.closest('button[data-tab]');
    if (!b) return;
    root.querySelectorAll('#rollupSeg button').forEach(x => x.classList.toggle('active', x === b));
    root.querySelector('#rollupBody').innerHTML = b.dataset.tab === 'week' ? rollupTable(weekly, true) : rollupTable(monthly, false);
  });

  root.querySelector('#csvBtn')?.addEventListener('click', () => {
    const rows = [...daily]
      .sort((x, y) => x.day.localeCompare(y.day) || x.pc_name.localeCompare(y.pc_name, undefined, { numeric: true }))
      .map(r => [r.day, r.pc_name, r.menu_name || '', Math.round(r.minutes_on), Math.round(r.minutes_active), money2(r.minutes_active).toFixed(2), r.top_program || '']);
    downloadCsv(`revenue_${range.from}_${range.to}.csv`, ['day', 'computer', 'shop', 'minutes_on', 'minutes_active', 'estimated_revenue', 'top_program'], rows);
  });
}
