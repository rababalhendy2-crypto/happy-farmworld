# Happy Farm World

An original, child-friendly 3D farming game for Android, built with Godot 4.3 (GDScript).
Status: **Phase 1 (foundation) done.** See `docs/ARCHITECTURE.md` for the plan and `docs/STATUS.md`
for exactly what works and what doesn't.

## Run it
1. Install Godot 4.3 (standard, not .NET): https://godotengine.org/download/archive/4.3-stable/
2. Open `project.godot` in Godot and press F5. Mouse: left-drag pans, right-drag rotates, wheel zooms,
   click selects.

## Tests
```
godot --headless --path . -s res://tests/run_tests.gd          # rules + economy unit tests
xvfb-run godot --path . --rendering-driver opengl3 res://tests/shot.tscn   # screenshots to user://shots
```

## Build the Android test APK
1. Godot: Editor > Manage Export Templates > download 4.3.
2. Editor Settings > Export > Android: set the Java SDK (JDK 17+) and Android SDK paths
   (Android Studio installs the SDK; you need platform-tools and build-tools).
3. Project > Export > Android > Export Project (debug) or run:
   `godot --headless --path . --export-debug "Android" build/HappyFarmWorld-debug.apk`
4. Install on a phone: `adb install -r build/HappyFarmWorld-debug.apk`
   or copy the APK to the phone and open it (allow "install unknown apps").

The Google Play release bundle (AAB) needs the Gradle build (Android SDK + Gradle); set up in Phase 7.

## Changing the economy
All prices, timers, rewards and unlock levels: `data/game_config.json`. Texts: `data/i18n.json`.
