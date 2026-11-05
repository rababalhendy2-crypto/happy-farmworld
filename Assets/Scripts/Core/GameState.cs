using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FarmWorld.Core
{
    [Serializable] public class ItemStack { public string id; public int count; }
    [Serializable] public class PlotSave { public string crop = ""; public long plantedUtc; }
    /// <summary>fedUtc = 0 means hungry; otherwise the animal is producing (or done) since fedUtc. hungrySinceUtc drives happiness decay.</summary>
    [Serializable] public class AnimalSave { public string id; public string type; public long fedUtc; public int happiness; public long hungrySinceUtc; }

    [Serializable]
    public class SaveData
    {
        public int version = 1;
        public int coins;
        public int xp;
        public int level = 1;
        public List<ItemStack> inventory = new();
        public List<PlotSave> plots = new();
        public List<AnimalSave> animals = new();
        public long savedUtc;
    }

    /// <summary>
    /// The player's progress: coins, XP/level, storage and fields. Saved locally as JSON
    /// (cloud sync with Supabase comes in the accounts sprint). Growth uses real UTC time,
    /// so crops keep growing while the game is closed.
    /// </summary>
    public class GameState : MonoBehaviour
    {
        public static GameState I { get; private set; }
        public SaveData Data { get; private set; }
        public event Action Changed;
        public event Action<int> LeveledUp;

        string _path;
        float _nextSave;
        bool _dirty;

        public static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        void Awake()
        {
            I = this;
            GameConfig.Load();
            _path = Path.Combine(Application.persistentDataPath, "farmworld_save.json");
            Data = LoadOrNew();
            while (Data.plots.Count < GameConfig.StartingPlots) Data.plots.Add(new PlotSave());
            _nextSave = Time.unscaledTime + GameConfig.AutosaveSeconds;
        }

        SaveData LoadOrNew()
        {
            try
            {
                if (File.Exists(_path))
                {
                    var d = JsonUtility.FromJson<SaveData>(File.ReadAllText(_path));
                    if (d != null) return d;
                }
            }
            catch (Exception e) { Debug.LogWarning("[GameState] could not read save: " + e.Message); }

            var n = new SaveData { coins = GameConfig.StartCoins };
            foreach (var kv in GameConfig.StartInventory) n.inventory.Add(new ItemStack { id = kv.Key, count = kv.Value });
            return n;
        }

        public void Save()
        {
            try
            {
                Data.savedUtc = Now;
                File.WriteAllText(_path, JsonUtility.ToJson(Data));
                _dirty = false;
            }
            catch (Exception e) { Debug.LogWarning("[GameState] could not save: " + e.Message); }
        }

        void Update()
        {
            if (Time.unscaledTime < _nextSave) return;
            _nextSave = Time.unscaledTime + GameConfig.AutosaveSeconds;
            if (_dirty) Save();
        }

        void OnApplicationPause(bool paused) { if (paused) Save(); }
        void OnApplicationQuit() => Save();

        void Touch() { _dirty = true; Changed?.Invoke(); }

        // ------------------------------------------------------------- coins & items

        public bool Spend(int coins)
        {
            if (coins < 0 || Data.coins < coins) return false;
            Data.coins -= coins; Touch(); return true;
        }

        public void AddCoins(int coins) { Data.coins += Mathf.Max(0, coins); Touch(); }

        public int Count(string id)
        {
            foreach (var s in Data.inventory) if (s.id == id) return s.count;
            return 0;
        }

        public int StorageUsed()
        {
            int n = 0;
            foreach (var s in Data.inventory) n += s.count;
            return n;
        }

        public int StorageFree => Mathf.Max(0, GameConfig.StorageCapacity - StorageUsed());

        public void AddItem(string id, int count)
        {
            if (count <= 0) return;
            foreach (var s in Data.inventory) if (s.id == id) { s.count += count; Touch(); return; }
            Data.inventory.Add(new ItemStack { id = id, count = count });
            Touch();
        }

        public bool RemoveItem(string id, int count)
        {
            for (int i = 0; i < Data.inventory.Count; i++)
            {
                var s = Data.inventory[i];
                if (s.id != id) continue;
                if (s.count < count) return false;
                s.count -= count;
                if (s.count == 0) Data.inventory.RemoveAt(i);
                Touch();
                return true;
            }
            return false;
        }

        /// <summary>Sells items for their configured price; selling also gives a little XP.</summary>
        public int Sell(string id, int count)
        {
            count = Mathf.Min(count, Count(id));
            if (count <= 0 || !RemoveItem(id, count)) return 0;
            int coins = GameConfig.SellPriceOf(id) * count;
            AddCoins(coins);
            AddXp(Mathf.Max(1, coins / GameConfig.SellXpPerCoins));
            return coins;
        }

        // ------------------------------------------------------------- XP & level

        public int XpForLevel(int level)
        {
            var t = GameConfig.LevelXp;
            if (level - 1 < t.Length) return t[Mathf.Max(0, level - 1)];
            return t[t.Length - 1] + (level - t.Length) * 800;
        }

        public void AddXp(int xp)
        {
            if (xp <= 0) return;
            Data.xp += xp;
            while (Data.xp >= XpForLevel(Data.level + 1))
            {
                Data.level++;
                Data.coins += GameConfig.LevelRewardCoins;
                LeveledUp?.Invoke(Data.level);
            }
            Touch();
        }

        // ------------------------------------------------------------- animals

        public enum AnimalState { Hungry, Producing, Ready }

        public AnimalSave Animal(string id, string type)
        {
            foreach (var a in Data.animals) if (a.id == id) return a;
            var n = new AnimalSave { id = id, type = type, happiness = GameConfig.StartHappiness, hungrySinceUtc = Now };
            Data.animals.Add(n);
            Touch();
            return n;
        }

        public AnimalState StateOf(AnimalSave a)
        {
            if (a.fedUtc == 0 || !GameConfig.AnimalById.TryGetValue(a.type, out var def)) return AnimalState.Hungry;
            return Now - a.fedUtc >= def.ProduceSeconds ? AnimalState.Ready : AnimalState.Producing;
        }

        public long AnimalSecondsLeft(AnimalSave a)
        {
            if (a.fedUtc == 0 || !GameConfig.AnimalById.TryGetValue(a.type, out var def)) return 0;
            return Math.Max(0, a.fedUtc + def.ProduceSeconds - Now);
        }

        /// <summary>Happiness drops slowly while an animal waits hungry, never below the minimum.</summary>
        int CurrentHappiness(AnimalSave a)
        {
            if (a.fedUtc != 0 || a.hungrySinceUtc == 0) return a.happiness;
            int hours = (int)((Now - a.hungrySinceUtc) / 3600);
            return Mathf.Max(GameConfig.MinHappiness, a.happiness - hours * GameConfig.HungryDecayPerHour);
        }

        public int Happiness(AnimalSave a) => CurrentHappiness(a);

        public enum FeedResult { Ok, NoFeed, NotHungry }

        public FeedResult Feed(AnimalSave a)
        {
            if (StateOf(a) != AnimalState.Hungry || !GameConfig.AnimalById.TryGetValue(a.type, out var def)) return FeedResult.NotHungry;
            if (!RemoveItem(def.FeedItem, def.FeedQty)) return FeedResult.NoFeed;
            a.happiness = Mathf.Min(100, CurrentHappiness(a) + GameConfig.FeedBonus);
            a.fedUtc = Now; a.hungrySinceUtc = 0;
            Touch();
            return FeedResult.Ok;
        }

        public enum CollectResult { Ok, NotReady, StorageFull }

        public CollectResult Collect(AnimalSave a, out string product, out int qty)
        {
            product = null; qty = 0;
            if (StateOf(a) != AnimalState.Ready || !GameConfig.AnimalById.TryGetValue(a.type, out var def)) return CollectResult.NotReady;
            product = def.Product;
            qty = def.ProductQty + (a.happiness >= GameConfig.BonusThreshold ? GameConfig.BonusQty : 0);
            if (StorageFree < qty) return CollectResult.StorageFull;
            AddItem(product, qty);
            a.fedUtc = 0; a.hungrySinceUtc = Now;
            AddXp(def.Xp);
            Touch();
            return CollectResult.Ok;
        }

        // ------------------------------------------------------------- fields

        public PlotSave Plot(int index) => index >= 0 && index < Data.plots.Count ? Data.plots[index] : null;

        public bool Plant(int index, CropDef crop)
        {
            var p = Plot(index);
            if (p == null || !string.IsNullOrEmpty(p.crop) || crop == null) return false;
            if (Data.level < crop.UnlockLevel) return false;
            if (!Spend(crop.SeedPrice)) return false;
            p.crop = crop.Id; p.plantedUtc = Now;
            Touch();
            return true;
        }

        /// <summary>0..1 growth of a plot; 1 means ready to harvest.</summary>
        public float Progress(int index)
        {
            var p = Plot(index);
            if (p == null || string.IsNullOrEmpty(p.crop) || !GameConfig.CropById.TryGetValue(p.crop, out var c)) return 0f;
            return Mathf.Clamp01((Now - p.plantedUtc) / (float)c.GrowSeconds);
        }

        public long SecondsLeft(int index)
        {
            var p = Plot(index);
            if (p == null || string.IsNullOrEmpty(p.crop) || !GameConfig.CropById.TryGetValue(p.crop, out var c)) return 0;
            return Math.Max(0, p.plantedUtc + c.GrowSeconds - Now);
        }

        public enum HarvestResult { Ok, NotReady, StorageFull, Empty }

        public HarvestResult Harvest(int index, out CropDef crop)
        {
            crop = null;
            var p = Plot(index);
            if (p == null || string.IsNullOrEmpty(p.crop) || !GameConfig.CropById.TryGetValue(p.crop, out crop)) return HarvestResult.Empty;
            if (Progress(index) < 1f) return HarvestResult.NotReady;
            if (StorageFree < crop.Yield) return HarvestResult.StorageFull;
            AddItem(crop.Id, crop.Yield);
            p.crop = ""; p.plantedUtc = 0;
            AddXp(crop.Xp);
            Touch();
            return HarvestResult.Ok;
        }
    }
}
