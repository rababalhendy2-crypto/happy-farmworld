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
    [Serializable] public class OrderSave { public List<ItemStack> items = new(); public int coins; public int xp; public long availableUtc; }
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
        /// <summary>True once the server has sent this farm: from then on the server owns market orders.</summary>
        public bool serverLinked;
        public List<WorkshopSave> workshops = new();
        public List<OrderSave> orders = new();
        public List<MissionBucket> missions = new();
        public long savedUtc;
        /// <summary>Gems (premium currency); only the server grants them.</summary>
        public int gems;
        /// <summary>Daily gift: last UTC day number it was claimed and how many were claimed so far (server owned).</summary>
        public int dailyLastDay = -1, dailyStep;
        /// <summary>Where every field, building, fence, tree, decoration and pet stands (empty = the default farm).</summary>
        public List<PlacedItem> layout = new();
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
        /// <summary>The server replaced the local state: fields and animals must redraw.</summary>
        public event Action ServerApplied;

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
            EnsureLayout(Data);
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

        /// <summary>Forgets the local farm (after the account was deleted) and starts a new one.</summary>
        /// <summary>Set when another account's farm is about to load: its level is not a level-up.</summary>
        public bool QuietNextApply { get; set; }

        public void ResetLocal()
        {
            QuietNextApply = true;
            _home = null;
            try { if (File.Exists(_path)) File.Delete(_path); } catch (Exception e) { Debug.LogWarning("[GameState] " + e.Message); }
            Data = LoadOrNew();
            while (Data.plots.Count < GameConfig.StartingPlots) Data.plots.Add(new PlotSave());
            EnsureLayout(Data);
            FriendCode = "";
            Save();
            ServerApplied?.Invoke();
            Changed?.Invoke();
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void FW_SyncFiles();
#endif

        public void Save()
        {
            try
            {
                var own = _home ?? Data;   // a visited farm is never saved
                own.savedUtc = Now;
                File.WriteAllText(_path, JsonUtility.ToJson(own));
#if UNITY_WEBGL && !UNITY_EDITOR
                // In the browser, files live in memory until the file system is synced to IndexedDB
                // (Plugins/WebGL/FileSync.jslib); without it a reload loses everything since the last sync.
                FW_SyncFiles();
#endif
                _dirty = false;
            }
            catch (Exception e) { Debug.LogWarning("[GameState] could not save: " + e.Message); }
        }

        void Update()
        {
            // save two seconds after the last change (a browser tab can be closed at any moment)
            if (_dirty && Time.unscaledTime >= _nextSave) Save();
        }

        void OnApplicationPause(bool paused) { if (paused) Save(); }
        void OnApplicationQuit() => Save();

        void Touch()
        {
            if (!_dirty) _nextSave = Time.unscaledTime + 2f;
            _dirty = true;
            Changed?.Invoke();
        }

        static void Send(string action, string args = "{}") { if (Backend.I != null) Backend.I.Enqueue(action, args); }
        static string Q(string s) => Backend.Str(s);

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
            Send("upgrade_storage");
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
            AddXp(coins / GameConfig.SellXpPerCoins);   // same rule as the server: below 10 coins there is no XP
            Track("sell", id, count);
            Send("sell", $"{{\"item\":{Q(id)},\"qty\":{count}}}");
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
            Send("feed", $"{{\"idx\":{ServerAnimalIndex(a.id)}}}");
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
            Send("collect", $"{{\"idx\":{ServerAnimalIndex(a.id)}}}");
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
            Send("plant", $"{{\"idx\":{index},\"crop\":{Q(crop.Id)}}}");
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

        /// <summary>Gems to finish a growing crop now: 1 per started 5 minutes left (matches cfg.gems on the server).</summary>
        public int RushCost(int index)
        {
            long left = SecondsLeft(index);
            return left <= 0 ? 0 : (int)Math.Max(1, (left + GameConfig.RushSecondsPerGem - 1) / GameConfig.RushSecondsPerGem);
        }

        /// <summary>Finishes a growing crop at once for gems. The server checks the price again.</summary>
        public bool Rush(int index)
        {
            var p = Plot(index);
            if (p == null || string.IsNullOrEmpty(p.crop) || !GameConfig.CropById.TryGetValue(p.crop, out var c)) return false;
            int cost = RushCost(index);
            if (cost == 0 || Data.gems < cost) return false;
            Data.gems -= cost;
            p.plantedUtc = Now - c.GrowSeconds;
            Send("rush_plot", $"{{\"idx\":{index}}}");
            Touch();
            return true;
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
            Send("harvest", $"{{\"idx\":{index}}}");
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
            Send("buy_workshop", $"{{\"id\":{Q(def.Id)}}}");
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
            Send("start_job", $"{{\"ws\":{Q(def.Id)},\"recipe\":{Q(r.Id)}}}");
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
            Send("collect_job", $"{{\"ws\":{Q(def.Id)},\"job\":{index}}}");
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

        public const int ReplaceSeconds = 120;

        /// <summary>
        /// The order slots. Offline-only farms make their own orders; once the server has sent the farm it
        /// owns them, and a slot that was just delivered waits empty until the server's answer arrives.
        /// </summary>
        public List<OrderSave> Orders()
        {
            bool added = false;
            while (Data.orders.Count < OrderCount)
            {
                Data.orders.Add(Data.serverLinked ? new OrderSave { availableUtc = long.MaxValue } : NewOrder());
                added = true;
            }
            if (added) _dirty = true;
            return Data.orders;
        }

        public static bool IsWaiting(OrderSave o) => o.items.Count == 0 || o.availableUtc > Now;

        public bool CanDeliver(OrderSave o)
        {
            if (IsWaiting(o)) return false;
            foreach (var s in o.items) if (Count(s.id) < s.count) return false;
            return true;
        }

        public bool Deliver(int index)
        {
            var list = Orders();
            if (index < 0 || index >= list.Count || !CanDeliver(list[index])) return false;
            var o = list[index];
            foreach (var s in o.items) RemoveItem(s.id, s.count);
            list[index] = Data.serverLinked ? new OrderSave { availableUtc = long.MaxValue } : NewOrder();
            Send("deliver_order", $"{{\"idx\":{index}}}");
            AddCoins(o.coins);
            foreach (var s in o.items) Track("sell", s.id, s.count);
            AddXp(o.xp);
            Touch();
            return true;
        }

        /// <summary>Swaps an order for another one; the slot then waits two minutes, so orders cannot be rerolled endlessly.</summary>
        public bool ReplaceOrder(int index)
        {
            var list = Orders();
            if (index < 0 || index >= list.Count || IsWaiting(list[index])) return false;
            var n = Data.serverLinked ? new OrderSave() : NewOrder();
            n.availableUtc = Now + ReplaceSeconds;
            list[index] = n;
            Send("replace_order", $"{{\"idx\":{index}}}");
            Touch();
            return true;
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

        /// <summary>Raised for each game event (plant, harvest, feed, collect, produce, sell); the tutorial listens.</summary>
        public event Action<string> Tracked;

        void Track(string ev, string item, int qty)
        {
            Tracked?.Invoke(ev);
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
            Send("claim_mission", $"{{\"kind\":{Q(kind)},\"id\":{Q(m.Id)}}}");
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

        // ------------------------------------------------------------- farm layout (build and move)

        /// <summary>Raised when something was built, moved or sold, so the world redraws that piece.</summary>
        public event Action LayoutChanged;

        /// <summary>Default farm when nothing is stored, and a field for every plot that has none (e.g. old expansions).</summary>
        public static void EnsureLayout(SaveData d)
        {
            if (d.layout == null || d.layout.Count == 0) d.layout = Layout.DefaultCopy();
            var used = new HashSet<int>();
            foreach (var it in d.layout) if (it.t == "field" && it.p >= 0) used.Add(it.p);
            for (int i = 0; i < d.plots.Count; i++)
            {
                if (used.Contains(i)) continue;
                // same id and spot on every device, so the server can adopt it when it is first moved
                float x = Layout.Area.xMin + 3f + (i % 8) * 4.6f, z = Layout.Area.yMin + 3f + (i / 8) * 4.4f;
                d.layout.Add(new PlacedItem { u = "auto_p" + i, t = "field", x = x, z = z, r = 0, p = i });
            }
        }

        public PlacedItem Item(string u) => Data.layout.Find(i => i.u == u);

        public void MoveItem(PlacedItem it, float x, float z, int r)
        {
            if (Visiting || it == null) return;
            it.x = x; it.z = z; it.r = ((r % 4) + 4) % 4;
            Send("move_item", $"{{\"u\":{Q(it.u)},\"x\":{F(x)},\"z\":{F(z)},\"r\":{it.r}}}");
            Touch();
            LayoutChanged?.Invoke();
        }

        public enum BuildResult { Ok, Locked, NoCoins, Max }

        public BuildResult CanBuy(BuildItemDef def)
        {
            if (Data.level < def.Level) return BuildResult.Locked;
            if (Layout.Count(Data.layout, def.Id) >= def.Max) return BuildResult.Max;
            if (Data.coins < Layout.Price(Data.layout, def)) return BuildResult.NoCoins;
            return BuildResult.Ok;
        }

        public BuildResult BuyItem(BuildItemDef def, float x, float z, int r, out PlacedItem it)
        {
            it = null;
            if (Visiting || !def.Sale) return BuildResult.Locked;
            var can = CanBuy(def);
            if (can != BuildResult.Ok) return can;
            int price = Layout.Price(Data.layout, def);
            if (!Spend(price)) return BuildResult.NoCoins;
            it = new PlacedItem { u = def.Id + "_" + Guid.NewGuid().ToString("N").Substring(0, 8), t = def.Id, x = x, z = z, r = ((r % 4) + 4) % 4 };
            if (def.Cat == "field") { Data.plots.Add(new PlotSave()); it.p = Data.plots.Count - 1; }
            Data.layout.Add(it);
            Track("build", def.Id, 1);
            Send("buy_item", $"{{\"id\":{Q(def.Id)},\"u\":{Q(it.u)},\"x\":{F(x)},\"z\":{F(z)},\"r\":{it.r}}}");
            Touch();
            LayoutChanged?.Invoke();
            return BuildResult.Ok;
        }

        /// <summary>Half the price back for something bought (fields and the farm's own buildings stay).</summary>
        public static int SellValue(BuildItemDef def) => def.Cost / 2;

        public bool SellItem(PlacedItem it)
        {
            if (Visiting || it == null || !Layout.Items.TryGetValue(it.t, out var def) || !def.Sale || def.Cat == "field") return false;
            Data.layout.Remove(it);
            AddCoins(SellValue(def));
            Send("sell_item", $"{{\"u\":{Q(it.u)}}}");
            Touch();
            LayoutChanged?.Invoke();
            return true;
        }

        static string F(float v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        // ------------------------------------------------------------- server state

        /// <summary>Index of a Unity animal (cow_1) in the server's animal list (config world.animals).</summary>
        public static int ServerAnimalIndex(string id)
        {
            int cut = id.LastIndexOf('_');
            string type = id.Substring(0, cut);
            int n = int.Parse(id.Substring(cut + 1));
            for (int i = 0; i < GameConfig.WorldAnimals.Length; i++)
                if (GameConfig.WorldAnimals[i] == type && n-- == 0) return i;
            return -1;
        }

        static int Int(object o) => o is double d ? (int)d : 0;

        /// <summary>Replaces the local farm with the server's authoritative state (see Backend).</summary>
        public void ApplyServerState(Dictionary<string, object> s)
        {
            if (Visiting)
            {
                // the player's own farm changed on the server while they look at a friend's farm
                var guest = Data;
                Data = _home;
                ApplyCore(s);
                _home = Data;
                Data = guest;
                guest.coins = _home.coins; guest.xp = _home.xp; guest.level = _home.level; guest.inventory = _home.inventory;
                Changed?.Invoke();
                return;
            }
            int oldLevel = Data.level;
            ApplyCore(s);
            if (Data.level > oldLevel && !QuietNextApply) LeveledUp?.Invoke(Data.level);
            QuietNextApply = false;
            Touch();
            ServerApplied?.Invoke();
        }

        void ApplyCore(Dictionary<string, object> s)
        {
            Data.coins = (int)MiniJson.N(s, "coins", Data.coins);
            Data.xp = (int)MiniJson.N(s, "xp", Data.xp);
            Data.level = Mathf.Max(1, (int)MiniJson.N(s, "level", Data.level));
            Data.storageLevel = (int)MiniJson.N(s, "storage_level", Data.storageLevel);
            Data.gems = (int)MiniJson.N(s, "premium", Data.gems);
            var daily = MiniJson.O(s, "daily_reward");
            if (daily != null) { Data.dailyLastDay = (int)MiniJson.N(daily, "last_day", -1); Data.dailyStep = (int)MiniJson.N(daily, "step", 0); }
            else if (s.ContainsKey("daily_reward")) { Data.dailyLastDay = -1; Data.dailyStep = 0; }

            var inv = MiniJson.O(s, "inventory");
            if (inv != null)
            {
                var list = new List<ItemStack>();
                foreach (var old in Data.inventory)   // keep the order the player knows
                    if (inv.TryGetValue(old.id, out var q) && Int(q) > 0) list.Add(new ItemStack { id = old.id, count = Int(q) });
                foreach (var kv in inv)
                    if (Int(kv.Value) > 0 && list.Find(x => x.id == kv.Key) == null) list.Add(new ItemStack { id = kv.Key, count = Int(kv.Value) });
                Data.inventory = list;
            }

            ReadFarm(Data, s);

            var missions = MiniJson.O(s, "missions");
            if (missions != null)
            {
                Data.missions.Clear();
                foreach (var kind in new[] { "daily", "weekly" })
                {
                    var b = MiniJson.O(missions, kind);
                    if (b == null) continue;
                    var bucket = new MissionBucket { kind = kind, period = (long)MiniJson.N(b, "period") };
                    var prog = MiniJson.O(b, "progress");
                    if (prog != null) foreach (var kv in prog) bucket.progress.Add(new ItemStack { id = kv.Key, count = Int(kv.Value) });
                    var claimed = MiniJson.A(b, "claimed");
                    if (claimed != null) foreach (var c in claimed) if (c is string id) bucket.claimed.Add(id);
                    Data.missions.Add(bucket);
                }
            }

            var orders = MiniJson.A(s, "orders");
            if (orders != null)
            {
                Data.orders.Clear();
                foreach (var o in orders)
                {
                    var order = new OrderSave
                    {
                        coins = (int)MiniJson.N(o, "coins"), xp = (int)MiniJson.N(o, "xp"),
                        availableUtc = (long)Math.Ceiling(MiniJson.N(o, "available_at")),
                    };
                    var items = MiniJson.O(o, "items");
                    if (items != null) foreach (var kv in items) order.items.Add(new ItemStack { id = kv.Key, count = Int(kv.Value) });
                    Data.orders.Add(order);
                }
            }

            Data.serverLinked = true;
            if (MiniJson.S(s, "friend_code") != "") FriendCode = MiniJson.S(s, "friend_code");
        }

        /// <summary>Plots, animals and workshops from a server farm (own state or a friend's visit).</summary>
        static void ReadFarm(SaveData d, Dictionary<string, object> s)
        {

            var plots = MiniJson.A(s, "plots");
            if (plots != null)
            {
                d.plots.Clear();
                foreach (var p in plots)
                {
                    string crop = MiniJson.S(p, "crop");
                    d.plots.Add(new PlotSave { crop = crop, plantedUtc = crop == "" ? 0 : (long)MiniJson.N(p, "planted_at") });
                }
            }

            var animals = MiniJson.A(s, "animals");
            if (animals != null)
            {
                var counts = new Dictionary<string, int>();
                var list = new List<AnimalSave>();
                foreach (var a in animals)
                {
                    string type = MiniJson.S(a, "type");
                    counts.TryGetValue(type, out int n);
                    counts[type] = n + 1;
                    bool producing = MiniJson.S(a, "status") == "producing";
                    list.Add(new AnimalSave
                    {
                        id = type + "_" + n, type = type,
                        fedUtc = producing ? Math.Max(1, (long)MiniJson.N(a, "started_at")) : 0,
                        hungrySinceUtc = producing ? 0 : (long)MiniJson.N(a, "hungry_since"),
                        happiness = (int)MiniJson.N(a, "happiness", GameConfig.StartHappiness),
                    });
                }
                d.animals = list;
            }

            var workshops = MiniJson.O(s, "workshops");
            if (workshops != null)
            {
                d.workshops.Clear();
                foreach (var kv in workshops)
                {
                    var w = new WorkshopSave { id = kv.Key };
                    var jobs = MiniJson.A(kv.Value, "jobs");
                    if (jobs != null)
                        foreach (var j in jobs) w.jobs.Add(new JobSave { recipe = MiniJson.S(j, "recipe"), startUtc = (long)MiniJson.N(j, "started_at") });
                    d.workshops.Add(w);
                }
            }

            // null on the server = the farm was never rearranged: the default farm
            if (s.TryGetValue("layout", out var lay))
            {
                d.layout = new List<PlacedItem>();
                if (lay is List<object> list) foreach (var o in list) d.layout.Add(Layout.Parse(o));
            }
            EnsureLayout(d);
        }

        // ------------------------------------------------------------- friend visits (read-only)

        SaveData _home;
        public string FriendCode { get; private set; } = "";
        public bool Visiting => _home != null;
        public string VisitCode { get; private set; } = "";
        public string VisitName { get; private set; } = "";
        public int VisitLevel { get; private set; }
        public int VisitLikes { get; set; }
        public bool VisitLiked { get; set; }
        public bool VisitGifted { get; set; }

        /// <summary>Shows a friend's farm (hf_visit result) in place of the player's own. Nothing on it can be changed or saved.</summary>
        public void BeginVisit(Dictionary<string, object> v)
        {
            if (_dirty) Save();
            var own = _home ?? Data;
            var guest = new SaveData
            {
                coins = own.coins, xp = own.xp, level = own.level, inventory = own.inventory,   // the top bar keeps showing the visitor
                storageLevel = (int)MiniJson.N(v, "storage_level"), serverLinked = true,
            };
            _home = own;
            Data = guest;
            ReadFarm(guest, v);
            while (guest.plots.Count < GameConfig.StartingPlots) guest.plots.Add(new PlotSave());
            EnsureLayout(guest);
            VisitCode = MiniJson.S(v, "code");
            VisitName = MiniJson.S(v, "display_name");
            VisitLevel = (int)MiniJson.N(v, "level", 1);
            VisitLikes = (int)MiniJson.N(v, "likes");
            VisitLiked = MiniJson.B(v, "liked");
            VisitGifted = MiniJson.B(v, "gifted");
            ServerApplied?.Invoke();
            Changed?.Invoke();
        }

        public void EndVisit()
        {
            if (_home == null) return;
            Data = _home;
            _home = null;
            ServerApplied?.Invoke();
            Changed?.Invoke();
        }
}
}
