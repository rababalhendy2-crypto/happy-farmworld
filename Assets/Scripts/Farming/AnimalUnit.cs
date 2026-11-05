using FarmWorld.Core;
using UnityEngine;

namespace FarmWorld.Farming
{
    /// <summary>
    /// A farm animal the player cares for. Shows a bobbing marker above it: a wheat bundle when hungry,
    /// its product (egg, milk, wool) when there is something to collect. Nothing while it is producing.
    /// </summary>
    public class AnimalUnit : MonoBehaviour
    {
        public string Id;
        public string Type;      // chicken | cow | sheep
        public float Height = 1.4f;

        Transform _marker;
        GameState.AnimalState _shown = (GameState.AnimalState)(-1);
        float _nextCheck;

        public AnimalSave Save => GameState.I != null ? GameState.I.Animal(Id, Type) : null;

        void Start()
        {
            // Small animals (chicks) get a bigger tap area so they are easy to hit with a finger.
            var col = GetComponent<BoxCollider>();
            if (col != null) col.size = Vector3.Max(col.size, new Vector3(1.3f, 1.3f, 1.3f));
            Refresh(true);
        }

        void Update()
        {
            if (_marker != null)
            {
                float t = Time.time * 2.4f + Id.GetHashCode() % 7;
                _marker.position = transform.position + Vector3.up * (Height + 0.5f + Mathf.Sin(t) * 0.12f);
                _marker.rotation = Quaternion.Euler(0, Time.time * 70f, 0);
            }
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + 0.5f;
            Refresh(false);
        }

        public void Refresh(bool force)
        {
            var s = Save; if (s == null) return;
            var st = GameState.I.StateOf(s);
            if (!force && st == _shown) return;
            _shown = st;
            if (_marker != null) Destroy(_marker.gameObject);
            _marker = null;
            if (st == GameState.AnimalState.Producing) return;

            // Marker lives outside the animal so it does not inherit the animal's scale.
            _marker = new GameObject("Marker_" + Id).transform;
            if (st == GameState.AnimalState.Hungry)
            {
                var feed = GameConfig.AnimalById.TryGetValue(Type, out var d) ? d.FeedItem : "wheat";
                string model = feed == "carrot" ? "Carrot" : "Wheat";
                if (ModelLibrary.Spawn("Crops/" + model + "_Crop.obj", _marker, Vector3.zero, 0.5f, ModelLibrary.Fit.Height, 0, null, false) == null)
                    Ball(_marker, Vector3.zero, new Vector3(0.3f, 0.3f, 0.3f), new Color(0.92f, 0.78f, 0.3f));
            }
            else ProductIcon(_marker, GameConfig.AnimalById.TryGetValue(Type, out var d) ? d.Product : "egg");
        }

        public static void ProductIcon(Transform parent, string product)
        {
            switch (product)
            {
                case "milk":
                    var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Prep(body, parent, new Vector3(0, 0, 0), new Vector3(0.28f, 0.2f, 0.28f), new Color(0.97f, 0.97f, 0.95f));
                    var cap = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Prep(cap, parent, new Vector3(0, 0.24f, 0), new Vector3(0.14f, 0.06f, 0.14f), new Color(0.25f, 0.55f, 0.9f));
                    break;
                case "wool":
                    Ball(parent, new Vector3(-0.1f, 0, 0), new Vector3(0.26f, 0.22f, 0.26f), new Color(0.98f, 0.97f, 0.93f));
                    Ball(parent, new Vector3(0.1f, 0.02f, 0.04f), new Vector3(0.24f, 0.22f, 0.24f), new Color(0.96f, 0.95f, 0.9f));
                    Ball(parent, new Vector3(0f, 0.12f, -0.04f), new Vector3(0.22f, 0.2f, 0.22f), new Color(1f, 0.99f, 0.95f));
                    break;
                default: // egg
                    Ball(parent, Vector3.zero, new Vector3(0.22f, 0.3f, 0.22f), new Color(0.99f, 0.93f, 0.8f));
                    break;
            }
        }

        static void Ball(Transform parent, Vector3 pos, Vector3 size, Color c) =>
            Prep(GameObject.CreatePrimitive(PrimitiveType.Sphere), parent, pos, size, c);

        static void Prep(GameObject go, Transform parent, Vector3 pos, Vector3 size, Color c)
        {
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = ModelLibrary.Solid(c, 0.35f);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void OnDestroy() { if (_marker != null) Destroy(_marker.gameObject); }
    }
}
