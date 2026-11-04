using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace FarmWorld.EditorTools
{
    /// <summary>
    /// Command-line build used by GitHub Actions (game-ci). Configures player settings, prepares the
    /// material templates the runtime copies, then builds Android (APK) or WebGL.
    /// Args: -buildTarget Android|WebGL  -customBuildPath &lt;file or folder&gt;
    /// </summary>
    public static class BuildScript
    {
        const string MaterialsDir = "Assets/Resources/Materials";

        [MenuItem("Farm World/Prepare project")]
        public static void Prepare()
        {
            PlayerSettings.companyName = "Farm World";
            PlayerSettings.productName = "Farm World";
            PlayerSettings.bundleVersion = FarmWorld.Core.Bootstrap.Version;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.farmworld.game");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.bundleVersionCode = VersionCode(FarmWorld.Core.Bootstrap.Version);

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;   // works on any static host
            PlayerSettings.WebGL.memorySize = 512;

            EnsureMaterials();
            AssetDatabase.SaveAssets();
        }

        static int VersionCode(string v)
        {
            var p = v.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).Concat(new[] { 0, 0, 0 }).Take(3).ToArray();
            return p[0] * 10000 + p[1] * 100 + p[2] + 1;
        }

        /// <summary>
        /// Material assets in Resources so the build keeps the URP Lit variants we need
        /// (opaque, alpha-clipped foliage, transparent water).
        /// </summary>
        static void EnsureMaterials()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) { Debug.LogError("[Build] URP Lit shader not found"); return; }
            Directory.CreateDirectory(MaterialsDir);

            Make("FW_Opaque", lit, m => { m.SetFloat("_Smoothness", 0.18f); });
            Make("FW_Cutout", lit, m =>
            {
                m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.45f); m.SetFloat("_Cull", 0f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetOverrideTag("RenderType", "TransparentCutout");
                m.renderQueue = (int)RenderQueue.AlphaTest;
                m.doubleSidedGI = true;
            });
            Make("FW_Transparent", lit, m =>
            {
                m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_Smoothness", 0.85f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)RenderQueue.Transparent;
            });

            // Keep the Lit shader in every build even if nothing references it directly.
            var gs = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("ProjectSettings/GraphicsSettings.asset");
            var so = new SerializedObject(gs);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            bool present = false;
            for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue == lit) present = true;
            if (!present)
            {
                arr.InsertArrayElementAtIndex(arr.arraySize);
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = lit;
                so.ApplyModifiedProperties();
            }
        }

        static void Make(string name, Shader shader, Action<Material> setup)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            setup(m);
            EditorUtility.SetDirty(m);
        }

        // ------------------------------------------------------------------ CI entry points

        public static void BuildFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }

            var targetName = Arg("-buildTarget") ?? "Android";
            var target = targetName.Equals("WebGL", StringComparison.OrdinalIgnoreCase) ? BuildTarget.WebGL : BuildTarget.Android;
            var output = Arg("-customBuildPath") ?? (target == BuildTarget.Android ? "build/Android/FarmWorld.apk" : "build/WebGL/FarmWorld");
            if (target == BuildTarget.Android && !output.EndsWith(".apk")) output = Path.Combine(output, "FarmWorld.apk");

            Prepare();
            EditorUserBuildSettings.buildAppBundle = false;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)) ?? ".");

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = target,
                options = BuildOptions.None,
            });
            var sum = report.summary;
            Debug.Log($"[Build] {target} result={sum.result} size={sum.totalSize} errors={sum.totalErrors} out={output}");
            EditorApplication.Exit(sum.result == BuildResult.Succeeded ? 0 : 1);
        }
    }

    /// <summary>Import settings for the CC0 models: no embedded cameras/lights, readable sizes, generic rigs.</summary>
    public class ModelImportSettings : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.Contains("/Resources/Models/")) return;
            var mi = (ModelImporter)assetImporter;
            mi.importCameras = false;
            mi.importLights = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            bool animal = assetPath.Contains("/Animals/");
            mi.animationType = animal ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
            mi.importAnimation = animal;
            mi.meshCompression = ModelImporterMeshCompression.Medium;
            mi.isReadable = false;
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.Contains("/Resources/Models/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.maxTextureSize = 1024;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = true;
        }
    }
}
