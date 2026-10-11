// Jinxy feedback Worker.
//
// Three routes:
//   POST /submit  - public. Anyone's suggestion/bug/idea. Rate-limited per IP.
//   GET  /list    - dev only (Bearer DEV_TOKEN). Reads the inbox, newest first.
//   POST /delete  - dev only (Bearer DEV_TOKEN). Removes one by id.
//
// Storage is a single KV namespace bound as FEEDBACK. Submissions auto-expire
// after 90 days so the inbox stays a triage list, not an archive. Kept separate
// from the licence Worker on purpose: a flood of feedback can never slow or
// break key-checking.

const TYPES = new Set(["feature", "improvement", "bug"]);
const MAX_SUMMARY = 120;
const MAX_DETAILS = 4000;
const MAX_VERSION = 20;
const RATE_MAX = 30;           // submissions allowed per window, per IP — high enough
                               // no real person hits it, low enough to blunt a flood
const RATE_WINDOW = 3600;      // the window, in seconds (1 hour)
const KEEP_SECONDS = 60 * 60 * 24 * 90;  // 90 days

export default {
  async fetch(request, env) {
    const path = new URL(request.url).pathname;

    if (request.method === "OPTIONS") return cors(new Response(null, { status: 204 }));

    try {
      if (path === "/submit" && request.method === "POST") return cors(await handleSubmit(request, env));
      if (path === "/list"   && request.method === "GET")  return cors(await handleList(request, env));
      if (path === "/delete" && request.method === "POST") return cors(await handleDelete(request, env));
      return cors(json({ ok: false, error: "Not found" }, 404));
    } catch (err) {
      return cors(json({ ok: false, error: "Server error" }, 500));
    }
  },
};

async function handleSubmit(request, env) {
  const ip = request.headers.get("cf-connecting-ip") || "unknown";
  const rlKey = "rl:" + ip;
  const used = parseInt((await env.FEEDBACK.get(rlKey)) || "0", 10) || 0;
  if (used >= RATE_MAX) {
    return json({ ok: false, error: "You're sending these a little fast — give it a minute." }, 429);
  }

  let data;
  try {
    data = await request.json();
  } catch {
    return json({ ok: false, error: "Bad request." }, 400);
  }

  const type = String(data.type || "").toLowerCase();
  if (!TYPES.has(type)) return json({ ok: false, error: "Pick a category." }, 400);

  const summary = clean(data.summary, MAX_SUMMARY);
  if (!summary) return json({ ok: false, error: "Add a one-line summary." }, 400);

  const details = clean(data.details, MAX_DETAILS);
  const version = clean(data.version, MAX_VERSION);

  const ts = Date.now();
  const id = ts + "-" + Math.random().toString(36).slice(2, 8);
  const record = {
    id, type, summary, details, version, ts,
    country: (request.cf && request.cf.country) || "",
  };

  await env.FEEDBACK.put("fb:" + id, JSON.stringify(record), { expirationTtl: KEEP_SECONDS });
  await env.FEEDBACK.put(rlKey, String(used + 1), { expirationTtl: RATE_WINDOW });

  return json({ ok: true });
}

async function handleList(request, env) {
  if (!authed(request, env)) return json({ ok: false, error: "Unauthorized" }, 401);

  const listing = await env.FEEDBACK.list({ prefix: "fb:", limit: 200 });
  const items = [];
  for (const key of listing.keys) {
    const value = await env.FEEDBACK.get(key.name);
    if (!value) continue;
    try {
      items.push(JSON.parse(value));
    } catch {
      /* skip anything that isn't a record */
    }
  }
  items.sort((a, b) => (b.ts || 0) - (a.ts || 0));
  return json({ items });
}

async function handleDelete(request, env) {
  if (!authed(request, env)) return json({ ok: false, error: "Unauthorized" }, 401);

  let data;
  try {
    data = await request.json();
  } catch {
    return json({ ok: false, error: "Bad request." }, 400);
  }

  const id = clean(data.id, 64);
  if (!id) return json({ ok: false, error: "No id." }, 400);

  await env.FEEDBACK.delete("fb:" + id);
  return json({ ok: true });
}

// ---- helpers ----

function authed(request, env) {
  const header = request.headers.get("authorization") || "";
  const match = header.match(/^Bearer\s+(.+)$/i);
  return Boolean(env.DEV_TOKEN) && match !== null && match[1] === env.DEV_TOKEN;
}

// Strips control characters (except tab/newline), trims, and caps the length.
function clean(value, max) {
  return String(value == null ? "" : value)
    .replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f]/g, "")
    .trim()
    .slice(0, max);
}

function json(obj, status = 200) {
  return new Response(JSON.stringify(obj), {
    status,
    headers: { "content-type": "application/json" },
  });
}

function cors(res) {
  res.headers.set("access-control-allow-origin", "*");
  res.headers.set("access-control-allow-methods", "GET,POST,OPTIONS");
  res.headers.set("access-control-allow-headers", "content-type,authorization");
  return res;
}
