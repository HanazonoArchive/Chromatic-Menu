// Client-side math over get_day_timeline rows. Every page that needs
// "who was active when" goes through here so the numbers agree.
import { dayStartMs, todayStr, NON_PROGRAMS, groupBy } from './util.js';

// Timeline rows clamped to the shop-local day (and to "now" for today),
// with numeric a/b bounds in ms. Zero-length segments are dropped.
//
// A segment starts one interval before its first heartbeat. Heartbeats can
// arrive slightly less than an interval apart (timer jitter, clock-offset
// corrections), so a new segment may start a moment before the previous one
// on the same PC ended. Each PC's segments are trimmed so they never overlap;
// otherwise one PC would count twice in "PCs in use at once".
export function clampSegments(rows, day) {
  const start = dayStartMs(day);
  const end = day === todayStr() ? Math.min(start + 86400000, Date.now()) : start + 86400000;
  const out = [];
  for (const list of groupBy(rows, 'pc_name').values()) {
    const sorted = list
      .map(r => ({ ...r, a: Math.max(start, Date.parse(r.seg_start)), b: Math.min(end, Date.parse(r.seg_end)) }))
      .sort((x, y) => x.a - y.a || x.b - y.b);
    let lastEnd = -Infinity;
    for (const s of sorted) {
      s.a = Math.max(s.a, lastEnd);
      if (s.b > s.a) {
        out.push(s);
        lastEnd = s.b;
      }
    }
  }
  return out.sort((x, y) => x.a - y.a);
}

export function minutesOf(segs, pred = () => true) {
  let ms = 0;
  for (const s of segs) if (pred(s)) ms += s.b - s.a;
  return ms / 60000;
}

// Active minutes that happened before `cutoffMs`.
export function activeMinutesBefore(segs, cutoffMs) {
  let ms = 0;
  for (const s of segs) {
    if (s.kind !== 'active') continue;
    ms += Math.max(0, Math.min(s.b, cutoffMs) - s.a);
  }
  return ms / 60000;
}

// Step function of how many PCs were active at once: [{a, b, count}].
// A PC's own segments never overlap, so each active segment adds exactly one.
export function concurrencySteps(segs) {
  const events = [];
  for (const s of segs) {
    if (s.kind !== 'active') continue;
    events.push([s.a, 1], [s.b, -1]);
  }
  events.sort((x, y) => x[0] - y[0] || x[1] - y[1]);
  const steps = [];
  let count = 0;
  let prev = null;
  for (const [t, d] of events) {
    if (prev !== null && t > prev && count > 0) steps.push({ a: prev, b: t, count });
    count += d;
    prev = t;
  }
  return steps;
}

export function peakOf(steps) {
  let best = null;
  for (const s of steps) if (!best || s.count > best.count) best = s;
  return best;
}

export function minutesAtLeast(steps, n) {
  return steps.reduce((m, s) => m + (s.count >= n ? s.b - s.a : 0), 0) / 60000;
}

// Average number of active PCs in each slot of the day (fractional).
// For today, the slot still in progress is averaged over its elapsed part only.
export function concurrencySlots(steps, day, slotMin = 15) {
  const start = dayStartMs(day);
  const slotMs = slotMin * 60000;
  const n = Math.round(1440 / slotMin);
  const slots = new Array(n).fill(0);
  for (const s of steps) {
    let t = s.a;
    while (t < s.b) {
      const i = Math.floor((t - start) / slotMs);
      const slotEnd = start + (i + 1) * slotMs;
      const until = Math.min(s.b, slotEnd);
      if (i >= 0 && i < n) slots[i] += (s.count * (until - t)) / slotMs;
      t = until;
    }
  }
  if (day === todayStr()) {
    const i = Math.floor((Date.now() - start) / slotMs);
    const elapsed = (Date.now() - start - i * slotMs) / slotMs;
    if (i >= 0 && i < n && elapsed > 0) slots[i] = Math.min(slots[i] / elapsed, Math.max(0, ...steps.map(s => s.count)));
  }
  return slots;
}

// When the shop was open: the span where at least one PC was on, ignoring
// short blips far from the main day (a PC left on past midnight that shuts
// down a few minutes later, a quick restart at dawn for Windows Update).
// Stretches separated by less than `gapMin` are joined; joined stretches
// shorter than `minBlockMin` count as blips unless nothing longer exists.
export function openHours(segs, gapMin = 60, minBlockMin = 30) {
  const sorted = [...segs].sort((x, y) => x.a - y.a);
  const blocks = [];
  for (const s of sorted) {
    const last = blocks[blocks.length - 1];
    if (last && s.a - last.b <= gapMin * 60000) last.b = Math.max(last.b, s.b);
    else blocks.push({ a: s.a, b: s.b });
  }
  if (!blocks.length) return null;
  const main = blocks.filter(b => b.b - b.a >= minBlockMin * 60000);
  const kept = main.length ? main : blocks;
  return {
    open: kept[0].a,
    close: kept[kept.length - 1].b,
    blips: blocks.filter(b => !kept.includes(b))
  };
}

// Minutes per program from active segments, leaving out Windows / menu / unknown.
export function programMinutes(segs) {
  const map = new Map();
  for (const s of segs) {
    if (s.kind !== 'active' || NON_PROGRAMS.has(s.program)) continue;
    map.set(s.program, (map.get(s.program) || 0) + (s.b - s.a) / 60000);
  }
  return [...map].map(([program, minutes]) => ({ program, minutes })).sort((x, y) => y.minutes - x.minutes);
}

// Per-PC breakdown of one day.
export function perPcDay(segs, isOnline = () => false) {
  const result = new Map();
  for (const [pc, list] of groupBy(segs, 'pc_name')) {
    const sorted = [...list].sort((x, y) => x.a - y.a);
    const boots = groupBy(sorted, 'power_on');
    const online = isOnline(pc);
    let longest = 0;
    for (const group of boots.values()) {
      longest = Math.max(longest, (group[group.length - 1].b - group[0].a) / 60000);
    }
    const last = sorted[sorted.length - 1];
    result.set(pc, {
      segs: sorted,
      onMin: minutesOf(sorted),
      activeMin: minutesOf(sorted, s => s.kind === 'active'),
      first: sorted[0].a,
      last: last.b,
      boots: boots.size,
      longest,
      current: online ? last : null
    });
  }
  return result;
}
