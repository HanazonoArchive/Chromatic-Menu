import { api } from '../api.js';
import { icon } from '../icons.js';
import { esc, fmtDateTime, fmtAgo } from '../util.js';
import { errorBox, empty, card, shopTag } from './common.js';

const view = { grouped: true, query: '' };

// "Marvel Rivals!", "marvel  rivals" -> "marvel rivals"
function normalize(title) {
  return String(title || '').toLowerCase().replace(/[^\p{L}\p{N}]+/gu, ' ').trim();
}

function groupRequests(rows, byStatus) {
  const map = new Map();
  for (const r of rows) {
    const key = normalize(r.title) + (byStatus ? '|' + r.status : '');
    if (!map.has(key)) map.set(key, []);
    map.get(key).push(r);
  }
  return [...map.values()].map(list => ({
    title: list[0].title,
    status: list[0].status,
    items: list,
    ids: list.map(r => r.id),
    pcs: [...new Set(list.map(r => r.pc_name))],
    latest: list[0].created_at,
    notes: list.filter(r => r.description).map(r => r.description)
  }));
}

const LABEL = { new: 'New', added: 'Added', rejected: 'Rejected' };

export async function renderRequests(root, filter, onChanged) {
  let all;
  try {
    all = await api.requests('all');
  } catch (e) {
    root.innerHTML = errorBox(e);
    return;
  }
  if (!root.isConnected) return;

  const counts = { new: 0, added: 0, rejected: 0, all: all.length };
  for (const r of all) counts[r.status] = (counts[r.status] || 0) + 1;
  const inFilter = filter === 'all' ? all : all.filter(r => r.status === filter);
  const q = normalize(view.query);
  const rows = q ? inFilter.filter(r => normalize(r.title + ' ' + r.description + ' ' + r.pc_name).includes(q)) : inFilter;

  const groups = view.grouped
    ? groupRequests(rows, filter === 'all').sort((a, b) => b.items.length - a.items.length || b.latest.localeCompare(a.latest))
    : rows.map(r => groupRequests([r], true)[0]);

  const actions = (g) => {
    const n = g.ids.length;
    const ids = esc(g.ids.join(','));
    const suffix = n > 1 ? ` (${n})` : '';
    return g.status === 'new'
      ? `<button class="btn sm success" data-ids="${ids}" data-status="added">${icon('check')}Added${suffix}</button><button class="btn sm ghost danger" data-ids="${ids}" data-status="rejected">${icon('x')}Reject${suffix}</button>`
      : `<button class="btn sm ghost" data-ids="${ids}" data-status="new">Move back to New${suffix}</button>`;
  };

  const item = (g) => `<li class="${g.status === 'new' ? 'is-new' : ''}">
    <div>
      <div class="title">${esc(g.title)}${g.items.length > 1 ? `<span class="badge count-badge" title="${g.items.length} requests for this game">&times;${g.items.length}</span>` : ''}</div>
      ${g.notes.slice(0, 3).map(n => `<div class="desc">&ldquo;${esc(n)}&rdquo;</div>`).join('')}
      ${g.notes.length > 3 ? `<div class="desc subtle">+${g.notes.length - 3} more notes</div>` : ''}
      <div class="meta">
        <span class="strong">${esc(g.pcs.slice(0, 4).join(', '))}${g.pcs.length > 4 ? ` +${g.pcs.length - 4}` : ''}</span>
        ${shopTag(g.items[0].pc_name, g.items[0].menu_name)}
        <span title="${esc(fmtDateTime(g.latest))}">${g.items.length > 1 ? 'Latest ' : ''}${esc(fmtAgo(g.latest))}</span>
      </div>
    </div>
    <div class="side"><span class="badge ${esc(g.status)}">${esc(LABEL[g.status] || g.status)}</span><div class="actions">${actions(g)}</div></div>
  </li>`;

  const filters = [['new', 'New'], ['added', 'Added'], ['rejected', 'Rejected'], ['all', 'All']];
  root.innerHTML = `<div class="toolbar">
      <div class="seg" id="reqFilter" role="tablist">
        ${filters.map(([k, l]) => `<button data-filter="${k}" class="${k === filter ? 'active' : ''}" role="tab" aria-selected="${k === filter}">${l}<span class="count">${counts[k] || 0}</span></button>`).join('')}
      </div>
      <div class="toolbar-right">
        <label class="search">${icon('search')}<input class="input" id="reqSearch" type="search" placeholder="Search requests" value="${esc(view.query)}" aria-label="Search requests"></label>
        <label class="check"><input type="checkbox" id="groupToggle" ${view.grouped ? 'checked' : ''}>Group same game</label>
      </div>
    </div>
    <div id="reqMsg"></div>
    ${card({
      title: filter === 'new' ? 'Waiting for review' : filter === 'all' ? 'All requests' : `${LABEL[filter]} requests`,
      meta: view.grouped ? 'Most-requested first. Actions apply to every request in a group.' : 'Newest first',
      flush: true,
      body: groups.length === 0
        ? empty(q ? 'No requests match that search.' : filter === 'new' ? 'Nothing waiting. New requests from the Request a Game button appear here.' : 'No requests here.', 'message-square')
        : `<ul class="feed">${groups.map(item).join('')}</ul>`
    })}
    <p class="footnote">Customers send these from the Request a Game button. Each PC can send one every 5 minutes.</p>`;

  root.querySelector('#reqFilter').addEventListener('click', (e) => {
    const b = e.target.closest('button[data-filter]');
    if (b) onChanged({ filter: b.dataset.filter });
  });
  root.querySelector('#groupToggle').addEventListener('change', (e) => {
    view.grouped = e.target.checked;
    onChanged({ filter });
  });
  const search = root.querySelector('#reqSearch');
  let timer = null;
  search.addEventListener('input', () => {
    clearTimeout(timer);
    timer = setTimeout(() => {
      view.query = search.value;
      renderRequests(root, filter, onChanged).then(() => {
        const el = root.querySelector('#reqSearch');
        if (el) { el.focus(); el.setSelectionRange(el.value.length, el.value.length); }
      });
    }, 200);
  });
  root.querySelectorAll('button[data-status]').forEach(b => b.addEventListener('click', async () => {
    root.querySelectorAll('button[data-status]').forEach(x => { x.disabled = true; });
    try {
      await api.setRequestStatus(b.dataset.ids.split(',').map(Number), b.dataset.status);
      onChanged({ filter });
    } catch (err) {
      root.querySelectorAll('button[data-status]').forEach(x => { x.disabled = false; });
      root.querySelector('#reqMsg').innerHTML = `<div style="margin-bottom:12px">${errorBox(err)}</div>`;
    }
  }));
}
