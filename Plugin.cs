using System;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace AudioMuffler
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "skript.audiomuffler";
        public const string PluginName = "AudioMuffler";
        public const string PluginVersion = "0.1.0";

        private void Awake()
        {
            AudioRules.Init(Paths.ConfigPath);

            var go = new GameObject("AudioMufflerManager");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<AudioMufflerManager>();

            var harmony = new Harmony(PluginGuid);

            AudioPatches.Apply(harmony);

            foreach (var t in new[] { typeof(MainMenuPatch), typeof(QuickMenuPatch) })
            {
                try { harmony.CreateClassProcessor(t).Patch(); }
                catch (Exception) { }
            }
        }
    }

    internal class AudioMufflerManager : MonoBehaviour
    {
        private float nextSave;

        private void LateUpdate()
        {
            AudioController.Tick();
        }

        private void Update()
        {
            AudioPreview.Tick();

            if (Time.unscaledTime >= nextSave)
            {
                nextSave = Time.unscaledTime + 5f;
                AudioRules.SaveIfDirty();
            }
        }

        private void OnApplicationQuit()
        {
            AudioRules.SaveIfDirty();
        }
    }
}