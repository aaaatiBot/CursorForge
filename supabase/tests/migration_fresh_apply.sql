-- =============================================================================
-- CursorForge Fresh Database Migration Verification Test
-- =============================================================================

begin;
create extension if not exists "pgtap";

select plan(5);

-- 1. Verify required tables exist
select has_table('public', 'profiles', 'profiles table exists');
select has_table('public', 'cursors', 'cursors table exists');
select has_table('public', 'upload_sessions', 'upload_sessions table exists');
select has_table('public', 'deletion_outbox', 'deletion_outbox table exists');

-- 2. Verify realtime publication contains cursors
select is(
  (select count(*) from pg_publication_tables where pubname = 'supabase_realtime' and tablename = 'cursors'),
  1::bigint,
  'cursors table is registered in supabase_realtime publication'
);

select * from finish();
rollback;
