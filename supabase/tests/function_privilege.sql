-- =============================================================================
-- CursorForge Function Execution Privilege Tests
-- =============================================================================

begin;
create extension if not exists "pgtap";

select plan(8);

-- 1. Anonymous cannot execute claim_recovery_code
set role anon;

select throws_ok(
  'select public.claim_recovery_code(''alice'', ''0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef'')',
  '42501',
  null,
  'Anon cannot execute claim_recovery_code'
);

-- 2. Authenticated cannot execute claim_recovery_code
set role authenticated;

select throws_ok(
  'select public.claim_recovery_code(''alice'', ''0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef'')',
  '42501',
  null,
  'Authenticated cannot execute claim_recovery_code'
);

-- 3. Authenticated cannot execute rotate_recovery_code
select throws_ok(
  'select public.rotate_recovery_code(gen_random_uuid(), ''0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef'')',
  '42501',
  null,
  'Authenticated cannot execute rotate_recovery_code'
);

-- 4. Authenticated cannot execute lease_deletion_tasks
select throws_ok(
  'select * from public.lease_deletion_tasks(gen_random_uuid())',
  '42501',
  null,
  'Authenticated cannot execute lease_deletion_tasks'
);

-- 5. Authenticated cannot execute mark_deletion_outcome
select throws_ok(
  'select public.mark_deletion_outcome(gen_random_uuid(), gen_random_uuid(), true)',
  '42501',
  null,
  'Authenticated cannot execute mark_deletion_outcome'
);

-- 6. Authenticated cannot execute rate_limit_write directly
select throws_ok(
  'select public.rate_limit_write(''test_key'', 10, interval ''1 hour'')',
  '42501',
  null,
  'Authenticated cannot execute rate_limit_write directly'
);

-- 7. Authenticated CAN execute create_upload_session (will fail on auth.uid() check if unauthenticated, but function permission is granted)
select throws_ok(
  'select public.create_upload_session(''Sample'', array[''tag1''], array[''Arrow''])',
  '42501',
  'Authentication required',
  'Authenticated has EXECUTE grant on create_upload_session'
);

-- 8. Service role can execute privileged functions
set role service_role;

select is(
  (select count(*) from public.lease_deletion_tasks(gen_random_uuid())),
  0::bigint,
  'Service role can execute lease_deletion_tasks'
);

select * from finish();
rollback;
