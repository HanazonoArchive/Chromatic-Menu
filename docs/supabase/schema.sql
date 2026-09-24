-- =====================================================================
-- Chromatic Menu - Supabase schema (telemetry, game requests, dashboard)
--
-- Paste this whole file into Supabase > SQL Editor and click Run.
-- It is safe to run again: every statement is idempotent.
--
-- Security model
--   anon          (the key built into Chromatic Menu): INSERT only.
--                 It can never read anything back.
--   authenticated (dashboard users you create in Authentication > Users):
--                 read everything, and change a game request's status.
-- =====================================================================

create extension if not exists pg_cron;

-- Shop-local time zone used to split data into days and hours.
-- Change the returned value if the shop is not in the Philippines.
create or replace function public.shop_tz()
returns text
language sql
immutable
as $$ select 'Asia/Manila' $$;

-- ---------------------------------------------------------------------
-- Tables
-- ---------------------------------------------------------------------

create table if not exists public.heartbeats (
    id               bigserial primary key,
    ts               timestamptz not null,
    pc_name          text        not null check (char_length(pc_name) between 1 and 64),
    menu_name        text        not null check (char_length(menu_name) between 1 and 64),
    program          text        not null check (char_length(program) between 1 and 128),
    interval_seconds int         not null check (interval_seconds between 15 and 3600),
    received_at      timestamptz not null default now()
);

create index if not exists heartbeats_ts_idx    on public.heartbeats (ts desc);
create index if not exists heartbeats_pc_ts_idx on public.heartbeats (pc_name, ts desc);

create table if not exists public.game_requests (
    id          bigserial primary key,
    created_at  timestamptz not null default now(),
    pc_name     text        not null check (char_length(pc_name) between 1 and 64),
    menu_name   text        not null check (char_length(menu_name) between 1 and 64),
    title       text        not null check (char_length(btrim(title)) between 1 and 100),
    description text        not null default '' check (char_length(description) <= 500),
    status      text        not null default 'new' check (status in ('new', 'added', 'rejected'))
);

create index if not exists game_requests_created_idx on public.game_requests (created_at desc);
create index if not exists game_requests_pc_idx      on public.game_requests (pc_name, created_at desc);

create table if not exists public.daily_summary (
    day            date    not null,
    pc_name        text    not null,
    menu_name      text    not null,
    minutes_on     numeric not null,
    minutes_active numeric not null,
    top_program    text,
    primary key (day, pc_name)
);

-- ---------------------------------------------------------------------
-- Game request cooldown: one request per PC every 5 minutes.
-- The client shows a friendly message when the error starts with "cooldown".
-- ---------------------------------------------------------------------

create or replace function public.game_requests_before_insert()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
declare
    last_at timestamptz;
begin
    select max(created_at) into last_at
    from public.game_requests
    where pc_name = new.pc_name;

    if last_at is not null and last_at > now() - interval '5 minutes' then
        raise exception 'cooldown: wait % seconds',
            ceil(extract(epoch from (last_at + interval '5 minutes' - now())))::int;
    end if;

    new.status     := 'new';
    new.created_at := now();
    new.title      := btrim(new.title);
    return new;
end
$$;

drop trigger if exists game_requests_before_insert on public.game_requests;
create trigger game_requests_before_insert
    before insert on public.game_requests
    for each row execute function public.game_requests_before_insert();

-- ---------------------------------------------------------------------
-- Row Level Security and grants
-- ---------------------------------------------------------------------

alter table public.heartbeats    enable row level security;
alter table public.game_requests enable row level security;
alter table public.daily_summary enable row level security;

revoke all on public.heartbeats    from anon, authenticated;
revoke all on public.game_requests from anon, authenticated;
revoke all on public.daily_summary from anon, authenticated;

grant insert (ts, pc_name, menu_name, program, interval_seconds) on public.heartbeats to anon;
grant usage on sequence public.heartbeats_id_seq to anon;
grant select on public.heartbeats to authenticated;

grant insert (pc_name, menu_name, title, description) on public.game_requests to anon;
grant usage on sequence public.game_requests_id_seq to anon;
grant select on public.game_requests to authenticated;
grant update (status) on public.game_requests to authenticated;

grant select on public.daily_summary to authenticated;

drop policy if exists heartbeats_anon_insert  on public.heartbeats;
drop policy if exists heartbeats_auth_select  on public.heartbeats;
drop policy if exists requests_anon_insert    on public.game_requests;
drop policy if exists requests_auth_select    on public.game_requests;
drop policy if exists requests_auth_update    on public.game_requests;
drop policy if exists summary_auth_select     on public.daily_summary;

create policy heartbeats_anon_insert on public.heartbeats
    for insert to anon with check (true);
create policy heartbeats_auth_select on public.heartbeats
    for select to authenticated using (true);

create policy requests_anon_insert on public.game_requests
    for insert to anon with check (status = 'new');
create policy requests_auth_select on public.game_requests
    for select to authenticated using (true);
create policy requests_auth_update on public.game_requests
    for update to authenticated using (true) with check (status in ('new', 'added', 'rejected'));

create policy summary_auth_select on public.daily_summary
    for select to authenticated using (true);

-- ---------------------------------------------------------------------
-- Aggregation
--
-- Every heartbeat row covers interval_seconds of time ending at ts.
-- Idle programs:   'Chromatic Menu', 'Unknown'
-- Active programs: everything else (including 'Windows')
-- Top-program lists also leave out 'Windows'.
-- ---------------------------------------------------------------------

create or replace function public.summarize_range(p_from date, p_to date)
returns table (
    day            date,
    pc_name        text,
    menu_name      text,
    minutes_on     numeric,
    minutes_active numeric,
    top_program    text
)
language sql
stable
security invoker
set search_path = public
as $$
    with base as (
        select (h.ts at time zone public.shop_tz())::date as day,
               h.pc_name, h.menu_name, h.program, h.interval_seconds, h.ts
        from public.heartbeats h
        where h.ts >= (p_from::timestamp at time zone public.shop_tz())
          and h.ts <  ((p_to + 1)::timestamp at time zone public.shop_tz())
    ),
    per_pc as (
        select b.day, b.pc_name,
               (array_agg(b.menu_name order by b.ts desc))[1] as menu_name,
               sum(b.interval_seconds) / 60.0 as minutes_on,
               coalesce(sum(b.interval_seconds) filter (
                   where b.program not in ('Chromatic Menu', 'Unknown')), 0) / 60.0 as minutes_active
        from base b
        group by b.day, b.pc_name
    ),
    programs as (
        select b.day, b.pc_name, b.program,
               row_number() over (partition by b.day, b.pc_name
                                  order by sum(b.interval_seconds) desc, b.program) as rn
        from base b
        where b.program not in ('Chromatic Menu', 'Unknown', 'Windows')
        group by b.day, b.pc_name, b.program
    )
    select p.day, p.pc_name, p.menu_name,
           round(p.minutes_on, 2), round(p.minutes_active, 2),
           t.program
    from per_pc p
    left join programs t on t.day = p.day and t.pc_name = p.pc_name and t.rn = 1
$$;

create or replace function public.refresh_daily_summary(p_from date, p_to date)
returns void
language sql
security definer
set search_path = public
as $$
    insert into public.daily_summary (day, pc_name, menu_name, minutes_on, minutes_active, top_program)
    select s.day, s.pc_name, s.menu_name, s.minutes_on, s.minutes_active, s.top_program
    from public.summarize_range(p_from, p_to) s
    on conflict (day, pc_name) do update
        set menu_name      = excluded.menu_name,
            minutes_on     = excluded.minutes_on,
            minutes_active = excluded.minutes_active,
            top_program    = excluded.top_program
$$;

-- Keeps 90 days of raw heartbeats. Days are summarized before deletion,
-- so daily_summary keeps the long-term history.
create or replace function public.purge_old_heartbeats()
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
    cutoff_day date := (now() at time zone public.shop_tz())::date - 90;
begin
    perform public.refresh_daily_summary(cutoff_day - 3, cutoff_day - 1);
    delete from public.heartbeats
    where ts < (cutoff_day::timestamp at time zone public.shop_tz());
end
$$;

-- ---------------------------------------------------------------------
-- Dashboard functions (called by signed-in dashboard users)
-- ---------------------------------------------------------------------

create or replace function public.get_pc_status()
returns table (
    pc_name               text,
    menu_name             text,
    last_seen             timestamptz,
    last_program          text,
    last_interval_seconds int,
    is_online             boolean
)
language sql
stable
security invoker
set search_path = public
as $$
    with pcs as (
        select s.pc_name from public.daily_summary s
        union
        select distinct h.pc_name from public.heartbeats h where h.ts > now() - interval '2 days'
    )
    select l.pc_name, l.menu_name, l.ts, l.program, l.interval_seconds,
           l.ts >= now() - make_interval(secs => l.interval_seconds * 2 + 60)
    from pcs
    cross join lateral (
        select h.pc_name, h.menu_name, h.ts, h.program, h.interval_seconds
        from public.heartbeats h
        where h.pc_name = pcs.pc_name
        order by h.ts desc
        limit 1
    ) l
    order by l.pc_name
$$;

-- Timeline of one shop-local day. Consecutive heartbeats with the same program
-- form a segment. A gap larger than two intervals (+30 s) means the PC was off;
-- power_on counts those power-on blocks per PC (1, 2, 3, ...).
create or replace function public.get_day_timeline(p_day date)
returns table (
    pc_name   text,
    power_on  int,
    seg_start timestamptz,
    seg_end   timestamptz,
    kind      text,
    program   text
)
language sql
stable
security invoker
set search_path = public
as $$
    with rows as (
        select h.pc_name, h.ts, h.program, h.interval_seconds,
               lag(h.ts)      over w as prev_ts,
               lag(h.program) over w as prev_program
        from public.heartbeats h
        where h.ts >= (p_day::timestamp at time zone public.shop_tz())
          and h.ts <  ((p_day + 1)::timestamp at time zone public.shop_tz())
        window w as (partition by h.pc_name order by h.ts)
    ),
    flagged as (
        select r.*,
               (r.prev_ts is null
                or r.ts - r.prev_ts > make_interval(secs => r.interval_seconds * 2 + 30)) as is_gap,
               (r.prev_ts is null
                or r.ts - r.prev_ts > make_interval(secs => r.interval_seconds * 2 + 30)
                or r.program is distinct from r.prev_program) as is_break
        from rows r
    ),
    grouped as (
        select f.*,
               sum(case when f.is_gap then 1 else 0 end)   over (partition by f.pc_name order by f.ts) as power_on,
               sum(case when f.is_break then 1 else 0 end) over (partition by f.pc_name order by f.ts) as seg
        from flagged f
    )
    select g.pc_name,
           g.power_on::int,
           min(g.ts - make_interval(secs => g.interval_seconds)),
           max(g.ts),
           case when g.program in ('Chromatic Menu', 'Unknown') then 'idle' else 'active' end,
           g.program
    from grouped g
    group by g.pc_name, g.power_on, g.seg, g.program
    order by g.pc_name, min(g.ts)
$$;

create or replace function public.get_usage(p_from date, p_to date)
returns table (program text, pc_name text, minutes numeric)
language sql
stable
security invoker
set search_path = public
as $$
    select h.program, h.pc_name, round(sum(h.interval_seconds) / 60.0, 2)
    from public.heartbeats h
    where h.ts >= (p_from::timestamp at time zone public.shop_tz())
      and h.ts <  ((p_to + 1)::timestamp at time zone public.shop_tz())
    group by h.program, h.pc_name
    order by 3 desc
$$;

-- Per-day totals: stored summaries for older days, live numbers for
-- yesterday and today (the hourly job may not have caught up yet).
create or replace function public.get_daily(p_from date, p_to date)
returns table (
    day            date,
    pc_name        text,
    menu_name      text,
    minutes_on     numeric,
    minutes_active numeric,
    top_program    text
)
language sql
stable
security invoker
set search_path = public
as $$
    with bounds as (
        select (now() at time zone public.shop_tz())::date - 1 as live_from
    )
    select s.day, s.pc_name, s.menu_name, s.minutes_on, s.minutes_active, s.top_program
    from public.daily_summary s, bounds b
    where s.day between p_from and least(p_to, b.live_from - 1)
    union all
    select l.day, l.pc_name, l.menu_name, l.minutes_on, l.minutes_active, l.top_program
    from bounds b,
         lateral public.summarize_range(greatest(p_from, b.live_from), p_to) l
    where p_to >= b.live_from
    order by 1, 2
$$;

-- Average number of PCs actively in use, per weekday (1 = Monday) and hour.
create or replace function public.get_hourly_heatmap(p_from date, p_to date)
returns table (weekday int, hour int, avg_active_pcs numeric)
language sql
stable
security invoker
set search_path = public
as $$
    with days as (
        select extract(isodow from d)::int as weekday, count(*) as n
        from generate_series(p_from::timestamp, p_to::timestamp, interval '1 day') d
        group by 1
    ),
    active as (
        select extract(isodow from x.local_ts)::int as weekday,
               extract(hour from x.local_ts)::int  as hour,
               sum(x.interval_seconds)             as secs
        from (
            select h.ts at time zone public.shop_tz() as local_ts, h.interval_seconds
            from public.heartbeats h
            where h.ts >= (p_from::timestamp at time zone public.shop_tz())
              and h.ts <  ((p_to + 1)::timestamp at time zone public.shop_tz())
              and h.program not in ('Chromatic Menu', 'Unknown')
        ) x
        group by 1, 2
    )
    select d.weekday, hr.hour, round(coalesce(a.secs, 0) / 3600.0 / d.n, 3)
    from days d
    cross join generate_series(0, 23) as hr(hour)
    left join active a on a.weekday = d.weekday and a.hour = hr.hour
    order by 1, 2
$$;

-- Contiguous play session statistics by program across a date range.
-- Consecutive heartbeats with the same program on the same PC form a session.
create or replace function public.get_session_stats(p_from date, p_to date)
returns table (
    program         text,
    session_count   bigint,
    total_minutes   numeric,
    avg_minutes     numeric,
    median_minutes  numeric,
    max_minutes     numeric,
    quick_count     bigint,
    standard_count  bigint,
    marathon_count  bigint
)
language sql
stable
security invoker
set search_path = public
as $$
    with rows as (
        select h.pc_name, h.ts, h.program, h.interval_seconds,
               lag(h.ts)      over w as prev_ts,
               lag(h.program) over w as prev_program
        from public.heartbeats h
        where h.ts >= (p_from::timestamp at time zone public.shop_tz())
          and h.ts <  ((p_to + 1)::timestamp at time zone public.shop_tz())
          and h.program not in ('Chromatic Menu', 'Unknown', 'Windows')
        window w as (partition by h.pc_name order by h.ts)
    ),
    breaks as (
        select r.*,
               (r.prev_ts is null
                or r.ts - r.prev_ts > make_interval(secs => r.interval_seconds * 2 + 30)
                or r.program is distinct from r.prev_program) as is_break
        from rows r
    ),
    sessions as (
        select b.pc_name, b.program,
               sum(case when b.is_break then 1 else 0 end) over (partition by b.pc_name order by b.ts) as session_id,
               b.interval_seconds
        from breaks b
    ),
    aggregated as (
        select s.program, s.pc_name, s.session_id,
               sum(s.interval_seconds) / 60.0 as session_mins
        from sessions s
        group by s.program, s.pc_name, s.session_id
    )
    select a.program,
           count(*)::bigint as session_count,
           round(sum(a.session_mins), 1) as total_minutes,
           round(avg(a.session_mins), 1) as avg_minutes,
           round(percentile_cont(0.5) within group (order by a.session_mins)::numeric, 1) as median_minutes,
           round(max(a.session_mins), 1) as max_minutes,
           count(*) filter (where a.session_mins < 20)::bigint as quick_count,
           count(*) filter (where a.session_mins between 20 and 60)::bigint as standard_count,
           count(*) filter (where a.session_mins > 60)::bigint as marathon_count
    from aggregated a
    group by a.program
    order by total_minutes desc;
$$;

-- Detects network latency/outages where heartbeats buffered in client memory
-- were flushed in a delayed batch (received_at - ts > interval_seconds * 2 + 30).
create or replace function public.get_network_incidents(p_day date)
returns table (
    pc_name        text,
    incident_time  timestamptz,
    delay_seconds  int,
    program        text
)
language sql
stable
security invoker
set search_path = public
as $$
    select h.pc_name,
           h.ts as incident_time,
           round(extract(epoch from (h.received_at - h.ts)))::int as delay_seconds,
           h.program
    from public.heartbeats h
    where h.ts >= (p_day::timestamp at time zone public.shop_tz())
      and h.ts <  ((p_day + 1)::timestamp at time zone public.shop_tz())
      and extract(epoch from (h.received_at - h.ts)) > (h.interval_seconds * 2 + 30)
    order by h.ts desc;
$$;

-- Supabase grants EXECUTE on new functions to anon by default; only signed-in
-- dashboard users may call the read functions, and nobody may call the
-- maintenance functions through the API.
revoke execute on function public.summarize_range(date, date)       from public, anon, authenticated;
revoke execute on function public.refresh_daily_summary(date, date) from public, anon, authenticated;
revoke execute on function public.purge_old_heartbeats()            from public, anon, authenticated;
revoke execute on function public.get_pc_status()                   from public, anon;
revoke execute on function public.get_day_timeline(date)            from public, anon;
revoke execute on function public.get_usage(date, date)             from public, anon;
revoke execute on function public.get_daily(date, date)             from public, anon;
revoke execute on function public.get_hourly_heatmap(date, date)    from public, anon;
revoke execute on function public.get_session_stats(date, date)     from public, anon;
revoke execute on function public.get_network_incidents(date)       from public, anon;
revoke execute on function public.game_requests_before_insert()     from public, anon, authenticated;

grant execute on function public.summarize_range(date, date)    to authenticated;
grant execute on function public.get_pc_status()                to authenticated;
grant execute on function public.get_day_timeline(date)         to authenticated;
grant execute on function public.get_usage(date, date)          to authenticated;
grant execute on function public.get_daily(date, date)          to authenticated;
grant execute on function public.get_hourly_heatmap(date, date) to authenticated;
grant execute on function public.get_session_stats(date, date)  to authenticated;
grant execute on function public.get_network_incidents(date)    to authenticated;

-- ---------------------------------------------------------------------
-- Scheduled jobs (pg_cron runs in UTC; 19:00 UTC = 03:00 Manila)
-- ---------------------------------------------------------------------

select cron.unschedule(jobid)
from cron.job
where jobname in ('chromatic_daily_summary', 'chromatic_purge_heartbeats');

select cron.schedule(
    'chromatic_daily_summary',
    '5 * * * *',
    $$ select public.refresh_daily_summary(
           (now() at time zone public.shop_tz())::date - 1,
           (now() at time zone public.shop_tz())::date) $$
);

select cron.schedule(
    'chromatic_purge_heartbeats',
    '0 19 * * *',
    $$ select public.purge_old_heartbeats() $$
);
