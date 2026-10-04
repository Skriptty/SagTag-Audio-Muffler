using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AudioMuffler
{
    internal static class ButtonInjector
    {
        public static void Inject(Button template, string objName, string label, UnityAction onClick)
        {
            Transform parent = template.transform.parent;
            if (parent.Find(objName) != null) return;

            var siblings = new List<RectTransform>();
            foreach (Transform c in parent)
            {
                if (!c.gameObject.activeSelf) continue;
                if (c.GetComponent<Button>() == null) continue;
                var r = c as RectTransform;
                if (r != null) siblings.Add(r);
            }
            if (siblings.Count == 0) siblings.Add(template.GetComponent<RectTransform>());
            siblings = siblings.OrderByDescending(r => r.anchoredPosition.y).ToList();

            RectTransform top = siblings[0];
            float step = siblings.Count > 1
                ? top.anchoredPosition.y - siblings[1].anchoredPosition.y
                : top.rect.height + 5f;

            GameObject obj = UnityEngine.Object.Instantiate(top.gameObject, parent);
            obj.name = objName;
            obj.SetActive(true);

            RectTransform rt = obj.GetComponent<RectTransform>();
            rt.anchoredPosition = top.anchoredPosition + new Vector2(0f, step);

            Button btn = obj.GetComponent<Button>();
            for (int i = 0; i < btn.onClick.GetPersistentEventCount(); i++)
                btn.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(onClick);

            var tmp = obj.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null)
            {
                tmp.text = label;
                if (AudioMufflerUI.Font == null) AudioMufflerUI.Font = tmp.font;
            }
        }
    }

    [HarmonyPatch(typeof(MenuManager), "Start")]
    internal static class MainMenuPatch
    {
        [HarmonyPostfix]
        private static void Postfix(MenuManager __instance)
        {
            try
            {
                GameObject menuContainer = GameObject.Find("Canvas/MenuContainer") ?? GameObject.Find("MenuContainer");
                if (menuContainer == null) return;

                Button template = menuContainer.GetComponentInChildren<Button>(true);
                if (template == null) return;

                ButtonInjector.Inject(template, "AudioMufflerButton", "> AUDIO", () => AudioMufflerUI.GetOrCreate().Open());
            }
            catch (Exception)
            {
            }
        }
    }

    [HarmonyPatch(typeof(QuickMenuManager), "Start")]
    internal static class QuickMenuPatch
    {
        [HarmonyPostfix]
        private static void Postfix(QuickMenuManager __instance)
        {
            try
            {
                Button resume = Resources.FindObjectsOfTypeAll<Button>().FirstOrDefault(b =>
                {
                    if (!b.gameObject.scene.IsValid()) return false;
                    var t = b.GetComponentInChildren<TMP_Text>(true);
                    return t != null && t.text.IndexOf("resume", StringComparison.OrdinalIgnoreCase) >= 0;
                });

                Func<bool> isOpen;
                if (resume != null)
                {
                    if (AudioMufflerUI.Font == null)
                    {
                        var tmp = resume.GetComponentInChildren<TMP_Text>(true);
                        if (tmp != null) AudioMufflerUI.Font = tmp.font;
                    }
                    isOpen = () => resume != null && resume.gameObject.activeInHierarchy;
                }
                else
                {
                    isOpen = () => __instance != null && Traverse.Create(__instance).Field("isMenuOpen").GetValue<bool>();
                }

                AudioMufflerUI.GetOrCreate().SetPauseMenuCheck(isOpen);
            }
            catch (Exception)
            {
            }
        }
    }
}