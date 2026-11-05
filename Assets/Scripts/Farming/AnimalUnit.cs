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
            if (col != null) col.size = Vector3.Max(col.size * 1.3f, new Vector3(1.6f, 1.4f, 1.6f));
            Refresh(true);
        }

        void Update()
        {
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
            if (st != GameState.AnimalState.Ready) return;

            // The product is laid on the ground where the animal stands (no floating icons).
            _marker = new GameObject("Product_" + Id).transform;
            var p = transform.position; p.y = 0f;
            var side = transform.right * (Height * 0.45f + 0.25f);
            _marker.position = p + new Vector3(side.x, 0f, side.z);
            _marker.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            // Straw nest under the product so it reads as "something to pick up" from the iso camera.
            var nest = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Prep(nest, _marker, new Vector3(0, 0.03f, 0), new Vector3(0.95f, 0.03f, 0.95f), new Color(0.86f, 0.72f, 0.38f));
            var icon = new GameObject("Icon").transform; icon.SetParent(_marker, false);
            icon.localPosition = new Vector3(0, 0.05f, 0); icon.localScale = Vector3.one * 1.9f;
            ProductIcon(icon, GameConfig.AnimalById.TryGetValue(Type, out var d) ? d.Product : "egg");
            var col = _marker.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 0.4f, 0); col.size = new Vector3(1.4f, 1.0f, 1.4f);
            var sel = _marker.gameObject.AddComponent<World.Selectable>();
            sel.Id = Id + "_product"; sel.Kind = "product"; sel.TitleKey = GetComponent<World.Selectable>()?.TitleKey;
            _marker.gameObject.AddComponent<ProductDrop>().Unit = this;
        }

        public static void ProductIcon(Transform parent, string product)
        {
            switch (product)
            {
                case "milk":
                    var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Prep(body, parent, new Vector3(0, 0.2f, 0), new Vector3(0.28f, 0.2f, 0.28f), new Color(0.97f, 0.97f, 0.95f));
                    var cap = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Prep(cap, parent, new Vector3(0, 0.44f, 0), new Vector3(0.14f, 0.06f, 0.14f), new Color(0.25f, 0.55f, 0.9f));
                    break;
                case "wool":
                    Ball(parent, new Vector3(-0.1f, 0.1f, 0), new Vector3(0.26f, 0.22f, 0.26f), new Color(0.98f, 0.97f, 0.93f));
                    Ball(parent, new Vector3(0.1f, 0.1f, 0.04f), new Vector3(0.24f, 0.22f, 0.24f), new Color(0.96f, 0.95f, 0.9f));
                    Ball(parent, new Vector3(0f, 0.2f, -0.04f), new Vector3(0.22f, 0.2f, 0.22f), new Color(1f, 0.99f, 0.95f));
                    break;
                default: // egg
                    Ball(parent, new Vector3(0, 0.14f, 0), new Vector3(0.2f, 0.28f, 0.2f), new Color(0.99f, 0.93f, 0.8f));
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
        }

        void OnDestroy() { if (_marker != null) Destroy(_marker.gameObject); }
    }

    /// <summary>A collectable product lying on the ground; tapping it collects from its animal.</summary>
    public class ProductDrop : MonoBehaviour
    {
        public AnimalUnit Unit;
    }
}
