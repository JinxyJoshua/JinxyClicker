// Jinxy site Worker.
//
// Serves the static site (from the `site/` assets) and handles /download.
//
// /download redirects to the GitHub release asset rather than streaming it
// through the Worker. Streaming an 85 MB installer through a *.workers.dev
// domain was failing for some visitors: browsers (Chrome/SmartScreen) block
// executables from low-reputation worker domains far more than from github.com,
// and edge-caching an installer whose size changes each release could serve a
// truncated file (cached content-length vs a fresher body). GitHub's CDN is
// reputable, sets correct headers, and never truncates. On a new release,
// publishing the installer under the same asset name keeps this pointing at the
// newest one. The permalink always resolves to the latest release's asset.

const INSTALLER_URL =
  "https://github.com/oneforetheages/jinxy-download/releases/latest/download/JinxyAutoClicker-Beta-Setup.exe";

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (url.pathname === "/download") {
      if (request.method !== "GET" && request.method !== "HEAD") {
        return new Response("Method not allowed", { status: 405, headers: { allow: "GET, HEAD" } });
      }
      // no-store so the redirect itself is never cached — it always points at
      // whatever the latest release currently is.
      return new Response(null, {
        status: 302,
        headers: { location: INSTALLER_URL, "cache-control": "no-store" },
      });
    }

    // Everything else: serve the static site.
    return env.ASSETS.fetch(request);
  },
};
