using System;
using System.Collections;
using FarmWorld.Core;
using FarmWorld.Farming;
using UnityEngine;
using UnityEngine.UI;

namespace FarmWorld.UI
{
    /// <summary>
    /// Sprint 7 (part 1): the daily gift and the gem counter. The server decides everything (claim_daily in
    /// hf_action: one gift per UTC day, a 7-day cycle from cfg.daily_rewards); the client only shows it.
    /// Gems can only come from the server (daily gift day 7, level rewards, later real purchases).
    /// </summary>
    public partial class GameHud
    {
        Text _gems, _giftBadge;
        bool _giftShownThisRun;

        /// <summary>Mirror of cfg.daily_rewards on the server, only for display. The server's answer is what counts.</summary>
        static readonly (int coins, string item, int qty, int gems)[] DailyGifts =
        {
            (30, null, 0, 0), (40, "wheat", 5, 0), (50, null, 0, 0), (60, "carrot", 5, 0),
            (70, null, 0, 0), (80, "wheat", 10, 0), (100, null, 0, 2),
        };

        static int TodayUtc => (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 86400);

        bool GiftAvailable()
        {
            var gs = GameState.I; var be = Backend.I;
            return gs != null && !gs.Visiting && be != null && be.Online && gs.Data.serverLinked && gs.Data.dailyLastDay < TodayUtc;
        }

        void BuildGemsPill()
        {
            var pill = Box(_root, "Gems", new Vector2(0, 1), new Vector2(20, -84), new Vector2(150, 44), Panel);
            Gem(pill, new Vector2(10, 0), 30);
            _gems = Label(pill, "0", 24, Ink, TextAnchor.MiddleLeft, new Vector2(52, 0), new Vector2(92, 44), new Vector2(0, 0.5f));
        }

        static Sprite _gemSprite;

        /// <summary>A cut gem: a crown with a flat top and a pointed lower half, each face in its own shade of blue.</summary>
        void Gem(RectTransform parent, Vector2 pos, float size)
        {
            if (_gemSprite == null) _gemSprite = MakeGemSprite();
            var img = Img(parent, _gemSprite, Color.white, new Vector2(0, 0.5f), pos, new Vector2(size, size));
            img.GetComponent<Image>().preserveAspect = true;
        }

        static Sprite MakeGemSprite()
        {
            const int n = 96, ss = 4;
            // Faces in unit coordinates (y up), drawn in order; the last one that contains a point wins.
            Vector2 V(float x, float y) => new Vector2(x, y);
            var tl = V(0.30f, 0.86f); var tr = V(0.70f, 0.86f); var gl = V(0.06f, 0.62f); var gr = V(0.94f, 0.62f);
            var ml = V(0.36f, 0.62f); var mr = V(0.64f, 0.62f); var tip = V(0.5f, 0.06f);
            var faces = new (Vector2[] poly, Color32 col)[]
            {
                (new[] { gl, tl, ml }, new Color32(120, 215, 250, 255)),
                (new[] { tl, tr, mr, ml }, new Color32(170, 236, 255, 255)),
                (new[] { tr, gr, mr }, new Color32(70, 175, 230, 255)),
                (new[] { gl, ml, tip }, new Color32(55, 160, 225, 255)),
                (new[] { ml, mr, tip }, new Color32(90, 190, 240, 255)),
                (new[] { mr, gr, tip }, new Color32(30, 115, 195, 255)),
            };
            var outline = new[] { gl, tl, tr, gr, tip };
            static bool Inside(Vector2[] poly, Vector2 p)
            {
                bool c = false;
                for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                    if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) c = !c;
                return c;
            }
            static float EdgeDist(Vector2[] poly, Vector2 p)
            {
                float d = float.MaxValue;
                for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                {
                    var a = poly[j]; var v = poly[i] - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, v) / v.sqrMagnitude);
                    d = Mathf.Min(d, Vector2.Distance(p, a + t * v));
                }
                return d;
            }
            var edge = new Color32(25, 85, 150, 255);
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float r = 0, g = 0, b = 0, a = 0;
                    for (int sy = 0; sy < ss; sy++)
                        for (int sx = 0; sx < ss; sx++)
                        {
                            var p = new Vector2((x + (sx + 0.5f) / ss) / n, (y + (sy + 0.5f) / ss) / n);
                            if (!Inside(outline, p)) continue;
                            Color32 c = faces[0].col;
                            foreach (var f in faces) if (Inside(f.poly, p)) c = f.col;
                            if (EdgeDist(outline, p) < 0.035f) c = edge;
                            // A small white glint on the upper left face.
                            if (Vector2.Distance(p, V(0.27f, 0.74f)) < 0.045f) c = new Color32(255, 255, 255, 255);
                            r += c.r; g += c.g; b += c.b; a += 255;
                        }
                    float k = 1f / (ss * ss);
                    float al = a * k;
                    px[y * n + x] = al <= 0 ? new Color32(0, 0, 0, 0)
                        : new Color32((byte)(r / (a / 255f)), (byte)(g / (a / 255f)), (byte)(b / (a / 255f)), (byte)al);
                }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100);
        }

        void BuildGiftButton()
        {
            var b = Button(_root, new Vector2(1, 1), new Vector2(-506, -16), new Vector2(60, 52), Panel, () => OpenDailyGift());
            var box = Img(b, _round, new Color(0.88f, 0.3f, 0.3f), new Vector2(0.5f, 0.5f), new Vector2(0, -4), new Vector2(34, 26));
            Img(box, null, Gold, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(7, 26));
            var lid = Img(b, _round, new Color(0.95f, 0.4f, 0.38f), new Vector2(0.5f, 0.5f), new Vector2(0, 12), new Vector2(40, 10));
            Img(lid, null, Gold, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(7, 10));
            Img(b, _circle, Gold, new Vector2(0.5f, 0.5f), new Vector2(-6, 20), new Vector2(12, 10));
            Img(b, _circle, Gold, new Vector2(0.5f, 0.5f), new Vector2(6, 20), new Vector2(12, 10));
            _giftBadge = Badge(b);
        }

        void RefreshGemsAndGift()
        {
            var gs = GameState.I; if (gs == null) return;
            if (_gems != null) Set(_gems, gs.Data.gems.ToString("N0"));
            if (_giftBadge != null) { _giftBadge.transform.parent.gameObject.SetActive(GiftAvailable()); _giftBadge.text = "!"; }
        }

        /// <summary>Called when the game comes online: show the gift once per run if it is waiting.</summary>
        void MaybeShowGift()
        {
            if (_giftShownThisRun) return;
            StartCoroutine(ShowGiftSoon());
        }

        IEnumerator ShowGiftSoon()
        {
            yield return new WaitForSecondsRealtime(1.5f);
            RefreshGemsAndGift();
            if (_giftShownThisRun || !GiftAvailable() || _panel != null) yield break;
            _giftShownThisRun = true;
            OpenDailyGift();
        }

        void OpenDailyGift()
        {
            _giftShownThisRun = true;
            OpenPanel(Texts.Get("ui_daily_gift"), new Vector2(940, 460), FillDailyGift);
            _panelKind = "gift";
        }

        void FillDailyGift()
        {
            var gs = GameState.I; var be = Backend.I;
            if (be == null || !be.Enabled || !be.Online || !gs.Data.serverLinked)
            {
                Heading(Texts.Get(be != null && be.Enabled && be.State == Backend.Status.Connecting ? "ui_loading" : "ui_online_only"), 150)
                    .alignment = TextAnchor.MiddleCenter;
                return;
            }
            bool available = gs.Data.dailyLastDay < TodayUtc;
            int n = DailyGifts.Length;
            // position in the 7-day cycle: the next gift to take, or (once taken today) the one just taken
            int next = gs.Data.dailyStep % n;
            int today = available ? next : (gs.Data.dailyStep - 1 + n) % n;

            Heading(Texts.Get(available ? "ui_gift_hint" : "ui_gift_tomorrow"), 0).fontSize = 20;
            float cardW = 116, gap = 10, total = n * cardW + (n - 1) * gap;
            for (int i = 0; i < n; i++)
            {
                int slot = Texts.Rtl ? n - 1 - i : i;   // day 1 on the right in Arabic
                var g = DailyGifts[i];
                bool taken = i < today || (!available && i == today);
                bool isToday = available && i == today;
                var col = isToday ? new Color(1f, 0.9f, 0.55f) : taken ? new Color(0.82f, 0.9f, 0.75f) : RowColor;
                var card = Img(_panelBody, _round, col, new Vector2(0.5f, 1), new Vector2(-total / 2 + cardW / 2 + slot * (cardW + gap), -50), new Vector2(cardW, 200));
                var day = Label(card, Fmt("ui_day_n", i + 1), 18, Ink, TextAnchor.UpperCenter, new Vector2(0, -8), new Vector2(cardW, 28), new Vector2(0.5f, 1));
                day.fontStyle = FontStyle.Bold;
                var coin = Img(card, _circle, Gold, new Vector2(0.5f, 1), new Vector2(0, -44), new Vector2(40, 40));
                Img(coin, _circle, new Color(1f, 0.88f, 0.45f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24, 24));
                Label(card, g.coins.ToString(), 24, Ink, TextAnchor.UpperCenter, new Vector2(0, -88), new Vector2(cardW, 30), new Vector2(0.5f, 1)).font = _font;
                if (g.item != null)
                {
                    var t = Label(card, $"{g.qty} {Texts.Get(g.item)}", 16, Muted, TextAnchor.UpperCenter, new Vector2(0, -122), new Vector2(cardW - 8, 44), new Vector2(0.5f, 1));
                    t.horizontalOverflow = HorizontalWrapMode.Wrap;
                }
                if (g.gems > 0)
                {
                    var gemRow = Img(card, null, Color.clear, new Vector2(0.5f, 1), new Vector2(0, -124), new Vector2(80, 32));
                    Gem(gemRow, new Vector2(4, 0), 30);
                    Label(gemRow, g.gems.ToString(), 22, Ink, TextAnchor.MiddleLeft, new Vector2(40, 0), new Vector2(40, 32), new Vector2(0, 0.5f)).font = _font;
                }
                if (taken)
                {
                    var tick = Img(card, _circle, Green, new Vector2(0.5f, 0), new Vector2(0, 10), new Vector2(34, 34));
                    var a = Img(tick, null, Color.white, new Vector2(0.5f, 0.5f), new Vector2(-5, -2), new Vector2(4, 11));
                    a.localRotation = Quaternion.Euler(0, 0, 45);
                    var b = Img(tick, null, Color.white, new Vector2(0.5f, 0.5f), new Vector2(3, 1), new Vector2(4, 18));
                    b.localRotation = Quaternion.Euler(0, 0, -40);
                }
            }
            if (available)
            {
                var claim = Button(_panelBody, new Vector2(0.5f, 1), new Vector2(0, -276), new Vector2(360, 60), Green, ClaimDailyGift);
                FitLabel(claim, Texts.Get("ui_collect_gift"), 24);
            }
        }

        void ClaimDailyGift()
        {
            if (_socialBusy) return;
            _socialBusy = true;
            string json = "{\"p_action\":\"claim_daily\",\"p_args\":{},\"p_key\":" + Backend.Str(Guid.NewGuid().ToString()) + "}";
            Backend.I.Rpc("hf_action", json, r =>
            {
                _socialBusy = false;
                if (r == null) { ErrorToast(Texts.Get("ui_try_again"), 2f); return; }
                var state = MiniJson.O(r, "state");
                if (state != null) Backend.I.ApplyState(state);
                if (!MiniJson.B(r, "ok"))
                {
                    Toast(Texts.Get(MiniJson.S(r, "error") == "already_done" ? "ui_gift_tomorrow" : "ui_try_again"), 2.5f);
                }
                else
                {
                    var rw = MiniJson.O(r, "reward");
                    int coins = (int)MiniJson.N(rw, "coins"), gems = (int)MiniJson.N(rw, "premium");
                    Toast(gems > 0 ? Fmt("ui_gift_got_gems", coins, gems) : Fmt("ui_gift_got", coins), 3f);
                    Sound.Play("coins");
                }
                if (_panel != null && _panelKind == "gift") RefillPanel();
                RefreshGemsAndGift();
            });
        }
    
        // ------------------------------------------------------------------ gems: finish a crop now

        void OpenRush(FieldPlot plot)
        {
            int idx = plot.Index;
            OpenPanel(Texts.Get(GameState.I.Plot(idx).crop), new Vector2(640, 300), () => FillRush(plot));
            _panelKind = "rush";
        }

        void FillRush(FieldPlot plot)
        {
            var gs = GameState.I; int idx = plot.Index;
            if (gs.Progress(idx) >= 1f) { ClosePanel(); return; }
            Heading(Texts.Get("ui_ready_in"), 0).alignment = TextAnchor.MiddleCenter;
            var left = Heading(Duration(gs.SecondsLeft(idx)), 32);
            left.alignment = TextAnchor.MiddleCenter; left.font = _font; left.fontSize = 28;
            Countdown(left, () => gs.SecondsLeft(idx));
            int cost = gs.RushCost(idx);
            bool can = gs.Data.gems >= cost;
            var b = Button(_panelBody, new Vector2(0.5f, 1), new Vector2(0, -84), new Vector2(380, 64), can ? Green : Grey, () =>
            {
                if (!gs.Rush(idx)) { ErrorToast(Texts.Get("ui_not_enough_gems"), 2.5f); return; }
                Sound.Play("confirm");
                plot.Refresh(true);
                ClosePanel();
            });
            var label = FitLabel(b, Texts.Get("ui_finish_now"), 24);
            label.alignment = TextAnchor.MiddleLeft;
            label.rectTransform.offsetMin = new Vector2(24, 0); label.rectTransform.offsetMax = new Vector2(-110, 0);
            Gem(b, new Vector2(286, 0), 34);
            Label(b, cost.ToString(), 26, Color.white, TextAnchor.MiddleLeft, new Vector2(326, 0), new Vector2(50, 64), new Vector2(0, 0.5f)).font = _font;
            Note(Fmt("ui_gems_have", gs.Data.gems), 162, 18);
        }
    }
}
