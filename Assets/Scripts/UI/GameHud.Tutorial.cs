using FarmWorld.Core;
using UnityEngine;
using UnityEngine.UI;

namespace FarmWorld.UI
{
    /// <summary>
    /// First-time help: one short hint at a time under the top bar. Each hint goes away when the player does it
    /// (plant, harvest, feed, collect, deliver an order). Kept on the phone (fw_tut); new farms only, and it can be
    /// started again from the settings.
    /// </summary>
    public partial class GameHud
    {
        const string TutKey = "fw_tut";
        static readonly (string ev, string text)[] TutSteps =
        {
            ("plant", "tut_plant"), ("harvest", "tut_harvest"), ("feed", "tut_feed"), ("collect", "tut_collect"), ("sell", "tut_sell"),
        };
        RectTransform _tutBox;
        Text _tutText;

        static int TutStep
        {
            get => PlayerPrefs.GetInt(TutKey, -1);
            set { PlayerPrefs.SetInt(TutKey, value); PlayerPrefs.Save(); }
        }

        void BuildTutorial()
        {
            var gs = GameState.I;
            if (gs == null || gs.Visiting) return;
            if (TutStep < 0) TutStep = gs.Data.level <= 2 ? 0 : TutSteps.Length;   // existing farms skip it
            _tutBox = Box(_root, "Tutorial", new Vector2(0.5f, 1), new Vector2(0, -150), new Vector2(760, 64), new Color(1f, 0.93f, 0.6f, 0.97f));
            var bulb = Img(_tutBox, _circle, Gold, new Vector2(Texts.Rtl ? 1 : 0, 0.5f), new Vector2(Texts.Rtl ? -12 : 12, 0), new Vector2(40, 40));
            Label(bulb, "!", 26, Color.white, TextAnchor.MiddleCenter).font = _font;
            _tutText = Label(_tutBox, "", 22, Ink, TextAnchor.MiddleCenter, new Vector2(0, 0), new Vector2(600, 60), new Vector2(0.5f, 0.5f));
            _tutText.horizontalOverflow = HorizontalWrapMode.Wrap; _tutText.resizeTextForBestFit = true;
            _tutText.resizeTextMinSize = 14; _tutText.resizeTextMaxSize = 22;
            var skip = Button(_tutBox, new Vector2(Texts.Rtl ? 0 : 1, 0.5f), new Vector2(Texts.Rtl ? 10 : -10, 0), new Vector2(44, 44),
                new Color(0.85f, 0.78f, 0.55f), () => { TutStep = TutSteps.Length; RefreshTutorial(); });
            Label(skip, "x", 24, Ink, TextAnchor.MiddleCenter).font = _font;
            gs.Tracked -= OnTracked; gs.Tracked += OnTracked;
            RefreshTutorial();
        }

        void OnTracked(string ev)
        {
            int s = TutStep;
            if (s < 0 || s >= TutSteps.Length || TutSteps[s].ev != ev) return;
            TutStep = s + 1;
            if (TutStep >= TutSteps.Length) Toast(Texts.Get("tut_done"), 3f);
            RefreshTutorial();
        }

        void RefreshTutorial()
        {
            if (_tutBox == null) return;
            int s = TutStep;
            bool show = s >= 0 && s < TutSteps.Length;
            _tutBox.gameObject.SetActive(show);
            if (show) Set(_tutText, Texts.Get(TutSteps[s].text));
        }

        void RestartTutorial()
        {
            TutStep = 0;
            ClosePanel();
            RefreshTutorial();
        }
    }
}
