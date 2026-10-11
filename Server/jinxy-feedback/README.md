# Jinxy feedback Worker

Receives feedback from the app's **Feedback** page and lets the **DEV** build read
it back. Nothing goes through GitHub. Everyone can submit; only you (with the dev
token) can read or delete.

- `POST /submit` — public. One suggestion/bug/idea. Rate-limited to 30 per hour per IP.
- `GET /list` — needs `Authorization: Bearer <DEV_TOKEN>`. Newest first.
- `POST /delete` — needs the same bearer token. Body `{"id": "..."}`.

Submissions auto-expire after 90 days.

## Deploy (one time)

Run these in this folder (`server/jinxy-feedback`). You need Node installed.

1. **Log in** to the same Cloudflare account the licence Worker is on:
   ```
   npx wrangler login
   ```

2. **Create the store** and copy the id it prints into `wrangler.toml`
   (replace `PASTE_KV_NAMESPACE_ID_HERE`):
   ```
   npx wrangler kv namespace create FEEDBACK
   ```

3. **Set your read token.** Pick a long random string — this is the secret that
   gates reading/deleting the inbox. You'll put the *same* string in the app
   (step 6).
   ```
   npx wrangler secret put DEV_TOKEN
   ```

4. **Publish:**
   ```
   npx wrangler deploy
   ```
   Note the URL it prints, e.g. `https://jinxy-feedback.<your-subdomain>.workers.dev`.

5. **Point the app at it.** In `Feedback.cs`, set `ServerUrl` to that URL (without a
   trailing slash). It's currently:
   ```
   public const string ServerUrl = "https://jinxy-feedback.jinxy-server-globe.workers.dev";
   ```
   If your subdomain matches `jinxy-server-globe`, it's already correct.

6. **Give your DEV build the read token.** Put the *same* string from step 3 in a
   file named `feedback-dev.token` in the app's settings folder
   (`%APPDATA%\JinxyClicker\feedback-dev.token`). The public build never reads this
   file and the token is never compiled into any binary. Without it, the DEV
   inbox just says to set it.

## Test it (optional)

```
# should succeed:
curl -X POST https://jinxy-feedback.<sub>.workers.dev/submit \
  -H "content-type: application/json" \
  -d '{"type":"bug","summary":"test from curl","details":"hello","version":"1.4.15"}'

# should list it (replace TOKEN):
curl https://jinxy-feedback.<sub>.workers.dev/list -H "authorization: Bearer TOKEN"

# reading without the token should be 401:
curl -i https://jinxy-feedback.<sub>.workers.dev/list
```
