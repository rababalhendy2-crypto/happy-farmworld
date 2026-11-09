using System.Collections.Generic;
using FarmWorld.Core;
using FarmWorld.Farming;
using FarmWorld.World;
using UnityEngine;
using UnityEngine.UI;
using Selectable = FarmWorld.World.Selectable;

namespace FarmWorld.UI
{
    /// <summary>
    /// Farming tools: after choosing a seed, the seed bag stays in hand and a swipe over empty fields plants
    /// them all; after harvesting one ripe field, the sickle stays in hand and a swipe harvests every ripe
    /// field it passes. A press on grass still moves the camera; the X on the tool bar puts the tool away.
    /// </summary>
    public partial class GameHud
    {
        enum Tool { None, Seed, Sickle }
        Tool _tool;
        CropDef _toolCrop;
        RectTransform _toolBar, _toolCursor;
        Sprite _sickle;
        readonly HashSet<int> _strokeDone = new();
        bool _strokeWarned;

        void HookTools()
        {
            var rig = GameRoot.Instance != null ? GameRoot.Instance.CameraRig : null;
            if (rig == null) return;
            rig.StrokeStart += OnStrokeStart;
            rig.StrokeMove += OnStrokeMove;
            rig.StrokeEnd += OnStrokeEnd;
        }

        void UnhookTools()
        {
            var rig = GameRoot.Instance != null ? GameRoot.Instance.CameraRig : null;
            if (rig == null) return;
            rig.StrokeStart -= OnStrokeStart;
            rig.StrokeMove -= OnStrokeMove;
            rig.StrokeEnd -= OnStrokeEnd;
        }

        void StartTool(Tool tool, CropDef crop = null)
        {
            if (GameState.I == null || GameState.I.Visiting) return;
            _tool = tool; _toolCrop = crop;
            if (!AnyTarget()) { EndTool(); return; }
            BuildToolBar();
        }

        void EndTool()
        {
            _tool = Tool.None; _toolCrop = null;
            if (_toolBar != null) Destroy(_toolBar.gameObject);
            if (_toolCursor != null) Destroy(_toolCursor.gameObject);
            _toolBar = _toolCursor = null;
        }

        /// <summary>True while at least one field can still take the tool in hand.</summary>
        bool AnyTarget()
        {
            var gs = GameState.I;
            for (int i = 0; i < gs.Data.plots.Count; i++) if (Applies(i)) return true;
            return false;
        }

        bool Applies(int idx)
        {
            var gs = GameState.I; var p = gs.Plot(idx);
            if (p == null) return false;
            bool empty = string.IsNullOrEmpty(p.crop);
            return _tool == Tool.Seed ? empty : _tool == Tool.Sickle && !empty && gs.Progress(idx) >= 1f;
        }

        void BuildToolBar()
        {
            if (_toolBar != null) Destroy(_toolBar.gameObject);
            _toolBar = Box(_root, "ToolBar", new Vector2(0.5f, 0), new Vector2(0, 104), new Vector2(640, 66), Panel);
            ToolIcon(_toolBar, new Vector2(10, 0), 54);
            string text = _tool == Tool.Seed ? Fmt("ui_tool_seed", Texts.Get(_toolCrop.Id)) : Texts.Get("ui_tool_sickle");
            var t = Label(_toolBar, text, 22, Ink, TextAnchor.MiddleCenter, new Vector2(0, 0), new Vector2(480, 60), new Vector2(0.5f, 0.5f));
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            var close = Button(_toolBar, new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(46, 46), new Color(0.85f, 0.35f, 0.3f), EndTool);
            Label(close, "x", 26, Color.white, TextAnchor.MiddleCenter);
        }

        /// <summary>The tool's picture: a sickle, or a seed bag in the crop's colour.</summary>
        RectTransform ToolIcon(RectTransform parent, Vector2 pos, float size)
        {
            if (_tool == Tool.Sickle)
            {
                if (_sickle == null) _sickle = MakeSickleSprite();
                var s = Img(parent, _sickle, Color.white, new Vector2(0, 0.5f), pos, new Vector2(size, size));
                s.GetComponent<Image>().raycastTarget = false;
                return s;
            }
            var holder = Img(parent, null, Color.clear, new Vector2(0, 0.5f), pos, new Vector2(size, size));
            holder.GetComponent<Image>().raycastTarget = false;
            var bag = Img(holder, _round, new Color(0.86f, 0.74f, 0.52f), new Vector2(0.5f, 0), new Vector2(0, 2), new Vector2(size * 0.72f, size * 0.7f));
            Img(holder, _round, new Color(0.7f, 0.55f, 0.32f), new Vector2(0.5f, 0), new Vector2(0, size * 0.66f), new Vector2(size * 0.42f, size * 0.16f));
            Img(bag, _circle, _toolCrop != null ? _toolCrop.Color : Green, new Vector2(0.5f, 0.5f), new Vector2(0, -2), new Vector2(size * 0.36f, size * 0.36f));
            foreach (var g in holder.GetComponentsInChildren<Image>()) g.raycastTarget = false;
            return holder;
        }

        // ------------------------------------------------------------------ strokes from the camera rig

        bool OnStrokeStart(Selectable s, Vector2 screen)
        {
            if (_tool == Tool.None || _panel != null || GameState.I.Visiting) return false;
            var plot = s != null ? s.GetComponent<FieldPlot>() : null;
            if (plot == null || !Applies(plot.Index)) return false;
            _strokeDone.Clear(); _strokeWarned = false;
            CloseSeeds();
            if (_toolCursor != null) Destroy(_toolCursor.gameObject);
            _toolCursor = ToolIcon(_root, Vector2.zero, 84);
            _toolCursor.anchorMin = _toolCursor.anchorMax = Vector2.zero;
            MoveCursor(screen);
            UseOn(plot);
            return true;
        }

        void OnStrokeMove(Selectable s, Vector2 screen)
        {
            MoveCursor(screen);
            var plot = s != null ? s.GetComponent<FieldPlot>() : null;
            if (plot != null && _tool != Tool.None && !_strokeDone.Contains(plot.Index) && Applies(plot.Index)) UseOn(plot);
        }

        void OnStrokeEnd()
        {
            if (_toolCursor != null) Destroy(_toolCursor.gameObject);
            _toolCursor = null;
            if (_tool != Tool.None && !AnyTarget()) EndTool();
        }

        void MoveCursor(Vector2 screen)
        {
            if (_toolCursor == null) return;
            float scale = _canvas != null ? _canvas.scaleFactor : 1f;
            // a little above and beside the finger, so the finger does not hide it
            _toolCursor.anchoredPosition = screen / scale + new Vector2(-20, 30);
        }

        void UseOn(FieldPlot plot)
        {
            var gs = GameState.I; int idx = plot.Index;
            _strokeDone.Add(idx);
            if (_tool == Tool.Seed)
            {
                var crop = _toolCrop;
                if (gs.Data.coins < crop.SeedPrice)
                {
                    if (!_strokeWarned) ErrorToast(Texts.Get("ui_not_enough_coins"), 2f);
                    _strokeWarned = true;
                    return;
                }
                if (!gs.Plant(idx, crop)) return;
                Sound.Play("plant", 0.7f);
                plot.Refresh(true);
                Floater("-" + crop.SeedPrice, plot.transform.position + Vector3.up * 1.2f);
            }
            else if (_tool == Tool.Sickle)
            {
                switch (gs.Harvest(idx, out var crop))
                {
                    case GameState.HarvestResult.Ok:
                        Floater(Fmt("ui_harvested", crop.Yield, Texts.Get(crop.Id)), plot.transform.position + Vector3.up * 1.5f);
                        Sound.Play("harvest", 0.8f);
                        plot.Refresh(true);
                        break;
                    case GameState.HarvestResult.StorageFull:
                        if (!_strokeWarned) ErrorToast(Texts.Get("ui_storage_full"), 2.5f);
                        _strokeWarned = true;
                        break;
                }
            }
        }

        /// <summary>A grey crescent blade on a wooden handle, drawn once at runtime.</summary>
        static Sprite MakeSickleSprite()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            static float Seg(Vector2 p, Vector2 a, Vector2 b)
            {
                var v = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, v) / v.sqrMagnitude);
                return Vector2.Distance(p, a + t * v);
            }
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    Color32 c = new Color32(0, 0, 0, 0);
                    float h = Seg(p, new Vector2(16, 14), new Vector2(50, 50));
                    if (h < 7f) c = h < 5.5f ? new Color32(150, 95, 50, 255) : new Color32(100, 60, 30, 255);
                    if (Seg(p, new Vector2(46, 46), new Vector2(54, 54)) < 7f) c = new Color32(120, 120, 130, 255);
                    float d1 = Vector2.Distance(p, new Vector2(76, 74)), d2 = Vector2.Distance(p, new Vector2(84, 68));
                    if (d1 < 44f && d2 > 37f && p.y > 50f && !(p.x > 100f && p.y < 74f))
                    {
                        c = d2 - 37f < 3f ? new Color32(235, 240, 245, 255) : new Color32(190, 198, 210, 255);
                        if (44f - d1 < 2.5f) c = new Color32(130, 140, 155, 255);
                    }
                    px[y * n + x] = c;
                }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100);
        }
    }
}
