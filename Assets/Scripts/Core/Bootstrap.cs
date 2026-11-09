using UnityEngine;

namespace FarmWorld.Core
{
    /// <summary>
    /// Entry point. The game is assembled from code so the project has no hand-edited scene files:
    /// the single build scene is empty and this runs after it loads.
    /// </summary>
    public static class Bootstrap
    {
        public const string Version = "0.8.2";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (Object.FindAnyObjectByType<GameRoot>() != null) return;
            var go = new GameObject("GameRoot");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<GameRoot>();
        }
    }

    public class GameRoot : MonoBehaviour
    {
        public static GameRoot Instance { get; private set; }
        public World.IsoCameraRig CameraRig { get; private set; }
        public World.WorldBuilder World { get; private set; }

        void Awake()
        {
            Instance = this;
            Texts.Init();
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.SystemSetting;

            var gs = gameObject.AddComponent<GameState>();
            gameObject.AddComponent<Backend>();
            gameObject.AddComponent<Sound>();
            var sun = World_CreateSun();
            World = new GameObject("World").AddComponent<World.WorldBuilder>();
            World.Build();

            var cycle = new GameObject("DayNight").AddComponent<World.DayNightCycle>();
            cycle.Sun = sun;

            CameraRig = new GameObject("CameraRig").AddComponent<World.IsoCameraRig>();
            CameraRig.Bounds = World.PlayableBounds;
            CameraRig.FocusImmediate(World.FarmCenter);

            new GameObject("Hud").AddComponent<UI.GameHud>();

            // the server's state replaced the local one: redraw crops and animals
            gs.LayoutChanged += World.SyncLayout;
            gs.ServerApplied += () =>
            {
                World.SyncLayout();
                foreach (var f in World.Fields) if (f != null) f.GetComponent<Farming.FieldPlot>()?.Refresh(false);
                foreach (var a in Object.FindObjectsByType<Farming.AnimalUnit>(FindObjectsSortMode.None)) a.Refresh(false);
            };
        }

        static Light World_CreateSun()
        {
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) Destroy(l.gameObject);
            var go = new GameObject("Sun");
            var sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            sun.intensity = 1.25f;
            sun.color = new Color(1f, 0.93f, 0.82f);
            go.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            RenderSettings.sun = sun;
            return sun;
        }
    }
}
