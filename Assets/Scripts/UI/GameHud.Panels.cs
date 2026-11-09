using System;
using System.Collections.Generic;
using FarmWorld.Core;
using UnityEngine;
using UnityEngine.UI;

namespace FarmWorld.UI
{
    /// <summary>
    /// Sprint 4 windows: workshops (mill, dairy), market orders and daily / weekly missions.
    /// One window is open at a time. It is rebuilt when the game state changes; countdowns tick
    /// in place, and a finished countdown rebuilds the window so its Collect button appears.
    /// </summary>
    public partial class GameHud
    {
        static readonly Color Grey = new(0.62f, 0.6f, 0.56f);
        static readonly Color RowColor = new(1f, 1f, 1f, 0.92f);
        static readonly Color Muted = new(0.45f, 0.4f, 0.35f);
        static readonly Color Lack = new(0.78f, 0.25f, 0.18f);
        static readonly Color Have = new(0.25f, 0.55f, 0.2f);

        RectTransform _panel, _panelBody;
        Action _panelFill;
        bool _panelDirty;
        float _nextTick;
        readonly List<(Text label, Func<string> value)> _timers = new();
        Text _missionBadge, _marketBadge, _workshopBadge;

        // ------------------------------------------------------------------ HUD buttons

        void BuildPanelButtons()
        {
            var market = Button(_root, new Vector2(1, 0), new Vector2(-264, 20), new Vector2(200, 70), Panel, OpenMarket);
            var stall = Img(market, _round, new Color(0.85f, 0.3f, 0.25f), new Vector2(0, 0.5f), new Vector2(12, 4), new Vector2(46, 22));
            Img(stall, _round, new Color(0.96f, 0.92f, 0.85f), new Vector2(0.5f, 0), new Vector2(0, -18), new Vector2(38, 18));
            Label(market, Texts.Get("ui_market"), 24, Ink, TextAnchor.MiddleLeft, new Vector2(68, 0), new Vector2(130, 70), new Vector2(0, 0.5f));
            _marketBadge = Badge(market);

            var missions = Button(_root, new Vector2(1, 0), new Vector2(-478, 20), new Vector2(200, 70), Panel, OpenMissions);
            var board = Img(missions, _round, new Color(0.55f, 0.38f, 0.2f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(40, 46));
            Img(board, _round, new Color(0.98f, 0.95f, 0.85f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30, 36));
            Label(missions, Texts.Get("ui_missions"), 24, Ink, TextAnchor.MiddleLeft, new Vector2(62, 0), new Vector2(136, 70), new Vector2(0, 0.5f));
            _missionBadge = Badge(missions);

            var shops = Button(_root, new Vector2(1, 0), new Vector2(-692, 20), new Vector2(200, 70), Panel, OpenWorkshops);
            var roof = Img(shops, _round, new Color(0.7f, 0.45f, 0.25f), new Vector2(0, 0.5f), new Vector2(12, 6), new Vector2(44, 20));
            Img(roof, _round, new Color(0.95f, 0.9f, 0.8f), new Vector2(0.5f, 0), new Vector2(0, -20), new Vector2(34, 22));
            Label(shops, Texts.Get("ui_workshops"), 22, Ink, TextAnchor.MiddleLeft, new Vector2(64, 0), new Vector2(134, 70), new Vector2(0, 0.5f));
            _workshopBadge = Badge(shops);
            RefreshBadges();
        }

        Text Badge(RectTransform button)
        {
            var dot = Img(button, _circle, new Color(0.88f, 0.22f, 0.2f), new Vector2(1, 1), new Vector2(10, 10), new Vector2(32, 32));
            var t = Label(dot, "", 18, Color.white, TextAnchor.MiddleCenter);
            dot.gameObject.SetActive(false);
            return t;
        }

        void RefreshBadges()
        {
            var gs = GameState.I;
            if (gs == null) return;
            SetBadge(_missionBadge, gs.ClaimableMissions());
            int deliverable = 0;
            foreach (var o in gs.Orders()) if (gs.CanDeliver(o)) deliverable++;
            SetBadge(_marketBadge, deliverable);
            SetBadge(_workshopBadge, gs.ReadyJobs());
        }

        static void SetBadge(Text t, int n)
        {
            if (t == null) return;
            t.transform.parent.gameObject.SetActive(n > 0);
            t.text = n.ToString();
        }

        /// <summary>Taps on buildings: workshops open their window, market stalls the market, silo and warehouse the storage.</summary>
        bool OpenBuilding(string id)
        {
            var ws = GameConfig.WorkshopAt(id);
            if (ws != null) { OpenWorkshop(ws); return true; }
            if (id.StartsWith("stall_")) { OpenMarket(); return true; }
            if (id == "silo" || id == "warehouse" || id == "barn") { OpenStorage(); return true; }
            return false;
        }

        // ------------------------------------------------------------------ window frame

        void OpenPanel(string title, Vector2 size, Action fill)
        {
            ClosePanel();
            CloseSeeds();
            if (_storageWindow != null) CloseStorage();
            var dim = Img(_root, null, Dim, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            dim.anchorMin = Vector2.zero; dim.anchorMax = Vector2.one; dim.sizeDelta = Vector2.zero;
            dim.gameObject.AddComponent<Button>().onClick.AddListener(ClosePanel);
            dim.name = "Panel";
            _panel = dim;

            var win = Box(dim, "Window", new Vector2(0.5f, 0.5f), Vector2.zero, size, Panel);
            win.gameObject.AddComponent<Button>().transition = UnityEngine.UI.Selectable.Transition.None;   // swallow taps inside the window
            Label(win, title, 32, Ink, TextAnchor.UpperCenter, new Vector2(0, -16), new Vector2(size.x - 120, 44), new Vector2(0.5f, 1));
            var close = Button(win, new Vector2(1, 1), new Vector2(-12, -12), new Vector2(48, 48), new Color(0.85f, 0.35f, 0.3f), ClosePanel);
            Label(close, "x", 28, Color.white, TextAnchor.MiddleCenter);

            _panelBody = new GameObject("Body", typeof(RectTransform)).GetComponent<RectTransform>();
            _panelBody.SetParent(win, false);
            _panelBody.anchorMin = _panelBody.anchorMax = new Vector2(0.5f, 1); _panelBody.pivot = new Vector2(0.5f, 1);
            _panelBody.anchoredPosition = new Vector2(0, -74); _panelBody.sizeDelta = new Vector2(size.x - 40, size.y - 90);

            _panelFill = fill;
            RefillPanel();
            _toastGroup.transform.SetAsLastSibling();
        }

        void RefillPanel()
        {
            _panelDirty = false;
            if (_panel == null || _panelBody == null) return;
            _timers.Clear();
            foreach (Transform c in _panelBody) Destroy(c.gameObject);
            _panelFill?.Invoke();
        }

        void ClosePanel()
        {
            if (_panel != null) Destroy(_panel.gameObject);
            _panel = null; _panelBody = null; _panelFill = null;
            _panelKind = "";
            _timers.Clear();
        }

        void TickPanel()
        {
            if (_panel != null && _panelDirty) RefillPanel();
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 0.5f;
            foreach (var (label, value) in _timers)
            {
                string v = value();
                if (v == null) { _panelDirty = true; break; }
                if (label != null) label.text = v;
            }
            RefreshBadges();
        }

        // ------------------------------------------------------------------ row helpers (mirrored for Arabic)

        RectTransform Row(float y, float h, Color color)
        {
            return Box(_panelBody, "Row", new Vector2(0.5f, 1), new Vector2(0, -y), new Vector2(_panelBody.sizeDelta.x, h), color);
        }

        /// <summary>Text placed x pixels from the row's start edge (left, or right in Arabic).</summary>
        Text RowText(RectTransform row, string text, int size, Color color, float x, float w)
        {
            bool rtl = Texts.Rtl;
            return Label(row, text, size, color, rtl ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft,
                new Vector2(rtl ? -x : x, 0), new Vector2(w, row.sizeDelta.y), new Vector2(rtl ? 1 : 0, 0.5f));
        }

        /// <summary>Button at the row's end edge (right, or left in Arabic).</summary>
        RectTransform RowButton(RectTransform row, string text, float w, Color color, Action onClick)
        {
            bool rtl = Texts.Rtl;
            var b = Button(row, new Vector2(rtl ? 0 : 1, 0.5f), new Vector2(rtl ? 10 : -10, 0), new Vector2(w, row.sizeDelta.y - 14), color, onClick);
            FitLabel(b, text, 20);
            return b;
        }

        Text FitLabel(Component parent, string text, int max)
        {
            var t = Label(parent, text, max, Color.white, TextAnchor.MiddleCenter);
            t.rectTransform.sizeDelta = new Vector2(-12, 0);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = 12;
            t.resizeTextMaxSize = max;
            return t;
        }

        Text Heading(string text, float y)
        {
            bool rtl = Texts.Rtl;
            return Label(_panelBody, text, 24, Ink, rtl ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft,
                new Vector2(0, -y), new Vector2(_panelBody.sizeDelta.x - 8, 34), new Vector2(0.5f, 1));
        }

        /// <summary>Live countdown label; when it reaches zero the window is rebuilt.</summary>
        void Countdown(Text label, Func<long> secondsLeft)
        {
            _timers.Add((label, () => { long s = secondsLeft(); return s > 0 ? Duration(s) : null; }));
        }

        // ------------------------------------------------------------------ workshops

        /// <summary>All workshops in one list, so the player does not have to scroll to the production area.</summary>
        void OpenWorkshops() => OpenPanel(Texts.Get("ui_workshops"), new Vector2(760, 400), FillWorkshops);

        void FillWorkshops()
        {
            var gs = GameState.I;
            float y = 0;
            foreach (var def in GameConfig.Workshops)
            {
                var ws = def;
                var save = gs.Workshop(def.Id);
                var row = Row(y, 80, RowColor);
                RowText(row, Texts.Get(def.TitleKey), 26, Ink, 20, 260);
                string status;
                if (save == null) status = gs.Data.level < def.UnlockLevel ? Fmt("ui_workshop_locked", def.UnlockLevel) : Fmt("ui_open_for", def.Cost);
                else
                {
                    int ready = 0;
                    foreach (var j in save.jobs) if (gs.JobSecondsLeft(j) == 0) ready++;
                    status = ready > 0 ? Fmt("ui_ready_count", ready) : Fmt("ui_slots", save.jobs.Count, def.Slots);
                }
                RowText(row, status, 20, Muted, 290, 260);
                RowButton(row, Texts.Get("ui_go"), 150, Green, () => OpenWorkshop(ws));
                y += 92;
            }
        }

        void OpenWorkshop(WorkshopDef def) =>
            OpenPanel(Texts.Get(def.TitleKey), new Vector2(820, 580), () => FillWorkshop(def));

        void FillWorkshop(WorkshopDef def)
        {
            var gs = GameState.I;
            var save = gs.Workshop(def.Id);
            float y = 0;
            if (save == null)
            {
                bool locked = gs.Data.level < def.UnlockLevel;
                foreach (var r in def.Recipes)
                {
                    var row = Row(y, 56, RowColor);
                    RowText(row, Texts.Get(r.Id), 22, Ink, 16, 200);
                    RowText(row, InputsText(r, false), 18, Muted, 230, 380);
                    y += 62;
                }
                y += 16;
                if (locked) { Heading(Fmt("ui_workshop_locked", def.UnlockLevel), y); y += 44; }
                bool can = !locked && gs.Data.coins >= def.Cost;
                var buy = Button(_panelBody, new Vector2(0.5f, 1), new Vector2(0, -y), new Vector2(360, 62), can ? Green : Grey, () =>
                {
                    switch (gs.BuyWorkshop(def))
                    {
                        case GameState.BuyResult.Ok: Toast(Fmt("ui_opened", Texts.Get(def.TitleKey)), 2.5f); break;
                        case GameState.BuyResult.Locked: Toast(Fmt("ui_workshop_locked", def.UnlockLevel), 2f); break;
                        case GameState.BuyResult.NoCoins: Toast(Texts.Get("ui_not_enough_coins"), 2f); break;
                    }
                });
                FitLabel(buy, Fmt("ui_open_for", def.Cost), 24);
                return;
            }

            Heading(Fmt("ui_slots", save.jobs.Count, def.Slots), y);
            y += 40;
            for (int i = 0; i < save.jobs.Count; i++)
            {
                var job = save.jobs[i];
                int index = i;
                var row = Row(y, 56, new Color(1f, 0.93f, 0.75f));
                RowText(row, Texts.Get(job.recipe), 22, Ink, 16, 260);
                if (gs.JobSecondsLeft(job) > 0)
                {
                    var t = RowText(row, Duration(gs.JobSecondsLeft(job)), 22, Muted, 300, 140);
                    Countdown(t, () => gs.JobSecondsLeft(job));
                }
                else
                    RowButton(row, Texts.Get("ui_collect"), 150, Green, () =>
                    {
                        switch (gs.CollectJob(def, index, out var made))
                        {
                            case GameState.CollectResult.Ok: Toast(Fmt("ui_made", made.Qty, Texts.Get(made.Id)), 2f); break;
                            case GameState.CollectResult.StorageFull: Toast(Texts.Get("ui_storage_full"), 2.5f); break;
                        }
                    });
                y += 62;
            }
            y += 10;
            bool free = save.jobs.Count < def.Slots;
            foreach (var r in def.Recipes)
            {
                var recipe = r;
                bool ok = free && gs.HasInputs(r);
                var row = Row(y, 64, RowColor);
                RowText(row, Texts.Get(r.Id), 22, Ink, 16, 190);
                RowText(row, InputsText(r, true), 18, gs.HasInputs(r) ? Have : Lack, 210, 330);
                RowText(row, Duration(r.Seconds), 18, Muted, 545, 80);
                RowButton(row, Texts.Get("ui_start"), 140, ok ? Green : Grey, () =>
                {
                    switch (gs.StartJob(def, recipe))
                    {
                        case GameState.JobResult.Busy: Toast(Texts.Get("ui_busy"), 2.2f); break;
                        case GameState.JobResult.Missing: Toast(Texts.Get("ui_missing") + ": " + InputsText(recipe, true), 2.4f); break;
                    }
                });
                y += 70;
            }
        }

        /// <summary>"Wheat 3/5 + Egg 1/1" (with stock) or "3 Wheat + 1 Egg".</summary>
        string InputsText(RecipeDef r, bool withStock)
        {
            var gs = GameState.I;
            var parts = new List<string>();
            foreach (var (item, qty) in r.Inputs)
                parts.Add(withStock ? $"{Texts.Get(item)} {gs.Count(item)}/{qty}" : $"{qty} {Texts.Get(item)}");
            return string.Join(" + ", parts);
        }

        // ------------------------------------------------------------------ market orders

        void OpenMarket() => OpenPanel(Texts.Get("ui_orders"), new Vector2(940, 520), FillMarket);

        void FillMarket()
        {
            var gs = GameState.I;
            var orders = gs.Orders();
            float w = 280, gap = 16;
            for (int i = 0; i < orders.Count; i++)
            {
                var o = orders[i];
                int index = i;
                float x = (i - (orders.Count - 1) / 2f) * (w + gap) * (Texts.Rtl ? -1 : 1);
                var card = Box(_panelBody, "Order", new Vector2(0.5f, 1), new Vector2(x, 0), new Vector2(w, 410), RowColor);
                if (GameState.IsWaiting(o))
                {
                    // a replaced order rests for two minutes; a delivered one waits for the server's new order
                    var wait = Label(card, "", 22, Muted, TextAnchor.MiddleCenter, new Vector2(0, 0), new Vector2(w - 20, 80), new Vector2(0.5f, 0.5f));
                    if (o.items.Count == 0 || o.availableUtc == long.MaxValue) Set(wait, Texts.Get("ui_order_coming"));
                    else
                    {
                        Set(wait, Fmt("ui_order_wait", Duration(o.availableUtc - GameState.Now)));
                        var order = o;
                        _timers.Add((wait, () => order.availableUtc > GameState.Now ? Texts.Display(Fmt("ui_order_wait", Duration(order.availableUtc - GameState.Now))) : null));
                    }
                    continue;
                }
                float y = -18;
                foreach (var it in o.items)
                {
                    int have = gs.Count(it.id);
                    Label(card, $"{Texts.Get(it.id)}  {have}/{it.count}", 24, have >= it.count ? Have : Lack, TextAnchor.MiddleCenter,
                        new Vector2(0, y), new Vector2(w - 20, 40), new Vector2(0.5f, 1));
                    y -= 46;
                }
                Label(card, Fmt("ui_reward", o.coins, o.xp), 22, new Color(0.62f, 0.42f, 0.02f), TextAnchor.MiddleCenter,
                    new Vector2(0, 150), new Vector2(w - 20, 36), new Vector2(0.5f, 0));
                bool can = gs.CanDeliver(o);
                var deliver = Button(card, new Vector2(0.5f, 0), new Vector2(0, 74), new Vector2(w - 40, 60), can ? Green : Grey, () =>
                {
                    int coins = o.coins;
                    if (gs.Deliver(index)) Toast(Fmt("ui_delivered", coins), 2.4f);
                    else Toast(Texts.Get("ui_order_missing"), 2f);
                });
                FitLabel(deliver, Texts.Get("ui_deliver"), 26);
                var skip = Button(card, new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(w - 80, 46), new Color(0.88f, 0.84f, 0.76f), () => gs.ReplaceOrder(index));
                var st = FitLabel(skip, Texts.Get("ui_new_order"), 20);
                st.color = Ink;
            }
        }

        // ------------------------------------------------------------------ missions

        void OpenMissions() => OpenPanel(Texts.Get("ui_missions"), new Vector2(840, 600), FillMissions);

        void FillMissions()
        {
            var gs = GameState.I;
            float y = 0;
            foreach (var kind in new[] { "daily", "weekly" })
            {
                Heading(Texts.Get(kind == "daily" ? "ui_daily" : "ui_weekly"), y);
                y += 40;
                foreach (var m in gs.ActiveMissions(kind))
                {
                    var mission = m;
                    int done = gs.MissionProgress(kind, m);
                    bool claimed = gs.MissionClaimed(kind, m);
                    bool complete = done >= m.Count;
                    var row = Row(y, 60, claimed ? new Color(0.9f, 0.95f, 0.85f) : RowColor);
                    RowText(row, MissionText(m), 21, Ink, 16, 360);
                    RowText(row, $"{done}/{m.Count}", 21, complete ? Have : Muted, 390, 80);
                    RowText(row, Fmt("ui_reward", m.Coins, m.Xp), 18, new Color(0.62f, 0.42f, 0.02f), 470, 170);
                    if (claimed)
                        RowText(row, Texts.Get("ui_claimed"), 20, Have, 660, 120);
                    else
                        RowButton(row, Texts.Get("ui_claim"), 130, complete ? Green : Grey, () =>
                        {
                            if (gs.ClaimMission(kind, mission)) Toast(Fmt("ui_mission_reward", mission.Coins), 2f);
                        });
                    y += 66;
                }
                y += 12;
            }
        }

        static string MissionText(MissionDef m)
        {
            bool item = m.Item != "";
            switch (m.Event)
            {
                case "harvest": return item ? Fmt("m_harvest_item", m.Count, Texts.Get(m.Item)) : Fmt("m_harvest", m.Count);
                case "collect": return item ? Fmt("m_collect_item", m.Count, Texts.Get(m.Item)) : Fmt("m_collect", m.Count);
                case "plant": return Fmt("m_plant", m.Count);
                case "feed": return Fmt("m_feed", m.Count);
                case "sell": return Fmt("m_sell", m.Count);
                case "produce": return Fmt("m_produce", m.Count);
                default: return m.Id;
            }
        }

        // ------------------------------------------------------------------ storage upgrade

        void StorageUpgradeButton()
        {
            var gs = GameState.I;
            var next = gs.NextStorage;
            if (next == null) return;
            var (capacity, cost) = next.Value;
            var b = Button(_storageList, new Vector2(0.5f, 1), new Vector2(0, -372), new Vector2(520, 52),
                gs.Data.coins >= cost ? new Color(0.3f, 0.5f, 0.75f) : Grey, () =>
                {
                    if (gs.UpgradeStorage()) Toast(Fmt("ui_storage_upgraded", capacity), 2.2f);
                    else Toast(Texts.Get("ui_not_enough_coins"), 2f);
                });
            FitLabel(b, Fmt("ui_upgrade_storage", capacity, cost), 22);
        }
    }
}
