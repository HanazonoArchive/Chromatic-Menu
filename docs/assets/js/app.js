import { savedProject, normalizeUrl, verifyProject, connect, db, forgetProject, api, isMissingSchema, isDemo, setDemo } from './api.js';
import { icon } from './icons.js';
import { esc, store, todayStr, addDays, fmtDay, fmtTime, recordPcShops, getAllShops, startOfWeek, startOfMonth } from './util.js';
import { destroyCharts, bindTooltips, hideTooltip, loading } from './views/common.js';
import { renderOverview } from './views/overview.js';
import { renderTimeline } from './views/timeline.js';
import { renderRevenue } from './views/revenue.js';
import { renderPrograms } from './views/programs.js';
import { renderRequests } from './views/requests.js';
import { renderSettings } from './views/settings.js';

const root = document.getElementById('root');

// refresh: seconds between automatic reloads (null = manual only).
const PAGES = {
  overview: { group: 'Monitor', label: 'Overview', short: 'Live', icon: 'layout-dashboard', subtitle: 'Live floor and today so far', controls: 'none', refresh: 60 },
  timeline: { group: 'Monitor', label: 'Timeline', short: 'Timeline', icon: 'clock', subtitle: 'When each PC was on, in use or idle', controls: 'day', refresh: 60 },
  revenue: { group: 'Insights', label: 'Revenue', short: 'Revenue', icon: 'wallet', subtitle: 'Estimated from active time', controls: 'range', refresh: null },
  programs: { group: 'Insights', label: 'Games & apps', short: 'Games', icon: 'gamepad-2', subtitle: 'What customers play and for how long', controls: 'range', refresh: null },
  requests: { group: 'Manage', label: 'Game requests', short: 'Requests', icon: 'message-square', subtitle: 'Sent from the Request a Game button', controls: 'none', refresh: null },
  settings: { group: 'Manage', label: 'Settings', short: 'Settings', icon: 'settings', subtitle: 'Revenue rate, display and connection', controls: 'none', refresh: null }
};

const PRESETS = [['today', 'Today'], ['7d', '7D'], ['this_week', 'Week'], ['this_month', 'Month'], ['30d', '30D'], ['90d', '90D']];

function presetRange(preset, today = todayStr()) {
  const from = {
    today, '7d': addDays(today, -6), this_week: startOfWeek(today), this_month: startOfMonth(today),
    '30d': addDays(today, -29), '90d': addDays(today, -89)
  }[preset];
  return from ? { preset, from, to: today } : null;
}

const state = {
  theme: store.get('theme', 'dark'),
  day: todayStr(),
  followToday: true,
  range: presetRange(store.get('rangePreset', '7d')) || presetRange('7d'),
  requestFilter: 'new',
  email: '',
  timer: null,
  lastRender: 0
};

function setTheme(theme) {
  state.theme = theme === 'light' ? 'light' : 'dark';
  document.documentElement.setAttribute('data-theme', state.theme);
  store.set('theme', state.theme);
}

function shopLabel(fallback) {
  const shops = getAllShops();
  return shops.length === 1 ? shops[0] : shops.length > 1 ? `${shops.length} shops` : fallback;
}

function brand(sub) {
  return `<div class="brand"><div class="brand-mark">${icon('layout-grid')}</div>
    <div class="brand-text"><div class="brand-name">Chromatic Menu</div><div class="brand-sub" title="${esc(sub)}">${esc(sub)}</div></div></div>`;
}

function errorAlert(text) {
  return `<div class="alert error" style="margin-bottom:14px">${icon('alert-triangle')}<div>${esc(text)}</div></div>`;
}

// ---------------------------------------------------------------------------
// Routing: #page or #page?day=YYYY-MM-DD / #page?from=..&to=.. so reloads and
// shared links keep the selected dates.
// ---------------------------------------------------------------------------

const DATE_RE = /^\d{4}-\d{2}-\d{2}$/;

function parseHash() {
  const [name, query] = location.hash.slice(1).split('?');
  const page = PAGES[name] ? name : 'overview';
  const params = new URLSearchParams(query || '');
  const today = todayStr();
  const day = params.get('day');
  if (DATE_RE.test(day || '')) {
    state.day = day > today ? today : day;
    state.followToday = state.day === today;
  }
  const from = params.get('from');
  const to = params.get('to');
  if (DATE_RE.test(from || '') && DATE_RE.test(to || '')) {
    const [a, b] = from <= to ? [from, to] : [to, from];
    const preset = PRESETS.map(p => p[0]).find(p => {
      const r = presetRange(p, today);
      return r.from === a && r.to === b;
    });
    state.range = { preset: preset || 'custom', from: a, to: b > today ? today : b };
  }
  return page;
}

function hashFor(page) {
  const mode = PAGES[page].controls;
  if (mode === 'day') return state.followToday ? `#${page}` : `#${page}?day=${state.day}`;
  if (mode === 'range') return `#${page}?from=${state.range.from}&to=${state.range.to}`;
  return `#${page}`;
}

function navigate(page) {
  const target = hashFor(page);
  if (location.hash !== target) history.replaceState(null, '', target);
  renderPage();
}

// ---------------------------------------------------------------------------
// Auth screens
// ---------------------------------------------------------------------------

function authLayout(inner) {
  return `<div class="auth">
    <aside class="auth-side">
      ${brand('Shop dashboard')}
      <div class="pitch">
        <span class="eyebrow">Your shop, from anywhere</span>
        <h2>See which PCs are <em>earning</em>, right now.</h2>
        <p>Live status, usage and estimated revenue from every Chromatic Menu PC in your shop.</p>
        <ul class="feature-list">
          <li><span class="fi">${icon('monitor')}</span><div><b>Live floor</b><span>Which PCs are in use, idle or off, and for how long.</span></div></li>
          <li><span class="fi">${icon('wallet')}</span><div><b>Revenue estimate</b><span>By hour, day, week and PC, with CSV export.</span></div></li>
          <li><span class="fi">${icon('message-square')}</span><div><b>Game requests</b><span>What customers want you to install next.</span></div></li>
        </ul>
      </div>
      <div class="foot">Data stays in your own Supabase project.</div>
    </aside>
    <main class="auth-main"><div class="auth-card">${inner}</div></main>
  </div>`;
}

function stepPill(step) {
  return `<div class="step-pill"><i class="on"></i><i class="${step >= 2 ? 'on' : ''}"></i><span class="eyebrow">Step ${step} of 2</span></div>`;
}

function showConnect(message) {
  stopRefresh();
  root.innerHTML = authLayout(`
    ${stepPill(1)}
    <h1>Connect your Supabase project</h1>
    <p class="lead">Paste the Project URL and anon key from Supabase, Project Settings, API. They are saved in this browser only.</p>
    <form id="connectForm" novalidate>
      <label class="field"><span>Project URL</span><input class="input" id="url" placeholder="https://your-project.supabase.co" autocomplete="off" spellcheck="false"></label>
      <label class="field"><span>Anon (public) key</span><input class="input mono" id="key" placeholder="eyJhbGciOi..." autocomplete="off" spellcheck="false"></label>
      <div id="connectMsg">${message ? errorAlert(message) : ''}</div>
      <div class="actions">
        <button class="btn primary lg" type="submit" id="connectBtn">Verify and continue ${icon('arrow-right')}</button>
        <button class="btn lg" type="button" id="demoBtn">${icon('play-circle')}Explore with demo data</button>
      </div>
    </form>
    <div class="auth-foot"><span>First time here?</span><a href="setup.html">Set up Supabase step by step</a></div>`);

  root.querySelector('#demoBtn').addEventListener('click', () => {
    setDemo(true);
    enterApp();
  });

  const form = root.querySelector('#connectForm');
  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    const url = normalizeUrl(form.querySelector('#url').value);
    const key = form.querySelector('#key').value.trim();
    const msg = form.querySelector('#connectMsg');
    const btn = form.querySelector('#connectBtn');
    if (!/^https?:\/\//i.test(url) || !key) {
      msg.innerHTML = errorAlert('Enter both the Project URL (starting with https://) and the anon key.');
      return;
    }
    btn.disabled = true;
    btn.textContent = 'Verifying...';
    const error = await verifyProject(url, key);
    if (error) {
      btn.disabled = false;
      btn.innerHTML = `Verify and continue ${icon('arrow-right')}`;
      msg.innerHTML = errorAlert(error);
      return;
    }
    connect(url, key);
    showLogin();
  });
}

function showLogin(message) {
  stopRefresh();
  const project = savedProject();
  const host = project ? new URL(project.url).host : '';
  root.innerHTML = authLayout(`
    ${stepPill(2)}
    <h1>Sign in</h1>
    <p class="lead">Use the dashboard account you created in Supabase, Authentication, Users.</p>
    <div class="alert ok" style="margin-bottom:18px">${icon('database')}<div>Connected to <b>${esc(host)}</b></div></div>
    <form id="loginForm">
      <label class="field"><span>Email</span><input class="input" id="email" type="email" autocomplete="username" required></label>
      <label class="field"><span>Password</span><input class="input" id="password" type="password" autocomplete="current-password" required></label>
      <div id="loginMsg">${message ? errorAlert(message) : ''}</div>
      <div class="actions"><button class="btn primary lg" type="submit" id="loginBtn">${icon('lock')}Sign in</button></div>
    </form>
    <div class="auth-foot"><a href="#" id="changeProject">Use a different project</a><a href="setup.html">Setup guide</a></div>`);

  root.querySelector('#changeProject').addEventListener('click', async (e) => {
    e.preventDefault();
    await forgetProject();
    showConnect();
  });

  const form = root.querySelector('#loginForm');
  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    const btn = form.querySelector('#loginBtn');
    const msg = form.querySelector('#loginMsg');
    btn.disabled = true;
    const { error } = await db().auth.signInWithPassword({
      email: form.querySelector('#email').value.trim(),
      password: form.querySelector('#password').value
    });
    btn.disabled = false;
    if (error) {
      msg.innerHTML = errorAlert(/confirm/i.test(error.message)
        ? 'This account is not confirmed yet. In Supabase, open Authentication, Users and confirm it.'
        : 'Sign in failed: ' + error.message);
      return;
    }
    await enterApp();
  });
}

function showSchemaMissing() {
  stopRefresh();
  root.innerHTML = authLayout(`
    <span class="badge warn" style="margin-bottom:16px">${icon('database')}Setup needed</span>
    <h1>The database is not set up yet</h1>
    <p class="lead">You are signed in, but this Supabase project does not have the Chromatic Menu tables and functions yet.</p>
    <div class="alert info" style="margin-bottom:18px">${icon('info')}<div>Copy the schema from the setup guide and run it once in Supabase, <b>SQL Editor</b>. It takes about a minute.</div></div>
    <div class="actions">
      <a class="btn primary lg" href="setup.html#schema">${icon('book-open')}Open the setup guide</a>
      <button class="btn lg" id="retry">${icon('refresh-cw')}I ran it, check again</button>
      <button class="btn ghost" id="logout">${icon('log-out')}Log out</button>
    </div>`);
  root.querySelector('#retry').addEventListener('click', enterApp);
  root.querySelector('#logout').addEventListener('click', logout);
}

async function logout() {
  if (isDemo()) { exitDemo(); return; }
  await db().auth.signOut();
  showLogin();
}

async function changeProject() {
  await forgetProject();
  showConnect();
}

function exitDemo() {
  setDemo(false);
  const project = savedProject();
  if (project) { connect(project.url, project.key); showLogin(); } else showConnect();
}

// ---------------------------------------------------------------------------
// App shell
// ---------------------------------------------------------------------------

async function enterApp() {
  let initialStatus = [];
  try {
    initialStatus = await api.pcStatus();
  } catch (e) {
    if (isMissingSchema(e)) { showSchemaMissing(); return; }
    if (e && (e.code === 'PGRST301' || /JWT/i.test(e.message || ''))) { showLogin('Your session expired. Please sign in again.'); return; }
    showLogin('Could not load data: ' + (e.message || e));
    return;
  }
  recordPcShops(initialStatus);

  if (isDemo()) {
    state.email = 'demo@chromatic.menu';
  } else {
    const { data } = await db().auth.getUser();
    state.email = data?.user?.email || '';
  }
  renderShell();
  renderPage();
}

function renderShell() {
  const project = savedProject();
  const demo = isDemo();
  const groups = [...new Set(Object.values(PAGES).map(p => p.group))];
  root.innerHTML = `<div class="shell">
    <aside class="sidebar">
      ${brand(shopLabel(demo ? 'Demo shop' : 'Dashboard'))}
      <div class="nav-groups" id="nav">
        ${groups.map(g => `<div class="nav-group"><span class="eyebrow">${esc(g)}</span><nav class="nav" aria-label="${esc(g)}">
          ${Object.entries(PAGES).filter(([, p]) => p.group === g).map(([id, p]) => `<a href="#${id}" data-page="${id}" aria-label="${esc(p.label)}" title="${esc(p.label)}">${icon(p.icon)}<span class="label">${esc(p.label)}</span><span class="label-short">${esc(p.short)}</span>${id === 'requests' ? '<span class="badge hidden" id="reqBadge"></span>' : ''}</a>`).join('')}
        </nav></div>`).join('')}
      </div>
      <div class="sidebar-foot">
        <div class="user-card">
          <div class="avatar">${esc((state.email || '?').charAt(0))}</div>
          <div class="who"><div class="email" title="${esc(state.email)}">${esc(state.email)}</div><div class="conn">${demo ? 'Demo data' : esc(project ? new URL(project.url).host : '')}</div></div>
        </div>
        <div class="row">
          <button class="btn ghost icon-only" id="themeBtn" title="Switch theme" aria-label="Switch theme">${icon(state.theme === 'dark' ? 'sun' : 'moon')}</button>
          <button class="btn ghost icon-only" id="logoutBtn" title="${demo ? 'Leave demo' : 'Log out'}" aria-label="${demo ? 'Leave demo' : 'Log out'}">${icon('log-out')}</button>
        </div>
      </div>
    </aside>
    <main class="main">
      ${demo ? `<div class="demo-bar">${icon('play-circle')}<span>You are viewing <b>demo data</b>.</span><button class="link-btn" id="demoExit">Connect your shop</button></div>` : ''}
      <div class="topbar">
        <div class="title"><h1 id="pageTitle"></h1><div class="sub" id="pageSub"></div></div>
        <div class="controls" id="controls"></div>
      </div>
      <div class="content" id="page"></div>
    </main>
  </div>`;

  root.querySelector('#themeBtn').addEventListener('click', () => {
    setTheme(state.theme === 'dark' ? 'light' : 'dark');
    root.querySelector('#themeBtn').innerHTML = icon(state.theme === 'dark' ? 'sun' : 'moon');
    renderPage();
  });
  root.querySelector('#logoutBtn').addEventListener('click', logout);
  root.querySelector('#demoExit')?.addEventListener('click', exitDemo);
  bindTooltips(root.querySelector('#page'));
  updateRequestBadge();
}

function updateSidebarBrand() {
  const sub = root.querySelector('.sidebar .brand-sub');
  if (!sub) return;
  const text = shopLabel(isDemo() ? 'Demo shop' : 'Dashboard');
  sub.textContent = text;
  sub.title = text;
}

async function updateRequestBadge() {
  const badge = document.getElementById('reqBadge');
  if (!badge) return;
  try {
    const n = await api.newRequestCount();
    badge.textContent = n > 99 ? '99+' : String(n);
    badge.classList.toggle('hidden', n === 0);
  } catch {
    badge.classList.add('hidden');
  }
}

function renderControls(page) {
  const el = document.getElementById('controls');
  const refresh = `<button class="btn icon-only" id="refreshBtn" title="Refresh" aria-label="Refresh">${icon('refresh-cw')}</button>`;
  const mode = PAGES[page].controls;
  const today = todayStr();

  if (mode === 'day') {
    el.innerHTML = `<div class="day-nav">
        <button class="btn icon-only" id="prevDay" title="Previous day" aria-label="Previous day">${icon('chevron-left')}</button>
        <input class="input" type="date" id="dayInput" value="${state.day}" max="${today}" aria-label="Day">
        <button class="btn icon-only" id="nextDay" title="Next day" aria-label="Next day" ${state.day >= today ? 'disabled' : ''}>${icon('chevron-right')}</button>
      </div>
      <button class="btn ${state.day === today ? 'active-soft' : ''}" id="todayBtn">Today</button>${refresh}`;
    const go = (day) => {
      state.day = day > todayStr() ? todayStr() : day;
      state.followToday = state.day === todayStr();
      navigate(page);
    };
    el.querySelector('#prevDay').onclick = () => go(addDays(state.day, -1));
    el.querySelector('#nextDay').onclick = () => go(addDays(state.day, 1));
    el.querySelector('#todayBtn').onclick = () => go(todayStr());
    el.querySelector('#dayInput').onchange = (e) => e.target.value && go(e.target.value);
  } else if (mode === 'range') {
    el.innerHTML = `<div class="seg date-seg" id="presetSeg">${PRESETS.map(([k, l]) => `<button data-preset="${k}" class="${state.range.preset === k ? 'active' : ''}">${l}</button>`).join('')}</div>
      <div class="date-pickers">
        <input class="input" type="date" id="fromInput" value="${state.range.from}" max="${today}" aria-label="From">
        <span class="subtle">to</span>
        <input class="input" type="date" id="toInput" value="${state.range.to}" max="${today}" aria-label="To">
      </div>${refresh}`;
    el.querySelector('#presetSeg').onclick = (e) => {
      const b = e.target.closest('button[data-preset]');
      if (!b) return;
      state.range = presetRange(b.dataset.preset);
      store.set('rangePreset', b.dataset.preset);
      navigate(page);
    };
    const custom = () => {
      let from = el.querySelector('#fromInput').value;
      let to = el.querySelector('#toInput').value;
      if (!from || !to) return;
      if (from > to) [from, to] = [to, from];
      state.range = { preset: 'custom', from, to };
      navigate(page);
    };
    el.querySelector('#fromInput').onchange = custom;
    el.querySelector('#toInput').onchange = custom;
  } else {
    el.innerHTML = page === 'settings' ? '' : refresh;
  }
  el.querySelector('#refreshBtn')?.addEventListener('click', () => renderPage({ quiet: true }));
}

function stopRefresh() {
  if (state.timer) clearTimeout(state.timer);
  state.timer = null;
}

function scheduleRefresh(page) {
  stopRefresh();
  const secs = PAGES[page].refresh;
  if (!secs || (page === 'timeline' && state.day !== todayStr())) return;
  state.timer = setTimeout(() => {
    if (document.hidden) { scheduleRefresh(page); return; }
    renderPage({ quiet: true });
  }, secs * 1000);
}

function setUpdated() {
  const el = document.getElementById('updatedAt');
  if (el) el.textContent = `Updated ${fmtTime(Date.now())}`;
}

// Each full render gets its own host element. If the user navigates away while
// a page is still loading, the old render writes into a detached host and is
// dropped. Quiet refreshes reuse the current host, so the old content stays on
// screen until the new data arrives.
let renderSeq = 0;

async function renderPage({ quiet = false } = {}) {
  const pageEl = document.getElementById('page');
  if (!pageEl) return;
  const seq = ++renderSeq;
  const page = parseHash();
  const info = PAGES[page];

  // Past midnight, pages that were showing "today" move to the new day.
  const today = todayStr();
  if (state.followToday) state.day = today;
  if (state.range.preset !== 'custom' && state.range.to !== today) state.range = presetRange(state.range.preset, today);

  stopRefresh();
  hideTooltip();
  document.querySelectorAll('#nav a').forEach(a => {
    const on = a.dataset.page === page;
    a.classList.toggle('active', on);
    if (on) a.setAttribute('aria-current', 'page'); else a.removeAttribute('aria-current');
    a.setAttribute('href', hashFor(a.dataset.page));
  });
  document.getElementById('pageTitle').textContent = info.label;
  const sub = document.getElementById('pageSub');
  const range = info.controls === 'range'
    ? `<span class="badge">${esc(fmtDay(state.range.from))}${state.range.from !== state.range.to ? ` &ndash; ${esc(fmtDay(state.range.to))}` : ''}</span>`
    : '';
  const live = info.refresh && (page !== 'timeline' || state.day === today)
    ? `<span class="live"><span class="dot ring"></span>Live</span><span class="subtle" id="updatedAt"></span>` : '';
  sub.innerHTML = `${live}<span>${esc(info.subtitle)}</span>${range}`;
  document.title = `${info.label} - Chromatic Menu`;
  renderControls(page);

  let host = pageEl.firstElementChild;
  if (!quiet || !host || host.dataset.page !== page) {
    destroyCharts();
    host = document.createElement('div');
    host.className = 'page-host';
    host.dataset.page = page;
    host.innerHTML = loading();
    pageEl.replaceChildren(host);
  } else {
    pageEl.classList.add('refreshing');
  }

  switch (page) {
    case 'overview': await renderOverview(host); break;
    case 'timeline': await renderTimeline(host, state.day); break;
    case 'programs': await renderPrograms(host, state.range); break;
    case 'revenue': await renderRevenue(host, state.range); break;
    case 'requests':
      await renderRequests(host, state.requestFilter, ({ filter }) => {
        state.requestFilter = filter;
        updateRequestBadge();
        renderPage({ quiet: true });
      });
      break;
    case 'settings': {
      const project = savedProject();
      renderSettings(host, {
        theme: state.theme, setTheme, url: project?.url || '', email: state.email,
        logout, changeProject, exitDemo, demo: isDemo()
      });
      break;
    }
  }

  if (seq !== renderSeq || !host.isConnected) return;
  pageEl.classList.remove('refreshing');
  state.lastRender = Date.now();
  setUpdated();
  updateSidebarBrand();
  if (page === 'overview') updateRequestBadge();
  scheduleRefresh(page);
}

// ---------------------------------------------------------------------------
// Boot
// ---------------------------------------------------------------------------

async function boot() {
  setTheme(state.theme);
  window.addEventListener('hashchange', () => { if (document.getElementById('page')) renderPage(); });
  document.addEventListener('visibilitychange', () => {
    if (document.hidden || !document.getElementById('page')) return;
    const page = parseHash();
    if (PAGES[page].refresh && Date.now() - state.lastRender > 30000) renderPage({ quiet: true });
  });

  if (isDemo()) { await enterApp(); return; }

  const project = savedProject();
  if (!project) { showConnect(); return; }

  connect(project.url, project.key);
  db().auth.onAuthStateChange((event) => {
    if (event === 'SIGNED_OUT' && document.getElementById('page') && !isDemo()) showLogin();
  });

  const { data } = await db().auth.getSession();
  if (!data?.session) { showLogin(); return; }
  await enterApp();
}

boot().catch((e) => {
  root.innerHTML = authLayout(errorAlert(e.message || String(e)));
});
