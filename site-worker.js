// Jinxy site Worker.
//
// Serves the static site (from the `site/` assets) and handles /download by
// streaming the installer from the GitHub release server-side. Visitors only
// ever see this domain; the GitHub URL below exists only here, never in the
// browser. On a new release, upload the installer under the same asset name
// and this keeps pointing at the newest one.

const INSTALLER_URL =
  "https://github.com/oneforetheages/jinxy-download/releases/latest/download/JinxyAutoClicker-Beta-Setup.exe";
const FILENAME = "JinxyAutoClicker-Beta-Setup.exe";

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (url.pathname === "/download") {
      if (request.method !== "GET" && request.method !== "HEAD") {
        return new Response("Method not allowed", { status: 405, headers: { allow: "GET, HEAD" } });
      }
      let upstream;
      try {
        // Cache the installer at Cloudflare's edge so each visitor isn't a fresh
        // full fetch from GitHub.
        upstream = await fetch(INSTALLER_URL, { redirect: "follow", cf: { cacheEverything: true, cacheTtl: 600 } });
      } catch {
        upstream = null;
      }

      if (!upstream || !upstream.ok || !upstream.body) {
        return new Response(
          "The download is temporarily unavailable. Please try again in a moment.",
          { status: 502, headers: { "content-type": "text/plain; charset=utf-8" } }
        );
      }

      const headers = new Headers();
      headers.set("content-type", "application/octet-stream");
      headers.set("content-disposition", `attachment; filename="${FILENAME}"`);
      const len = upstream.headers.get("content-length");
      if (len) headers.set("content-length", len);
      headers.set("cache-control", "public, max-age=600");

      return new Response(upstream.body, { status: 200, headers });
    }

    // Everything else: serve the static site.
    return env.ASSETS.fetch(request);
  },
};
