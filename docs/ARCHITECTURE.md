# Happy Farm World: Architecture and Development Plan

Engine: Godot 4.3 (GDScript). Renderer: **GL Compatibility** (OpenGL ES 3), chosen because many
low-end Android phones have weak or missing Vulkan drivers. Orientation: landscape.

## 1. Guiding principles

1. **Rules are separate from presentation.** All game rules (plant, water, harvest, feed, collect,
   sell, level-up) live in one pure, static module (`scripts/core/farm_rules.gd`). It takes a state
   dictionary, the config, and a timestamp, and returns a result. No nodes, no rendering. This makes it
   unit-testable, and it is the exact logic that will be mirrored on the server (Supabase SQL functions).
2. **Data-driven economy.** Every price, timer, reward, unlock level and XP threshold is in
   `data/game_config.json`. Re-balancing never requires code changes. In Phase 4 the same config moves
   into a database table so it can be changed without shipping a new APK.
3. **One time source.** Code never calls the system clock directly; it calls `TimeService.now()`.
   Phase 1: device clock with an anti-rollback guard. Phase 4: server time offset from Supabase,
   so crops cannot be sped up by changing the phone clock.
4. **The client is not trusted (from Phase 4).** In Phase 1-3 the rules run locally. In Phase 4 every
   action that touches coins, premium currency or inventory becomes a server RPC (Postgres function,
   `SECURITY DEFINER`, row-level security, idempotency key, transaction log). The client then only
   renders the state the server returns. Because the client already sends "actions" rather than
   "new balances", this switch does not require rebuilding the game.
5. **Everything built from original, license-free parts.** 3D models are built procedurally from
   primitives in code (no third-party models). Sounds are synthesized at runtime. Fonts: Noto Sans and
   Noto Sans Arabic (SIL Open Font License).

## 2. Folder structure

```
project.godot            engine settings, autoloads, input, renderer
data/game_config.json    economy: crops, animals, items, levels, storage
data/i18n.json           English / German / Arabic strings
scenes/main.tscn         entry scene
scripts/autoload/        global services (singletons)
  config.gd              loads game_config.json
  time_service.gd        trusted time source
  game_state.gd          current player state, calls FarmRules, emits signals
  save_manager.gd        local save + autosave (cloud adapter added in Phase 4)
  localization.gd        builds translations, switches language, RTL for Arabic
  audio.gd               synthesized SFX, music/SFX volumes
scripts/core/
  farm_rules.gd          pure game rules (tested)
scripts/world/
  model_factory.gd       procedural low-poly meshes (buildings, crops, animals, trees)
  farm_world.gd          builds the farm map
  camera_rig.gd          touch pan / pinch zoom / twist rotate, bounds
  plot.gd                one farm plot (visual stages)
  animal.gd              one animal (wander, eat, idle animations)
scripts/ui/
  hud.gd                 top bar, bottom bar, panels (seeds, inventory, market, settings)
  ui_theme.gd            shared theme (fonts, rounded buttons, colors)
tests/run_tests.gd       headless unit tests for the rules/economy
backend/supabase/        SQL migrations, RLS, RPC functions (Phase 4)
docs/                    architecture, testing, setup, known limitations
```

## 3. Runtime flow

```
Input (touch) -> CameraRig (drag/zoom/rotate)  or  tap -> raycast -> Plot/Animal/Building
   -> HUD opens context panel -> GameState.action(...)
   -> FarmRules validates + mutates state -> signal state_changed -> visuals + HUD refresh
   -> SaveManager autosaves (every 20 s, on pause/background, on quit)
Phase 4: GameState.action(...) -> Backend RPC -> server validates -> returns new state.
```

## 4. Phase plan

| Phase | Content | Needs from the owner |
|---|---|---|
| 1 Foundation | Project, 3D farm map, camera, touch, HUD, plant/harvest, coins, local save, debug APK | nothing |
| 2 Farm gameplay | 6 crops with stages, watering, 6 animals with care, inventory with capacity | nothing |
| 3 Economy | XP/levels and rewards, seed shop, market, 4 workshops, land expansion | nothing |
| 4 Backend | Supabase schema, anonymous auth, RLS, server RPCs, cloud save | a Supabase project (free tier) |
| 5 Online | sync, missions, daily rewards, farm-visit architecture | Phase 4 |
| 6 Monetization | store UI, Google Play Billing, AdMob rewarded ads, server verification | Play Console (25 USD one-time), AdMob account, approval |
| 7 Finalization | optimization, tests, security + child-safety review, release assets | real Android devices to test |
