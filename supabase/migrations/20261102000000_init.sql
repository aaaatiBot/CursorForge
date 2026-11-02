-- =============================================================================
-- CursorForge Authoritative Database Migration v6.0 (.NET 10 LTS Target)
-- =============================================================================

-- 1. EXTENSIONS
create extension if not exists pgcrypto;
create extension if not exists pg_net;
create extension if not exists pg_cron;

-- 2. DEFAULT PRIVILEGES (DENY BY DEFAULT)
alter default privileges in schema public revoke all on tables    from anon, authenticated;
alter default privileges in schema public revoke all on functions from public, anon, authenticated;
grant usage on schema public to anon, authenticated;

-- 3. PROFILES TABLE
create table public.profiles (
  id                    uuid primary key references auth.users(id) on delete cascade,
  username              text not null,
  recovery_code_hash    text not null,
  recovery_code_used_at timestamptz,          -- NULL = usable; non-null = consumed
  terms_accepted_at     timestamptz not null, -- SERVER-SET at signup; immutable
  password_changed_at   timestamptz,
  deletion_requested_at timestamptz,
  created_at            timestamptz not null default now(),

  constraint username_format   check (username ~ '^[a-zA-Z0-9_]{3,20}$'),
  constraint username_reserved check (lower(username) not in (
    'admin','administrator','root','system','moderator','api','www',
    'mail','ftp','null','undefined','support','supabase','cursorforge'
  )),
  constraint recovery_hash_format check (recovery_code_hash ~ '^[0-9a-f]{64}$')
);

create unique index idx_profiles_username_lower on public.profiles(lower(username));

revoke all on public.profiles from anon, authenticated;
grant select (id, username, created_at, terms_accepted_at)
  on public.profiles to anon, authenticated;

-- 4. CURSORS TABLE (ONE ROW = ONE THEME)
create table public.cursors (
  id              uuid primary key default gen_random_uuid(),
  owner_id        uuid not null references auth.users(id) on delete cascade,
  name            text not null,
  tags            text[] not null default '{}',
  preview_path    text not null,
  roles           jsonb not null,
  status          text not null default 'pending'
                    check (status in ('pending','approved','rejected','archived')),
  hidden          boolean not null default false,
  like_count      int not null default 0,
  download_count  int not null default 0,
  search          tsvector generated always as (
    to_tsvector('simple',
      coalesce(name, '') || ' ' ||
      coalesce(regexp_replace(array_to_string(tags, ' '), '[&|!():*]', ' ', 'g'), ''))
  ) stored,
  created_at      timestamptz not null default now(),

  constraint name_length  check (length(name) between 3 and 100),
  constraint tags_limit   check (coalesce(array_length(tags, 1), 0) <= 10),
  constraint roles_shape  check (jsonb_typeof(roles) = 'object' and roles <> '{}'::jsonb),
  constraint like_count_nonneg      check (like_count >= 0),
  constraint download_count_nonneg  check (download_count >= 0)
);

create index idx_cursors_search on public.cursors using gin(search);
create index idx_cursors_browse on public.cursors(created_at desc, id desc)
  where status = 'approved' and hidden = false;

revoke all on public.cursors from anon, authenticated;
grant select (id, owner_id, name, tags, preview_path, roles,
              status, like_count, download_count, created_at)
  on public.cursors to anon, authenticated;

-- 5. SOCIAL INTERACTIONS
create table public.likes (
  user_id    uuid not null references auth.users(id) on delete cascade,
  cursor_id  uuid not null references public.cursors(id) on delete cascade,
  created_at timestamptz not null default now(),
  primary key (user_id, cursor_id)
);

create table public.downloads (
  id            uuid primary key default gen_random_uuid(),
  user_id       uuid not null references auth.users(id) on delete cascade,
  cursor_id     uuid not null references public.cursors(id) on delete cascade,
  download_date date not null default current_date,
  unique (user_id, cursor_id, download_date)
);

create table public.reports (
  id          uuid primary key default gen_random_uuid(),
  cursor_id   uuid not null references public.cursors(id) on delete cascade,
  reporter_id uuid not null references auth.users(id) on delete cascade,
  reason      text not null check (length(reason) between 5 and 500),
  created_at  timestamptz not null default now(),
  unique (cursor_id, reporter_id)
);

create index idx_reports_cursor_created on public.reports(cursor_id, created_at desc);

revoke all on public.likes, public.downloads, public.reports from anon, authenticated;
grant select, delete on public.likes to authenticated;
grant select on public.downloads to authenticated;

-- 6. MODERATION STATE (ISOLATED FROM REALTIME STREAM)
create table public.cursor_moderation (
  cursor_id     uuid primary key references public.cursors(id) on delete cascade,
  report_count  int not null default 0,
  hidden_reason text check (hidden_reason in ('reports','admin')),
  review_status text not null default 'pending'
                  check (review_status in ('pending','cleared','flagged')),
  updated_at    timestamptz not null default now()
);

revoke all on public.cursor_moderation from anon, authenticated;

-- 7. UPLOAD SESSIONS
create table public.upload_sessions (
  id             uuid primary key default gen_random_uuid(),
  owner_id       uuid not null references auth.users(id) on delete cascade,
  name           text not null,
  tags           text[] not null default '{}',
  preview_path   text not null,
  role_paths     jsonb not null,
  status         text not null default 'active'
                   check (status in ('active','validated','completed','expired','abandoned')),
  validated_at   timestamptz,
  created_at     timestamptz not null default now(),
  expires_at     timestamptz not null default (now() + interval '30 minutes')
);

create index idx_sessions_active on public.upload_sessions(status, expires_at)
  where status in ('active','validated');

revoke all on public.upload_sessions from anon, authenticated;
grant select on public.upload_sessions to authenticated;

-- 8. DURABLE DELETION OUTBOX
create table public.deletion_outbox (
  id               uuid primary key default gen_random_uuid(),
  cursor_id        uuid not null,
  owner_id         uuid not null,
  bucket_id        text not null,
  storage_paths    text[] not null,
  status           text not null default 'pending'
                     check (status in ('pending','leased','completed','dead_letter')),
  retry_count      int not null default 0,
  max_retries      int not null default 5,
  lease_id         uuid,
  lease_expires_at timestamptz,
  next_attempt_at  timestamptz not null default now(),
  last_error       text,
  created_at       timestamptz not null default now(),
  completed_at     timestamptz,
  constraint uq_deletion_outbox_cursor_bucket unique (cursor_id, bucket_id)
);

create index idx_outbox_pending on public.deletion_outbox(next_attempt_at)
  where status = 'pending';
create index idx_outbox_dead on public.deletion_outbox(created_at desc)
  where status = 'dead_letter';

revoke all on public.deletion_outbox from anon, authenticated;

-- 9. RATE LIMITING STORE
create table public.reset_attempts (
  id            uuid primary key default gen_random_uuid(),
  username_hash text not null,
  ip_hash       text not null,
  ts            timestamptz not null default now()
);
create index idx_reset_user on public.reset_attempts(username_hash, ts desc);
create index idx_reset_ip   on public.reset_attempts(ip_hash, ts desc);

revoke all on public.reset_attempts from anon, authenticated;

create table public.write_rate_limits (
  key          text primary key,
  count        int not null default 0,
  window_start timestamptz not null default now(),
  expires_at   timestamptz not null
);
create index idx_wrl_expires on public.write_rate_limits(expires_at);

revoke all on public.write_rate_limits from anon, authenticated;

-- 10. TRIGGERS

-- Profile creation: server-generated terms_accepted_at
create or replace function public._trg_create_profile()
returns trigger language plpgsql security definer set search_path = '' as $$
declare
  v_username text := NEW.raw_user_meta_data->>'username';
  v_hash     text := NEW.raw_user_meta_data->>'recovery_code_hash';
  v_accepted text := NEW.raw_user_meta_data->>'terms_accepted';
begin
  if v_accepted is null or v_accepted <> 'true' then
    raise exception 'Terms must be accepted' using errcode = '22023';
  end if;

  if v_username is null or v_username !~ '^[a-zA-Z0-9_]{3,20}$' then
    raise exception 'Invalid username format' using errcode = '22023';
  end if;
  if lower(v_username) in ('admin','administrator','root','system','moderator',
                           'api','www','mail','ftp','null','undefined',
                           'support','supabase','cursorforge') then
    raise exception 'Reserved username' using errcode = '22023';
  end if;
  if v_hash is null or v_hash !~ '^[0-9a-f]{64}$' then
    raise exception 'Invalid recovery code hash' using errcode = '22023';
  end if;

  insert into public.profiles (id, username, recovery_code_hash, terms_accepted_at)
  values (NEW.id, v_username, v_hash, now());
  return NEW;
end;
$$;
create trigger trg_create_profile after insert on auth.users
  for each row execute function public._trg_create_profile();

-- Moderation initialization
create or replace function public._trg_init_moderation()
returns trigger language plpgsql security definer set search_path = '' as $$
begin
  insert into public.cursor_moderation (cursor_id) values (NEW.id);
  return NEW;
end;
$$;
create trigger trg_init_moderation after insert on public.cursors
  for each row execute function public._trg_init_moderation();

-- Like count sync (restricted to approved, non-hidden cursors)
create or replace function public._trg_like_count()
returns trigger language plpgsql security definer set search_path = '' as $$
begin
  if TG_OP = 'INSERT' then
    update public.cursors set like_count = like_count + 1
    where id = NEW.cursor_id and status = 'approved' and hidden = false;
  elsif TG_OP = 'DELETE' then
    update public.cursors set like_count = greatest(0, like_count - 1)
    where id = OLD.cursor_id;
  end if;
  return null;
end;
$$;
create trigger trg_like_count after insert or delete on public.likes
  for each row execute function public._trg_like_count();

-- Report count
create or replace function public._trg_report_count()
returns trigger language plpgsql security definer set search_path = '' as $$
begin
  update public.cursor_moderation
  set report_count = report_count + 1, updated_at = now()
  where cursor_id = NEW.cursor_id;
  return null;
end;
$$;
create trigger trg_report_count after insert on public.reports
  for each row execute function public._trg_report_count();

-- 11. SECURE RPCs

-- Rate limit write overloads
create or replace function public.rate_limit_write(
  p_key text, p_limit int, p_window interval, p_increment int
)
returns boolean language plpgsql security definer set search_path = '' as $$
declare v_count int;
begin
  insert into public.write_rate_limits (key, count, window_start, expires_at)
  values (p_key, p_increment, now(), now() + p_window)
  on conflict (key) do update
    set count = case when public.write_rate_limits.window_start < now() - p_window
                     then p_increment
                     else public.write_rate_limits.count + p_increment end,
        window_start = case when public.write_rate_limits.window_start < now() - p_window
                            then now()
                            else public.write_rate_limits.window_start end,
        expires_at = case when public.write_rate_limits.window_start < now() - p_window
                          then now() + p_window
                          else public.write_rate_limits.expires_at end
  returning count into v_count;

  return v_count <= p_limit;
end;
$$;

create or replace function public.rate_limit_write(
  p_key text, p_limit int, p_window interval
)
returns boolean language plpgsql security definer set search_path = '' as $$
begin
  return public.rate_limit_write(p_key, p_limit, p_window, 1);
end;
$$;

-- Atomic download increment
create or replace function public.increment_download(p_cursor_id uuid)
returns void language plpgsql security definer set search_path = '' as $$
declare v_new_id uuid;
begin
  if auth.uid() is null then
    raise exception 'Authentication required' using errcode = '42501';
  end if;

  if not public.rate_limit_write('dl:' || auth.uid()::text, 200, interval '1 hour') then
    raise exception 'Download rate limit exceeded' using errcode = '42501';
  end if;

  if not exists (
    select 1 from public.cursors
    where id = p_cursor_id and status = 'approved' and hidden = false
  ) then
    raise exception 'Cursor not available' using errcode = '42501';
  end if;

  insert into public.downloads (user_id, cursor_id, download_date)
  values ((select auth.uid()), p_cursor_id, current_date)
  on conflict do nothing
  returning id into v_new_id;

  if v_new_id is not null then
    update public.cursors set download_count = download_count + 1
    where id = p_cursor_id;
  end if;
end;
$$;

-- Recovery code claim (single-use)
create or replace function public.claim_recovery_code(
  p_username text, p_hash text
)
returns uuid language plpgsql security definer set search_path = '' as $$
declare v_id uuid;
begin
  update public.profiles
  set recovery_code_used_at = now()
  where lower(username) = lower(p_username)
    and recovery_code_hash = p_hash
    and recovery_code_used_at is null
  returning id into v_id;
  return v_id;
end;
$$;

-- Recovery code rotation
create or replace function public.rotate_recovery_code(
  p_user_id uuid, p_new_hash text
)
returns void language plpgsql security definer set search_path = '' as $$
declare v_rows int;
begin
  if p_new_hash !~ '^[0-9a-f]{64}$' then
    raise exception 'Invalid hash format' using errcode = '22023';
  end if;

  update public.profiles
  set recovery_code_hash = p_new_hash,
      recovery_code_used_at = null,
      password_changed_at = now()
  where id = p_user_id
    and recovery_code_used_at is not null;

  get diagnostics v_rows = row_count;
  if v_rows <> 1 then
    raise exception 'Rotation affected % rows (expected 1)', v_rows;
  end if;
end;
$$;

-- Create Upload Session
create or replace function public.create_upload_session(
  p_name text, p_tags text[], p_roles text[]
)
returns jsonb language plpgsql security definer set search_path = '' as $$
declare
  v_uid        uuid := auth.uid();
  v_sid        uuid := gen_random_uuid();
  v_preview    text;
  v_role_paths jsonb := '{}'::jsonb;
  v_role       text;
  v_tag        text;
begin
  if v_uid is null then raise exception 'Authentication required' using errcode = '42501'; end if;

  if not public.rate_limit_write('upload:' || v_uid::text, 10, interval '1 day') then
    raise exception 'Upload session rate limit exceeded';
  end if;

  if length(p_name) < 3 or length(p_name) > 100 then
    raise exception 'Invalid name length';
  end if;
  if array_length(p_roles, 1) is null or array_length(p_roles, 1) = 0 then
    raise exception 'At least one role required';
  end if;
  if coalesce(array_length(p_tags, 1), 0) > 10 then
    raise exception 'Too many tags';
  end if;
  foreach v_tag in array p_tags loop
    if v_tag !~ '^[a-zA-Z0-9][a-zA-Z0-9 _-]{0,29}$' then
      raise exception 'Invalid tag: %', v_tag;
    end if;
  end loop;

  v_preview := v_uid::text || '/' || v_sid::text || '/preview.png';

  foreach v_role in array p_roles loop
    if v_role not in ('Arrow','Help','AppStarting','Wait','Crosshair','IBeam',
                      'NWPen','No','SizeAll','SizeNESW','SizeNS','SizeNWSE',
                      'SizeWE','Hand','UpArrow') then
      raise exception 'Invalid role: %', v_role;
    end if;
    v_role_paths := jsonb_set(
      v_role_paths, array[v_role],
      to_jsonb(v_uid::text || '/' || v_sid::text || '/' || v_role || '.png')
    );
  end loop;

  insert into public.upload_sessions
    (id, owner_id, name, tags, preview_path, role_paths)
  values (v_sid, v_uid, p_name, p_tags, v_preview, v_role_paths);

  return jsonb_build_object(
    'session_id', v_sid,
    'preview_path', v_preview,
    'role_paths', v_role_paths,
    'expires_at', (now() + interval '30 minutes')
  );
end;
$$;

-- Complete Upload Session
create or replace function public.complete_upload_session(
  p_session_id uuid, p_hotspots jsonb
)
returns uuid language plpgsql security definer set search_path = '' as $$
declare
  v_uid    uuid := auth.uid();
  v_sess   public.upload_sessions%rowtype;
  v_roles  jsonb := '{}'::jsonb;
  v_role   text;
  v_path   text;
  v_hx     int;
  v_hy     int;
begin
  if v_uid is null then raise exception 'Authentication required' using errcode = '42501'; end if;

  select * into v_sess from public.upload_sessions
  where id = p_session_id and owner_id = v_uid for update;

  if v_sess.id is null then raise exception 'Session not found'; end if;
  if v_sess.status <> 'validated' then
    raise exception 'Session must be validated before completion; status=%', v_sess.status;
  end if;
  if v_sess.expires_at < now() then
    raise exception 'Session expired';
  end if;

  for v_role, v_path in select * from jsonb_each_text(v_sess.role_paths) loop
    v_hx := coalesce((p_hotspots->v_role->>'x')::int, 0);
    v_hy := coalesce((p_hotspots->v_role->>'y')::int, 0);

    if v_hx < 0 or v_hx >= 128 or v_hy < 0 or v_hy >= 128 then
      raise exception 'Invalid hotspot for %', v_role;
    end if;

    v_roles := jsonb_set(
      v_roles, array[v_role],
      jsonb_build_object(
        'storage_path', v_path,
        'hotspot_x', v_hx,
        'hotspot_y', v_hy
      )
    );
  end loop;

  insert into public.cursors
    (id, owner_id, name, tags, preview_path, roles, status)
  values
    (p_session_id, v_uid, v_sess.name, v_sess.tags, v_sess.preview_path,
     v_roles, 'pending');

  update public.upload_sessions set status = 'completed' where id = p_session_id;
  return p_session_id;
end;
$$;

-- Request Cursor Deletion
create or replace function public.request_cursor_deletion(p_cursor_id uuid)
returns void language plpgsql security definer set search_path = '' as $$
declare
  v_uid       uuid := auth.uid();
  v_row       public.cursors%rowtype;
  v_src_paths text[] := array[]::text[];
  v_role      jsonb;
begin
  if v_uid is null then raise exception 'Authentication required' using errcode = '42501'; end if;

  select * into v_row from public.cursors
  where id = p_cursor_id and owner_id = v_uid for update;
  if v_row.id is null then raise exception 'Not found or access denied'; end if;

  for v_role in select value from jsonb_each(v_row.roles) loop
    v_src_paths := array_append(v_src_paths, v_role->>'storage_path');
  end loop;

  update public.cursors set status = 'archived', hidden = true where id = p_cursor_id;

  insert into public.deletion_outbox
    (cursor_id, owner_id, bucket_id, storage_paths)
  values
    (p_cursor_id, v_uid, 'cursor-sources',  v_src_paths),
    (p_cursor_id, v_uid, 'cursor-previews', array[v_row.preview_path])
  on conflict (cursor_id, bucket_id) do nothing;
end;
$$;

-- Lease Deletion Tasks
create or replace function public.lease_deletion_tasks(
  p_worker uuid, p_batch int default 10, p_lease interval default interval '5 minutes'
)
returns setof public.deletion_outbox
language plpgsql security definer set search_path = '' as $$
begin
  return query
  with t as (
    select id from public.deletion_outbox
    where (status = 'pending'
           or (status = 'leased' and lease_expires_at < now()))
      and next_attempt_at <= now()
      and retry_count < max_retries
    order by next_attempt_at
    limit p_batch
    for update skip locked
  )
  update public.deletion_outbox o
  set status = 'leased',
      lease_id = p_worker,
      lease_expires_at = now() + p_lease
  from t where o.id = t.id
  returning o.*;
end;
$$;

-- Mark Deletion Outcome
create or replace function public.mark_deletion_outcome(
  p_task_id uuid,
  p_lease_id uuid,
  p_success boolean,
  p_error text default null
)
returns boolean language plpgsql security definer set search_path = '' as $$
declare
  v_retry int;
  v_max int;
  v_rows int;
begin
  if p_success then
    update public.deletion_outbox
    set status = 'completed',
        completed_at = now(),
        lease_id = null,
        lease_expires_at = null
    where id = p_task_id and lease_id = p_lease_id;
  else
    select retry_count, max_retries into v_retry, v_max
    from public.deletion_outbox where id = p_task_id and lease_id = p_lease_id;

    if not found then
      return false;
    end if;

    v_retry := v_retry + 1;

    update public.deletion_outbox
    set retry_count = v_retry,
        status = case when v_retry >= v_max then 'dead_letter' else 'pending' end,
        next_attempt_at = case when v_retry >= v_max then now()
                               else now() + (interval '30 seconds' * power(2, v_retry) * (0.5 + random())) end,
        lease_id = null,
        lease_expires_at = null,
        last_error = p_error
    where id = p_task_id and lease_id = p_lease_id;
  end if;

  get diagnostics v_rows = row_count;
  return v_rows > 0;
end;
$$;

-- Check Report Threshold (auto-hide)
create or replace function public._check_report_threshold()
returns void language plpgsql security definer set search_path = '' as $$
declare
  v_hidden_ids uuid[];
begin
  with hidden_cursors as (
    update public.cursors c
    set hidden = true
    from public.cursor_moderation m
    where m.cursor_id = c.id
      and c.hidden = false
      and c.status = 'approved'
      and (select count(distinct r.reporter_id) from public.reports r
           where r.cursor_id = c.id
             and r.created_at >= now() - interval '7 days') >= 5
    returning c.id
  )
  select array_agg(id) into v_hidden_ids from hidden_cursors;

  if v_hidden_ids is not null and array_length(v_hidden_ids, 1) > 0 then
    update public.cursor_moderation
    set hidden_reason = 'reports',
        review_status = 'flagged',
        updated_at = now()
    where cursor_id = any(v_hidden_ids);
  end if;
end;
$$;

-- Social RPCs
create or replace function public.like_cursor(p_cursor_id uuid)
returns void language plpgsql security definer set search_path = '' as $$
declare v_uid uuid := auth.uid();
begin
  if v_uid is null then raise exception 'Authentication required' using errcode = '42501'; end if;

  if not public.rate_limit_write('like:' || v_uid::text, 60, interval '1 hour') then
    raise exception 'Like rate limit exceeded' using errcode = '42501';
  end if;

  if not exists (
    select 1 from public.cursors
    where id = p_cursor_id and status = 'approved' and hidden = false
  ) then
    raise exception 'Cursor not available' using errcode = '42501';
  end if;

  insert into public.likes (user_id, cursor_id)
  values (v_uid, p_cursor_id)
  on conflict do nothing;
end;
$$;

create or replace function public.unlike_cursor(p_cursor_id uuid)
returns void language plpgsql security definer set search_path = '' as $$
declare v_uid uuid := auth.uid();
begin
  if v_uid is null then raise exception 'Authentication required' using errcode = '42501'; end if;

  delete from public.likes
  where user_id = v_uid and cursor_id = p_cursor_id;
end;
$$;

create or replace function public.report_cursor(p_cursor_id uuid, p_reason text)
returns void language plpgsql security definer set search_path = '' as $$
declare v_uid uuid := auth.uid();
begin
  if v_uid is null then raise exception 'Authentication required' using errcode = '42501'; end if;

  if not public.rate_limit_write('report:' || v_uid::text, 10, interval '1 day') then
    raise exception 'Report rate limit exceeded' using errcode = '42501';
  end if;

  if not exists (
    select 1 from public.cursors
    where id = p_cursor_id and status = 'approved' and hidden = false and owner_id <> v_uid
  ) then
    raise exception 'Invalid cursor report' using errcode = '42501';
  end if;

  insert into public.reports (cursor_id, reporter_id, reason)
  values (p_cursor_id, v_uid, p_reason)
  on conflict (cursor_id, reporter_id) do nothing;
end;
$$;

-- 12. GRANT EXECUTE MATRIX
revoke execute on function public._trg_create_profile()      from public, anon, authenticated;
revoke execute on function public._trg_init_moderation()     from public, anon, authenticated;
revoke execute on function public._trg_like_count()          from public, anon, authenticated;
revoke execute on function public._trg_report_count()        from public, anon, authenticated;

revoke execute on function public.increment_download(uuid)   from public, anon;
grant   execute on function public.increment_download(uuid)   to authenticated;

revoke execute on function public.claim_recovery_code(text, text) from public, anon, authenticated;
revoke execute on function public.rotate_recovery_code(uuid, text) from public, anon, authenticated;

revoke execute on function public.create_upload_session(text, text[], text[]) from public, anon;
grant   execute on function public.create_upload_session(text, text[], text[]) to authenticated;

revoke execute on function public.complete_upload_session(uuid, jsonb) from public, anon;
grant   execute on function public.complete_upload_session(uuid, jsonb) to authenticated;

revoke execute on function public.request_cursor_deletion(uuid) from public, anon;
grant   execute on function public.request_cursor_deletion(uuid) to authenticated;

revoke execute on function public.lease_deletion_tasks(uuid, int, interval) from public, anon, authenticated;
revoke execute on function public.mark_deletion_outcome(uuid, uuid, boolean, text) from public, anon, authenticated;
revoke execute on function public._check_report_threshold() from public, anon, authenticated;

revoke execute on function public.rate_limit_write(text, int, interval) from public, anon, authenticated;
revoke execute on function public.rate_limit_write(text, int, interval, int) from public, anon, authenticated;

revoke execute on function public.like_cursor(uuid) from public, anon;
grant   execute on function public.like_cursor(uuid) to authenticated;

revoke execute on function public.unlike_cursor(uuid) from public, anon;
grant   execute on function public.unlike_cursor(uuid) to authenticated;

revoke execute on function public.report_cursor(uuid, text) from public, anon;
grant   execute on function public.report_cursor(uuid, text) to authenticated;

-- 13. ROW LEVEL SECURITY (RLS) POLICIES
alter table public.profiles           enable row level security;
alter table public.cursors            enable row level security;
alter table public.cursor_moderation  enable row level security;
alter table public.upload_sessions    enable row level security;
alter table public.likes              enable row level security;
alter table public.downloads          enable row level security;
alter table public.reports            enable row level security;
alter table public.deletion_outbox    enable row level security;
alter table public.reset_attempts     enable row level security;
alter table public.write_rate_limits  enable row level security;

create policy p_profiles_read on public.profiles for select to anon, authenticated using (true);
create policy p_cursors_read on public.cursors for select to anon, authenticated
  using ((status = 'approved' and hidden = false) or (select auth.uid()) = owner_id);
create policy p_sessions_read on public.upload_sessions for select to authenticated
  using ((select auth.uid()) = owner_id);
create policy p_likes_self on public.likes for select to authenticated
  using ((select auth.uid()) = user_id);
create policy p_dl_self on public.downloads for select to authenticated
  using ((select auth.uid()) = user_id);

-- 14. REALTIME PUBLICATION
do $$
begin
  if not exists (
    select 1 from pg_publication_tables
    where pubname = 'supabase_realtime' and tablename = 'cursors'
  ) then
    alter publication supabase_realtime add table public.cursors;
  end if;
end $$;

-- 15. STORAGE BUCKETS & POLICIES
insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values
  ('cursor-previews','cursor-previews', true,  102400, array['image/png']),
  ('cursor-sources', 'cursor-sources',  false, 102400, array['image/png'])
on conflict (id) do update set
  public = excluded.public,
  file_size_limit = excluded.file_size_limit,
  allowed_mime_types = excluded.allowed_mime_types;

create policy p_prev_read on storage.objects for select to public
  using (bucket_id = 'cursor-previews');

create policy p_prev_ins on storage.objects for insert to authenticated
  with check (
    bucket_id = 'cursor-previews'
    and (select auth.uid())::text = (storage.foldername(name))[1]
    and exists (select 1 from public.upload_sessions
                where id::text = (storage.foldername(name))[2]
                  and owner_id = (select auth.uid())
                  and status in ('active','validated')
                  and expires_at > now())
  );

create policy p_prev_del on storage.objects for delete to authenticated
  using (
    bucket_id = 'cursor-previews'
    and (select auth.uid())::text = (storage.foldername(name))[1]
    and exists (select 1 from public.upload_sessions
                where id::text = (storage.foldername(name))[2]
                  and owner_id = (select auth.uid())
                  and status in ('active','validated','abandoned'))
  );

create policy p_src_read on storage.objects for select to authenticated
  using (
    bucket_id = 'cursor-sources'
    and (
      (select auth.uid())::text = (storage.foldername(name))[1]
      or exists (select 1 from public.cursors c
                 where c.id::text = (storage.foldername(name))[2]
                   and c.status = 'approved' and c.hidden = false)
    )
  );

create policy p_src_ins on storage.objects for insert to authenticated
  with check (
    bucket_id = 'cursor-sources'
    and (select auth.uid())::text = (storage.foldername(name))[1]
    and exists (select 1 from public.upload_sessions
                where id::text = (storage.foldername(name))[2]
                  and owner_id = (select auth.uid())
                  and status in ('active','validated')
                  and expires_at > now())
  );

create policy p_src_del on storage.objects for delete to authenticated
  using (
    bucket_id = 'cursor-sources'
    and (select auth.uid())::text = (storage.foldername(name))[1]
    and exists (select 1 from public.upload_sessions
                where id::text = (storage.foldername(name))[2]
                  and owner_id = (select auth.uid())
                  and status in ('active','validated','abandoned'))
  );

-- 16. CRON JOBS
select cron.unschedule('report-threshold') where exists
  (select 1 from cron.job where jobname = 'report-threshold');

select cron.schedule('report-threshold', '*/5 * * * *',
  $$ select public._check_report_threshold(); $$);

select cron.unschedule('reset-attempts-cleanup') where exists
  (select 1 from cron.job where jobname = 'reset-attempts-cleanup');

select cron.schedule('reset-attempts-cleanup', '0 3 * * *',
  $$ delete from public.reset_attempts where ts < now() - interval '7 days';
     delete from public.write_rate_limits where expires_at < now() - interval '1 day'; $$);
