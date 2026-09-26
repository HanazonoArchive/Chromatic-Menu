import { createClient } from 'https://cdn.jsdelivr.net/npm/@supabase/supabase-js@2.45.4/+esm';
import { store } from './util.js';
import { demoApi } from './demo.js';

let client = null;

// Demo mode answers every call from generated data instead of Supabase.
export function isDemo() {
  return store.get('demo') === '1';
}

export function setDemo(on) {
  if (on) store.set('demo', '1');
  else store.remove('demo');
}

export function savedProject() {
  const url = store.get('supabaseUrl');
  const key = store.get('supabaseKey');
  return url && key ? { url, key } : null;
}

export function normalizeUrl(url) {
  return String(url || '').trim().replace(/\/+$/, '');
}

// Confirms the URL and anon key belong to a reachable Supabase project.
export async function verifyProject(url, key) {
  let res;
  try {
    res = await fetch(`${url}/auth/v1/settings`, { headers: { apikey: key } });
  } catch {
    return 'Could not reach that URL. Check it is your Supabase project URL.';
  }
  if (res.ok) return null;
  if (res.status === 401 || res.status === 403) return 'Supabase rejected the key. Use the anon / public key.';
  return `Supabase answered HTTP ${res.status}. Check the project URL.`;
}

export function connect(url, key) {
  store.set('supabaseUrl', url);
  store.set('supabaseKey', key);
  client = createClient(url, key, {
    auth: { persistSession: true, autoRefreshToken: true, storageKey: 'cm-dashboard-auth' }
  });
  return client;
}

export function db() {
  return client;
}

export async function forgetProject() {
  if (client) {
    try { await client.auth.signOut(); } catch { /* already signed out */ }
  }
  store.remove('supabaseUrl');
  store.remove('supabaseKey');
  client = null;
}

async function rpc(name, args) {
  const { data, error } = await client.rpc(name, args);
  if (error) throw error;
  return data || [];
}

const liveApi = {
  pcStatus: () => rpc('get_pc_status'),
  daily: (from, to) => rpc('get_daily', { p_from: from, p_to: to }),
  timeline: (day) => rpc('get_day_timeline', { p_day: day }),
  usage: (from, to) => rpc('get_usage', { p_from: from, p_to: to }),
  heatmap: (from, to) => rpc('get_hourly_heatmap', { p_from: from, p_to: to }),
  // Newer functions: older schemas without them simply hide those sections.
  sessionStats: async (from, to) => {
    try {
      return await rpc('get_session_stats', { p_from: from, p_to: to });
    } catch {
      return null;
    }
  },
  networkIncidents: async (day) => {
    try {
      return await rpc('get_network_incidents', { p_day: day });
    } catch {
      return null;
    }
  },
  // { today_minutes, yesterday_same_time_minutes, yesterday_minutes }, or null on older schemas.
  activeSoFar: async () => {
    try {
      const rows = await rpc('get_active_so_far');
      return rows[0] || null;
    } catch {
      return null;
    }
  },

  async requests(status, limit = 500) {
    let q = client.from('game_requests').select('*').order('created_at', { ascending: false }).limit(limit);
    if (status && status !== 'all') q = q.eq('status', status);
    const { data, error } = await q;
    if (error) throw error;
    return data || [];
  },

  async newRequestCount() {
    const { count, error } = await client.from('game_requests').select('id', { count: 'exact', head: true }).eq('status', 'new');
    if (error) throw error;
    return count || 0;
  },

  // ids: one id or an array (duplicate requests for the same game are handled together).
  async setRequestStatus(ids, status) {
    const list = [].concat(ids);
    const { error } = await client.from('game_requests').update({ status }).in('id', list);
    if (error) throw error;
  }
};

export const api = new Proxy(liveApi, {
  get: (target, prop) => (isDemo() ? demoApi : target)[prop]
});

// PostgREST reports a missing function/table with these codes: the schema was not run.
export function isMissingSchema(error) {
  const code = error && error.code;
  return code === 'PGRST202' || code === 'PGRST205' || code === '42883' || code === '42P01';
}
