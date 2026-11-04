# Status after Phase 1

## Works (verified here)
- Godot 4.3 project, GL Compatibility renderer (runs on low-end GPUs without Vulkan).
- 3D starter farm built from original procedural models: farmhouse, barn, chicken coop, warehouse,
  seed shop, market stand, well, pond, 6 plots, two fenced animal pens, trees, flowers, rocks,
  paths, 700 grass tufts in one draw call, sky, sun with shadows.
- Camera: one-finger pan, two-finger pinch zoom + twist rotate, tap to select, bounds, smoothing.
  Desktop: mouse equivalents.
- Planting (6 crops, level-locked), watering (25% faster), 4 visible growth stages, harvesting,
  floating reward text, particles, sounds.
- 6 animals (cow, chicken, sheep, rabbit, pig, goat) with idle, walking, eating animations,
  status bubbles, happiness, feed -> produce -> collect cycle, synthesized animal sounds.
- Inventory with storage capacity, market (sell 1 / sell all), coins, XP, level-ups with reward.
- Local save: autosave every 20 s, on app background and on quit; crash-safe write + backup copy;
  timers continue while the app is closed.
- English, German, Arabic with right-to-left layout and an Arabic font.
- Music and SFX volume controls, language switch, "start a new farm" with confirmation.
- 68 automated unit tests for rules and economy (all pass).
- Signed debug APK (arm64-v8a + armeabi-v7a) builds successfully.

## Not verified
- **Not run on a real Android phone or emulator** (none available in the build environment).
  Touch gestures, performance and frame rate on devices are untested.
- Screenshots were rendered with a software OpenGL driver, not a phone GPU.

## Not built yet (by plan)
- Cloud save, accounts, server-validated time and economy (Phase 4). Until then the device is
  trusted and a player could edit the local save file.
- Seed shop / store screens, workshops, expansion, missions, daily rewards, ads, purchases.
- Release AAB for Google Play (needs Gradle + full Android SDK).

## Known limitations
- If the phone clock was set forward and then corrected, timers pause until real time catches up
  (anti-rollback guard). Fixed properly by server time in Phase 4.
- Item icons are coloured circles; real icons come with the UI polish phase.
