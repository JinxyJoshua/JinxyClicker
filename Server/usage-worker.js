/**
 * The counter behind the DEV TOOLS panel.
 *
 * Two numbers, shared by every copy of the app: how many times it has been
 * opened, and how many seconds it has been open in total. Nothing else is
 * stored — no addresses, no identifiers, nothing that could separate one
 * person's launches from another's. It is a tally, not analytics.
 *
 * Runs on Cloudflare Workers, free tier, no card required.
 *
 * ---------------------------------------------------------------------------
 * DEPLOYING IT  (about five minutes, once)
 * ---------------------------------------------------------------------------
 *
 *  1. Make a Cloudflare account at https://dash.cloudflare.com/sign-up
 *
 *  2. Workers & Pages -> Create -> Workers -> Create Worker.
 *     Name it something like "jinxy-usage". Deploy the placeholder.
 *
 *  3. Edit code. Delete what is there, paste this whole file, Deploy.
 *
 *  4. Give it somewhere to keep the numbers:
 *       Storage & Databases -> KV -> Create namespace, name it "USAGE".
 *       Back in the Worker: Settings -> Bindings -> Add -> KV namespace.
 *       Variable name must be exactly  USAGE  and point at that namespace.
 *       Deploy again.
 *
 *  5. Copy the worker's address (like https://jinxy-usage.<you>.workers.dev)
 *     into UsageReporter.Endpoint in the app, and rebuild.
 *
 * That is the whole setup. GET returns the totals; POST adds to them.
 *
 * ---------------------------------------------------------------------------
 * IP ADDRESSES
 * ---------------------------------------------------------------------------
 *
 * This worker never reads one, never stores one, and never returns one.
 *
 * Cloudflare hands every request a CF-Connecting-IP header. The code below does
 * not touch it, does not touch request.headers at all, and writes exactly one
 * key to KV containing exactly two running totals and a map of dates. There is
 * nowhere in the stored data for an address to be, so the panel in the app
 * cannot show you one even by accident.
 *
 * Two things you have to do to keep that true, because they are outside this
 * file:
 *
 *   - Do not enable Logpush on the worker. That is Cloudflare shipping raw
 *     request logs, addresses included, somewhere you can read them.
 *   - Do not leave `wrangler tail` running. Live tail shows per-request detail
 *     including the client address. Fine for a moment while debugging, not
 *     something to sit in.
 *
 * Cloudflare's own edge sees the address, because every web server on earth
 * sees the address of whoever connects to it — that is how a connection works,
 * and it is true of the GitHub API call the app already makes. What it does not
 * do is reach you, get stored, or get shown.
 *
 * ---------------------------------------------------------------------------
 * WHAT THIS DOES NOT DO
 * ---------------------------------------------------------------------------
 *
 * It does not stop someone posting made-up numbers. Anyone who reads the app
 * can find this address and POST to it, and no secret shipped inside an app
 * that runs on other people's computers can prevent that — a secret on ten
 * thousand strangers' machines is not a secret. The caps below keep a single
 * request from doing much damage, and that is the honest limit of it. These
 * are numbers for your own curiosity; do not put them anywhere they would
 * matter if they were wrong.
 */

const MAX_SESSION_SECONDS = 24 * 60 * 60;   // a day; longer is a broken clock
const MAX_OPENS_PER_REQUEST = 1;            // a launch is one launch
const MAX_DOWNLOADS = 100_000_000;          // a sane ceiling on a running total
const MAX_DAYS = 730;                       // two years of daily buckets

export default {
  async fetch(request, env) {
    // The app calls this from a desktop client, but a browser tab is the
    // easiest way for you to check it, so allow that too.
    const cors = {
      "Access-Control-Allow-Origin": "*",
      "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
      "Access-Control-Allow-Headers": "Content-Type",
    };

    if (request.method === "OPTIONS") {
      return new Response(null, { headers: cors });
    }

    if (!env.USAGE) {
      return json({ error: "No KV binding named USAGE. See step 4." }, 500, cors);
    }

    if (request.method === "GET") {
      return json(await totals(env), 200, cors);
    }

    if (request.method !== "POST") {
      return json({ error: "GET for totals, POST to add." }, 405, cors);
    }

    // Only the body is read. request.headers is deliberately never touched —
    // CF-Connecting-IP lives there, and the surest way not to store something
    // is not to look at it.
    let body;
    try {
      body = await request.json();
    } catch {
      return json({ error: "Body must be JSON." }, 400, cors);
    }

    // Clamped rather than trusted. Everything arriving here came from a
    // machine we do not control, and a total that is only ever added to has
    // no way of recovering from one absurd value.
    const opens = clamp(body.opens, 0, MAX_OPENS_PER_REQUEST);
    const seconds = clamp(body.seconds, 0, MAX_SESSION_SECONDS);

    // A reading of the running download total, if one was sent. Counted as a
    // reason to write even on its own: the dev build posts a reading without
    // any opens or seconds attached, and an early return on those two alone
    // would silently drop every one of them.
    const downloads = clamp(body.downloads, 0, MAX_DOWNLOADS);

    if (opens === 0 && seconds === 0 && downloads === 0) {
      return json(await totals(env), 200, cors);
    }

    const current = await totals(env);

    const updated = {
      opens: current.opens + opens,
      seconds: current.seconds + seconds,
      days: current.days || {},
    };

    // Same numbers again, bucketed by UTC day, so the app can show daily and
    // monthly breakdowns rather than only a running total. UTC rather than the
    // reporter's local date: the copies reporting are in every timezone there
    // is, and a day boundary that moves per sender puts the same hour in two
    // different buckets depending on who sent it.
    const today = new Date().toISOString().slice(0, 10);
    const day = updated.days[today] || { opens: 0, seconds: 0 };

    day.opens += opens;
    day.seconds += seconds;

    // Replaced rather than added: it is a reading, not an amount, and two
    // readings on one day are the same fact twice. A day's downloads are then
    // the difference between consecutive readings, which is the only way to
    // get one — GitHub publishes the total and never the change.
    if (downloads > 0) day.downloads = downloads;

    updated.days[today] = day;

    // Bounded so the record cannot grow without limit. Two years of daily
    // buckets is far more than anyone will scroll, and KV holds one value.
    updated.days = trim(updated.days, MAX_DAYS);

    await env.USAGE.put("totals", JSON.stringify(updated));

    return json(updated, 200, cors);
  },
};

/** Keeps the most recent days and drops the rest. */
function trim(days, keep) {
  const dates = Object.keys(days).sort();

  if (dates.length <= keep) return days;

  const kept = {};
  for (const date of dates.slice(-keep)) kept[date] = days[date];
  return kept;
}

async function totals(env) {
  const stored = await env.USAGE.get("totals");

  if (!stored) return { opens: 0, seconds: 0, days: {} };

  try {
    const parsed = JSON.parse(stored);
    return {
      opens: Number(parsed.opens) || 0,
      seconds: Number(parsed.seconds) || 0,
      days: parsed.days && typeof parsed.days === "object" ? parsed.days : {},
    };
  } catch {
    return { opens: 0, seconds: 0, days: {} };
  }
}

function clamp(value, low, high) {
  const n = Number(value);
  if (!Number.isFinite(n) || n < low) return low;
  return Math.min(n, high);
}

function json(body, status, headers) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json", ...headers },
  });
}
