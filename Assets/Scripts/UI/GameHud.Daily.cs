using System;
using System.Collections;
using FarmWorld.Core;
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

        /// <summary>A small diamond: a turned square with a lighter centre.</summary>
        void Gem(RectTransform parent, Vector2 pos, float size)
        {
            var holder = Img(parent, null, Color.clear, new Vector2(0, 0.5f), pos, new Vector2(size, size));
            var d = Img(holder, _round, new Color(0.3f, 0.72f, 0.9f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 0.7f, size * 0.7f));
            d.localRotation = Quaternion.Euler(0, 0, 45);
            var c = Img(d, _round, new Color(0.7f, 0.92f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 0.3f, size * 0.3f));
            c.localRotation = Quaternion.identity;
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
    }
}
