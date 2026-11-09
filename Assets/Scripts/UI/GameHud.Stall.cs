using System.Collections.Generic;
using FarmWorld.Core;
using UnityEngine;
using UnityEngine.UI;

namespace FarmWorld.UI
{
    /// <summary>
    /// Sprint 6: market between friends. Each player has a stall with up to four offers; friends buy from it
    /// while visiting. Listing puts the goods aside at once; prices stay between the stall price and three
    /// times it. Every step runs on the server (hf_market_*), which also returns the player's new state.
    /// </summary>
    public partial class GameHud
    {
        const int MaxOffers = 4, MaxOfferQty = 10, MaxPriceFactor = 3;   // match cfg.market on the server

        List<object> _offers;          // offers shown in the open stall window (own or the visited friend's)
        bool _offersLoading;
        string _stallItem = "";
        int _stallQty = 1, _stallPrice;

        // ------------------------------------------------------------------ own stall

        void OpenStall()
        {
            _offers = null;
            OpenPanel(Texts.Get("ui_my_stall"), new Vector2(880, 600), FillStall);
            _panelKind = "stall";
            LoadOffers("");
        }

        void LoadOffers(string code)
        {
            var be = Backend.I;
            if (be == null || !be.Enabled) { _offersLoading = false; RefillStall(); return; }
            _offersLoading = true;
            be.Rpc("hf_market_offers", "{\"p_code\":" + (code == "" ? "null" : Backend.Str(code)) + "}", r =>
            {
                _offersLoading = false;
                _offers = r != null && MiniJson.B(r, "ok") ? MiniJson.A(r, "offers") ?? new List<object>() : null;
                RefillStall();
            });
        }

        void RefillStall() { if (_panel != null && (_panelKind == "stall" || _panelKind == "shop")) RefillPanel(); }

        void FillStall()
        {
            var gs = GameState.I;
            if (_offers == null)
            {
                Heading(Texts.Get(_offersLoading ? "ui_loading" : "ui_online_only"), 180).alignment = TextAnchor.MiddleCenter;
                return;
            }
            float y = 0;
            Heading(Texts.Get("ui_stall_hint"), y).fontSize = 18;
            y += 40;
            foreach (var o in _offers)
            {
                long id = (long)MiniJson.N(o, "id");
                string item = MiniJson.S(o, "item");
                int qty = (int)MiniJson.N(o, "qty"), price = (int)MiniJson.N(o, "unit_price");
                var row = Row(y, 64, RowColor);
                RowText(row, $"{qty} {Texts.Get(item)}", 22, Ink, 16, 300);
                RowText(row, Fmt("ui_coins_n", qty * price), 20, new Color(0.62f, 0.42f, 0.02f), 320, 200);
                RowButton(row, Texts.Get("ui_cancel"), 150, Grey, () => CancelOffer(id));
                y += 74;
            }
            if (_offers.Count >= MaxOffers) { Heading(Texts.Get("ui_stall_full"), y + 10).alignment = TextAnchor.MiddleCenter; return; }

            // new offer: item, amount and unit price, each with arrows
            var items = new List<string>();
            foreach (var s in gs.Data.inventory) if (s.count > 0) items.Add(s.id);
            if (items.Count == 0) { Heading(Texts.Get("ui_stall_empty"), y + 10).alignment = TextAnchor.MiddleCenter; return; }
            if (!items.Contains(_stallItem)) { _stallItem = items[0]; _stallQty = 1; _stallPrice = 0; }
            int basePrice = GameConfig.SellPriceOf(_stallItem);
            int maxQty = Mathf.Min(MaxOfferQty, gs.Count(_stallItem));
            _stallQty = Mathf.Clamp(_stallQty, 1, Mathf.Max(1, maxQty));
            if (_stallPrice == 0) _stallPrice = basePrice * 2;
            _stallPrice = Mathf.Clamp(_stallPrice, basePrice, basePrice * MaxPriceFactor);

            Heading(Texts.Get("ui_new_offer"), y + 6);
            y += 46;
            Stepper(y, Texts.Get(_stallItem), () => StepItem(items, -1), () => StepItem(items, 1));
            y += 66;
            Stepper(y, Fmt("ui_amount_n", _stallQty), () => { _stallQty--; RefillPanel(); }, () => { _stallQty++; RefillPanel(); });
            y += 66;
            Stepper(y, Fmt("ui_unit_price", _stallPrice), () => { _stallPrice -= Mathf.Max(1, basePrice / 4); RefillPanel(); },
                () => { _stallPrice += Mathf.Max(1, basePrice / 4); RefillPanel(); });
            y += 74;
            var sell = Button(_panelBody, new Vector2(0.5f, 1), new Vector2(0, -y), new Vector2(420, 60), Green, ListOffer);
            FitLabel(sell, Fmt("ui_put_on_sale", _stallQty * _stallPrice), 24);
        }

        void StepItem(List<string> items, int dir)
        {
            int i = items.IndexOf(_stallItem);
            _stallItem = items[(i + dir + items.Count) % items.Count];
            _stallQty = 1; _stallPrice = 0;
            RefillPanel();
        }

        /// <summary>A centred value with a minus and a plus button.</summary>
        void Stepper(float y, string value, System.Action minus, System.Action plus)
        {
            var row = Row(y, 56, RowColor);
            var m = Button(row, new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(70, 44), new Color(0.88f, 0.84f, 0.76f), Texts.Rtl ? plus : minus);
            Label(m, Texts.Rtl ? "+" : "-", 30, Ink, TextAnchor.MiddleCenter).font = _font;
            var p = Button(row, new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(70, 44), new Color(0.88f, 0.84f, 0.76f), Texts.Rtl ? minus : plus);
            Label(p, Texts.Rtl ? "-" : "+", 30, Ink, TextAnchor.MiddleCenter).font = _font;
            Label(row, value, 24, Ink, TextAnchor.MiddleCenter);
        }

        void ListOffer()
        {
            if (_socialBusy) return;
            _socialBusy = true;
            string json = "{\"p_item\":" + Backend.Str(_stallItem) + ",\"p_qty\":" + _stallQty + ",\"p_unit_price\":" + _stallPrice + "}";
            Backend.I.Rpc("hf_market_list", json, r =>
            {
                _socialBusy = false;
                if (!MarketResult(r)) return;
                _offers = MiniJson.A(r, "offers") ?? _offers;
                Toast(Texts.Get("ui_on_sale"), 2f);
                RefillStall();
            });
        }

        void CancelOffer(long id)
        {
            if (_socialBusy) return;
            _socialBusy = true;
            Backend.I.Rpc("hf_market_cancel", "{\"p_id\":" + id + "}", r =>
            {
                _socialBusy = false;
                if (!MarketResult(r)) return;
                _offers = MiniJson.A(r, "offers") ?? _offers;
                RefillStall();
            });
        }

        /// <summary>Shows the error of a market call, or applies the new state the server returned.</summary>
        bool MarketResult(Dictionary<string, object> r)
        {
            if (r == null) { Toast(Texts.Get("ui_try_again"), 2f); return false; }
            if (!MiniJson.B(r, "ok"))
            {
                Toast(Texts.Get(MiniJson.S(r, "error") switch
                {
                    "no_coins" => "ui_not_enough_coins",
                    "storage_full" => "ui_storage_full",
                    "gone" => "ui_offer_gone",
                    "full" => "ui_stall_full",
                    "missing_items" => "ui_order_missing",
                    _ => "ui_try_again",
                }), 2.5f);
                return false;
            }
            var state = MiniJson.O(r, "state");
            if (state != null) Backend.I.ApplyState(state);
            return true;
        }

        // ------------------------------------------------------------------ a friend's stall

        void OpenShop()
        {
            var gs = GameState.I;
            if (!gs.Visiting) return;
            _offers = null;
            OpenPanel(Texts.Get("ui_friend_stall"), new Vector2(820, 480), FillShop);
            _panelKind = "shop";
            LoadOffers(gs.VisitCode);
        }

        void FillShop()
        {
            if (_offers == null)
            {
                Heading(Texts.Get(_offersLoading ? "ui_loading" : "ui_try_again"), 150).alignment = TextAnchor.MiddleCenter;
                return;
            }
            if (_offers.Count == 0) { Heading(Texts.Get("ui_shop_empty"), 150).alignment = TextAnchor.MiddleCenter; return; }
            float y = 0;
            foreach (var o in _offers)
            {
                long id = (long)MiniJson.N(o, "id");
                string item = MiniJson.S(o, "item");
                int qty = (int)MiniJson.N(o, "qty"), total = qty * (int)MiniJson.N(o, "unit_price");
                var row = Row(y, 70, RowColor);
                RowText(row, $"{qty} {Texts.Get(item)}", 24, Ink, 16, 320);
                RowButton(row, Fmt("ui_buy_for", total), 230, GameState.I.Data.coins >= total ? Green : Grey, () => Buy(id, qty, item));
                y += 82;
            }
        }

        void Buy(long id, int qty, string item)
        {
            if (_socialBusy) return;
            _socialBusy = true;
            Backend.I.Rpc("hf_market_buy", "{\"p_id\":" + id + "}", r =>
            {
                _socialBusy = false;
                bool ok = MarketResult(r);
                if (ok) Toast(Fmt("ui_bought", qty, Texts.Get(item)), 2.5f);
                if (GameState.I.Visiting) LoadOffers(GameState.I.VisitCode);   // the offer is gone either way
            });
        }
    }
}
