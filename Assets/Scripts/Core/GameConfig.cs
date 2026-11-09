using System.Collections.Generic;
using UnityEngine;

namespace FarmWorld.Core
{
    public class CropDef
    {
        public string Id;          // wheat, carrot, ...
        public string Model;       // model prefix in Resources/Models/Crops (Wheat, Carrot, ...)
        public int SeedPrice;
        public int GrowSeconds;
        public int Yield;
        public int Xp;
        public int UnlockLevel;
        public Color Color;
    }

    public class AnimalDef
    {
        public string Id;          // chicken, cow, sheep
        public string FeedItem;
        public int FeedQty;
        public string Product;
        public int ProductQty;
        public int ProduceSeconds;
        public int Xp;
    }

    public class RecipeDef
    {
        public string Id;          // the product: flour, bread, cheese, ...
        public List<(string item, int qty)> Inputs = new();
        public int Seconds;
        public int Qty;
        public int Xp;
    }

    public class WorkshopDef
    {
        public string Id;          // config id: bakery, dairy
        public string Building;    // world building that hosts it: mill, dairy
        public string TitleKey;
        public int Cost;
        public int UnlockLevel;
        public int Slots;
        public List<RecipeDef> Recipes = new();
    }

    public class MissionDef
    {
        public string Id;
        public string Event;       // plant | harvest | feed | collect | sell | produce
        public string Item;        // "" = any item
        public int Count;
        public int Coins;
        public int Xp;
    }

    /// <summary>Balance values read from Resources/Data/game_config.json (one source of truth).</summary>
    public static class GameConfig
    {
        public const int RushSecondsPerGem = 300;   // match cfg.gems.rush_seconds_per_gem on the server

        /// <summary>The five MVP crops from the design document, in shop order, with their model names.</summary>
        static readonly (string id, string model)[] MvpCrops =
        {
            ("wheat", "Wheat"), ("carrot", "Carrot"), ("tomato", "Tomato"), ("corn", "Corn"), ("pumpkin", "Pumpkin"),
        };

        public static readonly List<CropDef> Crops = new();
        public static readonly Dictionary<string, CropDef> CropById = new();
        public static readonly Dictionary<string, int> SellPrice = new();
        public static readonly Dictionary<string, AnimalDef> AnimalById = new();
        public static int StartHappiness = 70, FeedBonus = 15, HungryDecayPerHour = 5, MinHappiness = 30, BonusThreshold = 85, BonusQty = 1;
        public static int StartCoins = 120;
        public const int GiftCoins = 10;   // matches cfg.social.gift_coins on the server
        public static int StartingPlots = 6;
        public static int StorageCapacity = 60;
        public static int SellXpPerCoins = 10;
        public static int LevelRewardCoins = 50;
        public static float AutosaveSeconds = 20f;
        public static readonly Dictionary<string, int> StartInventory = new();
        public static readonly List<(int capacity, int cost)> StorageLevels = new();
        public static readonly List<WorkshopDef> Workshops = new();
        public static readonly Dictionary<string, RecipeDef> RecipeById = new();
        public static readonly List<MissionDef> DailyMissions = new(), WeeklyMissions = new();
        public static int DailyCount = 3, WeeklyCount = 2;
        /// <summary>The animals in the Unity world, as the server lists them (config world.animals).</summary>
        public static string[] WorldAnimals = { "cow", "cow", "sheep", "sheep", "chicken", "chicken", "chicken", "chicken" };
        public static int[] LevelXp = { 0, 20, 55, 110, 190, 300, 450, 650, 900, 1200 };

        static bool _loaded;

        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            var ta = Resources.Load<TextAsset>("Data/game_config");
            object root = ta != null ? MiniJson.Parse(ta.text) : null;
            if (root == null) Debug.LogWarning("[GameConfig] game_config.json missing – using defaults");

            var eco = MiniJson.O(root, "economy");
            StartCoins = (int)MiniJson.N(eco, "start_coins", StartCoins);
            StartingPlots = (int)MiniJson.N(eco, "starting_plots", StartingPlots);
            SellXpPerCoins = Mathf.Max(1, (int)MiniJson.N(eco, "sell_xp_per_coins", SellXpPerCoins));
            LevelRewardCoins = (int)MiniJson.N(eco, "level_reward_coins", LevelRewardCoins);
            AutosaveSeconds = (float)MiniJson.N(eco, "autosave_seconds", AutosaveSeconds);
            var levels = MiniJson.A(eco, "level_xp");
            if (levels != null && levels.Count > 1)
            {
                LevelXp = new int[levels.Count];
                for (int i = 0; i < levels.Count; i++) LevelXp[i] = levels[i] is double d ? (int)d : 0;
            }
            var storage = MiniJson.A(eco, "storage_levels");
            if (storage != null)
                foreach (var lv in storage) StorageLevels.Add(((int)MiniJson.N(lv, "capacity", 60), (int)MiniJson.N(lv, "cost", 0)));
            if (StorageLevels.Count == 0) StorageLevels.Add((StorageCapacity, 0));
            StorageCapacity = StorageLevels[0].capacity;
            var inv = MiniJson.O(eco, "start_inventory");
            if (inv != null) foreach (var kv in inv) if (kv.Value is double n) StartInventory[kv.Key] = (int)n;

            var crops = MiniJson.O(root, "crops");
            foreach (var (id, model) in MvpCrops)
            {
                var c = MiniJson.O(crops, id);
                var def = new CropDef
                {
                    Id = id, Model = model,
                    SeedPrice = (int)MiniJson.N(c, "seed_price", 5),
                    GrowSeconds = Mathf.Max(5, (int)MiniJson.N(c, "grow_seconds", 60)),
                    Yield = Mathf.Max(1, (int)MiniJson.N(c, "yield", 3)),
                    Xp = (int)MiniJson.N(c, "xp", 1),
                    UnlockLevel = Mathf.Max(1, (int)MiniJson.N(c, "unlock_level", 1)),
                    Color = ColorUtility.TryParseHtmlString(MiniJson.S(c, "color", "#8bc34a"), out var col) ? col : Color.green,
                };
                Crops.Add(def);
                CropById[id] = def;
            }

            // MVP animals from the design document: chicken, cow, sheep.
            var animals = MiniJson.O(root, "animals");
            foreach (var id in new[] { "chicken", "cow", "sheep" })
            {
                var a = MiniJson.O(animals, id);
                AnimalById[id] = new AnimalDef
                {
                    Id = id,
                    FeedItem = MiniJson.S(a, "feed_item", "wheat"),
                    FeedQty = Mathf.Max(1, (int)MiniJson.N(a, "feed_qty", 1)),
                    Product = MiniJson.S(a, "product", id == "chicken" ? "egg" : id == "cow" ? "milk" : "wool"),
                    ProductQty = Mathf.Max(1, (int)MiniJson.N(a, "product_qty", 1)),
                    ProduceSeconds = Mathf.Max(5, (int)MiniJson.N(a, "produce_seconds", 120)),
                    Xp = (int)MiniJson.N(a, "xp", 2),
                };
            }
            var care = MiniJson.O(root, "animal_care");
            StartHappiness = (int)MiniJson.N(care, "start_happiness", StartHappiness);
            FeedBonus = (int)MiniJson.N(care, "feed_bonus", FeedBonus);
            HungryDecayPerHour = (int)MiniJson.N(care, "decay_per_hour_hungry", HungryDecayPerHour);
            MinHappiness = (int)MiniJson.N(care, "min_happiness", MinHappiness);
            BonusThreshold = (int)MiniJson.N(care, "bonus_threshold", BonusThreshold);
            BonusQty = (int)MiniJson.N(care, "bonus_qty", BonusQty);

            var items = MiniJson.O(root, "items");
            if (items != null)
                foreach (var kv in items) SellPrice[kv.Key] = (int)MiniJson.N(kv.Value, "sell_price", 1);
            LoadProduction(root);
        }

        /// <summary>
        /// Workshops that have a building in the Unity world. A recipe is kept only when every input can
        /// be made in this version (MVP crops, egg/milk/wool, or another kept recipe), so no recipe asks
        /// for goat milk or strawberries the player can never get.
        /// </summary>
        static readonly (string id, string building, string titleKey)[] WorldWorkshops =
        {
            ("bakery", "mill", "bld_mill"), ("dairy", "dairy", "bld_dairy"),
        };

        static void LoadProduction(object root)
        {
            var available = new HashSet<string>(CropById.Keys);
            foreach (var a in AnimalById.Values) available.Add(a.Product);
            var recipes = MiniJson.O(root, "recipes");
            var workshops = MiniJson.O(root, "workshops");
            foreach (var (id, building, titleKey) in WorldWorkshops)
            {
                var w = MiniJson.O(workshops, id);
                if (w == null) continue;
                var def = new WorkshopDef
                {
                    Id = id, Building = building, TitleKey = titleKey,
                    Cost = (int)MiniJson.N(w, "cost", 100),
                    UnlockLevel = Mathf.Max(1, (int)MiniJson.N(w, "unlock_level", 1)),
                    Slots = Mathf.Max(1, (int)MiniJson.N(w, "slots", 2)),
                };
                var list = MiniJson.A(w, "recipes");
                if (list != null)
                    foreach (var r in list)
                    {
                        if (r is not string rid) continue;
                        var rd = MiniJson.O(recipes, rid);
                        var inputs = MiniJson.O(rd, "inputs");
                        if (rd == null || inputs == null) continue;
                        var recipe = new RecipeDef
                        {
                            Id = rid,
                            Seconds = Mathf.Max(5, (int)MiniJson.N(rd, "seconds", 60)),
                            Qty = Mathf.Max(1, (int)MiniJson.N(rd, "qty", 1)),
                            Xp = (int)MiniJson.N(rd, "xp", 1),
                        };
                        bool ok = true;
                        foreach (var kv in inputs)
                        {
                            if (!available.Contains(kv.Key)) ok = false;
                            recipe.Inputs.Add((kv.Key, kv.Value is double q ? (int)q : 1));
                        }
                        if (!ok) continue;
                        available.Add(rid);
                        def.Recipes.Add(recipe);
                        RecipeById[rid] = recipe;
                    }
                if (def.Recipes.Count > 0) Workshops.Add(def);
            }

            var missions = MiniJson.O(root, "missions");
            DailyCount = Mathf.Max(1, (int)MiniJson.N(missions, "daily_count", DailyCount));
            WeeklyCount = Mathf.Max(1, (int)MiniJson.N(missions, "weekly_count", WeeklyCount));
            ReadMissions(MiniJson.A(missions, "daily"), DailyMissions, available);
            ReadMissions(MiniJson.A(missions, "weekly"), WeeklyMissions, available);
        }

        static void ReadMissions(List<object> src, List<MissionDef> dst, HashSet<string> available)
        {
            if (src == null) return;
            foreach (var m in src)
            {
                var reward = MiniJson.O(m, "reward");
                var def = new MissionDef
                {
                    Id = MiniJson.S(m, "id"), Event = MiniJson.S(m, "event"), Item = MiniJson.S(m, "item"),
                    Count = Mathf.Max(1, (int)MiniJson.N(m, "count", 1)),
                    Coins = (int)MiniJson.N(reward, "coins", 0), Xp = (int)MiniJson.N(reward, "xp", 0),
                };
                if (def.Id == "" || (def.Item != "" && !available.Contains(def.Item))) continue;
                if (def.Event == "produce" && Workshops.Count == 0) continue;
                dst.Add(def);
            }
        }

        public static WorkshopDef WorkshopAt(string building)
        {
            foreach (var w in Workshops) if (w.Building == building) return w;
            return null;
        }

        public static int SellPriceOf(string item) => SellPrice.TryGetValue(item, out var p) ? p : 1;
    }
}
