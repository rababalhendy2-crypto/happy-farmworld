using UnityEngine;

namespace FarmWorld.World
{
    /// <summary>Animals stroll to random points inside their pen, pause, and graze.</summary>
    public class AnimalWander : MonoBehaviour
    {
        public Rect Area;
        public float Speed = 0.6f;
        Vector3 _target; float _wait; float _phase;

        void Start() { _phase = Random.value * 10f; Pick(); _wait = Random.Range(0f, 3f); }

        void Pick() => _target = new Vector3(Random.Range(Area.xMin, Area.xMax), 0f, Random.Range(Area.yMin, Area.yMax));

        void Update()
        {
            var p = transform.localPosition;
            if (_wait > 0f) { _wait -= Time.deltaTime; return; }
            var d = _target - new Vector3(p.x, 0f, p.z);
            if (d.magnitude < 0.15f) { _wait = Random.Range(2f, 6f); Pick(); return; }
            var dir = d.normalized;
            var want = Quaternion.LookRotation(dir, Vector3.up);
            transform.localRotation = Quaternion.RotateTowards(transform.localRotation, want, 140f * Time.deltaTime);
            float step = Speed * Time.deltaTime * Mathf.Clamp01(Vector3.Dot(transform.forward, dir) * 1.5f);
            _phase += Time.deltaTime * Speed * 9f;
            transform.localPosition = new Vector3(p.x + dir.x * step, Mathf.Abs(Mathf.Sin(_phase)) * 0.04f, p.z + dir.z * step);
        }
    }

    /// <summary>Gentle wind sway for trees and tall plants.</summary>
    public class WindSway : MonoBehaviour
    {
        Quaternion _base; float _seed;
        void Start() { _base = transform.localRotation; _seed = Random.value * 100f; }
        void Update()
        {
            float t = Time.time * 0.9f + _seed;
            transform.localRotation = _base * Quaternion.Euler(Mathf.Sin(t) * 1.1f, 0f, Mathf.Sin(t * 0.7f + 1.3f) * 0.9f);
        }
    }

    /// <summary>Lake surface: slow bob and colour shimmer.</summary>
    public class WaterSurface : MonoBehaviour
    {
        Renderer _r; Color _c; Vector3 _p;
        void Start()
        {
            _r = GetComponent<Renderer>(); _p = transform.localPosition;
            _c = _r.sharedMaterial.HasProperty("_BaseColor") ? _r.sharedMaterial.GetColor("_BaseColor") : _r.sharedMaterial.color;
        }
        void Update()
        {
            float t = Time.time;
            transform.localPosition = _p + Vector3.up * Mathf.Sin(t * 0.8f) * 0.015f;
            var c = Color.Lerp(_c, _c * 1.12f, (Mathf.Sin(t * 0.5f) + 1f) * 0.5f); c.a = _c.a;
            if (_r.sharedMaterial.HasProperty("_BaseColor")) _r.sharedMaterial.SetColor("_BaseColor", c);
            else _r.sharedMaterial.color = c;
        }
    }

    public class CloudDrift : MonoBehaviour
    {
        public float Speed = 1f;
        void Update()
        {
            var p = transform.localPosition;
            p.x += Speed * Time.deltaTime;
            if (p.x > 95f) p.x = -95f;
            transform.localPosition = p;
        }
    }
}
