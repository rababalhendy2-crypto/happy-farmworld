using System.Collections;
using System.Collections.Generic;
using FarmWorld.Core;
using FarmWorld.World;
using UnityEngine;
using UnityEngine.UI;
using Selectable = FarmWorld.World.Selectable;

namespace FarmWorld.UI
{
    /// <summary>
    /// Building and arranging the farm.
    ///  * Build: a shop of fields, buildings, fences, trees, decorations and pets for coins. The chosen
    ///    thing appears on the farm as a preview with a green (fits) or red (blocked) patch; drag it into
    ///    place, turn it, then confirm. Fences, flowers and trees can be placed one after another.
    ///  * Arrange: drag anything on the farm to a new spot, turn it, or sell what was bought (half price back).
    /// Every change goes to the server (move_item / buy_item / sell_item), which checks prices and limits.
    /// </summary>
    public partial class GameHud
    {
        bool _editing;
        LayoutPiece _sel, _drag, _ghost;
        BuildItemDef _placing;
        int _ghostR;
        Vector3 _dragOffset;
        RectTransform _editBar, _pieceBar;
        string _buildTab = "field";
        readonly Dictionary<string, RenderTexture> _thumbs = new();
        readonly Queue<BuildItemDef> _thumbQueue = new();
        bool _thumbBusy;
        Camera _thumbCam;
        const int ThumbLayer = 31;

        WorldBuilder World => GameRoot.Instance != null ? GameRoot.Instance.World : null;
        IsoCameraRig Rig => GameRoot.Instance != null ? GameRoot.Instance.CameraRig : null;

        // ------------------------------------------------------------------ HUD buttons

        void BuildBuildButtons()
        {
            var build = Button(_root, new Vector2(0, 0), new Vector2(20, 44), new Vector2(176, 66), Panel, OpenBuildShop);
            Hammer(build, new Vector2(10, 0));
            FitLabel(build, Texts.Get("ui_build"), 22).color = Ink;
            ShiftLabel(build);

            var move = Button(_root, new Vector2(0, 0), new Vector2(20, 118), new Vector2(176, 66), Panel, ToggleEdit);
            Arrows(move, new Vector2(10, 0));
            FitLabel(move, Texts.Get("ui_arrange"), 22).color = Ink;
            ShiftLabel(move);
        }

        static void ShiftLabel(RectTransform b)
        {
            var t = b.GetComponentInChildren<Text>();
            t.rectTransform.offsetMin = new Vector2(62, 0); t.rectTransform.offsetMax = new Vector2(-6, 0);
        }

        void Hammer(RectTransform parent, Vector2 pos)
        {
            var h = Img(parent, null, Color.clear, new Vector2(0, 0.5f), pos, new Vector2(46, 46));
            var handle = Img(h, _round, new Color(0.62f, 0.4f, 0.22f), new Vector2(0.5f, 0.5f), new Vector2(2, -4), new Vector2(8, 36));
            handle.localRotation = Quaternion.Euler(0, 0, -35);
            var head = Img(h, _round, new Color(0.45f, 0.47f, 0.52f), new Vector2(0.5f, 0.5f), new Vector2(-6, 10), new Vector2(26, 12));
            head.localRotation = Quaternion.Euler(0, 0, -35);
        }

        void Arrows(RectTransform parent, Vector2 pos)
        {
            var h = Img(parent, _circle, new Color(0.36f, 0.6f, 0.85f), new Vector2(0, 0.5f), pos, new Vector2(44, 44));
            Img(h, _round, Color.white, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30, 6));
            Img(h, _round, Color.white, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(6, 30));
        }

        // ------------------------------------------------------------------ arrange mode

        void ToggleEdit() { if (_editing) EndEdit(); else BeginEdit(); }

        void BeginEdit()
        {
            if (GameState.I.Visiting) return;
            EndTool(); ClosePanel(); CloseSeeds(); CancelPlacing();
            _editing = true;
            _editBar = Box(_root, "EditBar", new Vector2(0.5f, 1), new Vector2(0, -150), new Vector2(720, 62), new Color(0.36f, 0.6f, 0.85f, 0.96f));
            var t = Label(_editBar, Texts.Get("ui_arrange_hint"), 22, Color.white, TextAnchor.MiddleCenter, new Vector2(-70, 0), new Vector2(560, 60), new Vector2(0.5f, 0.5f));
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            var done = Button(_editBar, new Vector2(1, 0.5f), new Vector2(-8, 0), new Vector2(130, 48), Green, EndEdit);
            FitLabel(done, Texts.Get("ui_done"), 22);
        }

        void EndEdit()
        {
            _editing = false;
            Select(null);
            if (_editBar != null) Destroy(_editBar.gameObject);
            _editBar = null;
        }

        void Select(LayoutPiece p)
        {
            if (_sel != null && _sel != p) _sel.ShowMarker(false, true);
            _sel = p;
            if (_pieceBar != null) Destroy(_pieceBar.gameObject);
            _pieceBar = null;
            if (p == null) return;
            p.ShowMarker(true, true);
            bool sell = p.Def.Sale && p.Def.Cat != "field";
            float w = sell ? 640 : 440;
            _pieceBar = Box(_root, "PieceBar", new Vector2(0.5f, 0), new Vector2(0, 104), new Vector2(w, 66), Panel);
            Label(_pieceBar, Texts.Get(TitleOf(p)), 22, Ink, TextAnchor.MiddleCenter, new Vector2(16, 0), new Vector2(200, 60), new Vector2(0, 0.5f))
                .horizontalOverflow = HorizontalWrapMode.Wrap;
            var rot = Button(_pieceBar, new Vector2(0, 0.5f), new Vector2(226, 0), new Vector2(190, 50), new Color(0.36f, 0.6f, 0.85f), () => RotateSelected());
            FitLabel(rot, Texts.Get("ui_rotate"), 22);
            if (sell)
            {
                var s = Button(_pieceBar, new Vector2(0, 0.5f), new Vector2(428, 0), new Vector2(196, 50), new Color(0.85f, 0.55f, 0.2f), SellSelected);
                FitLabel(s, Fmt("ui_sell_for", GameState.SellValue(p.Def)), 22);
            }
        }

        static string TitleOf(LayoutPiece p) => "build_" + p.Item.t;

        void RotateSelected()
        {
            var p = _sel; var w = World;
            if (p == null || w == null) return;
            int r = (p.Item.r + 1) % 4;
            if (!w.CanPlace(p, p.Item.x, p.Item.z, r)) { ErrorToast(Texts.Get("ui_no_room"), 2f); return; }
            GameState.I.MoveItem(p.Item, p.Item.x, p.Item.z, r);
            Sound.Play("tap", 0.6f);
            p.ShowMarker(true, true);
        }

        void SellSelected()
        {
            var p = _sel;
            if (p == null) return;
            int coins = GameState.SellValue(p.Def);
            Select(null);
            if (GameState.I.SellItem(p.Item)) { Toast(Fmt("ui_sold_item", coins), 2f); Sound.Play("coins"); }
        }

        /// <summary>Taps while arranging only choose what to move.</summary>
        bool EditTap(Selectable s)
        {
            if (_ghost != null) return true;   // while placing, taps on the farm do nothing else
            if (!_editing) return false;
            var p = s != null ? s.GetComponentInParent<LayoutPiece>() : null;
            if (p == null && s != null)
            {
                // an animal: choose its pen
                var unit = s.GetComponent<Farming.AnimalUnit>();
                if (unit != null && World != null)
                    foreach (var pc in World.Pieces)
                        if (pc != null && (pc.Def.Model == "proc:pen" || pc.Def.Model == "proc:coop") &&
                            (pc.Def.Model == "proc:coop") == (unit.Type == "chicken")) { p = pc; break; }
            }
            Select(p);
            return true;
        }

        // ------------------------------------------------------------------ dragging (arrange and placing)

        bool EditStrokeStart(Selectable s, Vector2 screen)
        {
            if (!_editing && _ghost == null) return false;
            var p = s != null ? s.GetComponentInParent<LayoutPiece>() : null;
            if (!Rig.GroundAt(screen, out var g)) return false;
            if (_ghost != null)
            {
                // A thin fence or a small tree is hard to hit with a finger, so a drag that starts near the new piece grabs it too.
                var gp = _ghost.transform.position;
                float reach = Mathf.Max(1.6f, _ghost.Def != null ? _ghost.Def.Size * 0.75f : 1.6f);
                if (p != _ghost && new Vector2(g.x - gp.x, g.z - gp.z).magnitude > reach) return false;
                p = _ghost;
            }
            else if (p == null || p.Ghost) return false;
            if (_ghost == null) Select(p);
            _drag = p;
            _dragOffset = p.transform.position - g;
            return true;
        }

        void EditStrokeMove(Vector2 screen)
        {
            if (_drag == null || !Rig.GroundAt(screen, out var g)) return;
            var target = g + _dragOffset;
            float x = Layout.Snap(target.x), z = Layout.Snap(target.z);
            int r = _drag == _ghost ? _ghostR : _drag.Item.r;
            _drag.Place(x, z, r);
            _drag.ShowMarker(true, World.CanPlace(_drag, x, z, r));
        }

        void EditStrokeEnd()
        {
            var p = _drag; _drag = null;
            if (p == null) return;
            var pos = p.transform.position;
            if (p == _ghost)
            {
                _ghost.Item.x = pos.x; _ghost.Item.z = pos.z;
                return;
            }
            float x = Layout.Snap(pos.x), z = Layout.Snap(pos.z);
            if (World.CanPlace(p, x, z, p.Item.r))
            {
                if (Mathf.Abs(x - p.Item.x) > 0.01f || Mathf.Abs(z - p.Item.z) > 0.01f)
                {
                    GameState.I.MoveItem(p.Item, x, z, p.Item.r);
                    Sound.Play("plant", 0.5f);
                }
                p.ShowMarker(true, true);
            }
            else
            {
                ErrorToast(Texts.Get("ui_no_room"), 1.6f);
                p.Place(p.Item.x, p.Item.z, p.Item.r);
                p.ShowMarker(true, true);
            }
        }

        // ------------------------------------------------------------------ build shop

        void OpenBuildShop()
        {
            if (GameState.I.Visiting) return;
            EndTool(); EndEdit(); CancelPlacing();
            OpenPanel(Texts.Get("ui_build"), new Vector2(1180, 640), FillBuild);
            _panelKind = "build";
        }

        void FillBuild()
        {
            var gs = GameState.I;
            // tabs
            int n = Layout.Categories.Length;
            float tw = 176, tg = 8, total = n * tw + (n - 1) * tg;
            for (int i = 0; i < n; i++)
            {
                string cat = Layout.Categories[i];
                int slot = Texts.Rtl ? n - 1 - i : i;
                bool on = cat == _buildTab;
                var tab = Button(_panelBody, new Vector2(0.5f, 1), new Vector2(-total / 2 + tw / 2 + slot * (tw + tg), 0), new Vector2(tw, 50),
                    on ? Green : new Color(0.86f, 0.8f, 0.7f), () => { _buildTab = cat; RefillPanel(); });
                tab.pivot = new Vector2(0.5f, 1);
                FitLabel(tab, Texts.Get("cat_" + cat), 22).color = on ? Color.white : Ink;
            }

            var list = new List<BuildItemDef>();
            foreach (var d in Layout.ItemList) if (d.Cat == _buildTab && d.Sale) list.Add(d);
            const int perRow = 7;
            float cw = 150, ch = 214, gap = 10;
            for (int i = 0; i < list.Count; i++)
            {
                var def = list[i];
                int col = i % perRow, row = i / perRow;
                int cols = Mathf.Min(perRow, list.Count - row * perRow);
                float rowW = cols * cw + (cols - 1) * gap;
                int c = Texts.Rtl ? cols - 1 - col : col;
                var pos = new Vector2(-rowW / 2 + cw / 2 + c * (cw + gap), -64 - row * (ch + gap));
                var can = gs.CanBuy(def);
                int price = Layout.Price(gs.Data.layout, def);
                var card = Button(_panelBody, new Vector2(0.5f, 1), pos, new Vector2(cw, ch),
                    can == GameState.BuildResult.Locked ? new Color(0.82f, 0.8f, 0.76f) : Color.white, () => PickBuild(def));
                card.pivot = new Vector2(0.5f, 1);
                Thumb(card, def, new Vector2(0, -6), 118, can == GameState.BuildResult.Locked);
                var name = Label(card, Texts.Get("build_" + def.Id), 18, Ink, TextAnchor.MiddleCenter, new Vector2(0, -126), new Vector2(cw - 8, 44), new Vector2(0.5f, 1));
                name.horizontalOverflow = HorizontalWrapMode.Wrap;
                if (can == GameState.BuildResult.Locked)
                    Label(card, Fmt("ui_locked", def.Level), 18, Muted, TextAnchor.MiddleCenter, new Vector2(0, 10), new Vector2(cw, 30), new Vector2(0.5f, 0));
                else if (can == GameState.BuildResult.Max)
                    Label(card, Texts.Get("ui_max_owned"), 16, Muted, TextAnchor.MiddleCenter, new Vector2(0, 10), new Vector2(cw - 8, 30), new Vector2(0.5f, 0))
                        .horizontalOverflow = HorizontalWrapMode.Wrap;
                else
                {
                    var coin = Img(card, _circle, Gold, new Vector2(0.5f, 0), new Vector2(-30, 14), new Vector2(24, 24));
                    Img(coin, _circle, new Color(1f, 0.88f, 0.45f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14, 14));
                    var pl = Label(card, price.ToString("N0"), 22, can == GameState.BuildResult.NoCoins ? Lack : Ink, TextAnchor.MiddleLeft,
                        new Vector2(32, 12), new Vector2(90, 28), new Vector2(0.5f, 0));
                    pl.font = _font;
                }
            }
        }

        void PickBuild(BuildItemDef def)
        {
            var gs = GameState.I;
            switch (gs.CanBuy(def))
            {
                case GameState.BuildResult.Locked: Toast(Fmt("ui_needs_level", def.Level), 2f); return;
                case GameState.BuildResult.Max: Toast(Texts.Get("ui_max_owned"), 2f); return;
                case GameState.BuildResult.NoCoins: ErrorToast(Texts.Get("ui_not_enough_coins"), 2f); return;
            }
            ClosePanel();
            Vector3 at = World.FarmCenter;
            if (Rig != null && Rig.GroundAt(new Vector2(Screen.width / 2f, Screen.height / 2f), out var g)) at = g;
            StartPlacing(def, at);
        }

        // ------------------------------------------------------------------ placing a new thing

        void StartPlacing(BuildItemDef def, Vector3 near)
        {
            CancelPlacing();
            var w = World; if (w == null) return;
            _placing = def;
            var item = new PlacedItem { u = "ghost", t = def.Id, x = Layout.Snap(near.x), z = Layout.Snap(near.z), r = _ghostR };
            _ghost = w.SpawnPiece(item, true);
            var spot = w.FreeSpotNear(_ghost, new Vector3(item.x, 0, item.z), _ghostR);
            item.x = spot.x; item.z = spot.z;
            _ghost.Place(item.x, item.z, _ghostR);
            _ghost.ShowMarker(true, w.CanPlace(_ghost, item.x, item.z, _ghostR));
            Rig.FocusOn(spot);
            BuildPlaceBar();
        }

        void BuildPlaceBar()
        {
            if (_pieceBar != null) Destroy(_pieceBar.gameObject);
            var def = _placing;
            _pieceBar = Box(_root, "PlaceBar", new Vector2(0.5f, 0), new Vector2(0, 104), new Vector2(820, 70), Panel);
            var t = Label(_pieceBar, Fmt("ui_place_hint", Texts.Get("build_" + def.Id)), 20, Ink, TextAnchor.MiddleCenter,
                new Vector2(14, 0), new Vector2(250, 66), new Vector2(0, 0.5f));
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            var rot = Button(_pieceBar, new Vector2(0, 0.5f), new Vector2(274, 0), new Vector2(150, 54), new Color(0.36f, 0.6f, 0.85f), RotateGhost);
            FitLabel(rot, Texts.Get("ui_rotate"), 22);
            var ok = Button(_pieceBar, new Vector2(0, 0.5f), new Vector2(434, 0), new Vector2(250, 54), Green, ConfirmPlacing);
            FitLabel(ok, Fmt("ui_buy_for", Layout.Price(GameState.I.Data.layout, def)), 22);
            var x = Button(_pieceBar, new Vector2(0, 0.5f), new Vector2(694, 0), new Vector2(112, 54), new Color(0.85f, 0.35f, 0.3f), CancelPlacing);
            FitLabel(x, Texts.Get("ui_cancel"), 20);
        }

        void RotateGhost()
        {
            if (_ghost == null) return;
            _ghostR = (_ghostR + 1) % 4;
            _ghost.Item.r = _ghostR;
            _ghost.Place(_ghost.Item.x, _ghost.Item.z, _ghostR);
            _ghost.ShowMarker(true, World.CanPlace(_ghost, _ghost.Item.x, _ghost.Item.z, _ghostR));
        }

        void ConfirmPlacing()
        {
            if (_ghost == null || _placing == null) return;
            var w = World; var gs = GameState.I; var def = _placing;
            float x = _ghost.Item.x, z = _ghost.Item.z;
            if (!w.CanPlace(_ghost, x, z, _ghostR)) { ErrorToast(Texts.Get("ui_no_room"), 2f); return; }
            int price = Layout.Price(gs.Data.layout, def);
            switch (gs.BuyItem(def, x, z, _ghostR, out _))
            {
                case GameState.BuildResult.Ok:
                    Sound.Play("coins");
                    Floater("-" + price, new Vector3(x, 2f, z));
                    var next = new Vector3(x, 0, z);
                    // fences, plants and fields: keep building, the next one goes right beside the last
                    bool again = def.Cat == "fence" || def.Cat == "tree" || def.Cat == "field" || (def.Cat == "decor" && def.Cost <= 20);
                    var size = _ghost.FootprintAt(x, z, _ghostR);
                    CancelPlacing();
                    if (again && gs.CanBuy(def) == GameState.BuildResult.Ok)
                    {
                        // fences continue in a line along their length
                        var step = def.Cat == "fence" ? ((_ghostR & 1) == 0 ? new Vector3(2f, 0, 0) : new Vector3(0, 0, 2f)) : new Vector3(size.width + 0.5f, 0, 0);
                        StartPlacing(def, next + step);
                    }
                    break;
                case GameState.BuildResult.NoCoins: ErrorToast(Texts.Get("ui_not_enough_coins"), 2f); break;
                case GameState.BuildResult.Max: Toast(Texts.Get("ui_max_owned"), 2f); CancelPlacing(); break;
                default: Toast(Fmt("ui_needs_level", def.Level), 2f); CancelPlacing(); break;
            }
        }

        void CancelPlacing()
        {
            if (_ghost != null) Destroy(_ghost.gameObject);
            _ghost = null; _placing = null; _drag = null;
            if (_pieceBar != null && _sel == null) { Destroy(_pieceBar.gameObject); _pieceBar = null; }
        }

        // ------------------------------------------------------------------ thumbnails of the shop items

        /// <summary>A picture of the item rendered once from the real model (or a plain tile while it is being made).</summary>
        void Thumb(RectTransform card, BuildItemDef def, Vector2 pos, float size, bool dim)
        {
            var bg = Img(card, _round, new Color(0.88f, 0.94f, 0.82f), new Vector2(0.5f, 1), pos, new Vector2(size + 16, size));
            bg.pivot = new Vector2(0.5f, 1);
            var go = new GameObject("Thumb", typeof(RectTransform));
            go.transform.SetParent(bg, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(size, size);
            var raw = go.AddComponent<RawImage>();
            raw.raycastTarget = false;
            raw.color = dim ? new Color(0.6f, 0.6f, 0.6f) : Color.white;
            if (!_thumbs.TryGetValue(def.Id, out var tex))
            {
                tex = new RenderTexture(160, 160, 24, RenderTextureFormat.ARGB32) { name = "Thumb_" + def.Id, antiAliasing = 1 };
                tex.Create();
                _thumbs[def.Id] = tex;
                _thumbQueue.Enqueue(def);
                if (!_thumbBusy) StartCoroutine(RenderThumbs());
            }
            raw.texture = tex;
        }

        IEnumerator RenderThumbs()
        {
            _thumbBusy = true;
            var w = World;
            if (Camera.main != null) Camera.main.cullingMask &= ~(1 << ThumbLayer);
            if (_thumbCam == null)
            {
                var cg = new GameObject("ThumbCamera");
                _thumbCam = cg.AddComponent<Camera>();
                _thumbCam.orthographic = true;
                _thumbCam.cullingMask = 1 << ThumbLayer;
                _thumbCam.clearFlags = CameraClearFlags.SolidColor;
                _thumbCam.backgroundColor = new Color(0, 0, 0, 0);
                _thumbCam.nearClipPlane = 0.1f; _thumbCam.farClipPlane = 200f;
                _thumbCam.enabled = false;
            }
            var stage = new Vector3(0, -400, 0);
            while (_thumbQueue.Count > 0 && w != null)
            {
                var def = _thumbQueue.Dequeue();
                if (!_thumbs.TryGetValue(def.Id, out var tex)) continue;
                var item = new PlacedItem { u = "thumb", t = def.Id, p = -1 };
                LayoutPiece piece = null;
                try { piece = w.SpawnPiece(item, true); } catch (System.Exception e) { Debug.LogWarning("[Thumb] " + def.Id + ": " + e.Message); }
                if (piece == null) continue;
                piece.transform.position = stage;
                piece.transform.rotation = Quaternion.Euler(0, 0, 0);
                foreach (var tr in piece.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = ThumbLayer;
                var walker = piece.GetComponentInChildren<Walker>(); if (walker != null) walker.enabled = false;
                var b = ModelLibrary.LocalBounds(piece.gameObject, piece.transform);
                var centre = stage + b.center;
                float radius = Mathf.Max(b.extents.magnitude, 0.3f);
                var rot = Quaternion.Euler(30f, 45f, 0f);
                _thumbCam.transform.SetPositionAndRotation(centre - rot * Vector3.forward * (radius * 4f + 2f), rot);
                _thumbCam.orthographicSize = radius * 1.05f;
                RenderThumb(tex);
                piece.gameObject.SetActive(false);
                Destroy(piece.gameObject);
                yield return null;
            }
            _thumbBusy = false;
        }

        /// <summary>Renders the thumb camera straight into the texture. A camera that is only switched on for a frame
        /// was skipped by URP on WebGL (empty tiles in 0.8.1), so the render is requested explicitly.</summary>
        void RenderThumb(RenderTexture tex)
        {
            bool done = false;
            try
            {
                var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = tex };
                if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(_thumbCam, req))
                {
                    UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(_thumbCam, req);
                    done = true;
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[Thumb] request: " + e.Message); }
            if (done) return;
            _thumbCam.targetTexture = tex;
            _thumbCam.Render();
            _thumbCam.targetTexture = null;
        }
    }
}
