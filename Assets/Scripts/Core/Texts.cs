using System.Collections.Generic;

namespace FarmWorld.Core
{
    /// <summary>
    /// UI strings. English for Sprint 1; Arabic (with letter shaping and right-to-left layout) and German
    /// are added with the full UI in the economy sprint, as the design document requires.
    /// </summary>
    public static class Texts
    {
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
            { "ui_choose_seed", "Choose a seed to plant" }, { "ui_level", "Level {0}" }, { "ui_locked", "Level {0}" },
            { "ui_storage", "Storage" }, { "ui_storage_count", "Storage {0}/{1}" }, { "ui_sell_all", "Sell all" },
            { "ui_empty_storage", "Storage is empty. Harvest crops to fill it." }, { "ui_close", "Close" },
            { "ui_not_enough_coins", "Not enough coins" }, { "ui_storage_full", "Storage is full – sell something first" },
            { "ui_growing", "{0} – ready in {1}" }, { "ui_planted", "{0} planted" }, { "ui_harvested", "+{0} {1}" },
            { "ui_sold", "+{0} coins" }, { "ui_level_up", "Level {0}! +{1} coins" }, { "ui_needs_level", "Unlocks at level {0}" },
            { "ui_fed", "{0} fed – {1} in {2}" }, { "ui_needs_feed", "{0} needs {1} {2} – grow some first" },
            { "ui_producing", "{0} – {1} in {2} · happiness {3}%" },
        };

        public static string Get(string key) => key != null && En.TryGetValue(key, out var v) ? v : key ?? "";
    }
}
