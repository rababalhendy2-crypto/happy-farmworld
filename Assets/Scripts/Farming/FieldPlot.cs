using FarmWorld.Core;
using UnityEngine;

namespace FarmWorld.Farming
{
    /// <summary>
    /// One field on the farm. Shows the planted crop at its current growth stage (4 model stages),
    /// and a bobbing crop above the field when it is ready to harvest.
    /// </summary>
    public class FieldPlot : MonoBehaviour
    {
        public int Index;

        Transform _plants, _marker;
        string _shownCrop = "";
        int _shownStage = -1;
        float _nextCheck;

        void Start() => Refresh(true);

        void Update()
        {
            if (_marker != null)
            {
                float t = Time.time * 2.2f + Index;
                _marker.localPosition = new Vector3(0, 2.3f + Mathf.Sin(t) * 0.18f, 0);
                _marker.localRotation = Quaternion.Euler(0, Time.time * 60f, 0);
            }
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + 0.5f;
            Refresh(false);
        }

        public static int StageFor(float progress) =>
            progress >= 1f ? 4 : progress >= 0.6f ? 3 : progress >= 0.25f ? 2 : 1;

        public void Refresh(bool force)
        {
            var gs = GameState.I;
            if (gs == null) return;
            var plot = gs.Plot(Index);
            string crop = plot != null ? plot.crop ?? "" : "";
            int stage = crop.Length == 0 ? 0 : StageFor(gs.Progress(Index));
            if (!force && crop == _shownCrop && stage == _shownStage) return;
            bool becameReady = stage == 4 && _shownStage == 3 && crop == _shownCrop;
            _shownCrop = crop; _shownStage = stage;
            Rebuild(crop, stage);
            if (becameReady) Pop();
        }

        void Rebuild(string cropId, int stage)
        {
            if (_plants != null) Destroy(_plants.gameObject);
            if (_marker != null) Destroy(_marker.gameObject);
            _plants = null; _marker = null;
            if (cropId.Length == 0 || !GameConfig.CropById.TryGetValue(cropId, out var crop)) return;

            _plants = new GameObject("Plants").transform;
            _plants.SetParent(transform, false);
            string m = crop.Model;
            float h = m == "Corn" ? 1.4f : m == "Wheat" ? 0.95f : m == "Tomato" ? 0.9f : 0.5f;
            h *= 0.35f + 0.65f * stage / 4f;
            int per = m == "Pumpkin" ? 2 : 4;
            var rnd = new System.Random(Index * 97 + stage);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            for (int r = 0; r < 4; r++)
                for (int k = 0; k < per; k++)
                {
                    float x = per == 2 ? -0.8f + k * 1.6f : -1.35f + k * 0.9f;
                    ModelLibrary.Spawn("Crops/" + m + "_" + stage + ".obj", _plants,
                        new Vector3(x + R(-0.08f, 0.08f), 0.28f, -1.35f + r * 0.9f), h * R(0.9f, 1.08f),
                        ModelLibrary.Fit.Height, R(0, 360), null, r == 0 || stage >= 3);
                }

            if (stage == 4)
            {
                _marker = new GameObject("ReadyMarker").transform;
                _marker.SetParent(transform, false);
                var icon = ModelLibrary.Spawn("Crops/" + m + "_Crop.obj", _marker, Vector3.zero, 0.9f, ModelLibrary.Fit.Height, 0, null, false);
                if (icon == null) ModelLibrary.Spawn("Crops/" + m + "_4.obj", _marker, Vector3.zero, 0.9f, ModelLibrary.Fit.Height, 0, null, false);
            }
        }

        /// <summary>Short squash-and-stretch so planting, ripening and harvesting feel responsive.</summary>
        public void Pop()
        {
            StopAllCoroutines();
            StartCoroutine(PopRoutine());
        }

        System.Collections.IEnumerator PopRoutine()
        {
            float t = 0;
            while (t < 0.35f && _plants != null)
            {
                t += Time.deltaTime;
                float k = 1f + Mathf.Sin(t / 0.35f * Mathf.PI) * 0.18f;
                _plants.localScale = new Vector3(2f - k, k, 2f - k);
                yield return null;
            }
            if (_plants != null) _plants.localScale = Vector3.one;
        }
    }
}
