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
        };

        public static string Get(string key) => key != null && En.TryGetValue(key, out var v) ? v : key ?? "";
    }
}
