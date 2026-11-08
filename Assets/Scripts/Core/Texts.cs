using System.Collections.Generic;
using UnityEngine;

namespace FarmWorld.Core
{
    /// <summary>
    /// UI strings in Arabic, German and English (design document: three languages).
    /// Get() returns the plain localized string; Display() prepares any string for a uGUI Text
    /// (Arabic letters joined and laid out right-to-left).
    /// </summary>
    public static class Texts
    {
        public static readonly string[] Languages = { "ar", "de", "en" };
        public static string Lang { get; private set; } = "en";
        public static bool Rtl => Lang == "ar";
        public static event System.Action Changed;

        const string PrefKey = "farmworld_lang";
        static bool _loaded;

        /// <summary>Reads the saved language (or the phone's language). Call once at startup.</summary>
        public static void Init()
        {
            if (_loaded) return;
            _loaded = true;
            string saved = PlayerPrefs.GetString(PrefKey, "");
            if (System.Array.IndexOf(Languages, saved) >= 0) Lang = saved;
            else Lang = Application.systemLanguage switch
            {
                SystemLanguage.German => "de",
                SystemLanguage.English => "en",
                _ => "ar",
            };
        }

        public static void SetLanguage(string lang)
        {
            if (System.Array.IndexOf(Languages, lang) < 0 || lang == Lang) return;
            Lang = lang;
            PlayerPrefs.SetString(PrefKey, lang);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static string NextLanguage()
        {
            int i = System.Array.IndexOf(Languages, Lang);
            return Languages[(i + 1) % Languages.Length];
        }

        /// <summary>Short label for the language button.</summary>
        public static string LanguageLabel(string lang) => lang switch { "ar" => "عربي", "de" => "DE", _ => "EN" };

        public static string Get(string key)
        {
            if (key == null) return "";
            var table = Lang == "ar" ? Ar : Lang == "de" ? De : En;
            if (table.TryGetValue(key, out var v)) return v;
            return En.TryGetValue(key, out var e) ? e : key;
        }

        public static string Format(string key, params object[] args) => string.Format(Get(key), args);

        /// <summary>Prepares text for display: Arabic shaping + right-to-left order when needed.</summary>
        public static string Display(string s) => ArabicText.Fix(s);

        static readonly Dictionary<string, string> En = new()
        {
            { "bld_house", "Farmhouse" }, { "bld_barn", "Barn" }, { "bld_coop", "Chicken coop" },
            { "bld_silo", "Grain silo" }, { "bld_warehouse", "Warehouse" }, { "bld_mill", "Mill" },
            { "bld_dairy", "Dairy" }, { "bld_tools", "Tool workshop" }, { "bld_inn", "Village inn" },
            { "bld_village_house", "Villager's house" }, { "bld_market", "Market stall" },
            { "field", "Field" }, { "animal_cow", "Cow" }, { "animal_sheep", "Sheep" }, { "animal_chicken", "Chicken" },
            { "zone_village", "Village" }, { "zone_market", "Market" }, { "zone_forest_lake", "Forest & lake" },
            { "zone_production", "Production area" }, { "zone_expansion", "Locked land – unlocks as you level up" },
            { "wheat", "Wheat" }, { "carrot", "Carrot" }, { "tomato", "Tomato" }, { "corn", "Corn" }, { "pumpkin", "Pumpkin" },
            { "strawberry", "Strawberry" }, { "egg", "Egg" }, { "milk", "Milk" }, { "wool", "Wool" },
            { "ui_coins", "coins" },
            { "ui_choose_seed", "Choose a seed to plant" }, { "ui_locked", "Level {0}" },
            { "ui_storage", "Storage" }, { "ui_storage_count", "Storage {0}/{1}" }, { "ui_sell_all", "Sell all" },
            { "ui_empty_storage", "Storage is empty. Harvest crops to fill it." },
            { "ui_not_enough_coins", "Not enough coins" }, { "ui_storage_full", "Storage is full – sell something first" },
            { "ui_growing", "{0} – ready in {1}" }, { "ui_harvested", "+{0} {1}" },
            { "ui_sold", "+{0} coins" }, { "ui_level_up", "Level {0}! +{1} coins" }, { "ui_needs_level", "Unlocks at level {0}" },
            { "ui_fed", "{0} fed – {1} in {2}" }, { "ui_needs_feed", "{0} needs {1} {2} – grow some first" },
            { "ui_producing", "{0} – {1} in {2} · happiness {3}%" },
        };

        static readonly Dictionary<string, string> De = new()
        {
            { "bld_house", "Bauernhaus" }, { "bld_barn", "Scheune" }, { "bld_coop", "Hühnerstall" },
            { "bld_silo", "Getreidesilo" }, { "bld_warehouse", "Lager" }, { "bld_mill", "Mühle" },
            { "bld_dairy", "Molkerei" }, { "bld_tools", "Werkzeugschmiede" }, { "bld_inn", "Dorfgasthaus" },
            { "bld_village_house", "Haus im Dorf" }, { "bld_market", "Marktstand" },
            { "field", "Feld" }, { "animal_cow", "Kuh" }, { "animal_sheep", "Schaf" }, { "animal_chicken", "Huhn" },
            { "zone_village", "Dorf" }, { "zone_market", "Markt" }, { "zone_forest_lake", "Wald & See" },
            { "zone_production", "Produktion" }, { "zone_expansion", "Gesperrtes Land – wird mit höherem Level frei" },
            { "wheat", "Weizen" }, { "carrot", "Karotte" }, { "tomato", "Tomate" }, { "corn", "Mais" }, { "pumpkin", "Kürbis" },
            { "strawberry", "Erdbeere" }, { "egg", "Ei" }, { "milk", "Milch" }, { "wool", "Wolle" },
            { "ui_coins", "Münzen" },
            { "ui_choose_seed", "Wähle Saatgut zum Pflanzen" }, { "ui_locked", "Level {0}" },
            { "ui_storage", "Lager" }, { "ui_storage_count", "Lager {0}/{1}" }, { "ui_sell_all", "Alles verkaufen" },
            { "ui_empty_storage", "Das Lager ist leer. Ernte etwas, um es zu füllen." },
            { "ui_not_enough_coins", "Nicht genug Münzen" }, { "ui_storage_full", "Lager voll – verkaufe zuerst etwas" },
            { "ui_growing", "{0} – fertig in {1}" }, { "ui_harvested", "+{0} {1}" },
            { "ui_sold", "+{0} Münzen" }, { "ui_level_up", "Level {0}! +{1} Münzen" }, { "ui_needs_level", "Ab Level {0}" },
            { "ui_fed", "{0} gefüttert – {1} in {2}" }, { "ui_needs_feed", "{0} braucht {1} {2} – baue zuerst etwas an" },
            { "ui_producing", "{0} – {1} in {2} · Zufriedenheit {3}%" },
        };

        static readonly Dictionary<string, string> Ar = new()
        {
            { "bld_house", "بيت المزرعة" }, { "bld_barn", "الحظيرة" }, { "bld_coop", "قن الدجاج" },
            { "bld_silo", "صومعة الحبوب" }, { "bld_warehouse", "المستودع" }, { "bld_mill", "الطاحونة" },
            { "bld_dairy", "معمل الألبان" }, { "bld_tools", "ورشة الأدوات" }, { "bld_inn", "نزل القرية" },
            { "bld_village_house", "بيت في القرية" }, { "bld_market", "كشك السوق" },
            { "field", "حقل" }, { "animal_cow", "بقرة" }, { "animal_sheep", "خروف" }, { "animal_chicken", "دجاجة" },
            { "zone_village", "القرية" }, { "zone_market", "السوق" }, { "zone_forest_lake", "الغابة والبحيرة" },
            { "zone_production", "منطقة الإنتاج" }, { "zone_expansion", "أرض مقفلة – تُفتح مع ارتفاع مستواك" },
            { "wheat", "قمح" }, { "carrot", "جزر" }, { "tomato", "طماطم" }, { "corn", "ذرة" }, { "pumpkin", "يقطين" },
            { "strawberry", "فراولة" }, { "egg", "بيض" }, { "milk", "حليب" }, { "wool", "صوف" },
            { "ui_coins", "عملة" },
            { "ui_choose_seed", "اختاري بذرة للزراعة" }, { "ui_locked", "المستوى {0}" },
            { "ui_storage", "المخزن" }, { "ui_storage_count", "المخزن {0}/{1}" }, { "ui_sell_all", "بيع الكل" },
            { "ui_empty_storage", "المخزن فارغ. احصدي محاصيل لملئه." },
            { "ui_not_enough_coins", "العملات لا تكفي" }, { "ui_storage_full", "المخزن ممتلئ – بيعي شيئًا أولًا" },
            { "ui_growing", "{0} – يجهز بعد {1}" }, { "ui_harvested", "+{0} {1}" },
            { "ui_sold", "+{0} عملة" }, { "ui_level_up", "المستوى {0}! +{1} عملة" }, { "ui_needs_level", "يُفتح في المستوى {0}" },
            { "ui_fed", "أطعمتِ {0} – {1} بعد {2}" }, { "ui_needs_feed", "{0} تحتاج {1} {2} – ازرعي أولًا" },
            { "ui_producing", "{0} – {1} بعد {2} · السعادة {3}%" },
        };
    }
}
