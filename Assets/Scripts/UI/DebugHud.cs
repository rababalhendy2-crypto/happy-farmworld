using UnityEngine;
using UnityEngine.UI;

namespace FarmWorld.UI
{
    /// <summary>
    /// Sprint 0–1 HUD: build version, clock, and the name of whatever the player tapped.
    /// The full game HUD (coins, gems, XP, menus) comes with the economy sprint.
    /// </summary>
    public class DebugHud : MonoBehaviour
    {
        Text _clock, _selection, _version;
        float _hideAt;

        void Start()
        {
            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var font = Resources.Load<Font>("Fonts/NotoSans-Bold");
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            _version = Label(canvas.transform, font, new Vector2(0, 0), new Vector2(16, 12), TextAnchor.LowerLeft, 20);
            _version.text = "Farm World " + Core.Bootstrap.Version + "  ·  Sprint 1";
            _clock = Label(canvas.transform, font, new Vector2(1, 1), new Vector2(-24, -20), TextAnchor.UpperRight, 30);
            _selection = Label(canvas.transform, font, new Vector2(0.5f, 1), new Vector2(0, -24), TextAnchor.UpperCenter, 34);

            var rig = Core.GameRoot.Instance != null ? Core.GameRoot.Instance.CameraRig : null;
            if (rig != null) rig.OnSelect += s =>
            {
                _selection.text = s == null ? "" : Core.Texts.Get(s.TitleKey);
                _hideAt = Time.time + 3f;
            };
            World.DayNightCycle.OnTimeChanged += t =>
            {
                int minutes = Mathf.FloorToInt(t * 24f * 60f);
                if (_clock != null) _clock.text = $"{minutes / 60:00}:{minutes % 60:00}";
            };
        }

        void Update()
        {
            if (_selection != null && _selection.text.Length > 0 && Time.time > _hideAt) _selection.text = "";
        }

        static Text Label(Transform parent, Font font, Vector2 anchor, Vector2 offset, TextAnchor align, int size)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor;
            rt.anchoredPosition = offset; rt.sizeDelta = new Vector2(900, 60);
            var t = go.AddComponent<Text>();
            t.font = font; t.fontSize = size; t.alignment = align; t.color = Color.white;
            t.raycastTarget = false;
            var sh = go.AddComponent<Outline>(); sh.effectColor = new Color(0, 0, 0, 0.55f); sh.effectDistance = new Vector2(2, -2);
            return t;
        }
    }
}
