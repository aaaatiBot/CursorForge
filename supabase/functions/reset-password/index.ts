// =============================================================================
// Edge Function: reset-password
// Deployed with --no-verify-jwt (callable before user authentication)
// Tradeoff note: recovery_code_used_at is consumed BEFORE the password update.
// If updateUserById fails transiently, the code stays consumed and user must
// contact support. This is intentional to prevent replay attacks.
// =============================================================================

import { serve } from "https://deno.land/std@0.177.0/http/server.ts";
import { createClient } from "https://esm.sh/@supabase/supabase-js@2.39.8";

const CROCKFORD_CHARS = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

function generateCrockford24(): string {
  const bytes = new Uint8Array(24);
  crypto.getRandomValues(bytes);
  let result = "";
  for (let i = 0; i < 24; i++) {
    result += CROCKFORD_CHARS[bytes[i] % 32];
  }
  return result;
}

function formatCrockford(code: string): string {
  return code.match(/.{1,4}/g)?.join("-") ?? code;
}

async function sha256Hex(text: string): Promise<string> {
  const encoder = new TextEncoder();
  const data = encoder.encode(text);
  const hash = await crypto.subtle.digest("SHA-256", data);
  return Array.from(new Uint8Array(hash))
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");
}

serve(async (req: Request) => {
  if (req.method !== "POST") {
    return new Response(JSON.stringify({ error: "Method not allowed" }), { status: 405 });
  }

  try {
    const { username, code, new_password } = await req.json();

    if (!username || !code || !new_password) {
      return new Response(JSON.stringify({ error: "Missing required fields" }), { status: 400 });
    }

    if (new_password.length < 8) {
      return new Response(JSON.stringify({ error: "Password must be at least 8 characters" }), { status: 400 });
    }

    const supabaseUrl = Deno.env.get("SUPABASE_URL")!;
    const serviceRoleKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
    const admin = createClient(supabaseUrl, serviceRoleKey);

    const cleanUsername = username.trim().toLowerCase();
    const cleanCode = code.replace(/[^A-Za-z0-9]/g, "").toUpperCase();

    // 1. Rate limiting
    const uHash = await sha256Hex(cleanUsername);
    const { data: allowed, error: rlErr } = await admin.rpc("rate_limit_write", {
      p_key: `reset:${uHash}`,
      p_limit: 5,
      p_window: "1 hour"
    });

    if (rlErr) {
      console.error({ event: "recovery.rate_limit_error", error: rlErr.message });
      return new Response(JSON.stringify({ error: "Service error." }), { status: 500 });
    }

    if (!allowed) {
      return new Response(JSON.stringify({ error: "Too many attempts. Please wait before trying again." }), { status: 429 });
    }

    // 2. Atomic claim
    const codeHash = await sha256Hex(cleanUsername + cleanCode);
    const { data: userId, error: claimErr } = await admin.rpc("claim_recovery_code", {
      p_username: cleanUsername,
      p_hash: codeHash
    });

    if (claimErr) {
      console.error({ event: "recovery.claim_error", error: claimErr.message });
      return new Response(JSON.stringify({ error: "Service error." }), { status: 500 });
    }

    if (!userId) {
      return new Response(JSON.stringify({ error: "Invalid username or recovery code." }), { status: 400 });
    }

    // 3. Update password via GoTrue
    try {
      const { error: updErr } = await admin.auth.admin.updateUserById(userId, {
        password: new_password
      });
      if (updErr) throw updErr;
    } catch (e: any) {
      console.error({ event: "recovery.password_update_failed", userId, error: e.message });
      return new Response(JSON.stringify({ error: "Password update failed. Contact support with your username." }), { status: 500 });
    }

    // 4. Rotate recovery code
    const newRawCode = generateCrockford24();
    const newHash = await sha256Hex(cleanUsername + newRawCode);

    const { error: rotErr } = await admin.rpc("rotate_recovery_code", {
      p_user_id: userId,
      p_new_hash: newHash
    });

    if (rotErr) {
      console.error({ event: "recovery.rotation_failed", userId, error: rotErr.message });
      return new Response(JSON.stringify({ error: "Password updated. Recovery code rotation failed; contact support." }), { status: 500 });
    }

    return new Response(JSON.stringify({ new_recovery_code: formatCrockford(newRawCode) }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  } catch (err: any) {
    console.error({ event: "recovery.unhandled_exception", error: err.message });
    return new Response(JSON.stringify({ error: "Internal server error" }), { status: 500 });
  }
});
