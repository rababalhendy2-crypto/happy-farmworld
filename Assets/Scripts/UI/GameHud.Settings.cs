using FarmWorld.Core;
using UnityEngine;
using UnityEngine.UI;

namespace FarmWorld.UI
{
    /// <summary>
    /// Sprint 5 (rest): settings window. A guest account can be saved with a parent's email and a password, so
    /// the farm can be loaded on another phone; "Delete my data" removes the account and every game record on
    /// the server (required for app stores and for a children's game).
    /// </summary>
    public partial class GameHud
    {
        string _email = "", _password = "", _accountEmail, _pendingEmail;
        bool _accountLoaded, _confirmDelete, _signInMode;

        void BuildSettingsButton()
        {
            var b = Button(_root, new Vector2(1, 1), new Vector2(-436, -16), new Vector2(60, 52), Panel, OpenSettings);
            var gear = Img(b, _circle, new Color(0.45f, 0.4f, 0.35f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(32, 32));
            for (int i = 0; i < 4; i++)
            {
                var tooth = Img(gear, _round, new Color(0.45f, 0.4f, 0.35f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(42, 10));
                tooth.localRotation = Quaternion.Euler(0, 0, i * 45);
            }
            Img(gear, _circle, Panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14, 14)).SetAsLastSibling();
        }

        void OpenSettings()
        {
            _confirmDelete = false; _signInMode = false; _accountLoaded = false;
            OpenPanel(Texts.Get("ui_settings"), new Vector2(860, 640), FillSettings);
            _panelKind = "settings";
            var be = Backend.I;
            if (be == null || !be.Enabled) return;
            be.GetAccount((email, pending) =>
            {
                _accountEmail = email; _pendingEmail = pending; _accountLoaded = email != null;
                if (_panel != null && _panelKind == "settings") RefillPanel();
            });
        }

        void FillSettings()
        {
            var be = Backend.I;
            float y = 0;

            // sound and music switches work without internet
            var sound = Row(y, 60, RowColor);
            RowText(sound, Texts.Get("ui_sound"), 22, Ink, 16, 140);
            var sfx = Button(sound, new Vector2(Texts.Rtl ? 1 : 0, 0.5f), new Vector2(Texts.Rtl ? -170 : 170, 0), new Vector2(210, 46),
                Sound.SfxOn ? Green : Grey, () => { Sound.SetSfx(!Sound.SfxOn); RefillPanel(); });
            FitLabel(sfx, Texts.Get(Sound.SfxOn ? "ui_effects_on" : "ui_effects_off"), 20);
            var mus = Button(sound, new Vector2(Texts.Rtl ? 1 : 0, 0.5f), new Vector2(Texts.Rtl ? -400 : 400, 0), new Vector2(210, 46),
                Sound.MusicOn ? Green : Grey, () => { Sound.SetMusic(!Sound.MusicOn); RefillPanel(); });
            FitLabel(mus, Texts.Get(Sound.MusicOn ? "ui_music_on" : "ui_music_off"), 20);
            var help = Button(sound, new Vector2(Texts.Rtl ? 1 : 0, 0.5f), new Vector2(Texts.Rtl ? -630 : 630, 0), new Vector2(190, 46),
                new Color(0.88f, 0.84f, 0.76f), RestartTutorial);
            FitLabel(help, Texts.Get("ui_help_again"), 18).color = Ink;
            y += 72;

            if (be == null || !be.Enabled || !_accountLoaded)
            {
                Heading(Texts.Get(be != null && be.Enabled && be.State == Backend.Status.Connecting ? "ui_loading" : "ui_online_only"), y + 100)
                    .alignment = TextAnchor.MiddleCenter;
                return;
            }

            // account status
            var status = Row(y, 64, RowColor);
            if (!string.IsNullOrEmpty(_accountEmail))
            {
                RowText(status, Texts.Get("ui_account_saved"), 22, Have, 16, 300);
                RowText(status, _accountEmail, 20, Muted, 320, 440).font = _font;
            }
            else if (!string.IsNullOrEmpty(_pendingEmail))
            {
                RowText(status, Texts.Get("ui_check_email"), 20, Have, 16, 420);
                RowText(status, _pendingEmail, 20, Muted, 440, 340).font = _font;
            }
            else
            {
                var g = RowText(status, Texts.Get("ui_guest_account"), 20, Ink, 16, 780);
                g.horizontalOverflow = HorizontalWrapMode.Wrap; g.resizeTextForBestFit = true; g.resizeTextMinSize = 12; g.resizeTextMaxSize = 20;
            }
            y += 76;

            if (string.IsNullOrEmpty(_accountEmail))
            {
                Note(Texts.Get(_signInMode ? "ui_sign_in_title" : "ui_save_account_title"), y, 20);
                y += 40;
                InputRow(y, Texts.Get("ui_parent_email"), _email, v => _email = v, InputField.ContentType.EmailAddress);
                y += 66;
                InputRow(y, Texts.Get("ui_password"), _password, v => _password = v, InputField.ContentType.Password);
                y += 70;
                var go = Button(_panelBody, new Vector2(0.5f, 1), new Vector2(Texts.Rtl ? 140 : -140, -y), new Vector2(380, 56), Green,
                    _signInMode ? SignIn : SaveAccount);
                FitLabel(go, Texts.Get(_signInMode ? "ui_sign_in" : "ui_save_account"), 22);
                var other = Button(_panelBody, new Vector2(0.5f, 1), new Vector2(Texts.Rtl ? -210 : 210, -y), new Vector2(300, 56),
                    new Color(0.88f, 0.84f, 0.76f), () => { _signInMode = !_signInMode; RefillPanel(); });
                FitLabel(other, Texts.Get(_signInMode ? "ui_new_account_instead" : "ui_have_account"), 18).color = Ink;
                y += 70;
                if (_signInMode) { Note(Texts.Get("ui_sign_in_warning"), y, 16); y += 30; }
            }

            // delete
            y = Mathf.Max(y + 10, _panelBody.sizeDelta.y - 76);
            var del = Button(_panelBody, new Vector2(0.5f, 1), new Vector2(0, -y), new Vector2(_confirmDelete ? 520 : 300, 56),
                _confirmDelete ? Red : Grey, DeleteData);
            FitLabel(del, Texts.Get(_confirmDelete ? "ui_delete_confirm" : "ui_delete_data"), 20);
        }

        void Note(string text, float y, int size)
        {
            var t = Heading(text, y);
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.resizeTextForBestFit = true; t.resizeTextMinSize = 12; t.resizeTextMaxSize = size;
        }

        void InputRow(float y, string label, string value, System.Action<string> changed, InputField.ContentType type)
        {
            bool rtl = Texts.Rtl;
            var row = Row(y, 56, RowColor);
            RowText(row, label, 20, Ink, 16, 200);
            var box = Img(row, _round, new Color(0.96f, 0.93f, 0.86f), new Vector2(rtl ? 0 : 1, 0.5f), new Vector2(rtl ? 10 : -10, 0), new Vector2(560, 44));
            var text = Label(box, "", 22, Ink, TextAnchor.MiddleLeft);
            text.font = _font;
            text.rectTransform.sizeDelta = new Vector2(-24, 0);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var input = box.gameObject.AddComponent<InputField>();
            input.textComponent = text;
            input.contentType = type;
            input.characterLimit = 80;
            input.text = value;
            input.onValueChanged.AddListener(v => changed(v));
        }

        bool AccountInputOk()
        {
            if (!_email.Contains("@") || !_email.Contains(".") || _email.Length < 6) { Toast(Texts.Get("ui_email_invalid"), 2.5f); return false; }
            if (_password.Length < 8) { Toast(Texts.Get("ui_password_short"), 2.5f); return false; }
            return true;
        }

        void SaveAccount()
        {
            if (_socialBusy || !AccountInputOk()) return;
            _socialBusy = true;
            Backend.I.LinkEmail(_email.Trim(), _password, (ok, msg) =>
            {
                _socialBusy = false;
                if (!ok) { Toast(Texts.Get(msg.Contains("registered") || msg.Contains("exists") ? "ui_email_taken" : "ui_try_again"), 3f); return; }
                _pendingEmail = _email.Trim();
                _password = "";
                Toast(Texts.Get("ui_check_email"), 3.5f);
                if (_panel != null && _panelKind == "settings") RefillPanel();
            });
        }

        void SignIn()
        {
            if (_socialBusy || !AccountInputOk()) return;
            _socialBusy = true;
            Backend.I.SignIn(_email.Trim(), _password, ok =>
            {
                _socialBusy = false;
                if (!ok) { ErrorToast(Texts.Get("ui_sign_in_failed"), 3f); return; }
                _password = "";
                ClosePanel();
                _friends = null;
                Toast(Texts.Get("ui_signed_in"), 3f);
            });
        }

        void DeleteData()
        {
            if (!_confirmDelete) { _confirmDelete = true; RefillPanel(); return; }
            if (_socialBusy) return;
            _socialBusy = true;
            Backend.I.DeleteAccount(ok =>
            {
                _socialBusy = false;
                if (!ok) { ErrorToast(Texts.Get("ui_try_again"), 2.5f); return; }
                ClosePanel();
                _friends = null; _accountEmail = null; _pendingEmail = null; _email = ""; _password = "";
                RebuildUi();
                Toast(Texts.Get("ui_data_deleted"), 3.5f);
            });
        }
    }
}
