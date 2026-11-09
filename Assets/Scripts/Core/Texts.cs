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
            { "ui_sound", "Sound" }, { "ui_effects_on", "Effects: on" }, { "ui_effects_off", "Effects: off" }, { "ui_music_on", "Music: on" }, { "ui_music_off", "Music: off" },
            { "ui_settings", "Settings" }, { "ui_account_saved", "Account saved" }, { "ui_check_email", "Open the email and confirm it" },
            { "ui_guest_account", "Guest account: this farm is only on this phone and our server" },
            { "ui_save_account_title", "Save the farm with a parent's email, to play on another phone" }, { "ui_sign_in_title", "Load a saved farm" },
            { "ui_parent_email", "Parent's email" }, { "ui_password", "Password" }, { "ui_save_account", "Save account" }, { "ui_sign_in", "Load farm" },
            { "ui_have_account", "I already have one" }, { "ui_new_account_instead", "Back" },
            { "ui_sign_in_warning", "The farm on this phone will be replaced by the saved farm" },
            { "ui_delete_data", "Delete my data" }, { "ui_delete_confirm", "Yes, delete my farm and account for ever" },
            { "ui_email_invalid", "This email does not look right" }, { "ui_password_short", "The password needs at least 8 characters" },
            { "ui_email_taken", "This email already has an account. Load it instead." }, { "ui_sign_in_failed", "Email or password is wrong, or the email is not confirmed yet" },
            { "ui_signed_in", "Your saved farm is loading" }, { "ui_data_deleted", "Everything was deleted. A new farm starts." },
            { "ui_my_stall", "My stall" }, { "ui_stall_hint", "Friends can buy these when they visit you" }, { "ui_coins_n", "{0} coins" }, { "ui_ready_in", "Ready in" }, { "ui_finish_now", "Finish now" }, { "ui_gems_have", "You have {0} gems. Gems come from the daily gift and new levels." }, { "ui_not_enough_gems", "Not enough gems" }, { "ui_daily_gift", "Daily gift" }, { "ui_day_n", "Day {0}" }, { "ui_collect_gift", "Collect today's gift" }, { "ui_gift_hint", "A new gift every day. Day 7 has gems!" }, { "ui_gift_tomorrow", "Come back tomorrow for the next gift" }, { "ui_gift_got", "Gift: {0} coins" }, { "ui_gift_got_gems", "Gift: {0} coins and {1} gems" },
            { "ui_stall_full", "Your stall is full (4 offers)" }, { "ui_stall_empty", "Your storage is empty" }, { "ui_new_offer", "New offer" },
            { "ui_amount_n", "Amount {0}" }, { "ui_unit_price", "{0} coins each" }, { "ui_put_on_sale", "Put on sale for {0} coins" },
            { "ui_on_sale", "Your offer is in the stall" }, { "ui_offer_gone", "Someone else bought it first" }, { "ui_friend_stall", "Friend's stall" },
            { "ui_shop_empty", "Nothing for sale right now" }, { "ui_buy_for", "Buy for {0} coins" }, { "ui_bought", "+{0} {1}" }, { "ui_shop", "Stall" },
            { "ui_friends", "Friends" }, { "ui_my_code", "Your friend code" }, { "ui_code_hint", "Share it only with people you know" },
            { "ui_code_placeholder", "Friend code" }, { "ui_add", "Add" }, { "ui_visit", "Visit" }, { "ui_accept", "Accept" }, { "ui_decline", "Decline" },
            { "ui_cancel", "Cancel" }, { "ui_waiting_reply", "Waiting for answer" }, { "ui_wants_friend", "Wants to be friends" },
            { "ui_no_friends", "No friends yet. Share your code!" }, { "ui_online_only", "Friends need an internet connection" },
            { "ui_loading", "Loading..." }, { "ui_request_sent", "Friend request sent" }, { "ui_friend_added", "You are now friends!" },
            { "ui_code_not_found", "No farm has this code" }, { "ui_already_sent", "Already sent" }, { "ui_friends_full", "The friend list is full" },
            { "ui_farm_of", "Farm of {0}" }, { "ui_friend_farm", "Your friend's farm" }, { "ui_level_n", "Level {0}" }, { "ui_likes_n", "{0} likes" }, { "ui_like", "Like" }, { "ui_liked", "Liked" },
            { "ui_gift", "Gift {0} coins" }, { "ui_gifted", "Gift sent" }, { "ui_home", "Back home" }, { "ui_look_only", "This is your friend's farm. You can look around." },
            { "ui_got_gifts", "Friends sent you {0} gifts!" }, { "ui_got_likes", "Friends liked your farm {0} times" }, { "ui_gift_full", "Your friend got enough gifts today" },
            { "ui_farmer", "Farmer" }, { "ui_try_again", "That did not work. Try again." }, { "ui_remove", "Remove" },
            { "ui_order_wait", "New order in {0}" }, { "ui_order_coming", "New order coming..." }, { "ui_order_missing", "Not enough goods for this order" },
            { "ui_workshops", "Workshops" }, { "ui_go", "Open" }, { "ui_ready_count", "{0} ready" }, { "ui_working", "Working" },

            { "flour", "Flour" }, { "bread", "Bread" }, { "pumpkin_pie", "Pumpkin pie" }, { "cheese", "Cheese" },
            { "ui_market", "Market" }, { "ui_orders", "Market orders" }, { "ui_deliver", "Deliver" }, { "ui_new_order", "New order" },
            { "ui_reward", "{0} coins + {1} XP" }, { "ui_delivered", "Order delivered! +{0} coins" },
            { "ui_missions", "Missions" }, { "ui_daily", "Today" }, { "ui_weekly", "This week" }, { "ui_claim", "Claim" },
            { "ui_claimed", "Done" }, { "ui_mission_reward", "+{0} coins" },
            { "m_plant", "Plant {0} fields" }, { "m_harvest", "Harvest {0} crops" }, { "m_harvest_item", "Harvest {1}: {0}" },
            { "m_feed", "Feed animals {0} times" }, { "m_collect", "Collect {0} animal products" }, { "m_collect_item", "Collect {1}: {0}" },
            { "m_sell", "Sell {0} items" }, { "m_produce", "Make {0} products in workshops" },
            { "ui_open_for", "Open for {0} coins" }, { "ui_workshop_locked", "Opens at level {0}" }, { "ui_opened", "{0} is open!" },
            { "ui_start", "Make" }, { "ui_collect", "Collect" }, { "ui_slots", "Working {0}/{1}" }, { "ui_busy", "All places are busy – collect first" },
            { "ui_missing", "Missing ingredients" }, { "ui_made", "+{0} {1}" },
            { "ui_upgrade_storage", "Bigger storage: {0} places – {1} coins" }, { "ui_storage_upgraded", "Storage now holds {0}" },

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
            { "ui_sound", "Ton" }, { "ui_effects_on", "Effekte: an" }, { "ui_effects_off", "Effekte: aus" }, { "ui_music_on", "Musik: an" }, { "ui_music_off", "Musik: aus" },
            { "ui_settings", "Einstellungen" }, { "ui_account_saved", "Konto gesichert" }, { "ui_check_email", "Öffne die E-Mail und bestätige sie" },
            { "ui_guest_account", "Gastkonto: diese Farm gibt es nur auf diesem Handy und unserem Server" },
            { "ui_save_account_title", "Farm mit der E-Mail eines Elternteils sichern, um auf einem anderen Handy zu spielen" }, { "ui_sign_in_title", "Gesicherte Farm laden" },
            { "ui_parent_email", "E-Mail der Eltern" }, { "ui_password", "Passwort" }, { "ui_save_account", "Konto sichern" }, { "ui_sign_in", "Farm laden" },
            { "ui_have_account", "Ich habe schon eins" }, { "ui_new_account_instead", "Zurück" },
            { "ui_sign_in_warning", "Die Farm auf diesem Handy wird durch die gesicherte ersetzt" },
            { "ui_delete_data", "Meine Daten löschen" }, { "ui_delete_confirm", "Ja, Farm und Konto für immer löschen" },
            { "ui_email_invalid", "Diese E-Mail sieht nicht richtig aus" }, { "ui_password_short", "Das Passwort braucht mindestens 8 Zeichen" },
            { "ui_email_taken", "Diese E-Mail hat schon ein Konto. Lade es stattdessen." }, { "ui_sign_in_failed", "E-Mail oder Passwort falsch, oder E-Mail noch nicht bestätigt" },
            { "ui_signed_in", "Deine gesicherte Farm wird geladen" }, { "ui_data_deleted", "Alles wurde gelöscht. Eine neue Farm beginnt." },
            { "ui_my_stall", "Mein Stand" }, { "ui_stall_hint", "Freunde können das kaufen, wenn sie dich besuchen" }, { "ui_coins_n", "{0} Münzen" }, { "ui_ready_in", "Fertig in" }, { "ui_finish_now", "Sofort fertig" }, { "ui_gems_have", "Du hast {0} Edelsteine. Es gibt sie beim Tagesgeschenk und bei neuen Stufen." }, { "ui_not_enough_gems", "Nicht genug Edelsteine" }, { "ui_daily_gift", "Tagesgeschenk" }, { "ui_day_n", "Tag {0}" }, { "ui_collect_gift", "Heutiges Geschenk abholen" }, { "ui_gift_hint", "Jeden Tag ein neues Geschenk. Am 7. Tag gibt es Edelsteine!" }, { "ui_gift_tomorrow", "Komm morgen für das nächste Geschenk wieder" }, { "ui_gift_got", "Geschenk: {0} Münzen" }, { "ui_gift_got_gems", "Geschenk: {0} Münzen und {1} Edelsteine" },
            { "ui_stall_full", "Dein Stand ist voll (4 Angebote)" }, { "ui_stall_empty", "Dein Lager ist leer" }, { "ui_new_offer", "Neues Angebot" },
            { "ui_amount_n", "Menge {0}" }, { "ui_unit_price", "{0} Münzen pro Stück" }, { "ui_put_on_sale", "Für {0} Münzen anbieten" },
            { "ui_on_sale", "Dein Angebot liegt im Stand" }, { "ui_offer_gone", "Jemand anderes war schneller" }, { "ui_friend_stall", "Stand des Freundes" },
            { "ui_shop_empty", "Gerade gibt es nichts zu kaufen" }, { "ui_buy_for", "Für {0} Münzen kaufen" }, { "ui_bought", "+{0} {1}" }, { "ui_shop", "Stand" },
            { "ui_friends", "Freunde" }, { "ui_my_code", "Dein Freundescode" }, { "ui_code_hint", "Gib ihn nur Leuten, die du kennst" },
            { "ui_code_placeholder", "Freundescode" }, { "ui_add", "Hinzufügen" }, { "ui_visit", "Besuchen" }, { "ui_accept", "Annehmen" }, { "ui_decline", "Ablehnen" },
            { "ui_cancel", "Abbrechen" }, { "ui_waiting_reply", "Wartet auf Antwort" }, { "ui_wants_friend", "Möchte befreundet sein" },
            { "ui_no_friends", "Noch keine Freunde. Teile deinen Code!" }, { "ui_online_only", "Für Freunde brauchst du Internet" },
            { "ui_loading", "Lädt..." }, { "ui_request_sent", "Anfrage gesendet" }, { "ui_friend_added", "Ihr seid jetzt Freunde!" },
            { "ui_code_not_found", "Keine Farm hat diesen Code" }, { "ui_already_sent", "Schon gesendet" }, { "ui_friends_full", "Die Freundesliste ist voll" },
            { "ui_farm_of", "Farm von {0}" }, { "ui_friend_farm", "Farm deines Freundes" }, { "ui_level_n", "Level {0}" }, { "ui_likes_n", "{0} Likes" }, { "ui_like", "Gefällt mir" }, { "ui_liked", "Gefällt dir" },
            { "ui_gift", "{0} Münzen schenken" }, { "ui_gifted", "Geschenk gesendet" }, { "ui_home", "Nach Hause" }, { "ui_look_only", "Das ist die Farm deines Freundes. Du kannst dich umsehen." },
            { "ui_got_gifts", "Freunde haben dir {0} Geschenke geschickt!" }, { "ui_got_likes", "Deine Farm hat {0} neue Likes" }, { "ui_gift_full", "Dein Freund hat heute genug Geschenke" },
            { "ui_farmer", "Bauer" }, { "ui_try_again", "Das hat nicht geklappt. Versuch es nochmal." }, { "ui_remove", "Entfernen" },
            { "ui_order_wait", "Neuer Auftrag in {0}" }, { "ui_order_coming", "Neuer Auftrag kommt..." }, { "ui_order_missing", "Nicht genug Waren für diesen Auftrag" },
            { "ui_workshops", "Werkstätten" }, { "ui_go", "Öffnen" }, { "ui_ready_count", "{0} fertig" }, { "ui_working", "In Arbeit" },

            { "flour", "Mehl" }, { "bread", "Brot" }, { "pumpkin_pie", "Kürbiskuchen" }, { "cheese", "Käse" },
            { "ui_market", "Markt" }, { "ui_orders", "Marktaufträge" }, { "ui_deliver", "Liefern" }, { "ui_new_order", "Neuer Auftrag" },
            { "ui_reward", "{0} Münzen + {1} XP" }, { "ui_delivered", "Auftrag geliefert! +{0} Münzen" },
            { "ui_missions", "Aufgaben" }, { "ui_daily", "Heute" }, { "ui_weekly", "Diese Woche" }, { "ui_claim", "Abholen" },
            { "ui_claimed", "Erledigt" }, { "ui_mission_reward", "+{0} Münzen" },
            { "m_plant", "Bepflanze {0} Felder" }, { "m_harvest", "Ernte {0} Pflanzen" }, { "m_harvest_item", "Ernte {1}: {0}" },
            { "m_feed", "Füttere {0}-mal Tiere" }, { "m_collect", "Sammle {0} Tierprodukte" }, { "m_collect_item", "Sammle {1}: {0}" },
            { "m_sell", "Verkaufe {0} Waren" }, { "m_produce", "Stelle {0} Produkte her" },
            { "ui_open_for", "Öffnen für {0} Münzen" }, { "ui_workshop_locked", "Ab Level {0}" }, { "ui_opened", "{0} ist geöffnet!" },
            { "ui_start", "Herstellen" }, { "ui_collect", "Abholen" }, { "ui_slots", "In Arbeit {0}/{1}" }, { "ui_busy", "Alle Plätze belegt – erst abholen" },
            { "ui_missing", "Zutaten fehlen" }, { "ui_made", "+{0} {1}" },
            { "ui_upgrade_storage", "Größeres Lager: {0} Plätze – {1} Münzen" }, { "ui_storage_upgraded", "Das Lager fasst jetzt {0}" },

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
            { "ui_sound", "الصوت" }, { "ui_effects_on", "المؤثرات تعمل" }, { "ui_effects_off", "المؤثرات مطفأة" }, { "ui_music_on", "الموسيقى تعمل" }, { "ui_music_off", "الموسيقى مطفأة" },
            { "ui_settings", "الإعدادات" }, { "ui_account_saved", "الحساب محفوظ" }, { "ui_check_email", "افتح الإيميل وأكّده" },
            { "ui_guest_account", "حساب ضيف: هذه المزرعة موجودة على هذا الهاتف وسيرفرنا فقط" },
            { "ui_save_account_title", "احفظ المزرعة بإيميل أحد الوالدين للعب على هاتف آخر" }, { "ui_sign_in_title", "تحميل مزرعة محفوظة" },
            { "ui_parent_email", "إيميل الأهل" }, { "ui_password", "كلمة السر" }, { "ui_save_account", "حفظ الحساب" }, { "ui_sign_in", "تحميل المزرعة" },
            { "ui_have_account", "عندي حساب" }, { "ui_new_account_instead", "رجوع" },
            { "ui_sign_in_warning", "المزرعة على هذا الهاتف ستُستبدل بالمزرعة المحفوظة" },
            { "ui_delete_data", "حذف بياناتي" }, { "ui_delete_confirm", "نعم، احذف المزرعة والحساب نهائيًا" },
            { "ui_email_invalid", "هذا الإيميل لا يبدو صحيحًا" }, { "ui_password_short", "كلمة السر تحتاج 8 أحرف على الأقل" },
            { "ui_email_taken", "لهذا الإيميل حساب من قبل. حمّله بدلًا من ذلك." }, { "ui_sign_in_failed", "الإيميل أو كلمة السر خطأ، أو الإيميل لم يُؤكَّد بعد" },
            { "ui_signed_in", "يتم تحميل مزرعتك المحفوظة" }, { "ui_data_deleted", "تم حذف كل شيء. تبدأ مزرعة جديدة." },
            { "ui_my_stall", "بسطتي" }, { "ui_stall_hint", "يستطيع أصدقاؤك شراء هذه عند زيارتك" }, { "ui_coins_n", "{0} عملة" }, { "ui_ready_in", "يجهز بعد" }, { "ui_finish_now", "إنهاء فوري" }, { "ui_gems_have", "معك {0} جوهرة، والجواهر تأتي من هدية اليوم والمستويات الجديدة" }, { "ui_not_enough_gems", "الجواهر غير كافية" }, { "ui_daily_gift", "هدية اليوم" }, { "ui_day_n", "يوم {0}" }, { "ui_collect_gift", "استلام هدية اليوم" }, { "ui_gift_hint", "هدية جديدة كل يوم، وفي اليوم السابع جواهر" }, { "ui_gift_tomorrow", "الهدية التالية غدا" }, { "ui_gift_got", "الهدية {0} عملة" }, { "ui_gift_got_gems", "الهدية {0} عملة و{1} جوهرة" },
            { "ui_stall_full", "البسطة ممتلئة، الحد 4 عروض" }, { "ui_stall_empty", "المخزن فارغ" }, { "ui_new_offer", "عرض جديد" },
            { "ui_amount_n", "الكمية {0}" }, { "ui_unit_price", "{0} عملة للقطعة" }, { "ui_put_on_sale", "اعرضه للبيع بـ {0} عملة" },
            { "ui_on_sale", "عرضك صار في البسطة" }, { "ui_offer_gone", "اشتراه شخص آخر قبلك" }, { "ui_friend_stall", "بسطة صديقك" },
            { "ui_shop_empty", "لا يوجد شيء للبيع الآن" }, { "ui_buy_for", "اشترِ بـ {0} عملة" }, { "ui_bought", "+{0} {1}" }, { "ui_shop", "البسطة" },
            { "ui_friends", "الأصدقاء" }, { "ui_my_code", "كود الصداقة الخاص بك" }, { "ui_code_hint", "شاركه فقط مع أشخاص تعرفهم" },
            { "ui_code_placeholder", "كود الصديق" }, { "ui_add", "إضافة" }, { "ui_visit", "زيارة" }, { "ui_accept", "قبول" }, { "ui_decline", "رفض" },
            { "ui_cancel", "إلغاء" }, { "ui_waiting_reply", "بانتظار الرد" }, { "ui_wants_friend", "يريد أن يكون صديقك" },
            { "ui_no_friends", "لا يوجد أصدقاء بعد. شارك الكود الخاص بك!" }, { "ui_online_only", "الأصدقاء يحتاجون اتصالًا بالإنترنت" },
            { "ui_loading", "جارٍ التحميل..." }, { "ui_request_sent", "تم إرسال طلب الصداقة" }, { "ui_friend_added", "أصبحتما صديقين!" },
            { "ui_code_not_found", "لا توجد مزرعة بهذا الكود" }, { "ui_already_sent", "تم الإرسال من قبل" }, { "ui_friends_full", "قائمة الأصدقاء ممتلئة" },
            { "ui_farm_of", "مزرعة {0}" }, { "ui_friend_farm", "مزرعة صديقك" }, { "ui_level_n", "المستوى {0}" }, { "ui_likes_n", "{0} إعجاب" }, { "ui_like", "إعجاب" }, { "ui_liked", "تم الإعجاب" },
            { "ui_gift", "هدية {0} عملة" }, { "ui_gifted", "تم إرسال الهدية" }, { "ui_home", "العودة للمزرعة" }, { "ui_look_only", "هذه مزرعة صديقك. يمكنك التفرج عليها." },
            { "ui_got_gifts", "أرسل لك الأصدقاء {0} هدايا!" }, { "ui_got_likes", "أُعجب الأصدقاء بمزرعتك {0} مرات" }, { "ui_gift_full", "صديقك حصل على هدايا كافية اليوم" },
            { "ui_farmer", "مزارع" }, { "ui_try_again", "لم ينجح ذلك. حاول مرة أخرى." }, { "ui_remove", "حذف" },
            { "ui_order_wait", "طلب جديد بعد {0}" }, { "ui_order_coming", "طلب جديد في الطريق..." }, { "ui_order_missing", "البضاعة لا تكفي لهذا الطلب" },
            { "ui_workshops", "الورشات" }, { "ui_go", "افتحي" }, { "ui_ready_count", "{0} جاهز" }, { "ui_working", "قيد العمل" },

            { "flour", "طحين" }, { "bread", "خبز" }, { "pumpkin_pie", "فطيرة يقطين" }, { "cheese", "جبنة" },
            { "ui_market", "السوق" }, { "ui_orders", "طلبات السوق" }, { "ui_deliver", "سلّمي" }, { "ui_new_order", "طلب جديد" },
            { "ui_reward", "{0} عملة + {1} خبرة" }, { "ui_delivered", "تم تسليم الطلب! +{0} عملة" },
            { "ui_missions", "المهام" }, { "ui_daily", "اليوم" }, { "ui_weekly", "هذا الأسبوع" }, { "ui_claim", "استلمي" },
            { "ui_claimed", "تمت" }, { "ui_mission_reward", "+{0} عملة" },
            { "m_plant", "ازرعي {0} حقول" }, { "m_harvest", "احصدي {0} محاصيل" }, { "m_harvest_item", "احصدي {0} {1}" },
            { "m_feed", "أطعمي الحيوانات {0} مرات" }, { "m_collect", "اجمعي {0} من منتجات الحيوانات" }, { "m_collect_item", "اجمعي {0} {1}" },
            { "m_sell", "بيعي {0} أغراض" }, { "m_produce", "اصنعي {0} منتجات في الورشات" },
            { "ui_open_for", "افتحيها بـ {0} عملة" }, { "ui_workshop_locked", "تُفتح في المستوى {0}" }, { "ui_opened", "{0} صارت مفتوحة!" },
            { "ui_start", "اصنعي" }, { "ui_collect", "استلمي" }, { "ui_slots", "قيد العمل {0}/{1}" }, { "ui_busy", "كل الأماكن مشغولة – استلمي أولًا" },
            { "ui_missing", "المكونات ناقصة" }, { "ui_made", "+{0} {1}" },
            { "ui_upgrade_storage", "مخزن أكبر: {0} مكان – {1} عملة" }, { "ui_storage_upgraded", "المخزن صار يتسع لـ {0}" },

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
