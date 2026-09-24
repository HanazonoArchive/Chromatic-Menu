import { savedProject, normalizeUrl, verifyProject, connect, db, forgetProject, api, isMissingSchema } from './api.js';
import { icon } from './icons.js';
import { esc, store, todayStr, addDays, fmtDay } from './util.js';
import {
  renderOverview, renderTimeline, renderPrograms, renderRevenue, renderRequests, renderSettings,
  destroyCharts, bindTooltips, hideTooltip, loading
} from './views.js';

const root = document.getElementById('root');

const PAGES = {
  overview: { group: 'Monitor', label: 'Overview', icon: 'layout-dashboard', subtitle: 'Live status and today at a glance', controls: 'none' },
  timeline: { group: 'Monitor', label: 'Timeline', icon: 'clock', subtitle: 'When each PC was on, active or idle', controls: 'day' },
  programs: { group: 'Insights', label: 'Programs', icon: 'app-window', subtitle: 'What customers use the most', controls: 'range' },
  revenue: { group: 'Insights', label: 'Revenue', icon: 'wallet', subtitle: 'Estimated from active time', controls: 'range' },
  requests: { group: 'Manage', label: 'Game requests', icon: 'message-square', subtitle: 'Sent from the Request a Game button', controls: 'none' },
  settings: { group: 'Manage', label: 'Settings', icon: 'settings', subtitle: 'Dashboard preferences and connection', controls: 'none' }
};

const state = {
  theme: store.get('theme', 'dark'),
  day: todayStr(),
  range: { preset: '7d', from: addDays(todayStr(), -6), to: todayStr() },
  requestFilter: 'new',
  email: '',
  refreshTimer: null
};

function setTheme(theme) {
  state.theme = theme === 'light' ? 'light' : 'dark';
  document.documentElement.setAttribute('data-theme', state.theme);
  store.set('theme', state.theme);
}

function brand(sub) {
  return `<div class="brand"><div class="brand-mark">${icon('layout-grid')}</div>
    <div class="brand-text"><div class="brand-name">Chromatic Menu</div><div class="brand-sub">${esc(sub)}</div></div></div>`;
}

function errorAlert(text) {
  return `<div class="alert error" style="margin-bottom:14px">${icon('alert-triangle')}<div>${esc(text)}</div></div>`;
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
          <li><span class="fi">${icon('monitor')}</span><div><b>Live PC status</b><span>Online or offline, and the program in use.</span></div></li>
          <li><span class="fi">${icon('wallet')}</span><div><b>Revenue estimate</b><span>Estimated from active minutes, by day and by PC.</span></div></li>
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
      <div class="actions"><button class="btn primary lg" type="submit" id="connectBtn">Verify and continue ${icon('arrow-right')}</button></div>
    </form>
    <div class="auth-foot"><span>First time here?</span><a href="setup.html">Set up Supabase step by step</a></div>`);

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
  await db().auth.signOut();
  showLogin();
}

async function changeProject() {
  await forgetProject();
  showConnect();
}

// ---------------------------------------------------------------------------
// App shell
// ---------------------------------------------------------------------------

async function enterApp() {
  try {
    await api.pcStatus();
  } catch (e) {
    if (isMissingSchema(e)) { showSchemaMissing(); return; }
    if (e && (e.code === 'PGRST301' || /JWT/i.test(e.message || ''))) { showLogin('Your session expired. Please sign in again.'); return; }
    showLogin('Could not load data: ' + (e.message || e));
    return;
  }

  const { data } = await db().auth.getUser();
  state.email = data?.user?.email || '';
  renderShell();
  if (!location.hash || !PAGES[location.hash.slice(1)]) location.hash = '#overview';
  renderPage();
}

function renderShell() {
  const project = savedProject();
  const groups = [...new Set(Object.values(PAGES).map(p => p.group))];
  root.innerHTML = `<div class="shell">
    <aside class="sidebar">
      ${brand('Dashboard')}
      <div class="nav-groups" id="nav">
        ${groups.map(g => `<div class="nav-group"><span class="eyebrow">${esc(g)}</span><nav class="nav">
          ${Object.entries(PAGES).filter(([, p]) => p.group === g).map(([id, p]) => `<a href="#${id}" data-page="${id}">${icon(p.icon)}<span class="label">${esc(p.label)}</span>${id === 'requests' ? '<span class="badge hidden" id="reqBadge"></span>' : ''}</a>`).join('')}
        </nav></div>`).join('')}
      </div>
      <div class="sidebar-foot">
        <div class="user-card">
          <div class="avatar">${esc((state.email || '?').charAt(0))}</div>
          <div class="who"><div class="email" title="${esc(state.email)}">${esc(state.email)}</div><div class="conn">${esc(project ? new URL(project.url).host : '')}</div></div>
        </div>
        <div class="row">
          <button class="btn ghost icon-only" id="themeBtn" title="Switch theme" aria-label="Switch theme">${icon(state.theme === 'dark' ? 'sun' : 'moon')}</button>
          <button class="btn ghost icon-only" id="logoutBtn" title="Log out" aria-label="Log out">${icon('log-out')}</button>
        </div>
      </div>
    </aside>
    <main class="main">
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
  bindTooltips(root.querySelector('#page'));
  updateRequestBadge();
}

async function updateRequestBadge() {
  const badge = document.getElementById('reqBadge');
  if (!badge) return;
  try {
    const n = await api.newRequestCount();
    badge.textContent = String(n);
    badge.classList.toggle('hidden', n === 0);
  } catch {
    badge.classList.add('hidden');
  }
}

function renderControls(page) {
  const el = document.getElementById('controls');
  const refresh = `<button class="btn icon-only" id="refreshBtn" title="Refresh" aria-label="Refresh">${icon('refresh-cw')}</button>`;
  const mode = PAGES[page].controls;
  if (mode === 'day') {
    const today = todayStr();
    el.innerHTML = `<div class="seg">
        <button id="prevDay" aria-label="Previous day">Prev</button>
        <button id="todayBtn" class="${state.day === today ? 'active' : ''}">Today</button>
        <button id="nextDay" aria-label="Next day" ${state.day >= today ? 'disabled' : ''}>Next</button>
      </div>
      <input class="input" type="date" id="dayInput" value="${state.day}" max="${today}">${refresh}`;
    const go = (day) => { state.day = day > todayStr() ? todayStr() : day; renderPage(); };
    el.querySelector('#prevDay').onclick = () => go(addDays(state.day, -1));
    el.querySelector('#nextDay').onclick = () => go(addDays(state.day, 1));
    el.querySelector('#todayBtn').onclick = () => go(todayStr());
    el.querySelector('#dayInput').onchange = (e) => e.target.value && go(e.target.value);
  } else if (mode === 'range') {
    const presets = [['today', 'Today'], ['7d', '7D'], ['30d', '30D'], ['90d', '90D']];
    el.innerHTML = `<div class="seg" id="presetSeg">${presets.map(([k, l]) => `<button data-preset="${k}" class="${state.range.preset === k ? 'active' : ''}">${l}</button>`).join('')}</div>
      <input class="input" type="date" id="fromInput" value="${state.range.from}" max="${todayStr()}" aria-label="From">
      <span class="subtle">to</span>
      <input class="input" type="date" id="toInput" value="${state.range.to}" max="${todayStr()}" aria-label="To">${refresh}`;
    el.querySelector('#presetSeg').onclick = (e) => {
      const b = e.target.closest('button[data-preset]');
      if (!b) return;
      const today = todayStr();
      const span = { today: 0, '7d': 6, '30d': 29, '90d': 89 }[b.dataset.preset];
      state.range = { preset: b.dataset.preset, from: addDays(today, -span), to: today };
      renderPage();
    };
    const custom = () => {
      let from = el.querySelector('#fromInput').value;
      let to = el.querySelector('#toInput').value;
      if (!from || !to) return;
      if (from > to) [from, to] = [to, from];
      state.range = { preset: 'custom', from, to };
      renderPage();
    };
    el.querySelector('#fromInput').onchange = custom;
    el.querySelector('#toInput').onchange = custom;
  } else {
    el.innerHTML = page === 'settings' ? '' : refresh;
  }
  const btn = el.querySelector('#refreshBtn');
  if (btn) btn.onclick = () => renderPage();
}

function stopRefresh() {
  if (state.refreshTimer) clearInterval(state.refreshTimer);
  state.refreshTimer = null;
}

async function renderPage() {
  const pageEl = document.getElementById('page');
  if (!pageEl) return;
  const page = PAGES[location.hash.slice(1)] ? location.hash.slice(1) : 'overview';
  const info = PAGES[page];

  stopRefresh();
  destroyCharts();
  hideTooltip();
  document.querySelectorAll('#nav a').forEach(a => a.classList.toggle('active', a.dataset.page === page));
  document.getElementById('pageTitle').textContent = info.label;
  const sub = document.getElementById('pageSub');
  if (info.controls === 'range') {
    sub.innerHTML = `${esc(info.subtitle)} <span class="badge">${esc(fmtDay(state.range.from))} &ndash; ${esc(fmtDay(state.range.to))}</span>`;
  } else if (page === 'overview') {
    sub.innerHTML = `<span class="live"><span class="dot ring"></span>Live</span><span>${esc(info.subtitle)}</span>`;
  } else {
    sub.textContent = info.subtitle;
  }
  document.title = `${info.label} - Chromatic Menu Dashboard`;
  renderControls(page);

  pageEl.innerHTML = loading();
  switch (page) {
    case 'overview':
      await renderOverview(pageEl);
      state.refreshTimer = setInterval(async () => {
        await renderOverview(pageEl);
        updateRequestBadge();
      }, 60000);
      break;
    case 'timeline':
      await renderTimeline(pageEl, state.day);
      break;
    case 'programs':
      await renderPrograms(pageEl, state.range);
      break;
    case 'revenue':
      await renderRevenue(pageEl, state.range);
      break;
    case 'requests':
      await renderRequests(pageEl, state.requestFilter, ({ filter }) => {
        state.requestFilter = filter;
        updateRequestBadge();
        renderPage();
      });
      break;
    case 'settings': {
      const project = savedProject();
      renderSettings(pageEl, { theme: state.theme, setTheme, url: project?.url || '', email: state.email, logout, changeProject });
      break;
    }
  }
}

// ---------------------------------------------------------------------------
// Boot
// ---------------------------------------------------------------------------

async function boot() {
  setTheme(state.theme);
  window.addEventListener('hashchange', () => { if (document.getElementById('page')) renderPage(); });

  const project = savedProject();
  if (!project) { showConnect(); return; }

  connect(project.url, project.key);
  db().auth.onAuthStateChange((event) => {
    if (event === 'SIGNED_OUT' && document.getElementById('page')) showLogin();
  });

  const { data } = await db().auth.getSession();
  if (!data?.session) { showLogin(); return; }
  await enterApp();
}

boot().catch((e) => {
  root.innerHTML = authLayout(errorAlert(e.message || String(e)));
});
