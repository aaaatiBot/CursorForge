// =============================================================================
// Edge Function: validate-upload
// Server-side PNG validation for upload sessions before publishing
// =============================================================================

import { serve } from "https://deno.land/std@0.177.0/http/server.ts";
import { createClient } from "https://esm.sh/@supabase/supabase-js@2.39.8";

const PNG_MAGIC = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
const MAX_BYTES = 100 * 1024; // 100 KB
const MAX_DIMENSION = 128;
const MAX_PIXELS = 128 * 128; // 16384

function validatePngBytes(bytes: Uint8Array): { valid: boolean; error?: string } {
  if (bytes.length < 24) {
    return { valid: false, error: "File too small to be a valid PNG" };
  }
  if (bytes.length > MAX_BYTES) {
    return { valid: false, error: "File size exceeds 100 KB limit" };
  }

  // Check magic bytes
  for (let i = 0; i < 8; i++) {
    if (bytes[i] !== PNG_MAGIC[i]) {
      return { valid: false, error: "Invalid PNG signature" };
    }
  }

  // First chunk must be IHDR (starts at byte 8)
  const chunkLength = (bytes[8] << 24) | (bytes[9] << 16) | (bytes[10] << 8) | bytes[11];
  const chunkType = String.fromCharCode(bytes[12], bytes[13], bytes[14], bytes[15]);
  if (chunkType !== "IHDR" || chunkLength < 13) {
    return { valid: false, error: "Invalid IHDR chunk" };
  }

  // Parse width and height (big endian 32-bit unsigned integers)
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const width = view.getUint32(16);
  const height = view.getUint32(20);

  if (width === 0 || height === 0) {
    return { valid: false, error: "Zero dimensions in PNG" };
  }
  if (width > MAX_DIMENSION || height > MAX_DIMENSION) {
    return { valid: false, error: `Dimensions ${width}x${height} exceed ${MAX_DIMENSION}x${MAX_DIMENSION} limit` };
  }
  if (width * height > MAX_PIXELS) {
    return { valid: false, error: "Pixel count exceeds maximum allowed" };
  }

  return { valid: true };
}

serve(async (req: Request) => {
  const authHeader = req.headers.get("Authorization");
  if (!authHeader) {
    return new Response(JSON.stringify({ error: "Missing authorization header" }), { status: 401 });
  }

  const supabaseUrl = Deno.env.get("SUPABASE_URL")!;
  const serviceRoleKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
  const admin = createClient(supabaseUrl, serviceRoleKey);

  // 1. Verify caller JWT -> uid
  const token = authHeader.replace("Bearer ", "");
  const { data: { user }, error: userErr } = await admin.auth.getUser(token);
  if (userErr || !user) {
    return new Response(JSON.stringify({ error: "Unauthorized" }), { status: 401 });
  }
  const uid = user.id;

  // 2. Parse request body
  let body: { session_id?: string };
  try {
    body = await req.json();
  } catch {
    return new Response(JSON.stringify({ error: "Invalid JSON body" }), { status: 400 });
  }

  const sessionId = body.session_id;
  if (!sessionId) {
    return new Response(JSON.stringify({ error: "Missing session_id" }), { status: 400 });
  }

  // Rate limit: 20 validations per hour per user
  const { data: allowed, error: rateErr } = await admin.rpc("rate_limit_write", {
    p_key: `upload-validate:${uid}`,
    p_limit: 20,
    p_window: "1 hour",
  });
  if (rateErr || !allowed) {
    return new Response(JSON.stringify({ error: "Too many requests. Please wait." }), { status: 429 });
  }

  // 3. Load upload session
  const { data: session, error: sessErr } = await admin
    .from("upload_sessions")
    .select("*")
    .eq("id", sessionId)
    .eq("owner_id", uid)
    .single();

  if (sessErr || !session) {
    return new Response(JSON.stringify({ error: "Upload session not found" }), { status: 404 });
  }

  if (session.status !== "active") {
    return new Response(JSON.stringify({ error: `Session is ${session.status}` }), { status: 400 });
  }

  if (new Date(session.expires_at) < new Date()) {
    return new Response(JSON.stringify({ error: "Upload session has expired" }), { status: 400 });
  }

  // 4. Download and validate each path: preview + role source files
  // preview is stored in cursor-previews bucket
  // role source files are stored in cursor-sources bucket
  const previewPath = session.preview_path;
  const rolePaths = session.role_paths as Record<string, string>;

  // Validate preview PNG
  if (previewPath) {
    const { data: pBlob, error: pErr } = await admin.storage
      .from("cursor-previews")
      .download(previewPath);

    if (pErr || !pBlob) {
      return new Response(JSON.stringify({ error: "Failed to download preview image" }), { status: 400 });
    }

    const pBytes = new Uint8Array(await pBlob.arrayBuffer());
    const pCheck = validatePngBytes(pBytes);
    if (!pCheck.valid) {
      return new Response(JSON.stringify({ error: `Invalid preview image: ${pCheck.error}` }), { status: 400 });
    }
  }

  // Validate role PNGs
  for (const [roleName, path] of Object.entries(rolePaths)) {
    const { data: rBlob, error: rErr } = await admin.storage
      .from("cursor-sources")
      .download(path);

    if (rErr || !rBlob) {
      return new Response(JSON.stringify({ error: `Failed to download file for role: ${roleName}` }), { status: 400 });
    }

    const rBytes = new Uint8Array(await rBlob.arrayBuffer());
    const rCheck = validatePngBytes(rBytes);
    if (!rCheck.valid) {
      return new Response(JSON.stringify({ error: `Invalid image for role ${roleName}: ${rCheck.error}` }), { status: 400 });
    }
  }

  // 5. Update session status to 'validated'
  const { error: updateErr } = await admin
    .from("upload_sessions")
    .update({
      status: "validated",
      validated_at: new Date().toISOString(),
    })
    .eq("id", sessionId)
    .eq("owner_id", uid);

  if (updateErr) {
    return new Response(JSON.stringify({ error: "Failed to mark session as validated" }), { status: 500 });
  }

  return new Response(JSON.stringify({ success: true, status: "validated" }), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
});
