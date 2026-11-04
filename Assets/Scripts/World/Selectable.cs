using UnityEngine;

namespace FarmWorld.World
{
    /// <summary>Something the player can tap: a building, a zone sign, an animal, a field.</summary>
    public class Selectable : MonoBehaviour
    {
        public string Id;
        public string Kind;          // building | zone | animal | field | locked
        public string TitleKey;      // i18n key for the name shown when selected

        Vector3 _base; float _pulse;

        void Awake() => _base = transform.localScale;

        public void SetHighlighted(bool on)
        {
            if (on) _pulse = 1f;
        }

        void Update()
        {
            if (_pulse <= 0f) return;
            _pulse = Mathf.Max(0f, _pulse - Time.deltaTime * 3f);
            float s = 1f + Mathf.Sin((1f - _pulse) * Mathf.PI) * 0.06f;
            transform.localScale = _base * s;
        }

        /// <summary>Adds a box collider that wraps all renderers below this object.</summary>
        public static Selectable Make(GameObject go, string id, string kind, string titleKey)
        {
            var b = Core.ModelLibrary.LocalBounds(go, go.transform);
            var col = go.AddComponent<BoxCollider>();
            col.center = b.center; col.size = b.size + Vector3.one * 0.2f;
            var s = go.AddComponent<Selectable>();
            s.Id = id; s.Kind = kind; s.TitleKey = titleKey;
            return s;
        }
    }
}
