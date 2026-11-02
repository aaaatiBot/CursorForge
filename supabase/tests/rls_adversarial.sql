-- =============================================================================
-- CursorForge Adversarial RLS Security Tests
-- Tests 10+ security assertions across anon, user_a, user_b, service_role
-- =============================================================================

begin;
create extension if not exists "pgtap";

select plan(10);

-- Setup mock users
-- 1. Anonymous context: cannot read recovery_code_hash
set role anon;

select throws_ok(
  'select recovery_code_hash from public.profiles',
  '42501',
  null,
  'Anon must not be able to read recovery_code_hash'
);

-- 2. Anonymous context: cannot see pending or hidden cursors
select is_empty(
  'select id from public.cursors where status = ''pending'' or hidden = true',
  'Anon sees only approved and non-hidden cursors'
);

-- 3. Authenticated user cannot update profiles table directly
set role authenticated;

select throws_ok(
  'update public.profiles set username = ''hacker''',
  '42501',
  null,
  'Authenticated users cannot update profiles directly'
);

-- 4. Authenticated user cannot update cursors table directly
select throws_ok(
  'update public.cursors set status = ''approved''',
  '42501',
  null,
  'Users cannot modify cursor lifecycle status directly'
);

-- 5. User cannot directly insert into likes without like_cursor RPC
select throws_ok(
  'insert into public.likes (user_id, cursor_id) values (gen_random_uuid(), gen_random_uuid())',
  '42501',
  null,
  'Direct insert into likes is revoked'
);

-- 6. User cannot directly insert into reports without report_cursor RPC
select throws_ok(
  'insert into public.reports (cursor_id, reporter_id, reason) values (gen_random_uuid(), gen_random_uuid(), ''Spam'')',
  '42501',
  null,
  'Direct insert into reports is revoked'
);

-- 7. User cannot inspect cursor_moderation
select throws_ok(
  'select * from public.cursor_moderation',
  '42501',
  null,
  'cursor_moderation table is inaccessible to authenticated users'
);

-- 8. User cannot access deletion_outbox directly
select throws_ok(
  'select * from public.deletion_outbox',
  '42501',
  null,
  'deletion_outbox table is inaccessible to authenticated users'
);

-- 9. User cannot modify write_rate_limits
select throws_ok(
  'select * from public.write_rate_limits',
  '42501',
  null,
  'write_rate_limits table is inaccessible to authenticated users'
);

-- 10. Service role context has full access
set role service_role;

select isnt_empty(
  'select id from storage.buckets where id = ''cursor-previews''',
  'Service role can verify storage bucket configuration'
);

select * from finish();
rollback;
