using System;
using System.Collections.Generic;
using UnityEngine;

namespace FarmWorld.Core
{
    /// <summary>One piece on the farm: a field, building, fence piece, tree, decoration or pet.
    /// u = unique id, t = catalog id, x/z = position in metres, r = quarter turns, p = plot index (fields only).</summary>
    [Serializable]
    public class PlacedItem
    {
        public string u; public string t; public float x, z; public int r; public int p = -1;
        public PlacedItem Copy() => (PlacedItem)MemberwiseClone();
    }

    /// <summary>A thing the player can build or buy (game_config.json build_items; the server keeps the same list).</summary>
    public class BuildItemDef
    {
        public string Id, Cat, Model, Fit;
        public int Cost, Step, Level, Max;
        public float Size;
        public bool Sale;
    }

    /// <summary>The build catalog, the default farm and the area where things may stand.</summary>
    public static class Layout
    {
        public static readonly Dictionary<string, BuildItemDef> Items = new();
        public static readonly List<BuildItemDef> ItemList = new();
        public static readonly List<PlacedItem> Default = new();
        public static Rect Area = new Rect(-21f, -22f, 42f, 37f);
        public static readonly string[] Categories = { "field", "building", "fence", "tree", "decor", "pet" };
        public const float Grid = 0.5f;

        public static void Load(object root)
        {
            Items.Clear(); ItemList.Clear(); Default.Clear();
            var items = MiniJson.O(root, "build_items");
            if (items != null)
                foreach (var kv in items)
                {
                    var d = new BuildItemDef
                    {
                        Id = kv.Key, Cat = MiniJson.S(kv.Value, "cat"), Model = MiniJson.S(kv.Value, "model"), Fit = MiniJson.S(kv.Value, "fit", "height"),
                        Cost = (int)MiniJson.N(kv.Value, "cost"), Step = (int)MiniJson.N(kv.Value, "step"),
                        Level = Mathf.Max(1, (int)MiniJson.N(kv.Value, "level", 1)), Max = (int)MiniJson.N(kv.Value, "max", 99),
                        Size = (float)MiniJson.N(kv.Value, "size", 1), Sale = MiniJson.B(kv.Value, "sale"),
                    };
                    Items[d.Id] = d; ItemList.Add(d);
                }
            var def = MiniJson.A(root, "default_layout");
            if (def != null)
                foreach (var o in def)
                    Default.Add(new PlacedItem
                    {
                        u = MiniJson.S(o, "u"), t = MiniJson.S(o, "t"), x = (float)MiniJson.N(o, "x"), z = (float)MiniJson.N(o, "z"),
                        r = (int)MiniJson.N(o, "r"), p = (int)MiniJson.N(o, "p", -1),
                    });
            var a = MiniJson.O(root, "build_area");
            if (a != null) Area = new Rect((float)MiniJson.N(a, "x"), (float)MiniJson.N(a, "z"), (float)MiniJson.N(a, "w"), (float)MiniJson.N(a, "d"));
        }

        public static List<PlacedItem> DefaultCopy()
        {
            var l = new List<PlacedItem>();
            foreach (var d in Default) l.Add(d.Copy());
            return l;
        }

        public static int Count(List<PlacedItem> layout, string type)
        {
            int n = 0;
            foreach (var it in layout) if (it.t == type) n++;
            return n;
        }

        static int DefaultCount(string type)
        {
            int n = 0;
            foreach (var it in Default) if (it.t == type) n++;
            return n;
        }

        /// <summary>Price of the next one: fields get dearer with every field bought beyond the starting ones.</summary>
        public static int Price(List<PlacedItem> layout, BuildItemDef def) =>
            def.Cost + def.Step * Mathf.Max(0, Count(layout, def.Id) - DefaultCount(def.Id));

        public static float Snap(float v) => Mathf.Round(v / Grid) * Grid;

        public static PlacedItem Parse(object o) => new PlacedItem
        {
            u = MiniJson.S(o, "u"), t = MiniJson.S(o, "t"), x = (float)MiniJson.N(o, "x"), z = (float)MiniJson.N(o, "z"),
            r = ((int)MiniJson.N(o, "r") % 4 + 4) % 4, p = (int)MiniJson.N(o, "p", -1),
        };
    }
}
