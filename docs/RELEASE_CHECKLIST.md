# Release checklist (nothing is published until the owner says so)

## Done
- [x] App icon (`assets/icon.png`), package `com.happyfarmworld.game`, landscape.
- [x] Screenshots of all screens (`/screenshots`).
- [x] Privacy policy draft (`PRIVACY_POLICY.md`) – needs legal review + a public URL.
- [x] Data safety answers (`DATA_SAFETY.md`).
- [x] Security: RLS on all tables, no client writes, service key only in Edge Function secrets,
      idempotent server actions, server time, purchase/ad verification server-side.
- [x] Child safety: no chat, names from word lists, parental gate on purchases and parent area,
      no loot boxes, ads only on request and non-personalised.

## Owner actions (cost or account needed)
- [ ] Supabase project (free slot freed by pausing another project) → apply migrations, deploy functions.
- [ ] Google Play Console account (25 USD once).
- [ ] AdMob account + rewarded unit + SSV URL.
- [ ] Google Cloud service account for purchase verification.
- [ ] Public URL for the privacy policy.

## Engineering still open
- [ ] Gradle build → release AAB, Billing + AdMob plugins, release signing key (kept outside the repo).
- [ ] First real-device test (see TESTING.md), performance on a low-end phone.
- [ ] Replace procedural models with CC0 model packs (owner uploading).
- [ ] Item icons (currently coloured circles).

## Known limitations
- Monthly membership not implemented. Farm visits designed only (SOCIAL.md).
- Without network the game works locally; the server state wins on reconnect.

## Roadmap after launch
Farm visits (read-only, preset reactions), seasonal events, more crops/animals, membership.
