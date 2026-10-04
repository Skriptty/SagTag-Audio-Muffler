using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace AudioMuffler
{
    internal class Rule
    {
        public bool Muted;
        public float Volume = 1f;
        public bool IsDefault { get { return !Muted && Volume >= 0.999f; } }
    }

    internal static class AudioRules
    {
        private static readonly Dictionary<string, Rule> rules = new Dictionary<string, Rule>();
        private static string path;
        private static bool dirty;

        public static int Count { get { return rules.Count; } }

        public static void Init(string configDir)
        {
            path = Path.Combine(configDir, "AudioMuffler.rules.txt");
            Load();
        }

        public static Rule Get(string name)
        {
            Rule r;
            return rules.TryGetValue(name, out r) ? r : new Rule();
        }

        public static float Multiplier(string name)
        {
            if (rules.Count == 0) return 1f;
            Rule r;
            if (!rules.TryGetValue(name, out r)) return 1f;
            return r.Muted ? 0f : r.Volume;
        }

        public static void Set(string name, bool muted, float volume)
        {
            if (string.IsNullOrEmpty(name)) return;
            volume = Mathf.Clamp01(volume);
            if (!muted && volume >= 0.999f)
            {
                if (rules.Remove(name)) dirty = true;
                return;
            }
            rules[name] = new Rule { Muted = muted, Volume = volume };
            SoundRegistry.Ensure(name);
            dirty = true;
        }

        public static void ResetAll()
        {
            if (rules.Count == 0) return;
            rules.Clear();
            dirty = true;
        }

        private static void Load()
        {
            try
            {
                if (!File.Exists(path)) return;
                foreach (var line in File.ReadAllLines(path))
                {
                    var p = line.Split('\t');
                    if (p.Length < 3 || p[0].Length == 0) continue;
                    float vol;
                    if (!float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out vol)) vol = 1f;
                    rules[p[0]] = new Rule { Muted = p[1] == "1", Volume = Mathf.Clamp01(vol) };
                    SoundRegistry.Ensure(p[0]);
                }
            }
            catch (Exception)
            {
            }
        }

        public static void SaveIfDirty()
        {
            if (!dirty || path == null) return;
            dirty = false;
            try
            {
                var lines = rules.Select(kv => kv.Key + "\t" + (kv.Value.Muted ? "1" : "0") + "\t" +
                                               kv.Value.Volume.ToString("R", CultureInfo.InvariantCulture));
                File.WriteAllLines(path, lines);
            }
            catch (Exception)
            {
            }
        }
    }

    internal class SoundEntry
    {
        public string Name;
        public AudioClip Clip;
        public float LastHeard = -1f;
        public int Plays;
    }

    internal static class SoundRegistry
    {
        private static readonly Dictionary<string, SoundEntry> entries = new Dictionary<string, SoundEntry>();

        public static IEnumerable<SoundEntry> All { get { return entries.Values; } }

        public static SoundEntry Ensure(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            SoundEntry e;
            if (!entries.TryGetValue(name, out e))
            {
                e = new SoundEntry { Name = name };
                entries[name] = e;
            }
            return e;
        }

        public static void Heard(AudioClip clip)
        {
            if (clip == null) return;
            var e = Ensure(clip.name);
            if (e == null) return;
            e.Clip = clip;
            e.LastHeard = Time.unscaledTime;
            e.Plays++;
        }

        public static void ScanLoadedClips()
        {
            foreach (var c in Resources.FindObjectsOfTypeAll<AudioClip>())
            {
                var e = Ensure(c.name);
                if (e != null && e.Clip == null) e.Clip = c;
            }
        }
    }

    internal static class AudioController
    {
        private class State
        {
            public float Base;
            public float Last;
            public bool Modified;
        }

        private static readonly Dictionary<AudioSource, State> tracked = new Dictionary<AudioSource, State>();
        private static readonly List<AudioSource> dead = new List<AudioSource>();
        private static int modifiedCount;
        private static float nextScan;
        private static float nextCleanup;

        public static void Track(AudioSource src)
        {
            if (src == null || AudioPreview.IsPreview(src)) return;
            State st;
            if (!tracked.TryGetValue(src, out st))
            {
                st = new State();
                tracked[src] = st;
            }
            ApplyOne(src, st);
        }

        private static void ApplyOne(AudioSource src, State st)
        {
            var clip = src.clip;
            float mult = clip != null ? AudioRules.Multiplier(clip.name) : 1f;

            if (!st.Modified)
            {
                if (mult >= 0.999f) return;
                st.Base = src.volume;
                st.Modified = true;
                modifiedCount++;
                st.Last = st.Base * mult;
                src.volume = st.Last;
                return;
            }

            if (!Mathf.Approximately(src.volume, st.Last)) st.Base = src.volume;

            if (mult >= 0.999f)
            {
                src.volume = st.Base;
                st.Modified = false;
                modifiedCount--;
                return;
            }

            st.Last = st.Base * mult;
            src.volume = st.Last;
        }

        public static void Tick()
        {
            float now = Time.unscaledTime;

            if (now >= nextScan)
            {
                nextScan = now + 1f;
                Scan();
            }
            if (now >= nextCleanup)
            {
                nextCleanup = now + 10f;
                Cleanup();
            }

            if (AudioRules.Count == 0 && modifiedCount == 0) return;

            foreach (var kv in tracked)
            {
                if (kv.Key == null) continue;
                ApplyOne(kv.Key, kv.Value);
            }
        }

        private static void Scan()
        {
            foreach (var src in UnityEngine.Object.FindObjectsOfType<AudioSource>())
            {
                if (src.clip == null || !src.isPlaying) continue;
                if (AudioPreview.IsPreview(src)) continue;
                if (!tracked.ContainsKey(src)) SoundRegistry.Heard(src.clip);
                Track(src);
            }
        }

        private static void Cleanup()
        {
            dead.Clear();
            foreach (var kv in tracked)
            {
                if (kv.Key == null)
                {
                    if (kv.Value.Modified) modifiedCount--;
                    dead.Add(kv.Key);
                }
            }
            foreach (var d in dead) tracked.Remove(d);
        }
    }

    internal static class AudioPreview
    {
        private const float PreviewGain = 0.8f;

        private static AudioSource source;
        private static bool active;
        private static float prevVolume = 1f;

        public static event Action StateChanged;

        public static bool IsPreview(AudioSource s)
        {
            return source != null && s == source;
        }

        public static AudioClip Current
        {
            get { return active && source != null ? source.clip : null; }
        }

        public static void Play(AudioClip clip)
        {
            if (clip == null) return;
            if (source == null)
            {
                var go = new GameObject("AudioMufflerPreview");
                go.hideFlags = HideFlags.HideAndDontSave;
                source = go.AddComponent<AudioSource>();
                source.spatialBlend = 0f;
                source.playOnAwake = false;
                source.ignoreListenerVolume = true;
            }

            if (!active)
            {
                prevVolume = AudioListener.volume;
                AudioListener.volume = 0f;
                active = true;
            }

            source.volume = PreviewGain * prevVolume;
            source.Stop();
            source.clip = clip;
            source.Play();
            Raise();
        }

        public static void Stop()
        {
            if (source != null) source.Stop();
            if (!active) return;
            AudioListener.volume = prevVolume;
            active = false;
            Raise();
        }

        public static void Tick()
        {
            if (!active) return;

            if (AudioListener.volume > 0f)
            {
                prevVolume = AudioListener.volume;
                AudioListener.volume = 0f;
            }

            if (source == null || !source.isPlaying) Stop();
        }

        private static void Raise()
        {
            var h = StateChanged;
            if (h != null) h();
        }
    }

    internal static class AudioPatches
    {
        public static void Apply(Harmony h)
        {
            var playPrefix = new HarmonyMethod(typeof(AudioPatches), nameof(PlayPrefix));

            var targets = new[]
            {
                AccessTools.Method(typeof(AudioSource), "Play", Type.EmptyTypes),
                AccessTools.Method(typeof(AudioSource), "Play", new[] { typeof(ulong) }),
                AccessTools.Method(typeof(AudioSource), "PlayDelayed", new[] { typeof(float) }),
                AccessTools.Method(typeof(AudioSource), "PlayScheduled", new[] { typeof(double) }),
            };

            foreach (var m in targets)
            {
                if (m == null) continue;
                try
                {
                    h.Patch(m, prefix: playPrefix);
                }
                catch (Exception)
                {
                }
            }

            var oneShot = AccessTools.Method(typeof(AudioSource), "PlayOneShot", new[] { typeof(AudioClip), typeof(float) });
            if (oneShot != null)
            {
                try
                {
                    h.Patch(oneShot, prefix: new HarmonyMethod(typeof(AudioPatches), nameof(OneShotPrefix)));
                }
                catch (Exception)
                {
                }
            }
        }

        private static void PlayPrefix(AudioSource __instance)
        {
            if (__instance == null || AudioPreview.IsPreview(__instance)) return;
            var clip = __instance.clip;
            if (clip == null) return;
            SoundRegistry.Heard(clip);
            AudioController.Track(__instance);
        }

        private static void OneShotPrefix(AudioSource __instance, AudioClip __0, ref float __1)
        {
            if (__0 == null || AudioPreview.IsPreview(__instance)) return;
            SoundRegistry.Heard(__0);
            __1 *= AudioRules.Multiplier(__0.name);
        }
    }
}