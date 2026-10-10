# Selling Pro — PayPal + Discord (manual)

This is the whole manual process. No website, no shop — you take PayPal, you send
a key by hand. It costs nothing and nobody can shut it down.

## One-time setup

1. **PayPal account.** You must be 18+ to have one. If you're younger, a parent or
   guardian sets it up and you use it with their permission — there's no way
   around the age rule.
2. **Get your PayPal.me link.** It looks like `paypal.me/yourname`. This is the link
   you send buyers so they can pay you.
3. **Discord.** Make a channel (e.g. `#get-pro`) and pin the buyer message at the
   bottom of this file. Buyers DM you to buy.

## The golden rules (don't break these)

- **Get paid FIRST, send the key SECOND.** Never send a key before the money is
  actually in your PayPal. "I sent it, I promise" is how you get scammed — check
  your PayPal balance yourself before you send anything.
- **One key per purchase.** KeyGen logs every key to `keys.csv` automatically.
- **No exceptions.** Every "I'll pay you after, trust me" is a scam.

## The flow for each sale

1. Buyer DMs you wanting Pro.
2. Tell them the price and send your `paypal.me/yourname` link. Ask them to pay for
   the tier they want.
3. **Wait until the money shows up in your PayPal.** Open PayPal and confirm it's
   there and cleared.
4. Double-click the matching file in the KeyGen folder:
   `make-lifetime.cmd` / `make-month.cmd` / `make-weekend.cmd` / `make-day.cmd`.
5. Copy the key it prints, DM it to the buyer.
6. Done. `keys.csv` already logged the sale.

## Prices

| Tier | Price |
|------|-------|
| Lifetime | $20 |
| Month (30 days) | $8 |
| Weekend (3 days) | $4 |
| Day (1 day) | $2 |

## The PayPal risk: chargebacks

This is the real downside of PayPal. A buyer can pay, get the key, then open a
dispute or chargeback and try to get their money back while keeping the key.

- Use **Goods & Services** payments (the normal kind). Do NOT ask buyers for
  "Friends & Family" — it's against PayPal's rules for sales, it's a scam red flag,
  and it can get your account frozen.
- Keep proof: a screenshot of the payment and the Discord chat.
- **State "no refunds once the key is sent" up front** (it's in the buyer message
  below). A key can't be un-sent.
- If someone charges back anyway → kill their key (next section).

## Killing a key (chargeback, or a leaked/shared key)

1. Open `%APPDATA%\JinxyKeyGen\keys.csv` and find the key — each row is
   `date, tier, id, key`. Copy its **id** (the big number).
2. In the app source, open `LicenseKeys.cs` and add that id to the `Blocklist`:
   ```csharp
   public static readonly IReadOnlySet<ulong> Blocklist = new HashSet<ulong>
   {
       17409570906852044536,   // charged back 2026-10-10
   };
   ```
3. Build and release an app update. From then on, anyone who updates has that key
   refused. (It can't be killed instantly — only via an update — which is why you
   get paid before sending.)

## Taxes and the honest bit

Money you make is income. Depending on where you live and how much you make, you
may have to report it. If you're a minor using a parent's PayPal, that's their
account and their responsibility to sort out. Keep `keys.csv` — it's your record of
what you sold.

## Buyer message (pin this in your Discord)

```
JinxyClicker Pro — unlock Clip Studio, the timer overlay, themes, stats and more.

Prices (USD, PayPal):
  Lifetime — $20   (pay once, forever)
  Month    — $8    (30 days)
  Weekend  — $4    (3 days)
  Day      — $2    (1 day)

How to buy:
  1. DM me which tier you want.
  2. Pay with PayPal (Goods & Services) to: paypal.me/YOURNAME
  3. Once it clears, I send you your key.
  4. Open the app → PRO page → paste the key → Activate.

No refunds once the key is sent. One key per person.
```
