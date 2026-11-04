# Farm visits and social features: architecture (not built yet)

Goal: children can look at friends' farms and send fixed gifts, without any way to talk to strangers.

## Rules (child safety first)
- No chat, no free text, no photos, no user-made names (names are generated from word lists).
- Visiting is **friends only**. A friendship needs a code exchanged in person or by parents
  (a 6-character farm code shown in the profile) and must be approved by the receiving player.
  No public search, no "random visit", no leaderboard with names.
- Gifts are a fixed list (e.g. 1 wheat, 1 flower) with a daily limit, sent with one button.
- Parents can turn all social features off behind the parental gate (default OFF for under-13 if an
  age signal exists).
- Report and block buttons on every friend; block removes the friendship immediately.

## Data model (future migration)
```
farm_codes(player_id pk, code text unique)            -- random, can be regenerated
friendships(a uuid, b uuid, status text, created_at)  -- pending/accepted/blocked; a < b
gifts(id, from_id, to_id, item_id, qty, created_at, claimed_at)
reports(id, reporter_id, reported_id, reason_code, created_at)
social_settings(player_id pk, enabled bool default false)
```
RLS: a player reads only friendships they are part of. Farm data of a friend is never read directly.
A SECURITY DEFINER function `hf_visit_farm(friend_id)` checks an accepted friendship and both players'
`social_settings.enabled`, then returns a **read-only, reduced** view: plots, animals, workshops,
decorations, level, generated display name. No IDs, timestamps, transactions or inventory.

## Client
A "Visit" screen reuses FarmWorld with a read-only state and hides every action button.
It will only be added to the UI when the server side above exists (no fake buttons).
