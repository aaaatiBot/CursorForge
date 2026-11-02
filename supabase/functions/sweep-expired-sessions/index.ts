// =============================================================================
// Edge Function: sweep-expired-sessions
// Sweeps abandoned/expired upload sessions and cleans up partial storage objects
// =============================================================================

import { serve } from "https://deno.land/std@0.177.0/http/server.ts";
import { createClient } from "https://esm.sh/@supabase/supabase-js@2.39.8";

serve(async (_req: Request) => {
  const supabaseUrl = Deno.env.get("SUPABASE_URL")!;
  const serviceRoleKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
  const admin = createClient(supabaseUrl, serviceRoleKey);

  const { data: expired, error: qErr } = await admin
    .from("upload_sessions")
    .select("id, owner_id, preview_path, role_paths, status")
    .in("status", ["active", "validated"])
    .lt("expires_at", new Date().toISOString())
    .limit(50);

  if (qErr) {
    console.error({ event: "sweep.query_failed", error: qErr.message });
    return new Response(JSON.stringify({ error: "Query failed" }), { status: 500 });
  }

  for (const s of expired ?? []) {
    const srcPaths = Object.values(s.role_paths ?? {});
    const prevPaths = [s.preview_path];

    let srcOk = true;
    let prevOk = true;

    if (srcPaths.length > 0) {
      const { error: srcErr } = await admin.storage.from("cursor-sources").remove(srcPaths as string[]);
      if (srcErr) {
        srcOk = false;
        console.error({ event: "sweep.src_rm_failed", sessionId: s.id, error: srcErr.message });
      }
    }

    const { error: prevErr } = await admin.storage.from("cursor-previews").remove(prevPaths);
    if (prevErr) {
      prevOk = false;
      console.error({ event: "sweep.prev_rm_failed", sessionId: s.id, error: prevErr.message });
    }

    if (srcOk && prevOk) {
      await admin.from("upload_sessions").update({ status: "abandoned" }).eq("id", s.id);
    }
  }

  return new Response(JSON.stringify({ swept: expired?.length ?? 0 }), { status: 200 });
});
