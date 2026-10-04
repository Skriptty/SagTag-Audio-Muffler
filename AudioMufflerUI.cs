using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AudioMuffler
{
    internal class AudioMufflerUI : MonoBehaviour
    {
        public static TMP_FontAsset Font;
        private static AudioMufflerUI instance;

        private const float RowH = 46f;
        private const float RH = RowH - 4f;
        private const float BH = RH - 8f;

        private static readonly Color Accent = new Color(1f, 0.45f, 0.1f, 1f);
        private static readonly Color PanelBg = new Color(0.06f, 0.06f, 0.06f, 0.97f);
        private static readonly Color ButtonBg = new Color(0.17f, 0.17f, 0.17f, 1f);
        private static readonly Color RowBg = new Color(0.11f, 0.11f, 0.11f, 1f);
        private static readonly Color MutedBg = new Color(0.50f, 0.13f, 0.08f, 1f);

        private class Row
        {
            public RectTransform Root;
            public Image Bg;
            public TextMeshProUGUI Name, MuteLabel, Pct, PlayLabel;
            public Button PlayBtn, MuteBtn;
            public Image MuteImg;
            public Slider Slider;
            public SoundEntry Entry;
        }

        private GameObject root;
        private TMP_InputField search;
        private ScrollRect scroll;
        private RectTransform viewport, content;
        private TextMeshProUGUI status, sortLabel, modLabel;
        private readonly List<Row> rows = new List<Row>();
        private List<SoundEntry> view = new List<SoundEntry>();
        private bool sortByName, onlyModified;
        private GameObject floatBtn;
        private Func<bool> pauseOpen;

        public static AudioMufflerUI GetOrCreate()
        {
            if (instance != null) return instance;
            var go = new GameObject("AudioMufflerUI");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<AudioMufflerUI>();
            instance.Build();
            return instance;
        }

        public void Open()
        {
            root.SetActive(true);
            search.SetTextWithoutNotify("");
            SoundRegistry.ScanLoadedClips();
            Rebuild(true);
        }

        public void SetPauseMenuCheck(Func<bool> isOpen)
        {
            pauseOpen = isOpen;
        }

        public void Close()
        {
            AudioPreview.Stop();
            AudioRules.SaveIfDirty();
            if (root != null) root.SetActive(false);
        }

        private void Update()
        {
            if (root == null) return;

            if (root.activeSelf && Cursor.lockState == CursorLockMode.Locked) Close();

            if (floatBtn != null)
            {
                bool show = pauseOpen != null && !root.activeSelf && pauseOpen();
                if (floatBtn.activeSelf != show) floatBtn.SetActive(show);
            }
        }

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            var rootRT = NewRect("Root", transform);
            Stretch(rootRT);
            AddImage(rootRT, new Color(0f, 0f, 0f, 0.65f));
            root = rootRT.gameObject;

            var panel = NewRect("Panel", rootRT);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(1000f, 760f);
            panel.anchoredPosition = Vector2.zero;
            AddImage(panel, PanelBg);

            var title = AddText(panel, "Title", "AUDIO MUFFLER", 34, TextAlignmentOptions.MidlineLeft, Accent);
            SetAbs(title.rectTransform, 20, 10, 700, 50);

            TextMeshProUGUI dummy;
            AddButton(panel, "X", 28, ButtonBg, 920, 12, 60, 44, Close, out dummy);

            search = BuildInput(panel, "SEARCH...", 20, 70, 480, 44);
            search.onValueChanged.AddListener(_ => Rebuild(true));

            AddButton(panel, "SORT: RECENT", 22, ButtonBg, 510, 70, 200, 44, () =>
            {
                sortByName = !sortByName;
                sortLabel.text = sortByName ? "SORT: NAME" : "SORT: RECENT";
                Rebuild(true);
            }, out sortLabel);

            AddButton(panel, "ONLY MODIFIED: OFF", 20, ButtonBg, 720, 70, 260, 44, () =>
            {
                onlyModified = !onlyModified;
                modLabel.text = onlyModified ? "ONLY MODIFIED: ON" : "ONLY MODIFIED: OFF";
                Rebuild(true);
            }, out modLabel);

            var scrollRT = NewRect("Scroll", panel);
            SetAbs(scrollRT, 20, 124, 960, 560);
            AddImage(scrollRT, new Color(0f, 0f, 0f, 0.35f));
            scroll = scrollRT.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            viewport = NewRect("Viewport", scrollRT);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();

            content = NewRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            content.anchoredPosition = Vector2.zero;

            scroll.viewport = viewport;
            scroll.content = content;
            scroll.onValueChanged.AddListener(_ => RefreshRows());

            AddButton(panel, "RESET ALL", 22, ButtonBg, 20, 696, 200, 44, () =>
            {
                AudioRules.ResetAll();
                Rebuild(false);
            }, out dummy);

            AddButton(panel, "REFRESH", 22, ButtonBg, 230, 696, 160, 44, () =>
            {
                SoundRegistry.ScanLoadedClips();
                Rebuild(false);
            }, out dummy);

            status = AddText(panel, "Status", "", 20, TextAlignmentOptions.MidlineRight, new Color(1f, 1f, 1f, 0.6f));
            SetAbs(status.rectTransform, 400, 696, 580, 44);

            TextMeshProUGUI floatLabel;
            var fb = AddButton((RectTransform)transform, "> AUDIO", 26, ButtonBg, 30, 30, 220, 56, Open, out floatLabel);
            floatBtn = fb.gameObject;
            floatBtn.SetActive(false);

            AudioPreview.StateChanged += () => { if (root != null && root.activeSelf) RefreshRows(); };

            SceneManager.sceneLoaded += (s, m) => { if (root != null) Close(); };

            root.SetActive(false);
        }

        private void Rebuild(bool resetScroll)
        {
            string q = (search.text ?? "").Trim();

            IEnumerable<SoundEntry> src = SoundRegistry.All;
            if (q.Length > 0)
                src = src.Where(e => e.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            if (onlyModified)
                src = src.Where(e => !AudioRules.Get(e.Name).IsDefault);

            src = sortByName
                ? src.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                : src.OrderByDescending(e => e.LastHeard).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase);

            view = src.ToList();

            content.sizeDelta = new Vector2(0f, view.Count * RowH + 4f);
            if (resetScroll) content.anchoredPosition = Vector2.zero;

            status.text = view.Count + " sounds";
            RefreshRows();
        }

        private void RefreshRows()
        {
            if (viewport == null) return;

            float viewH = viewport.rect.height;
            if (viewH <= 1f) viewH = 560f;

            int first = Mathf.Max(0, Mathf.FloorToInt(content.anchoredPosition.y / RowH));
            int needed = Mathf.CeilToInt(viewH / RowH) + 2;
            while (rows.Count < needed) rows.Add(CreateRow());

            for (int i = 0; i < rows.Count; i++)
            {
                int idx = first + i;
                var row = rows[i];
                if (idx >= view.Count)
                {
                    row.Root.gameObject.SetActive(false);
                    continue;
                }
                row.Root.gameObject.SetActive(true);
                row.Root.anchoredPosition = new Vector2(0f, -idx * RowH - 2f);
                row.Entry = view[idx];
                BindRow(row);
            }
        }

        private Row CreateRow()
        {
            var r = new Row();
            r.Root = NewRect("Row", content);
            r.Root.anchorMin = new Vector2(0f, 1f);
            r.Root.anchorMax = new Vector2(1f, 1f);
            r.Root.pivot = new Vector2(0.5f, 1f);
            r.Root.sizeDelta = new Vector2(0f, RH);
            r.Bg = AddImage(r.Root, RowBg);

            r.Name = AddText(r.Root, "Name", "", 22, TextAlignmentOptions.MidlineLeft, Color.white);
            SetAbs(r.Name.rectTransform, 10, 0, 400, RH);

            r.PlayBtn = AddButton(r.Root, "PLAY", 20, ButtonBg, 420, 4, 70, BH, () => OnPlay(r), out r.PlayLabel);

            r.MuteBtn = AddButton(r.Root, "MUTE", 20, ButtonBg, 500, 4, 110, BH, () => OnMute(r), out r.MuteLabel);
            r.MuteImg = r.MuteBtn.GetComponent<Image>();

            r.Slider = AddSlider(r.Root);
            SetAbs(r.Slider.GetComponent<RectTransform>(), 630, 4, 230, BH);
            r.Slider.onValueChanged.AddListener(v => OnVolume(r, v));

            r.Pct = AddText(r.Root, "Pct", "100%", 20, TextAlignmentOptions.Midline, Color.white);
            SetAbs(r.Pct.rectTransform, 870, 0, 80, RH);

            return r;
        }

        private void BindRow(Row r)
        {
            var e = r.Entry;
            var rule = AudioRules.Get(e.Name);
            r.Name.text = e.Name;
            r.Name.color = rule.IsDefault ? Color.white : Accent;
            r.MuteLabel.text = rule.Muted ? "MUTED" : "MUTE";
            r.MuteImg.color = rule.Muted ? MutedBg : ButtonBg;
            r.Slider.SetValueWithoutNotify(rule.Volume);
            r.Pct.text = Mathf.RoundToInt(rule.Volume * 100f) + "%";
            r.PlayBtn.interactable = e.Clip != null;
            r.PlayLabel.text = (e.Clip != null && AudioPreview.Current == e.Clip) ? "STOP" : "PLAY";
        }

        private void OnPlay(Row r)
        {
            if (r.Entry == null) return;
            var clip = r.Entry.Clip;
            if (clip == null) return;
            if (AudioPreview.Current == clip) AudioPreview.Stop();
            else AudioPreview.Play(clip);
        }

        private void OnMute(Row r)
        {
            if (r.Entry == null) return;
            var rule = AudioRules.Get(r.Entry.Name);
            AudioRules.Set(r.Entry.Name, !rule.Muted, rule.Volume);
            BindRow(r);
        }

        private void OnVolume(Row r, float value)
        {
            if (r.Entry == null) return;
            float v = Mathf.Round(value * 100f) / 100f;
            var rule = AudioRules.Get(r.Entry.Name);
            AudioRules.Set(r.Entry.Name, rule.Muted, v);
            r.Pct.text = Mathf.RoundToInt(v * 100f) + "%";
            r.Name.color = AudioRules.Get(r.Entry.Name).IsDefault ? Color.white : Accent;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void SetAbs(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static Image AddImage(RectTransform rt, Color c)
        {
            var i = rt.gameObject.AddComponent<Image>();
            i.color = c;
            return i;
        }

        private static TextMeshProUGUI AddText(RectTransform parent, string name, string text, float size,
                                               TextAlignmentOptions align, Color color)
        {
            var rt = NewRect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (Font != null) t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            return t;
        }

        private static Button AddButton(RectTransform parent, string label, float size, Color bg,
                                        float x, float y, float w, float h, UnityAction onClick,
                                        out TextMeshProUGUI text)
        {
            var rt = NewRect("Btn_" + label, parent);
            SetAbs(rt, x, y, w, h);
            var img = AddImage(rt, bg);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            cb.pressedColor = new Color(0.65f, 0.65f, 0.65f, 1f);
            cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            btn.colors = cb;

            text = AddText(rt, "Label", label, size, TextAlignmentOptions.Midline, Accent);
            Stretch(text.rectTransform);

            btn.onClick.AddListener(onClick);
            return btn;
        }

        private static Slider AddSlider(RectTransform parent)
        {
            var rt = NewRect("Slider", parent);

            var bg = NewRect("Background", rt);
            bg.anchorMin = new Vector2(0f, 0.35f);
            bg.anchorMax = new Vector2(1f, 0.65f);
            bg.offsetMin = Vector2.zero;
            bg.offsetMax = Vector2.zero;
            AddImage(bg, new Color(0.25f, 0.25f, 0.25f, 1f));

            var fillArea = NewRect("Fill Area", rt);
            fillArea.anchorMin = new Vector2(0f, 0.35f);
            fillArea.anchorMax = new Vector2(1f, 0.65f);
            fillArea.offsetMin = Vector2.zero;
            fillArea.offsetMax = Vector2.zero;

            var fill = NewRect("Fill", fillArea);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            AddImage(fill, Accent);

            var handleArea = NewRect("Handle Slide Area", rt);
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(8f, 0f);
            handleArea.offsetMax = new Vector2(-8f, 0f);

            var handle = NewRect("Handle", handleArea);
            handle.anchorMin = new Vector2(0f, 0.1f);
            handle.anchorMax = new Vector2(0f, 0.9f);
            handle.sizeDelta = new Vector2(16f, 0f);
            var handleImg = AddImage(handle, Color.white);

            var slider = rt.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.value = 1f;
            return slider;
        }

        private static TMP_InputField BuildInput(RectTransform parent, string placeholder,
                                                 float x, float y, float w, float h)
        {
            var rt = NewRect("Search", parent);
            SetAbs(rt, x, y, w, h);
            rt.gameObject.SetActive(false);

            var bg = AddImage(rt, new Color(0.12f, 0.12f, 0.12f, 1f));
            var input = rt.gameObject.AddComponent<TMP_InputField>();

            var area = NewRect("TextArea", rt);
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(10f, 4f);
            area.offsetMax = new Vector2(-10f, -4f);
            area.gameObject.AddComponent<RectMask2D>();

            var text = AddText(area, "Text", "", 24, TextAlignmentOptions.MidlineLeft, Color.white);
            Stretch(text.rectTransform);

            var ph = AddText(area, "Placeholder", placeholder, 24, TextAlignmentOptions.MidlineLeft,
                             new Color(1f, 1f, 1f, 0.35f));
            ph.fontStyle = FontStyles.Italic;
            Stretch(ph.rectTransform);

            input.textViewport = area;
            input.textComponent = text;
            input.placeholder = ph;
            input.targetGraphic = bg;
            input.caretColor = Accent;
            input.selectionColor = new Color(1f, 0.45f, 0.1f, 0.35f);

            rt.gameObject.SetActive(true);
            return input;
        }
    }
}   