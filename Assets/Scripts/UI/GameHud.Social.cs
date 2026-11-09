using System.Collections.Generic;
using FarmWorld.Core;
using FarmWorld.Farming;
using UnityEngine;
using UnityEngine.UI;
using Selectable = FarmWorld.World.Selectable;

namespace FarmWorld.UI
{
    /// <summary>
    /// Sprint 6: friends and farm visits (design document, social model). Players add each other with a
    /// friend code; there is no chat and no free text between players. A visit shows the friend's farm
    /// read-only, with one like and one coin gift per friend per day. Everything goes through server RPCs.
    /// </summary>
    public partial class GameHud
    {
        static readonly Color Orange = new(0.93f, 0.55f, 0.2f);
        static readonly Color Red = new(0.85f, 0.35f, 0.3f);

        string _panelKind = "";
        Dictionary<string, object> _friends;
        bool _friendsLoading, _friendsFailed, _socialBusy, _checkedFriends;
        string _codeDraft = "";
        Text _friendsBadge;

        // ------------------------------------------------------------------ HUD

        void BuildFriendsButton()
        {
            var b = Button(_root, new Vector2(1, 1), new Vector2(-254, -16), new Vector2(170, 52), Panel, OpenFriends);
            var head = Img(b, _circle, new Color(0.35f, 0.6f, 0.85f), new Vector2(0, 0.5f), new Vector2(10, 6), new Vector2(18, 18));
            Img(head, _round, new Color(0.35f, 0.6f, 0.85f), new Vector2(0.5f, 0), new Vector2(0, -18), new Vector2(26, 14));
            var head2 = Img(b, _circle, new Color(0.95f, 0.6f, 0.3f), new Vector2(0, 0.5f), new Vector2(30, 6), new Vector2(18, 18));
            Img(head2, _round, new Color(0.95f, 0.6f, 0.3f), new Vector2(0.5f, 0), new Vector2(0, -18), new Vector2(26, 14));
            var t = Label(b, Texts.Get("ui_friends"), 22, Ink, TextAnchor.MiddleLeft, new Vector2(60, 0), new Vector2(106, 52), new Vector2(0, 0.5f));
            t.resizeTextForBestFit = true; t.resizeTextMinSize = 12; t.resizeTextMaxSize = 22;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            _friendsBadge = Badge(b);
            SetBadge(_friendsBadge, IncomingRequests());
        }

        int IncomingRequests()
        {
            int n = 0;
            var list = MiniJson.A(_friends, "friends");
            if (list != null) foreach (var f in list) if (MiniJson.S(f, "state") == "incoming") n++;
            return n;
        }

        /// <summary>Once online: look for friend requests and gifts that arrived while the player was away.</summary>
        void OnBackendStatus(Backend.Status s)
        {
            if (s != Backend.Status.Online || _checkedFriends) return;
            _checkedFriends = true;
            LoadFriends();
        }

        // ------------------------------------------------------------------ friends window

        void OpenFriends()
        {
            OpenPanel(Texts.Get("ui_friends"), new Vector2(880, 600), FillFriends);
            _panelKind = "friends";
            LoadFriends();
        }

        void LoadFriends()
        {
            var be = Backend.I;
            if (be == null || !be.Enabled) { _friendsFailed = true; RefillFriends(); return; }
            if (_friendsLoading) return;
            _friendsLoading = true;
            RefillFriends();
            be.Rpc("hf_friends", "{}", r =>
            {
                _friendsLoading = false;
                _friendsFailed = r == null || !MiniJson.B(r, "ok");
                if (!_friendsFailed)
                {
                    _friends = r;
                    int gifts = (int)MiniJson.N(r, "new_gifts"), likes = (int)MiniJson.N(r, "new_likes");
                    if (gifts > 0) { Toast(Fmt("ui_got_gifts", gifts), 3f); be.Refresh(); }
                    else if (likes > 0) Toast(Fmt("ui_got_likes", likes), 3f);
                }
                SetBadge(_friendsBadge, IncomingRequests());
                RefillFriends();
            });
        }

        void RefillFriends() { if (_panel != null && _panelKind == "friends") RefillPanel(); }

        void FillFriends()
        {
            float w = _panelBody.sizeDelta.x;
            bool rtl = Texts.Rtl;
            if (_friends == null)
            {
                string msg = _friendsLoading ? Texts.Get("ui_loading") : Texts.Get(_friendsFailed ? "ui_online_only" : "ui_loading");
                Heading(msg, 180).alignment = TextAnchor.MiddleCenter;
                return;
            }

            // own code (Latin letters, so it gets its own label in the Latin font)
            var mine = Row(0, 74, RowColor);
            RowText(mine, Texts.Get("ui_my_code"), 22, Ink, 16, 300);
            var code = Label(mine, MiniJson.S(_friends, "code"), 34, Have, TextAnchor.MiddleCenter,
                new Vector2(rtl ? -40 : 40, 0), new Vector2(240, 74), new Vector2(0.5f, 0.5f));
            code.font = _font;
            RowButton(mine, Texts.Get("ui_my_stall"), 200, Orange, OpenStall);
            Heading(Texts.Get("ui_code_hint"), 80).fontSize = 18;

            // add by code
            var add = Row(120, 74, RowColor);
            BuildCodeInput(add, rtl);
            RowButton(add, Texts.Get("ui_add"), 180, Green, SendRequest);

            // list
            var list = MiniJson.A(_friends, "friends") ?? new List<object>();
            float top = 210, height = _panelBody.sizeDelta.y - top;
            if (list.Count == 0)
            {
                Heading(Texts.Get("ui_no_friends"), top + 40).alignment = TextAnchor.MiddleCenter;
                return;
            }
            var content = ScrollArea(top, height, list.Count * 84);
            float y = 0;
            foreach (var f in list)
            {
                FriendRow(content, f as Dictionary<string, object>, y, w);
                y += 84;
            }
        }

        void BuildCodeInput(RectTransform row, bool rtl)
        {
            var box = Img(row, _round, new Color(0.96f, 0.93f, 0.86f), new Vector2(rtl ? 1 : 0, 0.5f),
                new Vector2(rtl ? -12 : 12, 0), new Vector2(380, 54));
            var text = Label(box, "", 30, Ink, TextAnchor.MiddleCenter);
            text.font = _font;
            text.raycastTarget = true;
            var hint = Label(box, Texts.Get("ui_code_placeholder"), 22, Muted, TextAnchor.MiddleCenter);
            var input = box.gameObject.AddComponent<InputField>();
            input.textComponent = text;
            input.placeholder = hint;
            input.characterLimit = 6;
            input.characterValidation = InputField.CharacterValidation.Alphanumeric;
            input.onValidateInput = (s, i, c) => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '\0';
            input.text = _codeDraft;
            input.onValueChanged.AddListener(v => _codeDraft = v);
            input.onEndEdit.AddListener(v => { _codeDraft = v; });
        }

        RectTransform ScrollArea(float top, float height, float contentHeight)
        {
            var view = new GameObject("Scroll", typeof(RectTransform)).GetComponent<RectTransform>();
            view.SetParent(_panelBody, false);
            view.anchorMin = view.anchorMax = new Vector2(0.5f, 1); view.pivot = new Vector2(0.5f, 1);
            view.anchoredPosition = new Vector2(0, -top); view.sizeDelta = new Vector2(_panelBody.sizeDelta.x, height);
            view.gameObject.AddComponent<RectMask2D>();
            var hit = view.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0);   // lets the list be dragged anywhere

            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(view, false);
            content.anchorMin = content.anchorMax = new Vector2(0.5f, 1); content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero; content.sizeDelta = new Vector2(view.sizeDelta.x, contentHeight);

            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = content; scroll.viewport = view;
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;
            return content;
        }

        void FriendRow(RectTransform content, Dictionary<string, object> f, float y, float w)
        {
            if (f == null) return;
            bool rtl = Texts.Rtl;
            string code = MiniJson.S(f, "code"), state = MiniJson.S(f, "state");
            var row = Box(content, "Friend", new Vector2(0.5f, 1), new Vector2(0, -y), new Vector2(w, 74), RowColor);
            RowText(row, PlayerName(MiniJson.S(f, "name")), 22, Ink, 16, 150);
            var codeLabel = RowText(row, code, 20, Muted, 170, 110);
            codeLabel.font = _font;
            RowText(row, Fmt("ui_level_n", (int)MiniJson.N(f, "level", 1)), 18, Muted, 285, 120);

            if (state == "friend")
            {
                RowButton(row, Texts.Get("ui_visit"), 150, Green, () => Visit(code));
                RowButtonAt(row, Texts.Get("ui_remove"), 120, 170, Grey, () => Respond(code, false));
            }
            else if (state == "incoming")
            {
                RowText(row, Texts.Get("ui_wants_friend"), 16, Have, 410, 150);
                RowButton(row, Texts.Get("ui_accept"), 120, Green, () => Respond(code, true));
                RowButtonAt(row, Texts.Get("ui_decline"), 110, 140, Grey, () => Respond(code, false));
            }
            else
            {
                RowText(row, Texts.Get("ui_waiting_reply"), 18, Muted, 410, 190);
                RowButton(row, Texts.Get("ui_cancel"), 130, Grey, () => Respond(code, false));
            }
        }

        /// <summary>A second button in a row, offset pixels from the row's end edge.</summary>
        RectTransform RowButtonAt(RectTransform row, string text, float w, float offset, Color color, System.Action onClick)
        {
            bool rtl = Texts.Rtl;
            var b = Button(row, new Vector2(rtl ? 0 : 1, 0.5f), new Vector2(rtl ? 10 + offset : -10 - offset, 0),
                new Vector2(w, row.sizeDelta.y - 14), color, onClick);
            FitLabel(b, text, 20);
            return b;
        }

        string PlayerName(string name) => string.IsNullOrEmpty(name) || name == "Farmer" ? Texts.Get("ui_farmer") : name;

        void SendRequest()
        {
            string code = (_codeDraft ?? "").Trim().ToUpperInvariant();
            if (code.Length != 6 || _socialBusy) { if (code.Length != 6) ErrorToast(Texts.Get("ui_code_not_found"), 2f); return; }
            _socialBusy = true;
            Backend.I.Rpc("hf_friend_request", "{\"p_code\":" + Backend.Str(code) + "}", r =>
            {
                _socialBusy = false;
                if (r == null) { ErrorToast(Texts.Get("ui_try_again"), 2f); return; }
                if (MiniJson.B(r, "ok"))
                {
                    _codeDraft = "";
                    Toast(Texts.Get(MiniJson.B(r, "accepted") ? "ui_friend_added" : "ui_request_sent"), 2.5f);
                    LoadFriends();
                    return;
                }
                Toast(Texts.Get(MiniJson.S(r, "error") switch
                {
                    "not_found" or "invalid" => "ui_code_not_found",
                    "already" => "ui_already_sent",
                    "full" => "ui_friends_full",
                    _ => "ui_try_again",
                }), 2.5f);
            });
        }

        void Respond(string code, bool accept)
        {
            if (_socialBusy) return;
            _socialBusy = true;
            Backend.I.Rpc("hf_friend_respond", "{\"p_code\":" + Backend.Str(code) + ",\"p_accept\":" + (accept ? "true" : "false") + "}", r =>
            {
                _socialBusy = false;
                if (r == null || !MiniJson.B(r, "ok")) { ErrorToast(Texts.Get("ui_try_again"), 2f); return; }
                if (accept) Toast(Texts.Get("ui_friend_added"), 2.5f);
                LoadFriends();
            });
        }

        // ------------------------------------------------------------------ visits

        void Visit(string code)
        {
            if (_socialBusy) return;
            _socialBusy = true;
            Backend.I.Rpc("hf_visit", "{\"p_code\":" + Backend.Str(code) + "}", r =>
            {
                _socialBusy = false;
                if (r == null || !MiniJson.B(r, "ok")) { ErrorToast(Texts.Get("ui_try_again"), 2f); return; }
                ClosePanel();
                GameState.I.BeginVisit(r);
                RebuildUi();
                Toast(Texts.Get("ui_look_only"), 3f);
            });
        }

        void GoHome()
        {
            GameState.I.EndVisit();
            RebuildUi();
            LoadFriends();
        }

        void RebuildUi() => OnLanguageChanged();

        void BuildVisitBar()
        {
            var gs = GameState.I;
            bool rtl = Texts.Rtl;
            var bar = Box(_root, "VisitBar", new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(1180, 92), Panel);
            string name = gs.VisitName;
            string titleText = string.IsNullOrEmpty(name) || name == "Farmer" ? Texts.Get("ui_friend_farm") : Fmt("ui_farm_of", name);
            var title = Label(bar, titleText, 26, Ink, rtl ? TextAnchor.UpperRight : TextAnchor.UpperLeft,
                new Vector2(rtl ? -20 : 20, -6), new Vector2(360, 40), new Vector2(rtl ? 1 : 0, 1));
            title.horizontalOverflow = HorizontalWrapMode.Wrap; title.resizeTextForBestFit = true; title.resizeTextMinSize = 14; title.resizeTextMaxSize = 26;
            // second line: level, likes and the friend code, each in its own label so Arabic and Latin never mix
            float lx = 20;
            foreach (var (text, latin, width) in new[] {
                (Fmt("ui_level_n", gs.VisitLevel), false, 130f), (Fmt("ui_likes_n", gs.VisitLikes), false, 130f), (gs.VisitCode, true, 120f) })
            {
                var l = Label(bar, text, 18, Muted, rtl ? TextAnchor.LowerRight : TextAnchor.LowerLeft,
                    new Vector2(rtl ? -lx : lx, 8), new Vector2(width, 30), new Vector2(rtl ? 1 : 0, 0));
                if (latin) l.font = _font;
                lx += width;
            }

            // buttons from the far edge: Home, Gift, Like
            float x = 14;
            VisitButton(bar, Texts.Get("ui_home"), 190, ref x, Orange, GoHome);
            VisitButton(bar, gs.VisitGifted ? Texts.Get("ui_gifted") : Fmt("ui_gift", GameConfig.GiftCoins), 200, ref x,
                gs.VisitGifted ? Grey : Gold, () => Social("gift"));
            VisitButton(bar, gs.VisitLiked ? Texts.Get("ui_liked") : Texts.Get("ui_like"), 160, ref x,
                gs.VisitLiked ? Grey : Red, () => Social("like"));
            VisitButton(bar, Texts.Get("ui_shop"), 170, ref x, Green, OpenShop);
        }

        void VisitButton(RectTransform bar, string text, float w, ref float x, Color color, System.Action onClick)
        {
            bool rtl = Texts.Rtl;
            var b = Button(bar, new Vector2(rtl ? 0 : 1, 0.5f), new Vector2(rtl ? x : -x, 0), new Vector2(w, 66), color, onClick);
            FitLabel(b, text, 24);
            x += w + 14;
        }

        void Social(string kind)
        {
            var gs = GameState.I;
            if (!gs.Visiting || _socialBusy) return;
            if ((kind == "like" && gs.VisitLiked) || (kind == "gift" && gs.VisitGifted)) return;
            _socialBusy = true;
            Backend.I.Rpc("hf_social", "{\"p_code\":" + Backend.Str(gs.VisitCode) + ",\"p_kind\":" + Backend.Str(kind) + "}", r =>
            {
                _socialBusy = false;
                if (!gs.Visiting) return;
                if (r == null) { ErrorToast(Texts.Get("ui_try_again"), 2f); return; }
                string err = MiniJson.S(r, "error");
                bool ok = MiniJson.B(r, "ok");
                if (ok || err == "already")
                {
                    if (kind == "like") { gs.VisitLiked = true; if (ok) gs.VisitLikes++; }
                    else gs.VisitGifted = true;
                    RebuildUi();
                    if (ok) Toast(Texts.Get(kind == "like" ? "ui_liked" : "ui_gifted"), 2f);
                    return;
                }
                Toast(Texts.Get(err == "full" ? "ui_gift_full" : "ui_try_again"), 2.5f);
            });
        }

        /// <summary>Taps while visiting only show information; nothing on a friend's farm can be changed.</summary>
        void VisitTap(Selectable s)
        {
            var gs = GameState.I;
            var plot = s.GetComponent<FieldPlot>();
            if (plot != null)
            {
                var save = gs.Plot(plot.Index);
                if (save == null || string.IsNullOrEmpty(save.crop)) return;
                long left = gs.SecondsLeft(plot.Index);
                Toast(left > 0 ? Fmt("ui_growing", Texts.Get(save.crop), Duration(left)) : Texts.Get(save.crop), 2f);
                return;
            }
            var drop = s.GetComponent<ProductDrop>();
            var animal = drop != null && drop.Unit != null ? drop.Unit : s.GetComponent<AnimalUnit>();
            var sel = animal != null ? animal.GetComponent<Selectable>() : s;
            Set(_selection, Texts.Get(sel.TitleKey));
            _selectionUntil = Time.time + 3f;
        }
    }
}
