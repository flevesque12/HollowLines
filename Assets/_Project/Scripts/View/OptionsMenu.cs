using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace HollowLines.View
{
    /// <summary>
    /// Player volume levels (R6.9, F10), 0..1 each, persisted in PlayerPrefs. These SCALE the
    /// AudioManager's designed mix — 1 = the mix as tuned, never louder — so a fresh install sounds
    /// exactly like the game did before the options existed.
    /// </summary>
    public struct VolumeSettings
    {
        public float Master;
        public float Music;
        public float Sfx;

        private const string MasterKey = "hl.volume.master";
        private const string MusicKey  = "hl.volume.music";
        private const string SfxKey    = "hl.volume.sfx";

        public static VolumeSettings Load() => new VolumeSettings
        {
            Master = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterKey, 1f)),
            Music  = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey,  1f)),
            Sfx    = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxKey,    1f)),
        };

        public void Save()
        {
            PlayerPrefs.SetFloat(MasterKey, Mathf.Clamp01(Master));
            PlayerPrefs.SetFloat(MusicKey,  Mathf.Clamp01(Music));
            PlayerPrefs.SetFloat(SfxKey,    Mathf.Clamp01(Sfx));
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// R6.9 (F10): the volume block of the OPTIONS screen. Like DeathRecapView, a plain class that
    /// builds a subtree into UIScreenManager's single overlay panel — no MonoBehaviour, no second
    /// UIDocument.
    ///
    /// Three rows (Général / Musique / Effets), each "−  ■■■■■■□□□□  +  60 %". A row is ONE focusable
    /// element: Up/Down (D-pad, stick, arrows) moves between rows with the default navigation, and
    /// Left/Right on the focused row changes its value by 10 % instead of moving focus. The −/+
    /// buttons and the segments are mouse-only (not focusable), so they never break the
    /// row-to-row navigation. Every change is applied and saved at once.
    /// </summary>
    public sealed class OptionsMenu
    {
        private const int Steps = 10;

        private static readonly Color ColMuted   = new Color(0.70f, 0.70f, 0.70f);
        private static readonly Color ColAmber   = new Color(0.94f, 0.62f, 0.15f);
        private static readonly Color ColSegOff  = new Color(0.22f, 0.18f, 0.15f);
        private static readonly Color ColRowBg   = new Color(0.13f, 0.11f, 0.09f);
        private static readonly Color ColButton  = new Color(0.20f, 0.17f, 0.14f);

        private readonly VisualElement _root;
        private readonly Row[] _rows;
        private VolumeSettings _settings;

        /// <summary>Fired after every change with the new levels (already saved).</summary>
        public event Action<VolumeSettings> Changed;

        /// <summary>Fired when the Effects level changes, so the player hears the new level.</summary>
        public event Action SfxPreviewRequested;

        private sealed class Row
        {
            public VisualElement Root;
            public VisualElement[] Segments;
            public Label Value;
            public int Level; // 0..Steps
        }

        public OptionsMenu(VisualElement parent)
        {
            _settings = VolumeSettings.Load();

            _root = new VisualElement();
            _root.style.alignItems   = Align.Stretch;
            _root.style.marginBottom = 24f;
            _root.style.flexShrink   = 0f;
            _root.style.display      = DisplayStyle.None;
            parent.Add(_root);

            _rows = new[]
            {
                BuildRow("GÉNÉRAL", _settings.Master),
                BuildRow("MUSIQUE", _settings.Music),
                BuildRow("EFFETS",  _settings.Sfx),
            };
        }

        public VolumeSettings Settings => _settings;

        public void Show()
        {
            _root.style.display = DisplayStyle.Flex;
            // After layout, so the gamepad starts on the first row.
            _rows[0].Root.schedule.Execute(() => _rows[0].Root.Focus());
        }

        public void Hide() => _root.style.display = DisplayStyle.None;

        // ─────────────────────────────────────────────────────────────────────

        private Row BuildRow(string caption, float value)
        {
            var row = new Row { Level = Mathf.Clamp(Mathf.RoundToInt(value * Steps), 0, Steps) };

            var root = new VisualElement { focusable = true };
            root.style.flexDirection           = FlexDirection.Row;
            root.style.alignItems              = Align.Center;
            root.style.marginTop               = 6f;
            root.style.paddingTop              = 8f;
            root.style.paddingBottom           = 8f;
            root.style.paddingLeft             = 14f;
            root.style.paddingRight            = 14f;
            root.style.backgroundColor         = new StyleColor(ColRowBg);
            root.style.borderTopLeftRadius     = 6f;
            root.style.borderTopRightRadius    = 6f;
            root.style.borderBottomLeftRadius  = 6f;
            root.style.borderBottomRightRadius = 6f;
            // Same focus cursor as the overlay buttons: a reserved 2 px border, white when focused.
            root.style.borderTopWidth    = 2f;
            root.style.borderBottomWidth = 2f;
            root.style.borderLeftWidth   = 2f;
            root.style.borderRightWidth  = 2f;
            SetBorderColor(root, Color.clear);
            root.RegisterCallback<FocusInEvent>(_  => SetBorderColor(root, Color.white));
            root.RegisterCallback<FocusOutEvent>(_ => SetBorderColor(root, Color.clear));
            root.RegisterCallback<NavigationMoveEvent>(evt =>
            {
                int delta = evt.direction == NavigationMoveEvent.Direction.Left  ? -1
                          : evt.direction == NavigationMoveEvent.Direction.Right ?  1 : 0;
                if (delta == 0) return; // Up/Down: let the default navigation move between rows
                SetLevel(row, row.Level + delta);
                evt.StopPropagation();
                root.focusController?.IgnoreEvent(evt); // Left/Right adjusts — it must not also move focus
            });
            _root.Add(root);
            row.Root = root;

            var label = new Label(caption);
            label.style.width                   = 110f;
            label.style.fontSize                = 14f;
            label.style.color                   = new StyleColor(ColMuted);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(label);

            root.Add(StepButton("−", () => SetLevel(row, row.Level - 1)));

            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.marginLeft    = 8f;
            bar.style.marginRight   = 8f;
            root.Add(bar);

            row.Segments = new VisualElement[Steps];
            for (int i = 0; i < Steps; i++)
            {
                int level = i + 1;
                var seg = new VisualElement();
                seg.style.width       = 18f;
                seg.style.height      = 22f;
                seg.style.marginLeft  = 2f;
                seg.style.marginRight = 2f;
                seg.style.borderTopLeftRadius     = 2f;
                seg.style.borderTopRightRadius    = 2f;
                seg.style.borderBottomLeftRadius  = 2f;
                seg.style.borderBottomRightRadius = 2f;
                seg.RegisterCallback<PointerDownEvent>(_ => SetLevel(row, level));
                bar.Add(seg);
                row.Segments[i] = seg;
            }

            root.Add(StepButton("+", () => SetLevel(row, row.Level + 1)));

            row.Value = new Label();
            row.Value.style.width                   = 60f;
            row.Value.style.marginLeft              = 10f;
            row.Value.style.fontSize                = 16f;
            row.Value.style.color                   = new StyleColor(Color.white);
            row.Value.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Value.style.unityTextAlign          = TextAnchor.MiddleRight;
            root.Add(row.Value);

            Refresh(row);
            return row;
        }

        private static Button StepButton(string text, Action onClick)
        {
            var b = new Button(onClick) { text = text, focusable = false }; // mouse only: rows own the focus
            b.style.width                   = 32f;
            b.style.height                  = 28f;
            b.style.fontSize                = 18f;
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.style.backgroundColor         = new StyleColor(ColButton);
            b.style.color                   = new StyleColor(Color.white);
            b.style.borderTopWidth    = 0f;
            b.style.borderBottomWidth = 0f;
            b.style.borderLeftWidth   = 0f;
            b.style.borderRightWidth  = 0f;
            return b;
        }

        private void SetLevel(Row row, int level)
        {
            level = Mathf.Clamp(level, 0, Steps);
            if (level == row.Level) return;
            row.Level = level;
            Refresh(row);

            float v = level / (float)Steps;
            if      (row == _rows[0]) _settings.Master = v;
            else if (row == _rows[1]) _settings.Music  = v;
            else                      _settings.Sfx    = v;

            _settings.Save();
            Changed?.Invoke(_settings);
            if (row == _rows[2]) SfxPreviewRequested?.Invoke();
        }

        private static void Refresh(Row row)
        {
            for (int i = 0; i < Steps; i++)
                row.Segments[i].style.backgroundColor = new StyleColor(i < row.Level ? ColAmber : ColSegOff);
            row.Value.text = row.Level == 0 ? "Muet" : $"{row.Level * 100 / Steps} %";
        }

        private static void SetBorderColor(VisualElement e, Color c)
        {
            var sc = new StyleColor(c);
            e.style.borderTopColor    = sc;
            e.style.borderBottomColor = sc;
            e.style.borderLeftColor   = sc;
            e.style.borderRightColor  = sc;
        }
    }
}
