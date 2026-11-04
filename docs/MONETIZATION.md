# Store, purchases and rewarded ads

## Principles (child safety + fairness)
- Everything sold for money can also be earned for free (gems come from level-ups and missions;
  the golden hen and premium decorations cost gems in the shop).
- No loot boxes or random rewards for money. No countdown pressure. No ads at start-up, no forced or
  interrupting ads, no ads disguised as game buttons.
- Every real-money purchase: parental gate (multiplication question) -> clear confirmation screen with
  the exact Google Play price -> Google Play's own purchase dialog.
- Rewarded videos are optional, clearly labelled, max 5 per day (config `ads.daily_limit`).
  Rewards by order of the day: 300, 150, 100, 100, 100 coins (config `ads.coin_rewards`), or a
  15-minute crop speed-up. Ads must be configured as child-directed, non-personalised, rated G.

## How rewards are protected
| Step | Where |
|---|---|
| Player taps "Watch video" | game |
| `hf_ad_begin` checks the daily limit and returns a one-time nonce | Supabase SQL |
| Ad SDK shows the ad with SSV options user_id + custom_data = nonce | game + AdMob |
| AdMob calls `admob-ssv` with a Google-signed URL | Edge Function |
| Signature verified (ECDSA P-256, Google's published keys); `hf_grant_ad_reward` checks nonce owner, single use, expiry, daily limit, unique transaction id; adds coins once and logs it | Edge Function + SQL |
| Game reloads state from the server | game |

Purchases: Play Billing flow with obfuscatedAccountId = player id -> game sends the purchase token to
`verify-purchase` -> the function asks Google Play Developer API, checks state = purchased and the
account id -> `hf_grant_purchase` grants once per token (non-consumables never twice) -> function
acknowledges the purchase -> game consumes consumables and reloads state.

## Products (config `store.products`, prices are placeholders until set in Play Console)
gems_20 (0.99 €), gems_60 (2.49 €), decor_pack (0.99 €), golden_hen (1.49 €),
cozy_farm_pack (2.99 €), harvest_festival_bundle (3.99 €, only during the festival).
Monthly membership (3.99 €) is **not implemented**: it needs Play subscriptions + real-time developer
notifications; postponed until the core game is live.

## What is needed to switch this on (owner)
1. Google Play Console developer account (25 USD one-time), app created with package
   `com.happyfarmworld.game`, products created with the ids above.
2. Google Cloud service account with access to the Play Developer API; its JSON key stored as
   Supabase secret `GOOGLE_SERVICE_ACCOUNT_JSON` (never in the app).
3. AdMob account, rewarded ad unit, SSV callback URL = `<supabase>/functions/v1/admob-ssv`,
   app set as child-directed / "Designed for Families".
4. Gradle Android build (full Android SDK) with the GodotGooglePlayBilling plugin and an AdMob plugin.
   This could not be done in the build environment used so far (Google's SDK servers blocked).
   Until then `Billing.available()` and `Ads.available()` are false and the store says so honestly.

## Tests
- `backend/tests/backend_tests.sql`: nonce, single use, player binding, daily limit, duplicate
  transaction, client cannot call grant functions, purchase once per token, restore without duplicates.
- `backend/supabase/functions/_shared/lib_test.ts` (`deno test`): AdMob signature valid / tampered /
  wrong key / missing, DER conversion, Play purchase state + account checks.
