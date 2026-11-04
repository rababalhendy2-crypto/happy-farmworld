# Farm World (Unity 6 + URP)

Implementation of the design document "FARM WORLD – وثيقة التصميم والهندسة التنفيذية".

- Engine: Unity 6000.0.85f1, Universal Render Pipeline, C#
- Platforms: Android (APK) and Web (WebGL)
- Backend: Supabase (Auth, Postgres, Edge Functions) — project `happy-farm-world`
- The scene is assembled from code (`Assets/Scripts/Core/Bootstrap.cs`), so there are no hand-edited scene files.
- Builds run on GitHub Actions (`.github/workflows/build.yml`, game-ci). Required repo secrets:
  `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD`. Output goes to the `builds` branch.
- 3D models: Quaternius, CC0 (licences in `Assets/Resources/Models`).

The earlier Godot prototype is kept on the `godot-legacy` branch.
