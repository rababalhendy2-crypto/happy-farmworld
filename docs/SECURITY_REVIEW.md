# Security & child-safety review (2026-11-04, live project miafcrcsoyusvvdxotrf)

## Checked on the live Supabase project
| Check | Result |
|---|---|
| RLS on every table | ON for all 10 tables |
| Clients can write tables directly | No (insert/update/delete revoked) |
| API-callable functions | Only 8 SECURITY DEFINER entry points (hf_action, hf_init_player, hf_get_state, hf_set_display_name, hf_delete_my_data, hf_ad_begin, hf_ad_status, hf_my_purchases). Helpers revoked (migration 0005). |
| Grant functions (coins for purchases / ads) | Not callable by anon or authenticated – service role only |
| Function search_path | Pinned on all hf_ functions (advisor warning fixed) |
| Real-server smoke test (rolled back) | init 120 coins, plant -5, early harvest refused, feed, daily gift 30 |
| Advisor leftovers | "SECURITY DEFINER callable by signed-in users" x8 = intended design; "RLS without policy" on processed_actions/ad_sessions = intended (server-only tables) |

## Client (APK)
- No service-role key, no secret key, no Google credentials in the APK (scanned). Only the publishable key + project URL.
- Android permissions: INTERNET. (A DUMP entry appeared in a raw manifest scan of the debug build – verify with a proper tool on the release build.)
- No web links, no browser opening, no chat, no free text from other players.

## Child safety
- Anonymous accounts, no email/name/birthday. Parent email only optional, behind the parental gate.
- Display names: client generates from word lists; server only allows 2–16 letters/digits/space/._- . Names are never shown to other players (no social features yet).
- Purchases: parental gate + confirmation + Google dialog. No loot boxes. Ads only on request, max 5/day, must be configured child-directed / non-personalised / G.

## Open items before release
1. Turn on CAPTCHA (Turnstile/hCaptcha) for anonymous sign-ins to limit fake-account spam (Supabase Auth → Attack protection).
2. If farm visits are added: names must then be chosen ONLY from the word list server-side.
3. Release build: verify manifest permissions, sign with a release key kept outside the repo.
4. Legal review of PRIVACY_POLICY.md (COPPA / GDPR-K) before listing in "Designed for Families".
