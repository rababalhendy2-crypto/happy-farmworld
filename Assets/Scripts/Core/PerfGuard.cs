using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FarmWorld.Core
{
    /// <summary>Keeps the game smooth on weak phones: it measures the frame rate and, when it stays under 30,
    /// lowers the render resolution and then the shadows, one step at a time. It never raises quality again
    /// in the same session, so the picture does not flicker between levels.</summary>
    public class PerfGuard : MonoBehaviour
    {
        (float scale, float shadow)[] Steps = { (1f, 50f), (0.7f, 35f), (0.6f, 0f), (0.5f, 0f) };
        int _step;
        float _time, _warm = 6f;
        int _frames;
        UniversalRenderPipelineAsset _asset;

        public static int Level { get; private set; }

        void Start()
        {
            _asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (_asset == null) { enabled = false; return; }
            Steps[0] = (_asset.renderScale, _asset.shadowDistance);   // step 0 = the project's own settings
            // Start one step down on phones with little memory or few cores.
            if (Application.isMobilePlatform && (SystemInfo.systemMemorySize < 3000 || SystemInfo.processorCount <= 4)) Apply(1);
            else Apply(0);
        }

        void Update()
        {
            if (_warm > 0f) { _warm -= Time.unscaledDeltaTime; return; }
            _time += Time.unscaledDeltaTime; _frames++;
            if (_time < 4f) return;
            float fps = _frames / _time;
            _time = 0f; _frames = 0;
            if (fps < 28f && _step < Steps.Length - 1)
            {
                Apply(_step + 1);
                _warm = 2f;
                Debug.Log($"[Perf] {fps:0} fps, quality step {_step}");
            }
        }

        void Apply(int step)
        {
            _step = Level = step;
            _asset.renderScale = Steps[step].scale;
            _asset.shadowDistance = Steps[step].shadow;
        }
    }
}
