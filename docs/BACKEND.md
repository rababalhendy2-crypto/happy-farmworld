# Backend (Supabase)

## What is on the server
| Table | Purpose | Client access |
|---|---|---|
| game_config | economy numbers (generated from data/game_config.json) | read |
| profiles | display name, level, XP, stats (no personal data) | read own row |
| wallets | coins, premium currency (CHECK >= 0) | read own row |
| inventory | items (CHECK quantity > 0) | read own rows |
| farms | plots, animals, workshops, expansions, decorations, storage level, timers | read own row |
| transactions | audit log of every coin/gem/item change | read own rows |
| processed_actions | idempotency keys + results | none |

Clients cannot INSERT/UPDATE/DELETE anything. All changes go through these RPCs (SECURITY DEFINER):
`hf_init_player`, `hf_get_state`, `hf_action(action, args, idempotency_key)`, `hf_set_display_name`,
`hf_delete_my_data`.

`hf_action` locks the player's wallet row, checks the idempotency key, validates the action with the
server clock and server config, applies it atomically, logs transactions and returns the full state.

## Deploy
1. Create a Supabase project (free tier is enough to start).
2. Authentication > Sign In / Providers: enable **Anonymous sign-ins**. (Turn on CAPTCHA or rate limits
   before public release to stop mass account creation.)
3. Apply the migrations in order (Supabase CLI `supabase db push`, or paste each file in the SQL editor):
   `backend/supabase/migrations/*.sql`
4. Put the project URL and the **publishable/anon key** in `data/backend.json`.
   Never put the service-role key in the game.
5. After changing `data/game_config.json`, regenerate the seed: `python3 backend/tools/gen_config_seed.py`
   and apply the new seed migration, so client and server use the same numbers.

## Tests
- `backend/tests/run_backend_tests.sh`: applies all migrations to a local PostgreSQL and runs 44 SQL
  tests (rules, idempotency, RLS isolation, blocked direct writes, delete-my-data).
- `backend/tests/mock_supabase.py` + `tests/backend_e2e.tscn`: end-to-end test of the Godot client
  against the real SQL through a local stand-in for Supabase Auth/REST (offline queue, account
  creation, cheat reverted by server, cloud restore).

## Known limits
- Anonymous accounts live in the app's session file. **Uninstalling the app loses the account** unless
  it is linked to a recoverable identity (planned: parent-email or Google link with parental gate).
- Local tests use PostgreSQL 16 and a stand-in for Supabase Auth; the real Supabase project has not
  been deployed yet.
