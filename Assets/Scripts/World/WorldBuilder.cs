using System.Collections.Generic;
using UnityEngine;
using FarmWorld.Core;
using Fit = FarmWorld.Core.ModelLibrary.Fit;

namespace FarmWorld.World
{
    /// <summary>
    /// Builds the open world described in the design document, section 2 "World regions":
    /// main farm (centre), village (north), market (east), forest and lake (west),
    /// production area (south) and locked expansion lands at the corners.
    /// </summary>
    public class WorldBuilder : MonoBehaviour
    {
        public Rect PlayableBounds => new Rect(-42f, -40f, 84f, 82f);
        public Vector3 FarmCenter => new Vector3(0f, 0f, 0f);
        public readonly List<Selectable> Fields = new();

        public static readonly Dictionary<string, Vector3> ZoneCenters = new()
        {
            { "farm", new Vector3(0, 0, 0) },
            { "village", new Vector3(0, 0, 30) },
            { "market", new Vector3(28, 0, 6) },
            { "forest_lake", new Vector3(-30, 0, 4) },
            { "production", new Vector3(4, 0, -28) },
        };

        Transform _static, _life;
        System.Random _rng = new System.Random(7);

        float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        public void Build()
        {
            _static = new GameObject("Static").transform; _static.SetParent(transform, false);
            _life = new GameObject("Living").transform; _life.SetParent(transform, false);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 120f; RenderSettings.fogEndDistance = 260f;
            RenderSettings.fogColor = new Color(0.72f, 0.85f, 0.95f);

            BuildGround();
            BuildPaths();
            BuildFarm();
            BuildVillage();
            BuildMarket();
            BuildForestAndLake();
            BuildProduction();
            BuildExpansions();
            BuildBorderForest();
            BuildClouds();
        }

        // ------------------------------------------------------------------ helpers

        GameObject Prim(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Color c, Vector3 euler = default, bool collider = false)
        {
            var g = GameObject.CreatePrimitive(t);
            if (!collider) Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos; g.transform.localScale = scale; g.transform.localEulerAngles = euler;
            g.GetComponent<Renderer>().sharedMaterial = SolidCached(c);
            return g;
        }

        readonly Dictionary<Color, Material> _solids = new();
        Material SolidCached(Color c)
        {
            if (!_solids.TryGetValue(c, out var m)) _solids[c] = m = ModelLibrary.Solid(c);
            return m;
        }

        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        GameObject Building(string id, string model, Vector3 pos, float footprint, float yaw, string titleKey)
        {
            var g = ModelLibrary.Spawn("Village/" + model, _static, pos, footprint, Fit.Footprint, yaw);
            if (g == null) return null;
            g.name = id;
            Selectable.Make(g, id, "building", titleKey);
            return g;
        }

        // ------------------------------------------------------------------ ground

        void BuildGround()
        {
            // Large vertex-coloured grass plane with gentle colour variation, so the ground never looks flat-grey.
            const int n = 90; const float size = 180f;
            var mesh = new Mesh { name = "Ground", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            var v = new Vector3[(n + 1) * (n + 1)]; var uv = new Vector2[v.Length];
            for (int z = 0, i = 0; z <= n; z++)
                for (int x = 0; x <= n; x++, i++)
                {
                    float px = -size / 2 + size * x / n, pz = -size / 2 + size * z / n;
                    float edge = Mathf.Max(Mathf.Abs(px), Mathf.Abs(pz));
                    float hill = edge > 55f ? Mathf.PerlinNoise(px * 0.05f, pz * 0.05f) * (edge - 55f) * 0.35f : 0f;
                    v[i] = new Vector3(px, hill - 0.01f, pz);
                    uv[i] = new Vector2((float)x / n, (float)z / n);
                }
            var tris = new int[n * n * 6];
            for (int z = 0, t = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int i = z * (n + 1) + x;
                    tris[t++] = i; tris[t++] = i + n + 1; tris[t++] = i + 1;
                    tris[t++] = i + 1; tris[t++] = i + n + 1; tris[t++] = i + n + 2;
                }
            mesh.vertices = v; mesh.uv = uv; mesh.triangles = tris;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();

            // Grass colour variation baked into a texture (URP Lit ignores vertex colours).
            const int texSize = 512;
            var tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "GroundTex" };
            var g1 = Hex("#5aa43c"); var g2 = Hex("#7cbc4c"); var g3 = Hex("#4a8f33");
            var px32 = new Color32[texSize * texSize];
            for (int y = 0; y < texSize; y++)
                for (int x = 0; x < texSize; x++)
                {
                    float wx = (x / (float)texSize - 0.5f) * size, wz = (y / (float)texSize - 0.5f) * size;
                    float a = Mathf.PerlinNoise(wx * 0.06f + 3f, wz * 0.06f + 7f);
                    float b = Mathf.PerlinNoise(wx * 0.35f, wz * 0.35f);
                    float speck = Mathf.PerlinNoise(wx * 2.1f + 11f, wz * 2.1f) > 0.72f ? 0.06f : 0f;
                    var c = Color.Lerp(Color.Lerp(g3, g1, a), g2, b * 0.5f) * (1f + speck);
                    c.a = 1f;
                    px32[y * texSize + x] = c;
                }
            tex.SetPixels32(px32); tex.Apply(true, true);

            var go = new GameObject("Ground");
            go.transform.SetParent(_static, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var mat = ModelLibrary.Solid(Color.white, 0.05f);
            mat.SetTexture(ModelLibrary.UsesUrp ? "_BaseMap" : "_MainTex", tex);
            mr.sharedMaterial = mat;
            mr.receiveShadows = true;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        void Path(Vector3 a, Vector3 b, float width)
        {
            var mid = (a + b) * 0.5f; var d = b - a;
            Prim(PrimitiveType.Cube, _static, mid + Vector3.up * 0.01f, new Vector3(width, 0.04f, d.magnitude + width * 0.6f),
                Hex("#c9a46a"), new Vector3(0, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 0));
        }

        void BuildPaths()
        {
            Path(new Vector3(0, 0, 6), new Vector3(0, 0, 26), 3f);     // farm → village
            Path(new Vector3(6, 0, 4), new Vector3(24, 0, 6), 3f);     // farm → market
            Path(new Vector3(-6, 0, 2), new Vector3(-22, 0, 4), 2.6f); // farm → lake
            Path(new Vector3(2, 0, -6), new Vector3(4, 0, -22), 3f);   // farm → production
            Path(new Vector3(-8, 0, 30), new Vector3(8, 0, 30), 6f);   // village square
            Path(new Vector3(24, 0, 1), new Vector3(24, 0, 11), 5f);   // market lane
        }

        // ------------------------------------------------------------------ main farm

        void BuildFarm()
        {
            var farm = new GameObject("Zone_Farm").transform; farm.SetParent(_static, false);
            Building("house", "House_1.obj", new Vector3(-7f, 0, 9f), 6.5f, 180f, "bld_house");
            Building("barn", "Stable.obj", new Vector3(9f, 0, 9f), 7.5f, 180f, "bld_barn");
            BuildCoop(new Vector3(-12f, 0, -4f));
            BuildSilo(new Vector3(13.5f, 0, 9.5f));

            // Six starter fields in a 3 × 2 grid. What grows on them comes from the save (Farming.FieldPlot).
            for (int i = 0; i < 6; i++)
            {
                var p = new Vector3(-4.6f + (i % 3) * 4.6f, 0, -1.5f - (i / 3) * 4.4f);
                BuildField(i, p);
            }

            // Animal pen east of the fields
            var pen = new Rect(8f, -11f, 9f, 9f);
            Fence(pen, Hex("#f4efe6"));
            Prim(PrimitiveType.Cube, _static, new Vector3(16f, 0.25f, -3.5f), new Vector3(0.6f, 0.5f, 2.2f), Hex("#8a5a36"));
            Prim(PrimitiveType.Cube, _static, new Vector3(16f, 0.47f, -3.5f), new Vector3(0.45f, 0.06f, 2.0f), Hex("#5cc3f0"));
            ModelLibrary.Spawn("Village/Hay.obj", _static, new Vector3(9.4f, 0, -10f), 1.6f, Fit.Footprint, 25f);
            Animal("Animals/Cow", new Vector3(11f, 0, -5f), 2.0f, pen, "animal_cow");
            Animal("Animals/Cow", new Vector3(14f, 0, -8f), 2.0f, pen, "animal_cow");
            Animal("Animals/Sheep", new Vector3(10f, 0, -9f), 1.3f, pen, "animal_sheep");
            Animal("Animals/Sheep", new Vector3(13f, 0, -4f), 1.3f, pen, "animal_sheep");
            var coopYard = new Rect(-16f, -10f, 7f, 4.5f);
            Fence(coopYard, Hex("#d9b98a"));
            for (int i = 0; i < 4; i++)
                Animal("Animals/Chick", new Vector3(R(-15f, -10f), 0, R(-9.5f, -6.5f)), 0.55f, coopYard, "animal_chicken");

            // Garden details around the house
            for (int i = 0; i < 6; i++)
                ModelLibrary.Spawn("Nature/Flower_" + (i % 2 == 0 ? "3" : "4") + "_Group", _static,
                    new Vector3(-10.5f + i * 1.3f, 0, 5.6f), 0.55f, Fit.Height, R(0, 360), null, false);
            ModelLibrary.Spawn("Village/Well.obj", _static, new Vector3(-2.5f, 0, 6.5f), 2.2f, Fit.Footprint);
            ModelLibrary.Spawn("Village/Cart.obj", _static, new Vector3(4.2f, 0, 6.8f), 2.2f, Fit.Footprint, 70f);
            ModelLibrary.Spawn("Village/Bench_1.obj", _static, new Vector3(-4.6f, 0, 6.4f), 1.6f, Fit.Footprint, 0f);
            ModelLibrary.Spawn("Village/Barrel.obj", _static, new Vector3(5.6f, 0, 8.6f), 0.9f, Fit.Height);
            ModelLibrary.Spawn("Village/Crate.obj", _static, new Vector3(6.2f, 0, 7.6f), 0.8f, Fit.Height, 20f);
            Fence(new Rect(-17.5f, -14f, 35f, 27f), Hex("#a87a4f"), 4.5f);
        }

        void BuildField(int index, Vector3 pos)
        {
            var root = new GameObject("Field_" + index); root.transform.SetParent(_static, false); root.transform.localPosition = pos;
            Prim(PrimitiveType.Cube, root.transform, new Vector3(0, 0.12f, 0), new Vector3(4f, 0.24f, 3.8f), Hex("#7a4b2c"));
            for (int r = 0; r < 4; r++)
                Prim(PrimitiveType.Cube, root.transform, new Vector3(0, 0.26f, -1.35f + r * 0.9f), new Vector3(3.6f, 0.08f, 0.42f), Hex("#5e3720"));
            var sel = Selectable.Make(root, "field_" + index, "field", "field");
            // The collider must also cover tall crops planted later.
            var col = root.GetComponent<BoxCollider>();
            col.center = new Vector3(0, 0.9f, 0); col.size = new Vector3(4.2f, 1.8f, 4.0f);
            root.AddComponent<Farming.FieldPlot>().Index = index;
            Fields.Add(sel);
        }

        void BuildCoop(Vector3 p)
        {
            var root = new GameObject("coop"); root.transform.SetParent(_static, false); root.transform.localPosition = p;
            var t = root.transform;
            Prim(PrimitiveType.Cube, t, new Vector3(0, 1.1f, 0), new Vector3(3.2f, 1.6f, 2.4f), Hex("#e9d3a6"));
            for (int i = 0; i < 5; i++)
                Prim(PrimitiveType.Cube, t, new Vector3(-1.28f + i * 0.64f, 1.1f, 1.22f), new Vector3(0.08f, 1.6f, 0.04f), Hex("#b98b5c"));
            Prim(PrimitiveType.Cube, t, new Vector3(0, 2.25f, 0.62f), new Vector3(3.7f, 0.12f, 1.55f), Hex("#b5452f"), new Vector3(-28, 0, 0));
            Prim(PrimitiveType.Cube, t, new Vector3(0, 2.25f, -0.62f), new Vector3(3.7f, 0.12f, 1.55f), Hex("#b5452f"), new Vector3(28, 0, 0));
            Prim(PrimitiveType.Cube, t, new Vector3(0, 0.75f, 1.24f), new Vector3(0.6f, 0.7f, 0.05f), Hex("#4a2e1a"));
            Prim(PrimitiveType.Cube, t, new Vector3(0, 0.3f, 1.8f), new Vector3(0.5f, 0.06f, 1.2f), Hex("#9b7048"), new Vector3(-20, 0, 0));
            foreach (var x in new[] { -1.4f, 1.4f })
                foreach (var z in new[] { -1f, 1f })
                    Prim(PrimitiveType.Cube, t, new Vector3(x, 0.15f, z), new Vector3(0.18f, 0.3f, 0.18f), Hex("#6e4a2c"));
            Selectable.Make(root, "coop", "building", "bld_coop");
        }

        void BuildSilo(Vector3 p)
        {
            var root = new GameObject("silo"); root.transform.SetParent(_static, false); root.transform.localPosition = p;
            Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0, 2.6f, 0), new Vector3(2.4f, 2.6f, 2.4f), Hex("#d7d2c8"));
            for (int i = 0; i < 4; i++)
                Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0, 0.9f + i * 1.25f, 0), new Vector3(2.46f, 0.05f, 2.46f), Hex("#9aa0a6"));
            Prim(PrimitiveType.Sphere, root.transform, new Vector3(0, 5.2f, 0), new Vector3(2.45f, 1.6f, 2.45f), Hex("#b5452f"));
            Selectable.Make(root, "silo", "building", "bld_silo");
        }

        void Fence(Rect r, Color c, float spacing = 1.5f)
        {
            var root = new GameObject("Fence").transform; root.SetParent(_static, false);
            void Side(Vector3 a, Vector3 b)
            {
                int posts = Mathf.Max(1, Mathf.RoundToInt((b - a).magnitude / spacing));
                for (int i = 0; i <= posts; i++)
                    Prim(PrimitiveType.Cube, root, Vector3.Lerp(a, b, i / (float)posts) + Vector3.up * 0.45f, new Vector3(0.14f, 0.9f, 0.14f), c);
                var d = b - a; var mid = (a + b) / 2;
                float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                foreach (var y in new[] { 0.35f, 0.7f })
                    Prim(PrimitiveType.Cube, root, mid + Vector3.up * y, new Vector3(0.06f, 0.1f, d.magnitude), c, new Vector3(0, yaw, 0));
            }
            var p0 = new Vector3(r.xMin, 0, r.yMin); var p1 = new Vector3(r.xMax, 0, r.yMin);
            var p2 = new Vector3(r.xMax, 0, r.yMax); var p3 = new Vector3(r.xMin, 0, r.yMax);
            Side(p0, p1); Side(p1, p2); Side(p2, p3); Side(p3, p0);
        }

        void Animal(string model, Vector3 pos, float length, Rect area, string titleKey)
        {
            var a = ModelLibrary.Spawn(model, _life, pos, length, Fit.Length, R(0, 360));
            if (a == null) return;
            var w = a.AddComponent<AnimalWander>();
            w.Area = new Rect(area.xMin + 0.8f, area.yMin + 0.8f, area.width - 1.6f, area.height - 1.6f);
            w.Speed = length < 1f ? 0.9f : 0.55f;
            Selectable.Make(a, titleKey, "animal", titleKey);
            string type = titleKey.Replace("animal_", "");
            _animalCount.TryGetValue(type, out int n);
            _animalCount[type] = n + 1;
            var unit = a.AddComponent<Farming.AnimalUnit>();
            unit.Id = type + "_" + n; unit.Type = type; unit.Height = Mathf.Max(0.5f, length * 0.6f);
        }

        readonly Dictionary<string, int> _animalCount = new();

        // ------------------------------------------------------------------ other regions

        void BuildVillage()
        {
            Building("inn", "Inn.obj", new Vector3(-10f, 0, 36f), 9f, 180f, "bld_inn");
            Building("blacksmith", "Blacksmith.obj", new Vector3(11f, 0, 36f), 7f, 180f, "bld_tools");
            Building("village_house_a", "House_1.obj", new Vector3(-17f, 0, 26f), 6f, 90f, "bld_village_house");
            Building("village_house_b", "House_1.obj", new Vector3(17f, 0, 26f), 6f, -90f, "bld_village_house");
            ModelLibrary.Spawn("Village/Well.obj", _static, new Vector3(0f, 0, 32f), 2.6f, Fit.Footprint);
            for (int i = 0; i < 4; i++)
                ModelLibrary.Spawn("Village/Bench_1.obj", _static, new Vector3(-6f + i * 4f, 0, 27.4f), 1.6f, Fit.Footprint, 180f);
            ZoneSign("village", new Vector3(3f, 0, 24f));
        }

        void BuildMarket()
        {
            for (int i = 0; i < 3; i++)
            {
                Building("stall_w" + i, i % 2 == 0 ? "MarketStand_1.obj" : "MarketStand_2.obj", new Vector3(20.5f, 0, 1.5f + i * 4f), 3f, 90f, "bld_market");
                Building("stall_e" + i, i % 2 == 0 ? "MarketStand_2.obj" : "MarketStand_1.obj", new Vector3(27.5f, 0, 1.5f + i * 4f), 3f, -90f, "bld_market");
            }
            for (int i = 0; i < 5; i++)
                ModelLibrary.Spawn(i % 2 == 0 ? "Village/Barrel.obj" : "Village/Crate.obj", _static,
                    new Vector3(R(21.5f, 26.5f), 0, R(13f, 16f)), 0.85f, Fit.Height, R(0, 360));
            ModelLibrary.Spawn("Village/Cart.obj", _static, new Vector3(30f, 0, 14f), 2.4f, Fit.Footprint, -30f);
            ZoneSign("market", new Vector3(20f, 0, -1.5f));
        }

        void BuildForestAndLake()
        {
            var lake = new Vector3(-30f, 0, 8f);
            Prim(PrimitiveType.Cylinder, _static, lake + Vector3.up * 0.02f, new Vector3(17f, 0.02f, 13f), Hex("#d9c38f"));
            var water = Prim(PrimitiveType.Cylinder, _static, lake + Vector3.up * 0.06f, new Vector3(15.5f, 0.02f, 11.5f), Color.white);
            water.GetComponent<Renderer>().sharedMaterial = ModelLibrary.Transparent(new Color(0.22f, 0.62f, 0.86f, 0.82f));
            water.AddComponent<WaterSurface>();
            water.name = "Lake";
            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f * Mathf.PI * 2f;
                ModelLibrary.Spawn("Nature/" + (i % 3 == 0 ? "Fern_1" : "Grass_Common_Tall"), _static,
                    lake + new Vector3(Mathf.Cos(a) * 8.3f, 0, Mathf.Sin(a) * 6.3f), R(0.6f, 0.9f), Fit.Height, R(0, 360), null, false);
            }
            ModelLibrary.Spawn("Nature/Pebble_Round_3", _static, lake + new Vector3(7f, 0, -3f), 0.6f, Fit.Height);
            // Forest west of the lake
            for (int i = 0; i < 26; i++)
            {
                var p = new Vector3(R(-44f, -24f), 0, R(-22f, 24f));
                if ((p - lake).magnitude < 10f) continue;
                Tree(p, R(5f, 8.5f));
            }
            for (int i = 0; i < 14; i++)
                ModelLibrary.Spawn(i % 2 == 0 ? "Nature/Bush_Common" : "Nature/Mushroom_Common", _static,
                    new Vector3(R(-40f, -20f), 0, R(-18f, 22f)), i % 2 == 0 ? R(0.9f, 1.4f) : 0.35f, Fit.Height, R(0, 360));
            ZoneSign("forest_lake", new Vector3(-21f, 0, 0f));
        }

        void BuildProduction()
        {
            Building("mill", "Mill.obj", new Vector3(-6f, 0, -30f), 7f, 0f, "bld_mill");
            Building("dairy", "Stable.obj", new Vector3(8f, 0, -31f), 7f, 0f, "bld_dairy");
            Building("warehouse", "Blacksmith.obj", new Vector3(20f, 0, -27f), 6.5f, -30f, "bld_warehouse");
            for (int i = 0; i < 3; i++)
                ModelLibrary.Spawn("Village/Hay.obj", _static, new Vector3(1f + i * 1.8f, 0, -23.5f), 1.5f, Fit.Footprint, R(0, 90));
            ZoneSign("production", new Vector3(6f, 0, -20f));
        }

        void BuildExpansions()
        {
            var spots = new[] { new Vector3(-24f, 0, -28f), new Vector3(30f, 0, -16f), new Vector3(30f, 0, 30f), new Vector3(-28f, 0, 32f) };
            for (int i = 0; i < spots.Length; i++)
            {
                var r = new Rect(spots[i].x - 5f, spots[i].z - 5f, 10f, 10f);
                Fence(r, Hex("#8f8a80"), 2.5f);
                var sign = new GameObject("expansion_" + i); sign.transform.SetParent(_static, false); sign.transform.localPosition = spots[i];
                Prim(PrimitiveType.Cube, sign.transform, new Vector3(0, 0.8f, 0), new Vector3(0.15f, 1.6f, 0.15f), Hex("#6e4a2c"));
                Prim(PrimitiveType.Cube, sign.transform, new Vector3(0, 1.5f, 0.1f), new Vector3(1.6f, 0.9f, 0.1f), Hex("#ffd166"));
                Prim(PrimitiveType.Sphere, sign.transform, new Vector3(0, 1.5f, 0.17f), new Vector3(0.35f, 0.42f, 0.05f), Hex("#7a5a3a"));
                Selectable.Make(sign, "expansion_" + i, "locked", "zone_expansion");
            }
        }

        void BuildBorderForest()
        {
            for (int i = 0; i < 70; i++)
            {
                float a = R(0, Mathf.PI * 2f), d = R(50f, 72f);
                var p = new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d);
                Tree(p, R(6f, 10f));
            }
            // scattered trees inside the map edges, away from regions
            for (int i = 0; i < 24; i++)
            {
                var p = new Vector3(R(-40f, 40f), 0, R(-38f, 42f));
                bool clear = true;
                foreach (var z in ZoneCenters.Values) if ((p - z).magnitude < 15f) clear = false;
                if (clear) Tree(p, R(4.5f, 7f));
            }
            for (int i = 0; i < 120; i++)
            {
                var p = new Vector3(R(-45f, 45f), 0, R(-42f, 45f));
                if (Mathf.Abs(p.x) < 18f && Mathf.Abs(p.z) < 15f) continue;
                ModelLibrary.Spawn(i % 4 == 0 ? "Nature/Clover_1" : "Nature/Grass_Common_Short", _static, p, R(0.25f, 0.45f), Fit.Height, R(0, 360), null, false);
            }
        }

        void Tree(Vector3 p, float h)
        {
            string[] kinds = { "CommonTree_1", "CommonTree_2", "CommonTree_3", "CommonTree_4", "CommonTree_5", "Pine_1", "Pine_2", "Pine_3" };
            var t = ModelLibrary.Spawn("Nature/" + kinds[_rng.Next(kinds.Length)], _static, p, h, Fit.Height, R(0, 360));
            if (t != null) t.AddComponent<WindSway>();
        }

        void ZoneSign(string zone, Vector3 p)
        {
            var root = new GameObject("sign_" + zone); root.transform.SetParent(_static, false); root.transform.localPosition = p;
            Prim(PrimitiveType.Cube, root.transform, new Vector3(0, 0.9f, 0), new Vector3(0.16f, 1.8f, 0.16f), Hex("#6e4a2c"));
            Prim(PrimitiveType.Cube, root.transform, new Vector3(0, 1.7f, 0), new Vector3(1.9f, 0.6f, 0.12f), Hex("#e8c88f"));
            Selectable.Make(root, zone, "zone", "zone_" + zone);
        }

        void BuildClouds()
        {
            var root = new GameObject("Clouds"); root.transform.SetParent(transform, false);
            for (int i = 0; i < 9; i++)
            {
                var c = new GameObject("Cloud"); c.transform.SetParent(root.transform, false);
                c.transform.localPosition = new Vector3(R(-80, 80), R(24, 32), R(-80, 80));
                int puffs = _rng.Next(3, 6);
                for (int k = 0; k < puffs; k++)
                {
                    var s = Prim(PrimitiveType.Sphere, c.transform, new Vector3(k * 2.2f - puffs, R(-0.4f, 0.6f), R(-1, 1)),
                        new Vector3(R(3.5f, 5f), R(2f, 3f), R(3f, 4f)), new Color(1f, 1f, 1f));
                    s.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                c.AddComponent<CloudDrift>().Speed = R(0.6f, 1.4f);
            }
        }
    }
}
