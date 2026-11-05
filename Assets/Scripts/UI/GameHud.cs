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
    /// Sprint 2 HUD: coins, level + XP bar, clock, storage button, the seed picker that opens when an
    /// empty field is tapped, the storage window (with selling), toasts and floating "+3 Wheat" labels.
    /// All UI is built from code with uGUI.
    /// </summary>
    public class GameHud : MonoBehaviour
    {
        static readonly Color Panel = new(1f, 0.97f, 0.9f, 0.97f);
        static readonly Color Ink = new(0.27f, 0.18f, 0.1f);
        static readonly Color Green = new(0.36f, 0.66f, 0.25f);
        static readonly Color Gold = new(0.98f, 0.77f, 0.2f);
        static readonly Color Dim = new(0f, 0f, 0f, 0.35f);

        Canvas _canvas;
        RectTransform _root;
        Font _font;
        Sprite _round, _circle;

        Text _coins, _level, _clock, _storageLabel, _toast, _selection;
        Image _xpFill;
        RectTransform _seedSheet, _storageWindow, _storageList;
        CanvasGroup _toastGroup;
        float _toastUntil;
        int _seedField = -1;
        readonly List<(Text label, Vector3 world, float born)> _floaters = new();

        // ------------------------------------------------------------------ setup

        void Start()
        {
            _font = Resources.Load<Font>("Fonts/NotoSans-Bold");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _round = MakeRoundedSprite(64, 18);
            _circle = MakeRoundedSprite(64, 32);

            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            _root = (RectTransform)canvasGo.transform;

            BuildTopBar();
            BuildBottomBar();
            BuildSeedSheet();
            BuildStorageWindow();
            BuildToast();

            var gs = GameState.I;
            gs.Changed += RefreshTop;
            gs.LeveledUp += lvl => Toast(Fmt("ui_level_up", lvl, GameConfig.LevelRewardCoins), 3f);
            RefreshTop();

            var rig = GameRoot.Instance != null ? GameRoot.Instance.CameraRig : null;
            if (rig != null) rig.OnSelect += OnSelect;
            DayNightCycle.OnTimeChanged += t =>
            {
                int minutes = Mathf.FloorToInt(t * 24f * 60f);
                if (_clock != null) _clock.text = $"{minutes / 60:00}:{minutes % 60:00}";
            };
        }

        void OnDestroy()
        {
            if (GameState.I != null) GameState.I.Changed -= RefreshTop;
        }

        // ------------------------------------------------------------------ top / bottom bars

        void BuildTopBar()
        {
            // Coins pill (top-left)
            var coinPill = Box(_root, "Coins", new Vector2(0, 1), new Vector2(20, -18), new Vector2(210, 58), Panel);
            var coin = Img(coinPill, _circle, Gold, new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(40, 40));
            Label(coin, "c", 24, new Color(0.6f, 0.4f, 0.05f), TextAnchor.MiddleCenter);
            _coins = Label(coinPill, "0", 30, Ink, TextAnchor.MiddleLeft, new Vector2(60, 0), new Vector2(140, 58), new Vector2(0, 0.5f));

            // Level badge + XP bar
            var lvlPill = Box(_root, "Level", new Vector2(0, 1), new Vector2(244, -18), new Vector2(280, 58), Panel);
            var badge = Img(lvlPill, _circle, Green, new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(44, 44));
            _level = Label(badge, "1", 26, Color.white, TextAnchor.MiddleCenter);
            var barBg = Img(lvlPill, _round, new Color(0.85f, 0.8f, 0.7f), new Vector2(0, 0.5f), new Vector2(64, 0), new Vector2(200, 18));
            _xpFill = Img(barBg, _round, Green, new Vector2(0, 0.5f), Vector2.zero, new Vector2(200, 18)).GetComponent<Image>();
            _xpFill.type = Image.Type.Filled; _xpFill.fillMethod = Image.FillMethod.Horizontal; _xpFill.fillAmount = 0;

            // Clock (top-right)
            _clock = Label(_root, "", 30, Color.white, TextAnchor.UpperRight, new Vector2(-24, -20), new Vector2(200, 50), new Vector2(1, 1));
            Shadow(_clock);

            // Selection name (top-centre)
            _selection = Label(_root, "", 32, Color.white, TextAnchor.UpperCenter, new Vector2(0, -90), new Vector2(900, 50), new Vector2(0.5f, 1));
            Shadow(_selection);
        }

        void BuildBottomBar()
        {
            var version = Label(_root, "Farm World " + Bootstrap.Version, 18, Color.white, TextAnchor.LowerLeft,
                new Vector2(16, 10), new Vector2(400, 30), new Vector2(0, 0));
            Shadow(version);

            var btn = Button(_root, new Vector2(1, 0), new Vector2(-20, 20), new Vector2(230, 70), Panel, OpenStorage);
            var icon = Img(btn, _round, new Color(0.65f, 0.42f, 0.22f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(46, 46));
            Img(icon, _round, new Color(0.85f, 0.62f, 0.35f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(36, 12));
            _storageLabel = Label(btn, "", 24, Ink, TextAnchor.MiddleLeft, new Vector2(68, 0), new Vector2(160, 70), new Vector2(0, 0.5f));
        }

        void RefreshTop()
        {
            var gs = GameState.I; if (gs == null || _coins == null) return;
            _coins.text = gs.Data.coins.ToString("N0");
            _level.text = gs.Data.level.ToString();
            int a = gs.XpForLevel(gs.Data.level), b = gs.XpForLevel(gs.Data.level + 1);
            _xpFill.fillAmount = Mathf.Clamp01((gs.Data.xp - a) / (float)Mathf.Max(1, b - a));
            _storageLabel.text = Fmt("ui_storage_count", gs.StorageUsed(), GameConfig.StorageCapacity);
            if (_storageWindow != null && _storageWindow.gameObject.activeSelf) FillStorage();
            if (_seedSheet != null && _seedSheet.gameObject.activeSelf) FillSeeds();
        }

        // ------------------------------------------------------------------ field taps

        void OnSelect(Selectable s)
        {
            CloseSeeds();
            if (s == null) { _selection.text = ""; return; }
            var drop = s.GetComponent<ProductDrop>();
            if (drop != null && drop.Unit != null) { OnAnimal(drop.Unit, drop.Unit.GetComponent<Selectable>()); return; }
            var animal = s.GetComponent<AnimalUnit>();
            if (animal != null) { OnAnimal(animal, s); return; }
            var plot = s.GetComponent<FieldPlot>();
            if (plot == null)
            {
                _selection.text = Texts.Get(s.TitleKey);
                _selectionUntil = Time.time + 3f;
                return;
            }
            _selection.text = "";
            var gs = GameState.I;
            var save = gs.Plot(plot.Index);
            if (save == null) return;
            if (string.IsNullOrEmpty(save.crop)) { OpenSeeds(plot.Index); return; }

            if (gs.Progress(plot.Index) < 1f)
            {
                Toast(Fmt("ui_growing", Texts.Get(save.crop), Duration(gs.SecondsLeft(plot.Index))), 2f);
                return;
            }
            switch (gs.Harvest(plot.Index, out var crop))
            {
                case GameState.HarvestResult.Ok:
                    Floater(Fmt("ui_harvested", crop.Yield, Texts.Get(crop.Id)), plot.transform.position + Vector3.up * 1.5f);
                    plot.Refresh(true);
                    break;
                case GameState.HarvestResult.StorageFull:
                    Toast(Texts.Get("ui_storage_full"), 2.5f);
                    break;
            }
        }

        float _selectionUntil;

        void OnAnimal(AnimalUnit unit, Selectable s)
        {
            var gs = GameState.I;
            var save = unit.Save;
            if (save == null || !GameConfig.AnimalById.TryGetValue(unit.Type, out var def)) return;
            string name = Texts.Get(s.TitleKey);
            switch (gs.StateOf(save))
            {
                case GameState.AnimalState.Hungry:
                    switch (gs.Feed(save))
                    {
                        case GameState.FeedResult.Ok:
                            Floater("-" + def.FeedQty + " " + Texts.Get(def.FeedItem), unit.transform.position + Vector3.up * 1.4f);
                            Toast(Fmt("ui_fed", name, Texts.Get(def.Product), Duration(def.ProduceSeconds)), 2.2f);
                            break;
                        case GameState.FeedResult.NoFeed:
                            Toast(Fmt("ui_needs_feed", name, def.FeedQty, Texts.Get(def.FeedItem)), 2.6f);
                            break;
                    }
                    break;
                case GameState.AnimalState.Producing:
                    Toast(Fmt("ui_producing", name, Texts.Get(def.Product), Duration(gs.AnimalSecondsLeft(save)), gs.Happiness(save)), 2.2f);
                    break;
                case GameState.AnimalState.Ready:
                    switch (gs.Collect(save, out var product, out int qty))
                    {
                        case GameState.CollectResult.Ok:
                            Floater(Fmt("ui_harvested", qty, Texts.Get(product)), unit.transform.position + Vector3.up * 1.6f);
                            break;
                        case GameState.CollectResult.StorageFull:
                            Toast(Texts.Get("ui_storage_full"), 2.5f);
                            break;
                    }
                    break;
            }
            unit.Refresh(true);
        }

        // ------------------------------------------------------------------ seed sheet

        void BuildSeedSheet()
        {
            _seedSheet = Box(_root, "SeedSheet", new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(980, 230), Panel);
            Label(_seedSheet, Texts.Get("ui_choose_seed"), 26, Ink, TextAnchor.UpperCenter, new Vector2(0, -10), new Vector2(900, 40), new Vector2(0.5f, 1));
            var close = Button(_seedSheet, new Vector2(1, 1), new Vector2(-10, -10), new Vector2(44, 44), new Color(0.85f, 0.35f, 0.3f), CloseSeeds);
            Label(close, "x", 26, Color.white, TextAnchor.MiddleCenter);
            _seedSheet.gameObject.SetActive(false);
        }

        void OpenSeeds(int field)
        {
            _seedField = field;
            _seedSheet.gameObject.SetActive(true);
            FillSeeds();
        }

        void CloseSeeds()
        {
            _seedField = -1;
            if (_seedSheet != null) _seedSheet.gameObject.SetActive(false);
        }

        void FillSeeds()
        {
            var old = _seedSheet.Find("Cards");
            if (old != null) Destroy(old.gameObject);
            var cards = new GameObject("Cards", typeof(RectTransform)).GetComponent<RectTransform>();
            cards.SetParent(_seedSheet, false);
            cards.anchorMin = cards.anchorMax = new Vector2(0.5f, 0); cards.pivot = new Vector2(0.5f, 0);
            cards.anchoredPosition = new Vector2(0, 16); cards.sizeDelta = new Vector2(940, 160);

            var gs = GameState.I;
            int n = GameConfig.Crops.Count;
            float w = 172f, gap = 12f, x0 = -(n * w + (n - 1) * gap) / 2f + w / 2f;
            for (int i = 0; i < n; i++)
            {
                var crop = GameConfig.Crops[i];
                bool locked = gs.Data.level < crop.UnlockLevel;
                bool afford = gs.Data.coins >= crop.SeedPrice;
                var c = crop;
                var card = Button(cards, new Vector2(0.5f, 0.5f), new Vector2(x0 + i * (w + gap), 0), new Vector2(w, 160),
                    locked ? new Color(0.82f, 0.8f, 0.76f) : Color.white, () => PickSeed(c));
                card.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 0.5f);
                var sw = Img(card, _circle, locked ? new Color(0.6f, 0.6f, 0.6f) : crop.Color, new Vector2(0.5f, 1), new Vector2(0, -12), new Vector2(54, 54));
                sw.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1);
                Label(card, Texts.Get(crop.Id), 22, Ink, TextAnchor.MiddleCenter, new Vector2(0, -72), new Vector2(w, 30), new Vector2(0.5f, 1));
                if (locked)
                    Label(card, Fmt("ui_locked", crop.UnlockLevel), 20, new Color(0.45f, 0.4f, 0.35f), TextAnchor.MiddleCenter, new Vector2(0, 20), new Vector2(w, 30), new Vector2(0.5f, 0));
                else
                {
                    Label(card, crop.SeedPrice + " c", 22, afford ? new Color(0.62f, 0.42f, 0.02f) : new Color(0.8f, 0.2f, 0.15f),
                        TextAnchor.MiddleCenter, new Vector2(0, 30), new Vector2(w, 28), new Vector2(0.5f, 0));
                    Label(card, Duration(crop.GrowSeconds), 18, new Color(0.4f, 0.35f, 0.3f), TextAnchor.MiddleCenter,
                        new Vector2(0, 8), new Vector2(w, 26), new Vector2(0.5f, 0));
                }
            }
        }

        void PickSeed(CropDef crop)
        {
            var gs = GameState.I;
            if (_seedField < 0) return;
            if (gs.Data.level < crop.UnlockLevel) { Toast(Fmt("ui_needs_level", crop.UnlockLevel), 2f); return; }
            if (gs.Data.coins < crop.SeedPrice) { Toast(Texts.Get("ui_not_enough_coins"), 2f); return; }
            int field = _seedField;
            if (!gs.Plant(field, crop)) return;
            CloseSeeds();
            var world = GameRoot.Instance != null ? GameRoot.Instance.World : null;
            if (world != null && field < world.Fields.Count)
            {
                var plot = world.Fields[field].GetComponent<FieldPlot>();
                plot.Refresh(true); plot.Pop();
                Floater("-" + crop.SeedPrice + " c", plot.transform.position + Vector3.up * 1.2f);
            }
        }

        // ------------------------------------------------------------------ storage window

        void BuildStorageWindow()
        {
            var dim = Img(_root, null, Dim, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var drt = dim;
            drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one; drt.sizeDelta = Vector2.zero;
            dim.gameObject.AddComponent<Button>().onClick.AddListener(CloseStorage);
            _storageWindow = drt;

            var win = Box(drt, "Window", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640, 520), Panel);
            win.pivot = new Vector2(0.5f, 0.5f);
            win.gameObject.AddComponent<Button>().transition = UnityEngine.UI.Selectable.Transition.None;   // swallow taps inside the window
            Label(win, Texts.Get("ui_storage"), 32, Ink, TextAnchor.UpperCenter, new Vector2(0, -16), new Vector2(600, 44), new Vector2(0.5f, 1));
            var close = Button(win, new Vector2(1, 1), new Vector2(-12, -12), new Vector2(48, 48), new Color(0.85f, 0.35f, 0.3f), CloseStorage);
            Label(close, "x", 28, Color.white, TextAnchor.MiddleCenter);

            _storageList = new GameObject("List", typeof(RectTransform)).GetComponent<RectTransform>();
            _storageList.SetParent(win, false);
            _storageList.anchorMin = _storageList.anchorMax = new Vector2(0.5f, 1); _storageList.pivot = new Vector2(0.5f, 1);
            _storageList.anchoredPosition = new Vector2(0, -76); _storageList.sizeDelta = new Vector2(600, 420);
            drt.gameObject.SetActive(false);
        }

        void OpenStorage()
        {
            CloseSeeds();
            _storageWindow.gameObject.SetActive(true);
            _storageWindow.SetAsLastSibling();
            _toastGroup.transform.SetAsLastSibling();
            FillStorage();
        }

        void CloseStorage() => _storageWindow.gameObject.SetActive(false);

        void FillStorage()
        {
            foreach (Transform c in _storageList) Destroy(c.gameObject);
            var inv = GameState.I.Data.inventory;
            if (inv.Count == 0)
            {
                Label(_storageList, Texts.Get("ui_empty_storage"), 24, new Color(0.45f, 0.4f, 0.35f), TextAnchor.MiddleCenter,
                    new Vector2(0, -60), new Vector2(560, 60), new Vector2(0.5f, 1));
                return;
            }
            float y = 0;
            foreach (var stack in inv)
            {
                var row = Box(_storageList, "Row", new Vector2(0.5f, 1), new Vector2(0, -y), new Vector2(580, 62), Color.white);
                row.pivot = new Vector2(0.5f, 1);
                var col = GameConfig.CropById.TryGetValue(stack.id, out var cd) ? cd.Color
                        : stack.id == "milk" ? new Color(0.95f, 0.96f, 1f) : stack.id == "wool" ? new Color(0.93f, 0.9f, 0.84f)
                        : stack.id == "egg" ? new Color(0.99f, 0.9f, 0.72f) : new Color(0.8f, 0.75f, 0.65f);
                Img(row, _circle, col, new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(40, 40));
                Label(row, Texts.Get(stack.id), 24, Ink, TextAnchor.MiddleLeft, new Vector2(64, 0), new Vector2(200, 62), new Vector2(0, 0.5f));
                Label(row, "x" + stack.count, 24, Ink, TextAnchor.MiddleLeft, new Vector2(250, 0), new Vector2(80, 62), new Vector2(0, 0.5f));
                int total = GameConfig.SellPriceOf(stack.id) * stack.count;
                string id = stack.id;
                var sell = Button(row, new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(200, 46), Green, () =>
                {
                    int got = GameState.I.Sell(id, int.MaxValue);
                    if (got > 0) Toast(Fmt("ui_sold", got), 1.6f);
                });
                sell.GetComponent<RectTransform>().pivot = new Vector2(1, 0.5f);
                Label(sell, Texts.Get("ui_sell_all") + "  " + total + " c", 20, Color.white, TextAnchor.MiddleCenter);
                y += 70;
                if (y > 400) break;
            }
        }

        // ------------------------------------------------------------------ toast + floaters

        void BuildToast()
        {
            var box = Box(_root, "Toast", new Vector2(0.5f, 1), new Vector2(0, -90), new Vector2(620, 54), new Color(0.15f, 0.12f, 0.08f, 0.85f));
            box.pivot = new Vector2(0.5f, 1);
            _toast = Label(box, "", 24, Color.white, TextAnchor.MiddleCenter);
            _toastGroup = box.gameObject.AddComponent<CanvasGroup>();
            _toastGroup.alpha = 0; _toastGroup.blocksRaycasts = false;
        }

        void Toast(string text, float seconds)
        {
            _toast.text = text;
            _toastUntil = Time.time + seconds;
            _toastGroup.alpha = 1;
            _selection.text = "";
        }

        void Floater(string text, Vector3 world)
        {
            var t = Label(_root, text, 30, Color.white, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(400, 50), new Vector2(0, 0));
            Shadow(t);
            _floaters.Add((t, world, Time.time));
        }

        void Update()
        {
            if (_toastGroup != null && _toastGroup.alpha > 0 && Time.time > _toastUntil)
                _toastGroup.alpha = Mathf.MoveTowards(_toastGroup.alpha, 0, Time.deltaTime * 3f);
            if (_selection != null && _selection.text.Length > 0 && Time.time > _selectionUntil) _selection.text = "";

            var cam = Camera.main;
            float scale = _canvas != null ? _canvas.scaleFactor : 1f;
            for (int i = _floaters.Count - 1; i >= 0; i--)
            {
                var (label, world, born) = _floaters[i];
                float age = Time.time - born;
                if (label == null || age > 1.4f || cam == null)
                {
                    if (label != null) Destroy(label.gameObject);
                    _floaters.RemoveAt(i);
                    continue;
                }
                Vector2 sp = cam.WorldToScreenPoint(world);
                var rt = label.rectTransform;
                rt.anchoredPosition = sp / scale + new Vector2(-200, age * 70f);
                var c = label.color; c.a = 1f - Mathf.Clamp01((age - 0.9f) / 0.5f); label.color = c;
            }
        }

        // ------------------------------------------------------------------ helpers

        static string Fmt(string key, params object[] args) => string.Format(Texts.Get(key), args);

        static string Duration(long seconds)
        {
            if (seconds >= 3600) return $"{seconds / 3600}h {seconds % 3600 / 60:00}m";
            return $"{seconds / 60}:{seconds % 60:00}";
        }

        RectTransform Box(Component parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
        {
            var rt = Img(parent, _round, color, anchor, pos, size);
            rt.name = name;
            return rt;
        }

        RectTransform Img(Component parent, Sprite sprite, Color color, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject("Image", typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.sprite = sprite; img.color = color;
            if (sprite != null && sprite.border != Vector4.zero) img.type = Image.Type.Sliced;
            return rt;
        }

        RectTransform Button(Component parent, Vector2 anchor, Vector2 pos, Vector2 size, Color color, System.Action onClick)
        {
            var rt = Img(parent, _round, color, anchor, pos, size);
            var go = rt.gameObject;
            go.name = "Button";
            var b = go.AddComponent<Button>();
            var colors = b.colors; colors.pressedColor = new Color(0.85f, 0.85f, 0.85f); b.colors = colors;
            b.onClick.AddListener(() => onClick());
            return rt;
        }

        Text Label(Component parent, string text, int size, Color color, TextAnchor align)
        {
            var t = Label(parent, text, size, color, align, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            var rt = t.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;
            return t;
        }

        Text Label(Component parent, string text, int size, Color color, TextAnchor align, Vector2 pos, Vector2 box, Vector2 anchor)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor;
            rt.anchoredPosition = pos; rt.sizeDelta = box;
            var t = go.AddComponent<Text>();
            t.font = _font; t.fontSize = size; t.alignment = align; t.color = color; t.text = text;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        static void Shadow(Text t)
        {
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0, 0, 0, 0.55f); o.effectDistance = new Vector2(2, -2);
        }

        /// <summary>White rounded-rectangle sprite with 9-slice borders, generated at runtime.</summary>
        static Sprite MakeRoundedSprite(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius), cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    byte a = (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255);
                    px[y * size + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px); tex.Apply();
            int border = Mathf.Min(radius, size / 2 - 1);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
        }
    }
}
