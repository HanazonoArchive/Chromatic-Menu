import { icon } from '../icons.js';
import {
  esc, store, currency, currencySymbol, CURRENCIES, minutesPerUnit, fmtMoney, rateText, staleDays, getAllShops
} from '../util.js';
import { card } from './common.js';

export function renderSettings(root, ctx) {
  const connection = ctx.demo
    ? `<div class="alert info">${icon('play-circle')}<div>You are exploring <b>demo data</b>. Nothing here comes from a real shop.</div></div>
       <div class="btn-row"><button class="btn primary" id="exitDemoBtn">${icon('database')}Connect a real project</button></div>`
    : `<dl class="kv">
        <dt>Supabase project</dt><dd class="mono">${esc(ctx.url)}</dd>
        <dt>Shops reporting</dt><dd>${esc(getAllShops().join(', ') || 'Waiting for PC telemetry')}</dd>
        <dt>Signed in as</dt><dd>${esc(ctx.email || '')}</dd>
      </dl>
      <div class="btn-row">
        <button class="btn" id="logoutBtn">${icon('log-out')}Log out</button>
        <button class="btn ghost danger" id="changeProjectBtn">${icon('database')}Use a different project</button>
      </div>
      <div class="hint">You stay signed in on this browser until you log out.</div>`;

  root.innerHTML = `<div class="grid cols-even">
    ${card({
      title: 'Revenue estimate',
      meta: 'Used by every money figure in the dashboard',
      body: `<div class="form-row">
          <label class="field"><span>Currency</span>
            <select class="input" id="currency">${CURRENCIES.map(c => `<option value="${c}" ${c === currency() ? 'selected' : ''}>${c} (${esc(currencySymbol(c))})</option>`).join('')}</select>
          </label>
          <label class="field"><span>Minutes per 1 coin / unit</span>
            <input class="input" id="mpp" type="number" min="1" max="120" step="1" inputmode="numeric" value="${minutesPerUnit()}">
          </label>
        </div>
        <div class="rate-preview" id="ratePreview"></div>
        <div class="btn-row"><button class="btn primary" id="saveRate">Save</button><span id="rateMsg" class="meta" role="status"></span></div>`
    })}
    ${card({
      title: 'Display',
      body: `<div class="eyebrow" style="margin-bottom:8px">Theme</div>
        <div class="seg" id="themeSeg">
          <button data-theme="dark" class="${ctx.theme === 'dark' ? 'active' : ''}">${icon('moon')}Dark</button>
          <button data-theme="light" class="${ctx.theme === 'light' ? 'active' : ''}">${icon('sun')}Light</button>
        </div>
        <label class="field" style="margin-top:18px"><span>Treat a PC as inactive after</span>
          <div class="input-suffix"><input class="input" id="staleDays" type="number" min="1" max="365" step="1" inputmode="numeric" value="${staleDays()}"><span>days without a heartbeat</span></div>
        </label>
        <div class="hint" style="margin-top:-8px">Inactive PCs are hidden from the floor and the timeline, and do not count as offline.</div>`
    })}
    ${card({ title: 'Connection', actions: `<span class="state ${ctx.demo ? 'idle' : 'use'}"><span class="dot"></span>${ctx.demo ? 'Demo' : 'Connected'}</span>`, body: connection })}
    ${card({
      title: 'Help',
      body: `<p class="muted" style="margin-top:0">Setup steps, the database schema and troubleshooting are in the setup guide.</p>
        <a class="btn" href="setup.html">${icon('book-open')}Open setup guide</a>`
    })}
  </div>`;

  const mpp = root.querySelector('#mpp');
  const cur = root.querySelector('#currency');
  const preview = root.querySelector('#ratePreview');
  const updatePreview = () => {
    const m = Number(mpp.value);
    if (!(m >= 1 && m <= 120)) { preview.textContent = 'Enter a number from 1 to 120.'; return; }
    const sym = currencySymbol(cur.value);
    const perHour = (60 / m).toLocaleString('en-US', { maximumFractionDigits: 2 });
    preview.innerHTML = `${esc(sym)}1 buys <b>${m} min</b> &middot; one PC busy for an hour earns <b>${esc(sym)}${perHour}</b>`;
  };
  mpp.addEventListener('input', updatePreview);
  cur.addEventListener('change', updatePreview);
  updatePreview();

  root.querySelector('#saveRate').addEventListener('click', () => {
    const v = Number(mpp.value);
    const msg = root.querySelector('#rateMsg');
    if (!(v >= 1 && v <= 120)) { msg.textContent = 'Enter a number from 1 to 120.'; return; }
    store.set('minutesPerUnit', String(v));
    store.set('currency', cur.value);
    msg.textContent = `Saved. ${rateText()}, ${fmtMoney(60 / v)} per PC-hour.`;
  });
  root.querySelector('#staleDays').addEventListener('change', (e) => {
    const v = Math.round(Number(e.target.value));
    if (v >= 1 && v <= 365) store.set('staleDays', String(v));
    else e.target.value = staleDays();
  });
  root.querySelector('#themeSeg').addEventListener('click', (e) => {
    const b = e.target.closest('button[data-theme]');
    if (!b) return;
    ctx.setTheme(b.dataset.theme);
    root.querySelectorAll('#themeSeg button').forEach(x => x.classList.toggle('active', x === b));
  });
  root.querySelector('#logoutBtn')?.addEventListener('click', ctx.logout);
  root.querySelector('#changeProjectBtn')?.addEventListener('click', ctx.changeProject);
  root.querySelector('#exitDemoBtn')?.addEventListener('click', ctx.exitDemo);
}
