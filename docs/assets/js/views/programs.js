import { api } from '../api.js';
import { icon } from '../icons.js';
import { esc, todayStr, addDays, fmtMinutes, fmtMoney, fmtPct, moneyFromMinutes, groupBy, sum, IDLE_PROGRAMS, NON_PROGRAMS } from '../util.js';
import { errorBox, empty, kpi, card, shopTag } from './common.js';

export async function renderPrograms(root, range) {
  let usage, sessions;
  try {
    [usage, sessions] = await Promise.all([api.usage(range.from, range.to), api.sessionStats(range.from, range.to)]);
  } catch (e) {
    root.innerHTML = errorBox(e);
    return;
  }
  if (!root.isConnected) return;

  const activeTotal = sum(usage.filter(u => !IDLE_PROGRAMS.has(u.program)), 'minutes');
  const idleTotal = sum(usage.filter(u => IDLE_PROGRAMS.has(u.program)), 'minutes');
  const windowsTotal = sum(usage.filter(u => u.program === 'Windows'), 'minutes');
  const real = usage.filter(u => !NON_PROGRAMS.has(u.program));

  const programs = [...groupBy(real, 'program')]
    .map(([program, rows]) => ({ program, minutes: sum(rows, 'minutes'), pcs: new Set(rows.map(r => r.pc_name)).size }))
    .sort((a, b) => b.minutes - a.minutes);
  const top = programs.slice(0, 10);
  const maxMin = top.length ? top[0].minutes : 1;
  const sessionByProgram = new Map((sessions || []).map(s => [s.program, s]));

  const perPc = [...groupBy(real, 'pc_name')]
    .map(([pc, rows]) => ({ pc, total: sum(rows, 'minutes'), top: [...rows].sort((a, b) => b.minutes - a.minutes).slice(0, 3) }))
    .sort((a, b) => a.pc.localeCompare(b.pc, undefined, { numeric: true }));

  let totalSessions = 0, allQuick = 0, allStd = 0, allMarathon = 0, sessionMinutes = 0;
  for (const s of sessions || []) {
    totalSessions += Number(s.session_count);
    allQuick += Number(s.quick_count);
    allStd += Number(s.standard_count);
    allMarathon += Number(s.marathon_count);
    sessionMinutes += Number(s.total_minutes);
  }
  const share = (n) => (totalSessions ? n / totalSessions : 0);

  const kpis = `<div class="kpis">
    ${kpi({ label: 'Most played', iconName: 'gamepad-2', tone: 'primary', value: programs.length ? esc(programs[0].program) : '&mdash;', foot: programs.length ? `<span>${fmtMinutes(programs[0].minutes)} &middot; ${fmtPct(activeTotal ? programs[0].minutes / activeTotal : 0)} of active time</span>` : '<span class="subtle">No usage</span>' })}
    ${kpi({ label: 'Games and apps used', iconName: 'app-window', value: String(programs.length), foot: `<span>Not counting Windows and the menu</span>` })}
    ${kpi({ label: 'Typical sitting', iconName: 'clock', value: totalSessions ? fmtMinutes(sessionMinutes / totalSessions) : '&mdash;', foot: totalSessions ? `<span>${totalSessions.toLocaleString('en-US')} sessions &middot; ${fmtPct(share(allMarathon))} over 1 hour</span>` : '<span class="subtle">Needs get_session_stats</span>' })}
    ${kpi({ label: 'Active time', iconName: 'zap', value: fmtMinutes(activeTotal), foot: `<span>Idle in menu ${fmtMinutes(idleTotal)} &middot; Windows apps ${fmtMinutes(windowsTotal)}</span>` })}
  </div>`;

  const topList = top.length ? `<ol class="rank-list">${top.map((p, i) => `<li class="${i < 3 ? 'top' : ''}">
      <span class="rank">${i + 1}</span>
      <div><div class="name" title="${esc(p.program)}">${esc(p.program)}</div><div class="bar-track"><div class="bar-fill" style="width:${(p.minutes / maxMin) * 100}%"></div></div></div>
      <div class="val">${fmtMinutes(p.minutes)}<span class="meta">${fmtPct(activeTotal ? p.minutes / activeTotal : 0)}</span></div>
    </li>`).join('')}</ol>` : empty('No program usage in this range.', 'app-window');

  const pcList = perPc.length ? `<div class="table-wrap"><table class="table"><tbody>${perPc.map(p => `<tr>
      <td style="width:36%"><div class="strong">${esc(p.pc)}</div>${shopTag(p.pc)}<div class="meta">${fmtMinutes(p.total)} in games and apps</div></td>
      <td>${p.top.map((t, i) => `<div class="mini-row"><span class="${i === 0 ? 'strong' : 'muted'}">${esc(t.program)}</span><span class="subtle tabular">${fmtMinutes(t.minutes)}</span></div>`).join('')}</td>
    </tr>`).join('')}</tbody></table></div>` : empty('No data.', 'monitor');

  const mixLegend = `<div class="legend">
    <span><i style="background:var(--success)"></i>Under 20 min ${fmtPct(share(allQuick))}</span>
    <span><i style="background:var(--accent)"></i>20&ndash;60 min ${fmtPct(share(allStd))}</span>
    <span><i style="background:var(--violet)"></i>Over 1 hour ${fmtPct(share(allMarathon))}</span>
  </div>`;

  const rowHtml = (p, i) => {
    const s = sessionByProgram.get(p.program);
    let mix = '<span class="subtle">&mdash;</span>';
    if (s) {
      const n = Number(s.session_count) || 1;
      const q = Math.round((Number(s.quick_count) / n) * 100);
      const st = Math.round((Number(s.standard_count) / n) * 100);
      mix = `<div class="session-mix" title="Under 20 min ${q}% | 20-60 min ${st}% | Over 1 hour ${Math.max(0, 100 - q - st)}%"><i class="quick" style="width:${q}%"></i><i class="std" style="width:${st}%"></i><i class="marathon" style="width:${Math.max(0, 100 - q - st)}%"></i></div>`;
    }
    return `<tr data-name="${esc(p.program.toLowerCase())}">
      <td class="subtle">${i + 1}</td>
      <td class="strong">${esc(p.program)}</td>
      <td class="num">${fmtMinutes(p.minutes)}</td>
      <td class="num">${fmtPct(activeTotal ? p.minutes / activeTotal : 0)}</td>
      <td class="num">${s ? Number(s.session_count).toLocaleString('en-US') : '&mdash;'}</td>
      <td class="num">${s ? fmtMinutes(Number(s.median_minutes)) : '&mdash;'}</td>
      <td class="num">${s ? fmtMinutes(Number(s.max_minutes)) : '&mdash;'}</td>
      <td style="min-width:120px">${mix}</td>
      <td class="num">${p.pcs}</td>
      <td class="num strong">${fmtMoney(moneyFromMinutes(p.minutes))}</td>
    </tr>`;
  };

  root.innerHTML = `${kpis}
    <div class="grid cols-2 section">
      ${card({ title: 'Top 10', meta: 'By time in the foreground', body: topList, flush: true })}
      ${card({ title: 'By computer', meta: 'Top 3 on each PC', body: pcList, flush: true })}
    </div>
    ${card({
      title: 'All games and apps',
      meta: `${programs.length} total &middot; a session is one unbroken run of the same program on one PC${range.from < addDays(todayStr(), -89) ? ' &middot; session columns cover the last 90 days only' : ''}`,
      actions: programs.length > 8 ? `<label class="search">${icon('search')}<input class="input" id="progSearch" type="search" placeholder="Filter" aria-label="Filter programs"></label>` : '',
      cls: 'section',
      flush: true,
      body: programs.length ? `<div class="table-wrap"><table class="table" id="progTable">
          <thead><tr><th style="width:40px">#</th><th>Program</th><th class="num">Time</th><th class="num">Share</th><th class="num">Sessions</th><th class="num">Median</th><th class="num">Longest</th><th>Session length</th><th class="num">PCs</th><th class="num">Est.</th></tr></thead>
          <tbody>${programs.map(rowHtml).join('')}</tbody>
        </table></div>
        ${sessions ? `<div class="card-foot">${mixLegend}</div>` : ''}
        <div class="empty hidden" id="progNone">No program matches that filter.</div>` : empty('No program usage in this range.', 'app-window')
    })}`;

  const search = root.querySelector('#progSearch');
  if (search) {
    search.addEventListener('input', () => {
      const q = search.value.trim().toLowerCase();
      let shown = 0;
      root.querySelectorAll('#progTable tbody tr').forEach(tr => {
        const hit = !q || tr.dataset.name.includes(q);
        tr.classList.toggle('hidden', !hit);
        if (hit) shown++;
      });
      root.querySelector('#progNone').classList.toggle('hidden', shown > 0);
    });
  }
}
