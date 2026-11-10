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

        Transform _plants;
        string _shownCrop = "";
        int _shownStage = -1;
        float _nextCheck;

        void Start() => Refresh(true);

        void Update()
        {
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
            _plants = null;
            if (cropId.Length == 0 || !GameConfig.CropById.TryGetValue(cropId, out var crop)) return;

            _plants = new GameObject("Plants").transform;
            _plants.SetParent(transform, false);
            string m = crop.Model;
            // Crop models (Tools/models/crops.py) are authored in metres for each growth stage; sizing each one to its
            // authored height times one scale per crop keeps the real proportions between stages. Planting grid matches the five soil ridges built by WorldBuilder.BuildField.
            float scale = m == "Corn" ? 0.75f : m == "Wheat" ? 0.9f : 1f;
            int perRow = m == "Pumpkin" ? 3 : m == "Wheat" ? 9 : m == "Tomato" ? 5 : 6;
            bool shadows = stage >= 3 && (m == "Corn" || m == "Tomato" || m == "Pumpkin");
            string model = m + "_" + stage;
            float h = scale * (CropHeights.Metres.TryGetValue(model, out var mh) ? mh : 0.5f);
            var rnd = new System.Random(Index * 97 + stage);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            for (int r = 0; r < Rows; r++)
            {
                float z = RowZ(r);
                for (int k = 0; k < perRow; k++)
                {
                    float x = -1.6f + (k + 0.5f) * 3.2f / perRow;
                    var p = new Vector3(x + R(-0.06f, 0.06f), 0.3f, z + R(-0.05f, 0.05f));
                    ModelLibrary.Spawn("Crops/" + model + ".obj", _plants, p, h * R(0.88f, 1.1f),
                        ModelLibrary.Fit.Height, R(0, 360), null, shadows);
                }
            }
            // Hundreds of small meshes per field: merge them into a few draw calls for phones.
            StaticBatchingUtility.Combine(_plants.gameObject);
        }

        public const int Rows = 5;
        public static float RowZ(int r) => -1.5f + r * 0.75f;

        /// <summary>Kept for callers; plants are static-batched, so feedback comes from the floating label instead.</summary>
        public void Pop() { }
    }
}
