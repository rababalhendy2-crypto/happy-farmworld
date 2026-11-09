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

    [Serializable] public class JobSave { public string recipe; public long startUtc; }
    [Serializable] public class WorkshopSave { public string id; public List<JobSave> jobs = new(); }
    /// <summary>A market order: deliver all items to earn the reward.</summary>
    [Serializable] public class OrderSave { public List<ItemStack> items = new(); public int coins; public int xp; }
    /// <summary>Progress of the daily or weekly missions for one period (day or week number).</summary>
    [Serializable] public class MissionBucket { public string kind; public long period; public List<ItemStack> progress = new(); public List<string> claimed = new(); }

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
        public int storageLevel;
        public List<WorkshopSave> workshops = new();
        public List<OrderSave> orders = new();
        public List<MissionBucket> missions = new();
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

        public int Capacity => GameConfig.StorageLevels[Mathf.Clamp(Data.storageLevel, 0, GameConfig.StorageLevels.Count - 1)].capacity;
        public int StorageFree => Mathf.Max(0, Capacity - StorageUsed());

        /// <summary>The next storage upgrade as (capacity, cost), or null when storage is at its largest.</summary>
        public (int capacity, int cost)? NextStorage =>
            Data.storageLevel + 1 < GameConfig.StorageLevels.Count ? GameConfig.StorageLevels[Data.storageLevel + 1] : null;

        public bool UpgradeStorage()
        {
            var next = NextStorage;
            if (next == null || !Spend(next.Value.cost)) return false;
            Data.storageLevel++;
            Touch();
            return true;
        }

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
            Track("sell", id, count);
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
            Track("feed", a.type, 1);
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
            Track("collect", product, qty);
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
            Track("plant", crop.Id, 1);
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
            Track("harvest", crop.Id, crop.Yield);
            AddXp(crop.Xp);
            Touch();
            return HarvestResult.Ok;
        }
    
        // ------------------------------------------------------------- workshops

        public WorkshopSave Workshop(string id)
        {
            foreach (var w in Data.workshops) if (w.id == id) return w;
            return null;
        }

        public enum BuyResult { Ok, Locked, NoCoins, Owned }

        public BuyResult BuyWorkshop(WorkshopDef def)
        {
            if (Workshop(def.Id) != null) return BuyResult.Owned;
            if (Data.level < def.UnlockLevel) return BuyResult.Locked;
            if (!Spend(def.Cost)) return BuyResult.NoCoins;
            Data.workshops.Add(new WorkshopSave { id = def.Id });
            Touch();
            return BuyResult.Ok;
        }

        public bool HasInputs(RecipeDef r)
        {
            foreach (var (item, qty) in r.Inputs) if (Count(item) < qty) return false;
            return true;
        }

        public enum JobResult { Ok, Busy, Missing, NotOwned }

        public JobResult StartJob(WorkshopDef def, RecipeDef r)
        {
            var w = Workshop(def.Id);
            if (w == null) return JobResult.NotOwned;
            if (w.jobs.Count >= def.Slots) return JobResult.Busy;
            if (!HasInputs(r)) return JobResult.Missing;   // checked first, so a failed start never removes anything
            foreach (var (item, qty) in r.Inputs) RemoveItem(item, qty);
            w.jobs.Add(new JobSave { recipe = r.Id, startUtc = Now });
            Touch();
            return JobResult.Ok;
        }

        public long JobSecondsLeft(JobSave j) =>
            GameConfig.RecipeById.TryGetValue(j.recipe, out var r) ? Math.Max(0, j.startUtc + r.Seconds - Now) : 0;

        public CollectResult CollectJob(WorkshopDef def, int index, out RecipeDef recipe)
        {
            recipe = null;
            var w = Workshop(def.Id);
            if (w == null || index < 0 || index >= w.jobs.Count) return CollectResult.NotReady;
            var j = w.jobs[index];
            if (!GameConfig.RecipeById.TryGetValue(j.recipe, out recipe) || JobSecondsLeft(j) > 0) return CollectResult.NotReady;
            if (StorageFree < recipe.Qty) return CollectResult.StorageFull;
            w.jobs.RemoveAt(index);
            AddItem(recipe.Id, recipe.Qty);
            Track("produce", recipe.Id, recipe.Qty);
            AddXp(recipe.Xp);
            Touch();
            return CollectResult.Ok;
        }

        /// <summary>Workshop products that are waiting to be collected (shown as a badge).</summary>
        public int ReadyJobs()
        {
            int n = 0;
            foreach (var w in Data.workshops) foreach (var j in w.jobs) if (JobSecondsLeft(j) == 0) n++;
            return n;
        }

        // ------------------------------------------------------------- market orders

        public const int OrderCount = 3;
        readonly System.Random _rng = new();

        /// <summary>Items an order may ask for: crops the player can plant, animal products, and products of owned workshops.</summary>
        List<string> OrderPool()
        {
            var pool = new List<string>();
            foreach (var c in GameConfig.Crops) if (Data.level >= c.UnlockLevel) pool.Add(c.Id);
            foreach (var a in GameConfig.AnimalById.Values) if (!pool.Contains(a.Product)) pool.Add(a.Product);
            foreach (var w in GameConfig.Workshops)
                if (Workshop(w.Id) != null) foreach (var r in w.Recipes) pool.Add(r.Id);
            return pool;
        }

        OrderSave NewOrder()
        {
            var pool = OrderPool();
            var o = new OrderSave();
            int kinds = pool.Count > 1 && _rng.NextDouble() < 0.5 ? 2 : 1;
            int value = 0;
            for (int k = 0; k < kinds; k++)
            {
                string id = pool[_rng.Next(pool.Count)];
                pool.Remove(id);
                int qty = GameConfig.CropById.ContainsKey(id) ? _rng.Next(3, 9)
                        : GameConfig.RecipeById.ContainsKey(id) ? _rng.Next(1, 3) : _rng.Next(1, 4);
                o.items.Add(new ItemStack { id = id, count = qty });
                value += GameConfig.SellPriceOf(id) * qty;
            }
            // orders pay half again what the market stall pays, so they are worth waiting for
            o.coins = Mathf.CeilToInt(value * 1.5f) + 5;
            o.xp = Mathf.Max(2, o.coins / 8);
            return o;
        }

        public List<OrderSave> Orders()
        {
            bool added = false;
            while (Data.orders.Count < OrderCount) { Data.orders.Add(NewOrder()); added = true; }
            if (added) _dirty = true;
            return Data.orders;
        }

        public bool CanDeliver(OrderSave o)
        {
            foreach (var s in o.items) if (Count(s.id) < s.count) return false;
            return true;
        }

        public bool Deliver(int index)
        {
            var list = Orders();
            if (index < 0 || index >= list.Count || !CanDeliver(list[index])) return false;
            var o = list[index];
            foreach (var s in o.items) RemoveItem(s.id, s.count);
            list[index] = NewOrder();
            AddCoins(o.coins);
            foreach (var s in o.items) Track("sell", s.id, s.count);
            AddXp(o.xp);
            Touch();
            return true;
        }

        public void ReplaceOrder(int index)
        {
            var list = Orders();
            if (index < 0 || index >= list.Count) return;
            list[index] = NewOrder();
            Touch();
        }

        // ------------------------------------------------------------- missions (daily / weekly)

        public static long DayIndex => Now / 86400;
        public static long WeekIndex => (DayIndex + 3) / 7;   // weeks start on Monday (1970-01-01 was a Thursday)

        public List<MissionDef> ActiveMissions(string kind)
        {
            var pool = kind == "daily" ? GameConfig.DailyMissions : GameConfig.WeeklyMissions;
            int count = Mathf.Min(kind == "daily" ? GameConfig.DailyCount : GameConfig.WeeklyCount, pool.Count);
            long period = kind == "daily" ? DayIndex : WeekIndex;
            var list = new List<MissionDef>();
            for (int k = 0; k < count; k++) list.Add(pool[(int)((period * count + k) % pool.Count)]);
            return list;
        }

        MissionBucket Bucket(string kind)
        {
            long period = kind == "daily" ? DayIndex : WeekIndex;
            foreach (var b in Data.missions)
                if (b.kind == kind)
                {
                    if (b.period != period) { b.period = period; b.progress.Clear(); b.claimed.Clear(); }
                    return b;
                }
            var n = new MissionBucket { kind = kind, period = period };
            Data.missions.Add(n);
            return n;
        }

        public int MissionProgress(string kind, MissionDef m)
        {
            foreach (var s in Bucket(kind).progress) if (s.id == m.Id) return Mathf.Min(s.count, m.Count);
            return 0;
        }

        public bool MissionClaimed(string kind, MissionDef m) => Bucket(kind).claimed.Contains(m.Id);

        void Track(string ev, string item, int qty)
        {
            foreach (var kind in new[] { "daily", "weekly" })
            {
                var b = Bucket(kind);
                foreach (var m in ActiveMissions(kind))
                {
                    if (m.Event != ev || (m.Item != "" && m.Item != item)) continue;
                    var s = b.progress.Find(p => p.id == m.Id);
                    if (s == null) b.progress.Add(s = new ItemStack { id = m.Id });
                    s.count = Mathf.Min(m.Count, s.count + qty);
                }
            }
        }

        public bool ClaimMission(string kind, MissionDef m)
        {
            if (MissionClaimed(kind, m) || MissionProgress(kind, m) < m.Count) return false;
            Bucket(kind).claimed.Add(m.Id);
            AddCoins(m.Coins);
            AddXp(m.Xp);
            Touch();
            return true;
        }

        /// <summary>Missions that are done but not yet claimed (shown as a badge).</summary>
        public int ClaimableMissions()
        {
            int n = 0;
            foreach (var kind in new[] { "daily", "weekly" })
                foreach (var m in ActiveMissions(kind))
                    if (!MissionClaimed(kind, m) && MissionProgress(kind, m) >= m.Count) n++;
            return n;
        }
}
}
