using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FarmWorld.Core
{
    /// <summary>
    /// Loads CC0 models from Resources/Models, sizes them to real-world metres and makes their
    /// materials render correctly under URP (or the built-in pipeline as a fallback).
    /// </summary>
    public static class ModelLibrary
    {
        static readonly Dictionary<string, GameObject> Prefabs = new();
        static readonly Dictionary<int, Material> Converted = new();
        static Shader _lit;

        public static Shader LitShader
        {
            get
            {
                if (_lit != null) return _lit;
                if (GraphicsSettings.currentRenderPipeline != null)
                    _lit = Shader.Find("Universal Render Pipeline/Lit");
                if (_lit == null) _lit = Shader.Find("Standard");
                return _lit;
            }
        }

        public static bool UsesUrp => LitShader != null && LitShader.name.StartsWith("Universal");

        public static GameObject Prefab(string path)
        {
            if (Prefabs.TryGetValue(path, out var p)) return p;
            // Resources.Load paths must not carry the file extension ("House_1.obj" -> "House_1").
            string ext = System.IO.Path.GetExtension(path);
            string key = ext.Length > 0 ? path.Substring(0, path.Length - ext.Length) : path;
            p = Resources.Load<GameObject>("Models/" + key);
            if (p == null) Debug.LogWarning("[ModelLibrary] missing model " + path);
            Prefabs[path] = p;
            return p;
        }

        public enum Fit { Height, Footprint, Length }

        /// <summary>Instantiate a model, scaled so the chosen dimension equals size, sitting on y=0 and centred.</summary>
        public static GameObject Spawn(string path, Transform parent, Vector3 position, float size, Fit fit = Fit.Height,
            float yaw = 0f, Color? tint = null, bool castShadows = true)
        {
            var prefab = Prefab(path);
            if (prefab == null) return null;
            var pivot = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path));
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = position;
            pivot.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            var model = Object.Instantiate(prefab, pivot.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = prefab.transform.localRotation;
            bool objColors = path.StartsWith("Village/") || path.StartsWith("Crops/") || path.EndsWith("Chick");
            FixMaterials(model, objColors, tint, castShadows);

            var b = LocalBounds(model, pivot.transform);
            float measure = fit switch
            {
                Fit.Height => b.size.y,
                Fit.Footprint => Mathf.Max(b.size.x, b.size.z),
                _ => Mathf.Max(b.size.x, b.size.z),
            };
            float k = size / Mathf.Max(measure, 0.0001f);
            model.transform.localScale = model.transform.localScale * k;
            b = LocalBounds(model, pivot.transform);
            model.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
            return pivot;
        }

        /// <summary>Bounds of all renderers expressed in the space of 'space'.</summary>
        public static Bounds LocalBounds(GameObject go, Transform space)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            bool any = false;
            var result = new Bounds();
            foreach (var r in rs)
            {
                Bounds mb;
                if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null) mb = smr.sharedMesh.bounds;
                else
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    mb = mf.sharedMesh.bounds;
                }
                var m = space.worldToLocalMatrix * r.transform.localToWorldMatrix;
                var c = mb.center; var e = mb.extents;
                for (int i = 0; i < 8; i++)
                {
                    var corner = c + Vector3.Scale(e, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (!any) { result = new Bounds(p, Vector3.zero); any = true; }
                    else result.Encapsulate(p);
                }
            }
            return result;
        }

        static readonly string[] CutoutHints = { "leaf", "leaves", "grass", "flower", "petal", "fern", "clover", "plant" };

        public static void FixMaterials(GameObject go, bool objColors, Color? tint, bool castShadows)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null) mats[i] = Convert(mats[i], objColors, tint);
                r.sharedMaterials = mats;
            }
        }

        static Material Convert(Material src, bool objColors, Color? tint)
        {
            int key = src.GetInstanceID() * 31 + (objColors ? 1 : 0) + (tint.HasValue ? tint.Value.GetHashCode() : 0);
            if (Converted.TryGetValue(key, out var done)) return done;

            Color col = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor")
                      : src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white;
            Texture tex = src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap")
                        : src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null;
            if (objColors && tex == null) col = col.gamma;   // .mtl Kd values are linear
            if (tint.HasValue) col *= tint.Value;
            col.a = 1f;

            string n = (src.name + " " + (tex != null ? tex.name : "")).ToLowerInvariant();
            bool cutout = tex != null && System.Array.Exists(CutoutHints, h => n.Contains(h));
            var m = NewMaterial(cutout ? "FW_Cutout" : "FW_Opaque");
            m.name = src.name + "_fw";
            bool urp = UsesUrp;
            m.SetColor(urp ? "_BaseColor" : "_Color", col);
            if (tex != null) m.SetTexture(urp ? "_BaseMap" : "_MainTex", tex);
            m.SetFloat(urp ? "_Smoothness" : "_Glossiness", 0.18f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);

            if (cutout && !_fromTemplate)
            {
                if (urp)
                {
                    m.SetFloat("_AlphaClip", 1f);
                    m.SetFloat("_Cutoff", 0.45f);
                    m.SetFloat("_Cull", 0f);
                    m.EnableKeyword("_ALPHATEST_ON");
                }
                else
                {
                    m.SetFloat("_Mode", 1f);
                    m.SetFloat("_Cutoff", 0.45f);
                    m.EnableKeyword("_ALPHATEST_ON");
                }
                m.SetOverrideTag("RenderType", "TransparentCutout");
                m.renderQueue = (int)RenderQueue.AlphaTest;
                m.doubleSidedGI = true;
            }
            Converted[key] = m;
            return m;
        }

        /// <summary>A plain coloured material for code-built geometry (ground, water, paths).</summary>
        static bool _fromTemplate;

        /// <summary>
        /// Copies a material asset prepared by the build script (Resources/Materials). Using assets
        /// guarantees the needed shader variants (alpha clip, transparency) are kept in the build.
        /// </summary>
        static Material NewMaterial(string template)
        {
            var t = Resources.Load<Material>("Materials/" + template);
            _fromTemplate = t != null;
            return t != null ? new Material(t) : new Material(LitShader);
        }

        public static Material Solid(Color c, float smoothness = 0.1f)
        {
            var m = NewMaterial("FW_Opaque");
            m.SetColor(UsesUrp ? "_BaseColor" : "_Color", c);
            m.SetFloat(UsesUrp ? "_Smoothness" : "_Glossiness", smoothness);
            return m;
        }

        public static Material Transparent(Color c, float smoothness = 0.85f)
        {
            var m = NewMaterial("FW_Transparent");
            m.SetColor(UsesUrp ? "_BaseColor" : "_Color", c);
            m.SetFloat(UsesUrp ? "_Smoothness" : "_Glossiness", smoothness);
            if (_fromTemplate) return m;
            if (UsesUrp)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            else
            {
                m.SetFloat("_Mode", 3f);
                m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_ALPHABLEND_ON");
            }
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }
    }
}
