using System.Collections.Generic;
using UnityEngine;

namespace FarmWorld.Core
{
    /// <summary>
    /// Background music and short sound effects (Resources/Audio, credits in CREDITS.txt).
    /// Sound and music can be switched off separately in the settings; the choice is kept on the phone.
    /// </summary>
    public class Sound : MonoBehaviour
    {
        public static Sound I { get; private set; }
        const string SfxKey = "fw_sfx", MusicKey = "fw_music";

        AudioSource _music;
        readonly List<AudioSource> _pool = new();
        readonly Dictionary<string, AudioClip> _clips = new();
        readonly Dictionary<string, float> _last = new();

        public static bool SfxOn => PlayerPrefs.GetInt(SfxKey, 1) == 1;
        /// <summary>False while the game ships without a music track.</summary>
        public static bool HasMusic => I != null && I._music != null && I._music.clip != null;
        public static bool MusicOn => PlayerPrefs.GetInt(MusicKey, 1) == 1;

        void Awake()
        {
            I = this;
            _music = gameObject.AddComponent<AudioSource>();
            _music.clip = Resources.Load<AudioClip>("Audio/music_farm");
            _music.loop = true;
            _music.volume = 0.28f;
            _music.playOnAwake = false;
            for (int i = 0; i < 6; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                _pool.Add(s);
            }
            if (MusicOn && _music.clip != null) _music.Play();
        }

        /// <summary>Plays Resources/Audio/name once. The same sound is not repeated within 60 ms.</summary>
        public static void Play(string name, float volume = 0.8f, float pitch = 1f)
        {
            if (I == null || !SfxOn) return;
            I.PlayClip(name, volume, pitch);
        }

        void PlayClip(string name, float volume, float pitch)
        {
            if (_last.TryGetValue(name, out var at) && Time.unscaledTime - at < 0.06f) return;
            _last[name] = Time.unscaledTime;
            if (!_clips.TryGetValue(name, out var clip)) { clip = Resources.Load<AudioClip>("Audio/" + name); _clips[name] = clip; }
            if (clip == null) return;
            AudioSource free = _pool[0];
            foreach (var s in _pool) if (!s.isPlaying) { free = s; break; }
            free.pitch = pitch;
            free.PlayOneShot(clip, volume);
        }

        public static void SetSfx(bool on) { PlayerPrefs.SetInt(SfxKey, on ? 1 : 0); PlayerPrefs.Save(); }

        public static void SetMusic(bool on)
        {
            PlayerPrefs.SetInt(MusicKey, on ? 1 : 0); PlayerPrefs.Save();
            if (I == null || I._music.clip == null) return;
            if (on && !I._music.isPlaying) I._music.Play();
            if (!on) I._music.Stop();
        }

        /// <summary>The call of an animal type (cow, sheep, chicken), a little higher or lower each time.</summary>
        public static void Animal(string type)
        {
            string clip = type == "cow" ? "cow" : type == "sheep" ? "sheep" : type == "chicken" ? "chicken" : null;
            if (clip != null) Play(clip, 0.7f, Random.Range(0.92f, 1.08f));
        }
    }
}
