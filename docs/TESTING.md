# Testing

| Suite | Command | Checks | Last result |
|---|---|---|---|
| Game rules & economy | `godot --headless -s res://tests/run_tests.gd` | 132 | all pass |
| Backend SQL (local PostgreSQL 16 + Supabase stub) | `backend/tests/run_backend_tests.sh` | 68 | all pass |
| Edge Function helpers (AdMob signature, Play checks) | `deno test backend/supabase/functions/_shared/` | 6 | all pass |
| Client ↔ server end to end (mock Supabase) | `python3 backend/tests/mock_supabase.py` then `godot --headless res://tests/backend_e2e.tscn` | 25 | all pass |
| Screenshots of every screen | `xvfb-run godot --rendering-driver opengl3 res://tests/shot.tscn` | 23 shots | reviewed |

## Not tested yet (needs hardware or accounts)
- A real Android phone or emulator: touch feel, frame rate, battery, memory, different screen sizes.
- A real Supabase project (only a local stub so far).
- Google Play Billing and AdMob (plugins need a Gradle build + Play Console / AdMob accounts).
- Parent email restore (needs real Supabase email sending).

## Manual test plan for the first phone test
1. Fresh install → daily gift → plant wheat → wait → harvest → sell.
2. Close app for 5 minutes → reopen → crops grew while closed.
3. Flight mode → play → back online → no progress lost, coins unchanged after sync.
4. Change phone clock forward 1 day → no free daily gift (server time).
5. Arabic: every screen right-to-left, no cut-off text.
6. Store without plugins → honest "not available" message, no dead buttons.
