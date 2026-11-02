// =============================================================================
// Edge Function: delete-account
// Purges user data, queues all owned storage objects to deletion_outbox, and deletes auth user
// =============================================================================

import { serve } from "https://deno.land/std@0.177.0/http/server.ts";
import { createClient } from "https://esm.sh/@supabase/supabase-js@2.39.8";

serve(async (req: Request) => {
  const authHeader = req.headers.get("Authorization");
  if (!authHeader) {
    return new Response(JSON.stringify({ error: "Missing authorization header" }), { status: 401 });
  }

  const supabaseUrl = Deno.env.get("SUPABASE_URL")!;
  const serviceRoleKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
  const admin = createClient(supabaseUrl, serviceRoleKey);

  // 1. Verify caller JWT
  const token = authHeader.replace("Bearer ", "");
  const { data: { user }, error: userErr } = await admin.auth.getUser(token);
  if (userErr || !user) {
    return new Response(JSON.stringify({ error: "Unauthorized" }), { status: 401 });
  }

  const uid = user.id;

  // 2. Mark deletion_requested_at in profiles (idempotent guard)
  await admin
    .from("profiles")
    .update({ deletion_requested_at: new Date().toISOString() })
    .eq("id", uid)
    .is("deletion_requested_at", null);

  // 3. Fetch all owned cursors
  const { data: cursors, error: qErr } = await admin
    .from("cursors")
    .select("id, preview_path, roles")
    .eq("owner_id", uid);

  if (qErr) {
    return new Response(JSON.stringify({ error: "Database query failed" }), { status: 500 });
  }

  // 4. Upsert outbox tasks for cursors
  for (const c of cursors ?? []) {
    const srcPaths = Object.values(c.roles ?? {}).map((r: any) => r.storage_path);
    const { error: insErr } = await admin.from("deletion_outbox").upsert([
      { cursor_id: c.id, owner_id: uid, bucket_id: "cursor-sources", storage_paths: srcPaths },
      { cursor_id: c.id, owner_id: uid, bucket_id: "cursor-previews", storage_paths: [c.preview_path] }
    ], { onConflict: "cursor_id,bucket_id", ignoreDuplicates: true });

    if (insErr) {
      console.error({ event: "delete_account.outbox_insert_failed", cursorId: c.id, error: insErr.message });
      return new Response(JSON.stringify({ error: "Could not queue storage cleanup. Try again." }), { status: 500 });
    }
  }

  // 5. Fetch and queue incomplete upload sessions
  const { data: sessions } = await admin
    .from("upload_sessions")
    .select("id, preview_path, role_paths")
    .eq("owner_id", uid)
    .in("status", ["active", "validated", "expired", "abandoned"]);

  for (const s of sessions ?? []) {
    const sPaths = Object.values(s.role_paths ?? {});
    await admin.from("deletion_outbox").upsert([
      { cursor_id: s.id, owner_id: uid, bucket_id: "cursor-sources", storage_paths: sPaths },
      { cursor_id: s.id, owner_id: uid, bucket_id: "cursor-previews", storage_paths: [s.preview_path] }
    ], { onConflict: "cursor_id,bucket_id", ignoreDuplicates: true });
  }

  // 6. Delete user from auth (cascades database rows)
  const { error: delErr } = await admin.auth.admin.deleteUser(uid);
  if (delErr) {
    console.error({ event: "delete_account.auth_delete_failed", uid, error: delErr.message });
    return new Response(JSON.stringify({ error: "Account deletion partially completed. Contact support." }), { status: 500 });
  }

  return new Response(JSON.stringify({ success: true }), { status: 200 });
});
