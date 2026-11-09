using System.Collections.Generic;
using FarmWorld.Core;
using UnityEngine;
using Fit = FarmWorld.Core.ModelLibrary.Fit;

namespace FarmWorld.World
{
    /// <summary>
    /// The player's farm drawn from the saved layout (GameState.Data.layout): fields, buildings, pens,
    /// fences, trees, decorations and pets, each one a LayoutPiece that can be moved or sold. The farmer
    /// boy and his dog walk around it.
    /// </summary>
    public partial class WorldBuilder
    {
        Transform _layoutRoot;
        readonly Dictionary<string, LayoutPiece> _pieces = new();
        Walker _farmer, _farmerDog;

        public IEnumerable<LayoutPiece> Pieces => _pieces.Values;

        void BuildLayoutRoot()
        {
            _layoutRoot = new GameObject("FarmLayout").transform;
            _layoutRoot.SetParent(transform, false);
            // the area where things may stand, marked by a low wooden fence
            Fence(new Rect(Layout.Area.xMin - 0.5f, Layout.Area.yMin - 0.5f, Layout.Area.width + 1f, Layout.Area.height + 1f), Hex("#a87a4f"), 4.5f);
        }

        /// <summary>Makes the world match the saved layout: spawns new pieces, moves moved ones, removes sold ones.</summary>
        public void SyncLayout()
        {
            var gs = GameState.I;
            if (gs == null || _layoutRoot == null) return;
            var keep = new HashSet<string>();
            foreach (var it in gs.Data.layout)
            {
                if (!Layout.Items.ContainsKey(it.t)) continue;
                keep.Add(it.u);
                if (_pieces.TryGetValue(it.u, out var pc) && pc != null && pc.Item.t == it.t && pc.Item.p == it.p)
                {
                    pc.Item = it;
                    pc.Place(it.x, it.z, it.r);
                }
                else
                {
                    if (pc != null) Destroy(pc.gameObject);
                    _pieces[it.u] = SpawnPiece(it, false);
                }
            }
            var gone = new List<string>();
            foreach (var kv in _pieces) if (!keep.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var u in gone) { if (_pieces[u] != null) Destroy(_pieces[u].gameObject); _pieces.Remove(u); }

            Fields.Clear();
            for (int i = 0; i < gs.Data.plots.Count; i++) Fields.Add(null);
            foreach (var pc in _pieces.Values)
                if (pc != null && pc.Item.t == "field" && pc.Item.p >= 0 && pc.Item.p < Fields.Count) Fields[pc.Item.p] = pc.GetComponent<Selectable>();

            if (_farmer == null) SpawnFarmer();
        }

        // ------------------------------------------------------------------ pieces

        public LayoutPiece SpawnPiece(PlacedItem it, bool ghost)
        {
            var def = Layout.Items[it.t];
            var root = new GameObject((ghost ? "Ghost_" : "P_") + it.u);
            root.transform.SetParent(_layoutRoot, false);
            var t = root.transform;
            var piece = root.AddComponent<LayoutPiece>();
            piece.Item = it; piece.Def = def; piece.Ghost = ghost;

            string kind = def.Cat == "pet" ? "pet" : def.Cat == "field" ? "field" : "decor";
            string id = it.t, title = "build_" + it.t;
            switch (def.Model)
            {
                case "proc:field": BuildFieldContent(t); piece.Size = new Vector2(4.2f, 4.0f); kind = "field"; id = "field_" + it.p; title = "field"; break;
                case "proc:coop": BuildCoopContent(t); piece.Size = new Vector2(7.2f, 7f); kind = "building"; id = "coop"; title = "bld_coop"; break;
                case "proc:silo": BuildSiloContent(t); piece.Size = new Vector2(2.8f, 2.8f); kind = "building"; id = "silo"; title = "bld_silo"; break;
                case "proc:pen": BuildPenContent(t); piece.Size = new Vector2(9.2f, 9.2f); kind = "decor"; title = "build_pen"; break;
                case "proc:fence_white": FenceSection(t, Hex("#f4efe6")); piece.Size = new Vector2(2f, 0.3f); break;
                case "proc:hedge":
                    Prim(PrimitiveType.Cube, t, new Vector3(0, 0.45f, 0), new Vector3(2f, 0.9f, 0.6f), Hex("#3f7f35"));
                    Prim(PrimitiveType.Cube, t, new Vector3(0, 0.75f, 0), new Vector3(1.9f, 0.4f, 0.5f), Hex("#4c9440"));
                    piece.Size = new Vector2(2f, 0.6f); break;
                default:
                    if (def.Cat == "pet") { SpawnPet(piece, t, def, ghost); piece.Size = Vector2.zero; break; }
                    var fit = def.Fit == "footprint" ? Fit.Footprint : def.Fit == "length" ? Fit.Length : Fit.Height;
                    var m = ModelLibrary.Spawn(def.Model, t, Vector3.zero, def.Size, fit, 0f);
                    if (m != null && def.Cat == "tree") m.AddComponent<WindSway>();
                    var b = ModelLibrary.LocalBounds(root, t);
                    piece.Size = new Vector2(b.size.x, b.size.z);
                    if (def.Cat == "tree") piece.Size = Vector2.one * (def.Size > 2f ? 1.6f : 1.0f);   // the trunk, not the crown
                    if (def.Cat == "decor" && def.Size < 0.7f) piece.Size = Vector2.one * 0.9f;
                    if (def.Cat == "fence") piece.Size = new Vector2(2f, 0.3f);
                    break;
            }
            if (it.t == "house" || it.t == "barn") { kind = "building"; id = it.t; title = "bld_" + it.t; }

            // A collider for taps: fields keep their own tall one; pens and coops get colliders only on
            // their walls, so taps on the animals inside still reach the animals.
            var sel = root.AddComponent<Selectable>();
            sel.Id = id; sel.Kind = kind; sel.TitleKey = title;
            if (kind == "field")
            {
                var col = root.AddComponent<BoxCollider>();
                col.center = new Vector3(0, 0.9f, 0); col.size = new Vector3(4.2f, 1.8f, 4.0f);
                if (!ghost) root.AddComponent<Farming.FieldPlot>().Index = it.p;
            }
            else if (def.Model != "proc:pen" && def.Model != "proc:coop" && def.Cat != "pet")
            {
                var b = ModelLibrary.LocalBounds(root, t);
                var col = root.AddComponent<BoxCollider>();
                col.center = b.center; col.size = b.size + Vector3.one * 0.2f;
                if (def.Cat == "fence") col.size = new Vector3(2f, 1.2f, 0.8f);
            }
            piece.Place(it.x, it.z, it.r);
            if (!ghost) piece.SpawnAnimals(this);
            return piece;
        }

        void FenceSection(Transform t, Color c)
        {
            foreach (var x in new[] { -0.93f, 0f, 0.93f })
                Prim(PrimitiveType.Cube, t, new Vector3(x, 0.45f, 0), new Vector3(0.14f, 0.9f, 0.14f), c);
            foreach (var y in new[] { 0.35f, 0.7f })
                Prim(PrimitiveType.Cube, t, new Vector3(0, y, 0), new Vector3(2f, 0.1f, 0.06f), c);
        }

        void BuildFieldContent(Transform t)
        {
            Prim(PrimitiveType.Cube, t, new Vector3(0, 0.12f, 0), new Vector3(4f, 0.24f, 3.8f), Hex("#7a4b2c"));
            for (int r = 0; r < Farming.FieldPlot.Rows; r++)
                Prim(PrimitiveType.Cube, t, new Vector3(0, 0.27f, Farming.FieldPlot.RowZ(r)), new Vector3(3.6f, 0.08f, 0.4f), Hex("#5e3720"));
        }

        /// <summary>Hen house with its yard in front (local -z). The chickens live in the yard.</summary>
        void BuildCoopContent(Transform root)
        {
            var house = new GameObject("CoopHouse").transform; house.SetParent(root, false); house.localPosition = new Vector3(0.5f, 0, 2f);
            var t = house;
            Prim(PrimitiveType.Cube, t, new Vector3(0, 1.1f, 0), new Vector3(3.2f, 1.6f, 2.4f), Hex("#e9d3a6"), default, true);
            for (int i = 0; i < 5; i++)
                Prim(PrimitiveType.Cube, t, new Vector3(-1.28f + i * 0.64f, 1.1f, 1.22f), new Vector3(0.08f, 1.6f, 0.04f), Hex("#b98b5c"));
            Prim(PrimitiveType.Cube, t, new Vector3(0, 2.25f, 0.62f), new Vector3(3.7f, 0.12f, 1.55f), Hex("#b5452f"), new Vector3(-28, 0, 0));
            Prim(PrimitiveType.Cube, t, new Vector3(0, 2.25f, -0.62f), new Vector3(3.7f, 0.12f, 1.55f), Hex("#b5452f"), new Vector3(28, 0, 0));
            Prim(PrimitiveType.Cube, t, new Vector3(0, 0.75f, -1.24f), new Vector3(0.6f, 0.7f, 0.05f), Hex("#4a2e1a"));
            foreach (var x in new[] { -1.4f, 1.4f })
                foreach (var z in new[] { -1f, 1f })
                    Prim(PrimitiveType.Cube, t, new Vector3(x, 0.15f, z), new Vector3(0.18f, 0.3f, 0.18f), Hex("#6e4a2c"));
            FenceLocal(root, new Rect(-3.5f, -3.5f, 7f, 4.3f), Hex("#d9b98a"), 1.5f, true);
        }

        void BuildSiloContent(Transform t)
        {
            Prim(PrimitiveType.Cylinder, t, new Vector3(0, 2.6f, 0), new Vector3(2.4f, 2.6f, 2.4f), Hex("#d7d2c8"));
            for (int i = 0; i < 4; i++)
                Prim(PrimitiveType.Cylinder, t, new Vector3(0, 0.9f + i * 1.25f, 0), new Vector3(2.46f, 0.05f, 2.46f), Hex("#9aa0a6"));
            Prim(PrimitiveType.Sphere, t, new Vector3(0, 5.2f, 0), new Vector3(2.45f, 1.6f, 2.45f), Hex("#b5452f"));
        }

        /// <summary>Animal pen: white fence, water trough and hay. Cows and sheep live inside.</summary>
        void BuildPenContent(Transform t)
        {
            FenceLocal(t, new Rect(-4.5f, -4.5f, 9f, 9f), Hex("#f4efe6"), 1.5f, true);
            Prim(PrimitiveType.Cube, t, new Vector3(3.5f, 0.25f, 3f), new Vector3(0.6f, 0.5f, 2.2f), Hex("#8a5a36"), default, true);
            Prim(PrimitiveType.Cube, t, new Vector3(3.5f, 0.47f, 3f), new Vector3(0.45f, 0.06f, 2.0f), Hex("#5cc3f0"));
            ModelLibrary.Spawn("Village/Hay.obj", t, new Vector3(-3.1f, 0, -3.5f), 1.0f, Fit.Height, 25f);
        }

        /// <summary>Fence around a local rect; with colliders = thin wall colliders for taps and moving.</summary>
        void FenceLocal(Transform parent, Rect r, Color c, float spacing, bool colliders)
        {
            void Side(Vector3 a, Vector3 b)
            {
                int posts = Mathf.Max(1, Mathf.RoundToInt((b - a).magnitude / spacing));
                for (int i = 0; i <= posts; i++)
                    Prim(PrimitiveType.Cube, parent, Vector3.Lerp(a, b, i / (float)posts) + Vector3.up * 0.45f, new Vector3(0.14f, 0.9f, 0.14f), c);
                var d = b - a; var mid = (a + b) / 2;
                float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                foreach (var y in new[] { 0.35f, 0.7f })
                    Prim(PrimitiveType.Cube, parent, mid + Vector3.up * y, new Vector3(0.06f, 0.1f, d.magnitude), c, new Vector3(0, yaw, 0));
                if (colliders)
                {
                    var g = new GameObject("Wall"); g.transform.SetParent(parent, false);
                    g.transform.localPosition = mid + Vector3.up * 0.6f; g.transform.localEulerAngles = new Vector3(0, yaw, 0);
                    g.AddComponent<BoxCollider>().size = new Vector3(0.7f, 1.2f, d.magnitude);
                }
            }
            var p0 = new Vector3(r.xMin, 0, r.yMin); var p1 = new Vector3(r.xMax, 0, r.yMin);
            var p2 = new Vector3(r.xMax, 0, r.yMax); var p3 = new Vector3(r.xMin, 0, r.yMax);
            Side(p0, p1); Side(p1, p2); Side(p2, p3); Side(p3, p0);
        }

        // ------------------------------------------------------------------ animals of pens and coops

        public GameObject SpawnAnimal(string model, string type, int n, Rect area, float length)
        {
            var pos = new Vector3(R(area.xMin, area.xMax), 0, R(area.yMin, area.yMax));
            var a = ModelLibrary.Spawn(model, _life, pos, length, Fit.Length, R(0, 360));
            if (a == null) return null;
            var w = a.AddComponent<AnimalWander>();
            w.Area = area;
            w.Speed = length < 1f ? 0.9f : 0.55f;
            Selectable.Make(a, "animal_" + type, "animal", "animal_" + type);
            var unit = a.AddComponent<Farming.AnimalUnit>();
            unit.Id = type + "_" + n; unit.Type = type; unit.Height = Mathf.Max(0.5f, length * 0.6f);
            return a;
        }

        // ------------------------------------------------------------------ farmer, his dog and pets

        static readonly Dictionary<string, Mesh[]> _frames = new();

        static Mesh[] Frames(string folder, string name, string anim, int count)
        {
            string key = folder + "/" + anim;
            if (_frames.TryGetValue(key, out var f)) return f;
            var list = new List<Mesh>();
            for (int i = 0; i < count; i++)
            {
                var p = ModelLibrary.Prefab($"Characters/{folder}/{name}_{anim}_{i}");
                var mf = p != null ? p.GetComponentInChildren<MeshFilter>() : null;
                if (mf != null) list.Add(mf.sharedMesh);
            }
            return _frames[key] = list.ToArray();
        }

        Walker SpawnWalker(Transform parent, string folder, float length, Fit fit, Vector3 pos)
        {
            var go = ModelLibrary.Spawn($"Characters/{folder}/{folder}_idle_0", parent, pos, length, fit, R(0, 360));
            if (go == null) return null;
            var w = go.AddComponent<Walker>();
            w.Idle = Frames(folder, folder, "idle", 4);
            w.Walk = Frames(folder, folder, "walk", 8);
            return w;
        }

        void SpawnPet(LayoutPiece piece, Transform t, BuildItemDef def, bool ghost)
        {
            string folder = def.Model.Substring("Characters/".Length);
            var w = SpawnWalker(t, folder, def.Size, Fit.Length, Vector3.zero);
            if (w == null) return;
            w.enabled = !ghost;
            w.Radius = 3f; w.Speed = def.Id == "cat" ? 0.7f : 1.1f;
            var col = t.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 0.4f, 0); col.size = new Vector3(1.4f, 0.9f, 1.4f);
            piece.Pet = w;
        }

        void SpawnFarmer()
        {
            var start = new Vector3(-1f, 0, 4f);
            _farmer = SpawnWalker(_life, "Boy", 1.25f, Fit.Height, start);
            if (_farmer == null) return;
            _farmer.name = "Farmer";
            _farmer.Speed = 1.3f; _farmer.Farmer = true; _farmer.Radius = 0f;
            Selectable.Make(_farmer.gameObject, "farmer", "farmer", "farmer_name");
            _farmerDog = SpawnWalker(_life, "Dog", 0.95f, Fit.Length, start + new Vector3(1.2f, 0, -0.6f));
            if (_farmerDog != null)
            {
                _farmerDog.name = "FarmerDog";
                _farmerDog.Speed = 1.6f; _farmerDog.Follow = _farmer.transform;
                Selectable.Make(_farmerDog.gameObject, "farmer_dog", "farmer", "farmer_dog_name");
            }
        }

        /// <summary>A spot for the farmer to walk to: next to a field (he looks after them) or anywhere on the farm.</summary>
        public Vector3 FarmerTarget()
        {
            if (Fields.Count > 0 && Random.value < 0.6f)
            {
                var f = Fields[Random.Range(0, Fields.Count)];
                if (f != null) return f.transform.position + new Vector3(Random.Range(-2.6f, 2.6f), 0, -2.4f);
            }
            var a = Layout.Area;
            return new Vector3(Random.Range(a.xMin + 2f, a.xMax - 2f), 0, Random.Range(a.yMin + 2f, a.yMax - 2f));
        }

        // ------------------------------------------------------------------ placement rules

        /// <summary>True when the piece fits at x,z,r: inside the farm area and not on top of something else.
        /// Pets can stand anywhere on the farm; fence pieces may touch each other.</summary>
        public bool CanPlace(LayoutPiece piece, float x, float z, int r)
        {
            var me = piece.FootprintAt(x, z, r);
            var a = Layout.Area;
            if (piece.Def.Cat == "pet")
                return x >= a.xMin && x <= a.xMax && z >= a.yMin && z <= a.yMax;
            if (me.xMin < a.xMin - 0.01f || me.xMax > a.xMax + 0.01f || me.yMin < a.yMin - 0.01f || me.yMax > a.yMax + 0.01f) return false;
            foreach (var other in _pieces.Values)
            {
                if (other == null || other == piece || other.Item.u == piece.Item.u || other.Def.Cat == "pet") continue;
                var o = other.FootprintAt(other.Item.x, other.Item.z, other.Item.r);
                const float eps = 0.06f;
                if (me.xMin < o.xMax - eps && me.xMax > o.xMin + eps && me.yMin < o.yMax - eps && me.yMax > o.yMin + eps) return false;
            }
            return true;
        }

        /// <summary>A free spot near p for a new piece (spiral search on the grid), or p itself if none is found.</summary>
        public Vector3 FreeSpotNear(LayoutPiece piece, Vector3 p, int r)
        {
            for (int ring = 0; ring < 30; ring++)
                for (int i = -ring; i <= ring; i++)
                    for (int j = -ring; j <= ring; j++)
                    {
                        if (Mathf.Max(Mathf.Abs(i), Mathf.Abs(j)) != ring) continue;
                        float x = Layout.Snap(p.x + i), z = Layout.Snap(p.z + j);
                        if (CanPlace(piece, x, z, r)) return new Vector3(x, 0, z);
                    }
            return p;
        }
    }

    /// <summary>One placed thing on the farm, linked to its saved PlacedItem.</summary>
    public class LayoutPiece : MonoBehaviour
    {
        public PlacedItem Item;
        public BuildItemDef Def;
        public bool Ghost;
        /// <summary>Footprint in metres at rotation 0 (x = width, y = depth).</summary>
        public Vector2 Size;
        public Walker Pet;
        readonly List<GameObject> _animals = new();
        GameObject _marker;
        Material _ok, _bad;

        public Rect FootprintAt(float x, float z, int r)
        {
            var s = (r & 1) == 0 ? Size : new Vector2(Size.y, Size.x);
            return new Rect(x - s.x / 2f, z - s.y / 2f, s.x, s.y);
        }

        public void Place(float x, float z, int r)
        {
            transform.position = new Vector3(x, 0, z);
            transform.rotation = Quaternion.Euler(0, r * 90f, 0);
            if (Pet != null) Pet.Home = transform.position;
            foreach (var a in _animals)
            {
                if (a == null) continue;
                var w = a.GetComponent<AnimalWander>();
                if (w == null) continue;
                var area = AnimalArea();
                w.Area = area;
                if (!area.Contains(new Vector2(a.transform.position.x, a.transform.position.z)))
                    a.transform.position = new Vector3(area.center.x, 0, area.center.y);
            }
        }

        /// <summary>World rect the animals of this pen or coop may walk in.</summary>
        Rect AnimalArea()
        {
            Rect local = Def.Model == "proc:pen" ? new Rect(-3.7f, -3.7f, 7.4f, 7.4f) : new Rect(-2.8f, -2.9f, 5.6f, 3f);
            var c1 = transform.TransformPoint(new Vector3(local.xMin, 0, local.yMin));
            var c2 = transform.TransformPoint(new Vector3(local.xMax, 0, local.yMax));
            return Rect.MinMaxRect(Mathf.Min(c1.x, c2.x), Mathf.Min(c1.z, c2.z), Mathf.Max(c1.x, c2.x), Mathf.Max(c1.z, c2.z));
        }

        public void SpawnAnimals(WorldBuilder world)
        {
            if (Def.Model == "proc:pen")
            {
                _animals.Add(world.SpawnAnimal("Animals/Cow", "cow", 0, AnimalArea(), 2.0f));
                _animals.Add(world.SpawnAnimal("Animals/Cow", "cow", 1, AnimalArea(), 2.0f));
                _animals.Add(world.SpawnAnimal("Animals/Sheep", "sheep", 0, AnimalArea(), 1.3f));
                _animals.Add(world.SpawnAnimal("Animals/Sheep", "sheep", 1, AnimalArea(), 1.3f));
            }
            else if (Def.Model == "proc:coop")
                for (int i = 0; i < 4; i++) _animals.Add(world.SpawnAnimal("Animals/Chick", "chicken", i, AnimalArea(), 0.75f));
        }

        /// <summary>Green or red patch under the piece while it is being moved or placed.</summary>
        public void ShowMarker(bool on, bool valid)
        {
            if (!on) { if (_marker != null) Destroy(_marker); _marker = null; return; }
            if (_marker == null)
            {
                _marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(_marker.GetComponent<Collider>());
                _marker.name = "Marker";
                _marker.transform.SetParent(transform, false);
                var s = Def.Cat == "pet" ? new Vector2(1.4f, 1.4f) : Size;
                _marker.transform.localPosition = new Vector3(0, 0.03f, 0);
                _marker.transform.localScale = new Vector3(s.x + 0.2f, 0.04f, s.y + 0.2f);
                _marker.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if (_ok == null) { _ok = ModelLibrary.Transparent(new Color(0.3f, 0.9f, 0.35f, 0.45f)); _bad = ModelLibrary.Transparent(new Color(0.95f, 0.25f, 0.2f, 0.5f)); }
            _marker.GetComponent<Renderer>().sharedMaterial = valid ? _ok : _bad;
        }

        void OnDestroy()
        {
            foreach (var a in _animals) if (a != null) Destroy(a);
        }
    }

    /// <summary>
    /// A character or pet shown as a flipbook of baked meshes (idle and walk). Pets stroll around their
    /// home spot; the farmer walks between the fields; the farmer's dog follows him.
    /// </summary>
    public class Walker : MonoBehaviour
    {
        public Mesh[] Idle, Walk;
        public float Speed = 1f, Radius = 3f;
        public Vector3 Home;
        public Transform Follow;
        public bool Farmer;

        MeshFilter _mf;
        Vector3 _target; float _wait; float _frameT; bool _moving;
        WorldBuilder _world;

        void Start()
        {
            _mf = GetComponentInChildren<MeshFilter>();
            if (Home == Vector3.zero) Home = transform.position;
            _world = GetComponentInParent<WorldBuilder>();
            if (_world == null && GameRoot.Instance != null) _world = GameRoot.Instance.World;
            _wait = Random.Range(0.5f, 3f);
            _target = transform.position;
        }

        void Update()
        {
            var pos = transform.position;
            if (Follow != null)
            {
                var behind = Follow.position - Follow.forward * 1.3f + Follow.right * 0.9f;
                float dist = (behind - pos).magnitude;
                if (dist > 0.6f) { _target = behind; _moving = true; }
                else if (dist < 0.3f) _moving = false;
                if (dist > 12f) transform.position = behind;   // fell far behind (after a long jump): catch up
            }
            else if (!_moving)
            {
                _wait -= Time.deltaTime;
                if (_wait <= 0f)
                {
                    if (Farmer && _world != null) _target = _world.FarmerTarget();
                    else _target = Home + new Vector3(Random.Range(-Radius, Radius), 0, Random.Range(-Radius, Radius));
                    var a = Layout.Area;
                    _target.x = Mathf.Clamp(_target.x, a.xMin + 0.5f, a.xMax - 0.5f);
                    _target.z = Mathf.Clamp(_target.z, a.yMin + 0.5f, a.yMax - 0.5f);
                    _moving = true;
                }
            }

            if (_moving)
            {
                var d = _target - pos; d.y = 0;
                if (d.magnitude < 0.12f)
                {
                    _moving = false;
                    _wait = Farmer ? Random.Range(2f, 5f) : Random.Range(2f, 7f);
                }
                else
                {
                    var dir = d.normalized;
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir, Vector3.up), 300f * Time.deltaTime);
                    float speed = Follow != null ? Mathf.Lerp(Speed * 0.6f, Speed * 1.8f, Mathf.Clamp01(d.magnitude / 4f)) : Speed;
                    float step = Mathf.Min(d.magnitude, speed * Time.deltaTime * Mathf.Clamp01(Vector3.Dot(transform.forward, dir) * 1.5f + 0.2f));
                    transform.position = new Vector3(pos.x + dir.x * step, 0f, pos.z + dir.z * step);
                }
            }

            var frames = _moving && Walk != null && Walk.Length > 0 ? Walk : Idle;
            if (_mf == null || frames == null || frames.Length == 0) return;
            _frameT += Time.deltaTime * (_moving ? 10f : 4f);
            _mf.sharedMesh = frames[(int)_frameT % frames.Length];
        }
    }
}
