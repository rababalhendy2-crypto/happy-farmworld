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
            // Planting grid matches the five soil ridges built by WorldBuilder.BuildField.
            float h = m == "Corn" ? 1.5f : m == "Wheat" ? 0.85f : m == "Tomato" ? 0.95f : m == "Pumpkin" ? 0.55f : 0.5f;
            h *= 0.35f + 0.65f * stage / 4f;
            int perRow = m == "Pumpkin" ? 3 : m == "Wheat" ? 9 : 6;
            int tuft = m == "Wheat" ? 3 : 1;                 // wheat stalks are thin: plant small clumps
            float thick = m == "Wheat" ? 1.8f : m == "Corn" ? 1.25f : 1.15f;
            Color? ripeTint = stage == 4 && m == "Wheat" ? new Color(1.35f, 1.2f, 0.8f) : (Color?)null;
            var rnd = new System.Random(Index * 97 + stage);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            for (int r = 0; r < Rows; r++)
            {
                float z = RowZ(r);
                for (int k = 0; k < perRow; k++)
                {
                    float x = -1.6f + (k + 0.5f) * 3.2f / perRow;
                    for (int t = 0; t < tuft; t++)
                    {
                        var p = new Vector3(x + R(-0.09f, 0.09f), 0.3f, z + R(-0.07f, 0.07f));
                        var plant = ModelLibrary.Spawn("Crops/" + m + "_" + stage + ".obj", _plants, p, h * R(0.85f, 1.1f),
                            ModelLibrary.Fit.Height, R(0, 360), ripeTint, stage >= 3 && t == 0);
                        if (plant == null) continue;
                        plant.transform.localScale = new Vector3(thick, 1f, thick);
                        if (tuft > 1) plant.transform.localRotation *= Quaternion.Euler(R(-8f, 8f), 0, R(-8f, 8f));
                    }
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
