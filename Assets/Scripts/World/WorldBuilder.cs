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
    public partial class WorldBuilder : MonoBehaviour
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
            // No 3D clouds: with the orthographic camera they looked like white blobs on the ground.
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
            // Everything on the farm itself comes from the saved layout (WorldBuilder.Layout.cs), so it can be moved.
            BuildLayoutRoot();
            SyncLayout();
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
                if (Layout.Area.Overlaps(new Rect(p.x - 4f, p.z - 4f, 8f, 8f))) clear = false;
                if (clear) Tree(p, R(4.5f, 7f));
            }
            for (int i = 0; i < 120; i++)
            {
                var p = new Vector3(R(-45f, 45f), 0, R(-42f, 45f));
                if (Mathf.Abs(p.x) < 18f && Mathf.Abs(p.z) < 15f) continue;
                if (Layout.Area.Contains(new Vector2(p.x, p.z))) continue;
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
