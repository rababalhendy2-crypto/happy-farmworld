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

    /// <summary>Balance values read from Resources/Data/game_config.json (one source of truth).</summary>
    public static class GameConfig
    {
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
        public static int StartingPlots = 6;
        public static int StorageCapacity = 60;
        public static int SellXpPerCoins = 10;
        public static int LevelRewardCoins = 50;
        public static float AutosaveSeconds = 20f;
        public static readonly Dictionary<string, int> StartInventory = new();
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
            if (storage != null && storage.Count > 0) StorageCapacity = (int)MiniJson.N(storage[0], "capacity", StorageCapacity);
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
        }

        public static int SellPriceOf(string item) => SellPrice.TryGetValue(item, out var p) ? p : 1;
    }
}
