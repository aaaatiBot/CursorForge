// =============================================================================
// Edge Function: process-deletion-outbox
// Background worker executing storage deletion outbox tasks with leases
// =============================================================================

import { serve } from "https://deno.land/std@0.177.0/http/server.ts";
import { createClient } from "https://esm.sh/@supabase/supabase-js@2.39.8";

serve(async (_req: Request) => {
  const supabaseUrl = Deno.env.get("SUPABASE_URL")!;
  const serviceRoleKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
  const admin = createClient(supabaseUrl, serviceRoleKey);

  const workerId = crypto.randomUUID();

  // 1. Lease batch of tasks
  const { data: tasks, error: leaseErr } = await admin.rpc("lease_deletion_tasks", {
    p_worker: workerId,
    p_batch: 10
  });

  if (leaseErr) {
    console.error({ event: "outbox.lease_failed", error: leaseErr.message });
    return new Response(JSON.stringify({ error: "Lease failed" }), { status: 500 });
  }

  if (!Array.isArray(tasks) || tasks.length === 0) {
    return new Response(JSON.stringify({ message: "No pending tasks" }), { status: 200 });
  }

  for (const t of tasks) {
    let success = false;
    let errMsg: string | null = null;

    try {
      const { error: rmErr } = await admin.storage.from(t.bucket_id).remove(t.storage_paths);
      if (rmErr) {
        errMsg = rmErr.message;
      } else {
        success = true;
      }
    } catch (e: any) {
      errMsg = e.message;
    }

    // Mark outcome with worker lease ID
    const { data: marked, error: markErr } = await admin.rpc("mark_deletion_outcome", {
      p_task_id: t.id,
      p_lease_id: workerId,
      p_success: success,
      p_error: errMsg
    });

    if (markErr) {
      console.error({ event: "outbox.mark_failed", taskId: t.id, error: markErr.message });
    }

    // Hard-delete cursor from DB if marked succeeded and no other tasks pending
    if (success && marked === true) {
      const { count, error: cntErr } = await admin
        .from("deletion_outbox")
        .select("*", { count: "exact", head: true })
        .eq("cursor_id", t.cursor_id)
        .in("status", ["pending", "leased"]);

      if (cntErr) {
        console.error({ event: "outbox.count_failed", cursorId: t.cursor_id, error: cntErr.message });
      } else if (count === 0) {
        const { error: delErr } = await admin.from("cursors").delete().eq("id", t.cursor_id);
        if (delErr) {
          console.error({ event: "outbox.hard_delete_failed", cursorId: t.cursor_id, error: delErr.message });
        }
      }
    }
  }

  return new Response(JSON.stringify({ processed: tasks.length }), { status: 200 });
});
