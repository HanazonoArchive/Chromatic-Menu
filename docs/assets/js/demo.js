// Demo backend: a made-up 8-PC pisonet so the dashboard can be explored
// without a Supabase project. It answers the same calls as the real RPCs,
// with the same row shapes, computed from generated program segments.
import { todayStr, addDays, dayStartMs, IDLE_PROGRAMS, NON_PROGRAMS } from './util.js';

const SHOP = 'Demo Pisonet';
const PCS = ['PC-01', 'PC-02', 'PC-03', 'PC-04', 'PC-05', 'PC-06', 'PC-07', 'PC-08'];
const RETIRED_PC = 'PC-08';
const RETIRED_AFTER_DAYS = 12;
const CRASHED_PC = 'PC-05';
const PROGRAMS = [
  ['Valorant', 14], ['Dota 2', 12], ['Roblox', 12], ['Google Chrome', 10], ['BlueStacks', 9],
  ['Minecraft', 8], ['Counter-Strike 2', 8], ['League of Legends', 7], ['Genshin Impact', 6],
  ['Crossfire', 5], ['Grand Theft Auto V', 5], ['Microsoft Word', 3]
];
const REQUEST_TITLES = [
  ['Marvel Rivals', 'Please add, many of us play it'], ['Marvel Rivals', ''], ['Marvel Rivals', 'pls'],
  ['Wuthering Waves', ''], ['Wuthering Waves', 'Gacha game, free to play'],
  ['Honkai: Star Rail', ''], ['Point Blank', 'Old but classic'], ['Tekken 8', ''],
  ['Apex Legends', 'Free on Steam'], ['Zenless Zone Zero', ''], ['PUBG: Battlegrounds', ''],
  ['Stardew Valley', 'For my little brother'], ['Rocket League', '']
];

function hash(str) {
  let h = 2166136261;
  for (let i = 0; i < str.length; i++) h = Math.imul(h ^ str.charCodeAt(i), 16777619);
  return h >>> 0;
}

function rng(seed) {
  let a = hash(seed);
  return () => {
    a = (a + 0x6D2B79F5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function pick(r, weighted) {
  const total = weighted.reduce((s, [, w]) => s + w, 0);
  let x = r() * total;
  for (const [v, w] of weighted) { if ((x -= w) < 0) return v; }
  return weighted[0][0];
}

// How busy the shop is (0-1) at a local hour on a weekday (1 = Mon).
function demand(hour, weekday) {
  const curve = [0, 0, 0, 0, 0, 0, 0, 0.15, 0.25, 0.3, 0.35, 0.45, 0.55, 0.6, 0.7, 0.8, 0.85, 0.9, 0.9, 0.85, 0.75, 0.6, 0.45, 0.3];
  const weekend = weekday >= 6 ? 1.2 : 1;
  return Math.min(0.95, curve[hour] * weekend);
}

const MIN = 60000;
const dayCache = new Map();

function generateDay(day) {
  if (dayCache.has(day)) return dayCache.get(day);
  const start = dayStartMs(day);
  const weekday = ((new Date(day + 'T00:00:00Z').getUTCDay() + 6) % 7) + 1;
  const shop = rng('shop' + day);
  const open = start + Math.round(8 * 60 + shop() * 60) * MIN;
  const close = start + Math.round(22 * 60 + shop() * 90) * MIN;
  const ageDays = Math.round((dayStartMs(todayStr()) - start) / 86400000);
  const segs = [];

  for (const pc of PCS) {
    if (pc === RETIRED_PC && ageDays < RETIRED_AFTER_DAYS) continue;
    const r = rng(pc + day);
    let on = open + Math.round(r() * 20) * MIN;
    const off = close - Math.round(r() * 15) * MIN;
    const reboot = r() < 0.25 ? on + Math.round((3 + r() * 8) * 60) * MIN : null;
    const powerCut = r() < 0.05;
    let boot = 1;
    let t = on;
    let idleNext = true;
    while (t < off) {
      if (reboot && t >= reboot && boot === 1) {
        t += Math.round(5 + r() * 10) * MIN;
        boot = 2;
        idleNext = true;
        continue;
      }
      const hour = Math.floor((t - start) / 3600000);
      const d = demand(hour, weekday);
      let len;
      let program;
      if (idleNext) {
        len = Math.max(1, Math.round(-Math.log(1 - r()) * (28 * (1 - d) + 3)));
        program = 'Chromatic Menu';
      } else {
        const kind = r();
        len = kind < 0.35 ? 5 + r() * 15 : kind < 0.8 ? 20 + r() * 40 : 60 + r() * 120;
        len = Math.round(len);
        program = r() < 0.08 ? 'Windows' : pick(r, PROGRAMS);
      }
      const end = Math.min(off, t + len * MIN);
      segs.push({ pc_name: pc, power_on: boot, a: t, b: end, program, kind: IDLE_PROGRAMS.has(program) ? 'idle' : 'active' });
      t = end;
      idleNext = !idleNext;
    }
    // Rare power cut: the day ends in the middle of a game.
    if (powerCut) {
      const last = segs[segs.length - 1];
      if (last && last.pc_name === pc && last.kind === 'idle') segs.pop();
    }
  }
  dayCache.set(day, segs);
  return segs;
}

// Segments of a day as they would look right now (today is cut at "now").
function segmentsOf(day) {
  const segs = generateDay(day);
  const today = todayStr();
  if (day > today) return [];
  if (day < today) return segs;
  const now = Math.floor(Date.now() / MIN) * MIN;
  const crashAt = now - 47 * MIN;
  const out = [];
  for (const s of segs) {
    const cut = s.pc_name === CRASHED_PC ? crashAt : now;
    if (s.a >= cut) continue;
    const seg = { ...s, b: Math.min(s.b, cut) };
    if (s.pc_name === CRASHED_PC && s.b > cut && seg.kind === 'idle') {
      seg.kind = 'active';
      seg.program = 'Valorant';
    }
    out.push(seg);
  }
  return out;
}

function daysBetween(from, to) {
  const out = [];
  for (let d = from; d <= to; d = addDays(d, 1)) out.push(d);
  return out;
}

const iso = (ms) => new Date(ms).toISOString();
const round2 = (n) => Math.round(n * 100) / 100;

function summarizeDay(day) {
  const byPc = new Map();
  for (const s of segmentsOf(day)) {
    let p = byPc.get(s.pc_name);
    if (!p) byPc.set(s.pc_name, p = { on: 0, active: 0, programs: new Map() });
    const m = (s.b - s.a) / MIN;
    p.on += m;
    if (s.kind === 'active') p.active += m;
    if (!NON_PROGRAMS.has(s.program)) p.programs.set(s.program, (p.programs.get(s.program) || 0) + m);
  }
  return [...byPc].sort(([x], [y]) => x.localeCompare(y)).map(([pc, p]) => ({
    day,
    pc_name: pc,
    menu_name: SHOP,
    minutes_on: round2(p.on),
    minutes_active: round2(p.active),
    top_program: [...p.programs].sort((x, y) => y[1] - x[1])[0]?.[0] || null
  }));
}

let requests = null;
function requestRows() {
  if (requests) return requests;
  const r = rng('requests');
  const now = Date.now();
  requests = REQUEST_TITLES.map(([title, description], i) => {
    const age = i < 5 ? r() * 2 * 86400000 : r() * 20 * 86400000;
    const status = i < 7 ? 'new' : i < 10 ? 'added' : 'rejected';
    return {
      id: i + 1,
      created_at: iso(now - age),
      pc_name: PCS[Math.floor(r() * 7)],
      menu_name: SHOP,
      title,
      description,
      status
    };
  }).sort((x, y) => y.created_at.localeCompare(x.created_at));
  return requests;
}

export const demoApi = {
  async pcStatus() {
    const today = todayStr();
    const now = Date.now();
    const rows = [];
    for (const pc of PCS) {
      for (let back = 0; back < 60; back++) {
        const segs = segmentsOf(addDays(today, -back)).filter(s => s.pc_name === pc);
        if (!segs.length) continue;
        const last = segs[segs.length - 1];
        rows.push({
          pc_name: pc,
          menu_name: SHOP,
          last_seen: iso(last.b),
          last_program: last.program,
          last_interval_seconds: 60,
          is_online: last.b >= now - 3 * MIN
        });
        break;
      }
    }
    return rows;
  },

  async daily(from, to) {
    return daysBetween(from, to).flatMap(summarizeDay);
  },

  async timeline(day) {
    return segmentsOf(day).map(s => ({
      pc_name: s.pc_name, power_on: s.power_on, seg_start: iso(s.a), seg_end: iso(s.b), kind: s.kind, program: s.program
    })).sort((x, y) => x.pc_name.localeCompare(y.pc_name) || x.seg_start.localeCompare(y.seg_start));
  },

  async usage(from, to) {
    const map = new Map();
    for (const day of daysBetween(from, to)) {
      for (const s of segmentsOf(day)) {
        const k = s.program + '|' + s.pc_name;
        map.set(k, (map.get(k) || 0) + (s.b - s.a) / MIN);
      }
    }
    return [...map].map(([k, minutes]) => {
      const [program, pc_name] = k.split('|');
      return { program, pc_name, minutes: round2(minutes) };
    }).sort((x, y) => y.minutes - x.minutes);
  },

  async heatmap(from, to) {
    const days = daysBetween(from, to);
    const count = new Map();
    const secs = new Map();
    for (const day of days) {
      const wd = ((new Date(day + 'T00:00:00Z').getUTCDay() + 6) % 7) + 1;
      count.set(wd, (count.get(wd) || 0) + 1);
      const start = dayStartMs(day);
      for (const s of segmentsOf(day)) {
        if (s.kind !== 'active') continue;
        for (let h = Math.floor((s.a - start) / 3600000); h <= Math.floor((s.b - 1 - start) / 3600000); h++) {
          const overlap = Math.min(s.b, start + (h + 1) * 3600000) - Math.max(s.a, start + h * 3600000);
          if (overlap > 0) secs.set(`${wd}|${h}`, (secs.get(`${wd}|${h}`) || 0) + overlap / 1000);
        }
      }
    }
    const rows = [];
    for (const [wd, n] of [...count].sort((x, y) => x[0] - y[0])) {
      for (let h = 0; h < 24; h++) rows.push({ weekday: wd, hour: h, avg_active_pcs: Math.round(((secs.get(`${wd}|${h}`) || 0) / 3600 / n) * 1000) / 1000 });
    }
    return rows;
  },

  async sessionStats(from, to) {
    const byProgram = new Map();
    for (const day of daysBetween(from, to)) {
      for (const s of segmentsOf(day)) {
        if (s.kind !== 'active' || NON_PROGRAMS.has(s.program)) continue;
        if (!byProgram.has(s.program)) byProgram.set(s.program, []);
        byProgram.get(s.program).push((s.b - s.a) / MIN);
      }
    }
    return [...byProgram].map(([program, list]) => {
      const sorted = [...list].sort((x, y) => x - y);
      const mid = sorted.length / 2;
      const median = sorted.length % 2 ? sorted[Math.floor(mid)] : (sorted[mid - 1] + sorted[mid]) / 2;
      const total = list.reduce((x, y) => x + y, 0);
      return {
        program,
        session_count: list.length,
        total_minutes: Math.round(total * 10) / 10,
        avg_minutes: Math.round((total / list.length) * 10) / 10,
        median_minutes: Math.round(median * 10) / 10,
        max_minutes: Math.round(sorted[sorted.length - 1] * 10) / 10,
        quick_count: list.filter(m => m < 20).length,
        standard_count: list.filter(m => m >= 20 && m <= 60).length,
        marathon_count: list.filter(m => m > 60).length
      };
    }).sort((x, y) => y.total_minutes - x.total_minutes);
  },

  async networkIncidents(day) {
    const r = rng('net' + day);
    if (r() > 0.35) return [];
    const active = segmentsOf(day).filter(s => s.kind === 'active');
    const n = 1 + Math.floor(r() * 3);
    const out = [];
    for (let i = 0; i < n && active.length; i++) {
      const s = active[Math.floor(r() * active.length)];
      out.push({ pc_name: s.pc_name, incident_time: iso(s.a + (s.b - s.a) / 2), delay_seconds: 150 + Math.floor(r() * 750), program: s.program });
    }
    return out.sort((x, y) => y.incident_time.localeCompare(x.incident_time));
  },

  async activeSoFar() {
    const today = todayStr();
    const yesterday = addDays(today, -1);
    const cutoff = Date.now() - 86400000;
    let todayMin = 0, sameTime = 0, total = 0;
    for (const s of segmentsOf(today)) if (s.kind === 'active') todayMin += (s.b - s.a) / MIN;
    for (const s of segmentsOf(yesterday)) {
      if (s.kind !== 'active') continue;
      total += (s.b - s.a) / MIN;
      sameTime += Math.max(0, Math.min(s.b, cutoff) - s.a) / MIN;
    }
    return { today_minutes: round2(todayMin), yesterday_same_time_minutes: round2(sameTime), yesterday_minutes: round2(total) };
  },

  async requests(status, limit = 500) {
    const rows = requestRows().filter(r => !status || status === 'all' || r.status === status);
    return rows.slice(0, limit).map(r => ({ ...r }));
  },

  async newRequestCount() {
    return requestRows().filter(r => r.status === 'new').length;
  },

  async setRequestStatus(ids, status) {
    const set = new Set([].concat(ids));
    for (const r of requestRows()) if (set.has(r.id)) r.status = status;
  }
};
