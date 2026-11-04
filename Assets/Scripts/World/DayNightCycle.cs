using System;
using UnityEngine;

namespace FarmWorld.World
{
    /// <summary>
    /// Day and night lighting (design doc §2). One game day = 24 real minutes by default, starting at
    /// morning. Night is kept bright enough to play: blue moonlight instead of darkness.
    /// </summary>
    public class DayNightCycle : MonoBehaviour
    {
        public Light Sun;
        public float DayLengthSeconds = 24f * 60f;
        [Range(0, 1)] public float TimeOfDay = 0.32f;   // 0 = midnight, 0.5 = noon

        static readonly Color SkyDay = new(0.53f, 0.78f, 0.97f), SkyDusk = new(0.98f, 0.66f, 0.45f), SkyNight = new(0.10f, 0.15f, 0.30f);
        static readonly Color SunDay = new(1f, 0.95f, 0.85f), SunDusk = new(1f, 0.62f, 0.38f), Moon = new(0.55f, 0.65f, 0.95f);

        public static event Action<float> OnTimeChanged;

        void Update()
        {
            TimeOfDay = Mathf.Repeat(TimeOfDay + Time.deltaTime / Mathf.Max(DayLengthSeconds, 1f), 1f);
            Apply();
            OnTimeChanged?.Invoke(TimeOfDay);
        }

        public void Apply()
        {
            if (Sun == null) return;
            // Sun elevation: 0 at 6:00 and 18:00, max ~60° at noon. At night the "sun" becomes moonlight from above.
            float dayPhase = Mathf.InverseLerp(0.25f, 0.75f, TimeOfDay);
            bool day = TimeOfDay > 0.25f && TimeOfDay < 0.75f;
            float elev = day ? Mathf.Sin(dayPhase * Mathf.PI) : 0.6f;
            float pitch = Mathf.Lerp(12f, 62f, elev);
            float yaw = day ? Mathf.Lerp(-110f, 70f, dayPhase) : -30f;
            Sun.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

            float dusk = day ? 1f - Mathf.Clamp01(elev * 3f) : 0f;   // strong near sunrise/sunset
            float night = day ? 0f : 1f;
            // fade across horizon so the switch is not abrupt
            float edge = Mathf.Min(Mathf.Abs(TimeOfDay - 0.25f), Mathf.Abs(TimeOfDay - 0.75f));
            float blend = Mathf.Clamp01(edge / 0.03f);
            if (!day) night = blend; else dusk = Mathf.Max(dusk, 1f - blend);

            Sun.color = day ? Color.Lerp(SunDay, SunDusk, dusk) : Color.Lerp(SunDusk, Moon, night);
            Sun.intensity = day ? Mathf.Lerp(0.55f, 1.3f, elev) : Mathf.Lerp(0.5f, 0.38f, night);

            var sky = day ? Color.Lerp(SkyDay, SkyDusk, dusk * 0.8f) : Color.Lerp(SkyDusk, SkyNight, night);
            RenderSettings.ambientSkyColor = Color.Lerp(sky, Color.white, 0.25f) * (day ? 1f : 0.75f);
            RenderSettings.ambientEquatorColor = Color.Lerp(new Color(0.72f, 0.78f, 0.66f), sky, 0.4f) * (day ? 0.85f : 0.55f);
            RenderSettings.ambientGroundColor = new Color(0.32f, 0.36f, 0.26f) * (day ? 1f : 0.6f);
            RenderSettings.fogColor = sky;
            var cam = Camera.main;
            if (cam != null) cam.backgroundColor = sky;
        }
    }
}
