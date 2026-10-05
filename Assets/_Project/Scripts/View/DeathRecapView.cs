using System.Globalization;
using HollowLines.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HollowLines.View
{
    /// <summary>
    /// R6.1 (F01): the "why did I die" block on the game-over card.
    ///
    /// Not a MonoBehaviour and not its own UIDocument — UIScreenManager owns the one overlay panel,
    /// so this just builds a VisualElement subtree into it and fills it from a DeathReport:
    ///   1. the killer, big and red ("Écrasé par un bloc");
    ///   2. one diagnostic line (time since the last drill for suffocation, run time otherwise);
    ///   3. a one-sentence tip that maps the cause back to the telegraph the player missed;
    ///   4. the run's heart-loss timeline, so a 3-hit death reads as three separate mistakes.
    /// All player-facing text is French (§12); the data comes from Core.DeathTracker.
    /// </summary>
    public sealed class DeathRecapView
    {
        private const int MaxTimelineRows = 5; // 3 hearts per level, but endless carries hearts over segments

        private static readonly Color ColDanger = new Color(0.95f, 0.30f, 0.18f);
        private static readonly Color ColMuted  = new Color(0.70f, 0.70f, 0.70f);
        private static readonly Color ColAmber  = new Color(0.94f, 0.62f, 0.15f);
        private static readonly CultureInfo Fr  = CultureInfo.GetCultureInfo("fr-CA");

        private readonly VisualElement _root;
        private readonly Label         _cause;
        private readonly Label         _detail;
        private readonly Label         _tip;
        private readonly VisualElement _timeline;

        public DeathRecapView(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.alignItems   = Align.Center;
            _root.style.marginBottom = 16f;
            _root.style.marginRight  = 48f; // gutter to the run summary beside it
            _root.style.flexShrink   = 0f; // the overlay panel is height-bound; squeezed rows overlap
            _root.style.width        = 420f; // fixed, so the tip wraps instead of widening the card
            _root.style.display      = DisplayStyle.None;
            parent.Add(_root);

            _cause = new Label();
            _cause.style.fontSize                = 26f;
            _cause.style.color                   = new StyleColor(ColDanger);
            _cause.style.unityFontStyleAndWeight = FontStyle.Bold;
            _cause.style.unityTextAlign          = TextAnchor.MiddleCenter;
            _root.Add(_cause);

            _detail = new Label();
            _detail.style.fontSize       = 14f;
            _detail.style.color          = new StyleColor(ColMuted);
            _detail.style.unityTextAlign = TextAnchor.MiddleCenter;
            _detail.style.marginTop      = 4f;
            _root.Add(_detail);

            _tip = new Label();
            _tip.style.fontSize       = 15f;
            _tip.style.color          = new StyleColor(ColAmber);
            _tip.style.unityTextAlign = TextAnchor.MiddleCenter;
            _tip.style.whiteSpace     = WhiteSpace.Normal;
            _tip.style.marginTop      = 8f;
            _root.Add(_tip);

            _timeline = new VisualElement();
            _timeline.style.alignItems = Align.Stretch;
            _timeline.style.minWidth   = 320f;
            _timeline.style.marginTop  = 10f;
            _timeline.style.flexShrink = 0f;
            _root.Add(_timeline);
        }

        public void Show(DeathReport report)
        {
            _cause.text  = CauseTitle(report.Cause);
            _detail.text = report.Cause == DeathCause.Suffocation
                ? $"Dernier forage il y a {Seconds(report.TimeSinceLastDrill)}  ·  après {Clock(report.Time)}"
                : $"Coup fatal à {Clock(report.Time)}  ·  air restant {Mathf.RoundToInt(report.Air)} %";
            _tip.text = "Astuce : " + CauseTip(report.Cause);

            BuildTimeline(report);
            _root.style.display = DisplayStyle.Flex;
        }

        public void Hide() => _root.style.display = DisplayStyle.None;

        // ─────────────────────────────────────────────────────────────────────
        // Timeline
        // ─────────────────────────────────────────────────────────────────────

        private void BuildTimeline(DeathReport report)
        {
            _timeline.Clear();
            var hits = report.Hits;
            if (hits.Count == 0)
            {
                _timeline.style.display = DisplayStyle.None;
                return;
            }
            _timeline.style.display = DisplayStyle.Flex;

            var caption = new Label("CŒURS PERDUS");
            caption.style.fontSize       = 12f;
            caption.style.color          = new StyleColor(ColMuted);
            caption.style.unityTextAlign = TextAnchor.MiddleCenter;
            caption.style.marginBottom   = 4f;
            _timeline.Add(caption);

            // Newest hits matter most; older ones collapse into a count.
            int first = Mathf.Max(0, hits.Count - MaxTimelineRows);
            if (first > 0)
                _timeline.Add(Row("…", $"{first} coup(s) plus tôt", "", ColMuted));

            for (int i = first; i < hits.Count; i++)
            {
                var hit   = hits[i];
                bool last = i == hits.Count - 1 && report.Cause != DeathCause.Suffocation;
                _timeline.Add(Row(Clock(hit.Time), CauseTitle(hit.Cause),
                                  hit.HeartsLeft > 0 ? $"reste {hit.HeartsLeft}" : "K.O.",
                                  last ? ColDanger : Color.white));
            }
        }

        private static VisualElement Row(string time, string what, string hearts, Color color)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.marginTop     = 2f;
            row.style.flexShrink    = 0f;

            var t = new Label(time);
            t.style.fontSize    = 13f;
            t.style.color       = new StyleColor(ColMuted);
            t.style.width       = 52f;
            row.Add(t);

            var w = new Label(what);
            w.style.fontSize = 14f;
            w.style.color    = new StyleColor(color);
            w.style.flexGrow = 1f;
            row.Add(w);

            var h = new Label(hearts);
            h.style.fontSize                = 13f;
            h.style.color                   = new StyleColor(color);
            h.style.unityFontStyleAndWeight = FontStyle.Bold;
            h.style.marginLeft              = 16f;
            row.Add(h);

            return row;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Copy
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>The killer, in the player's words.</summary>
        public static string CauseTitle(DeathCause cause)
        {
            switch (cause)
            {
                case DeathCause.ChunkCrush:     return "Écrasé par un bloc";
                case DeathCause.Collapse:       return "Écrasé par l'effondrement";
                case DeathCause.BurstShockwave: return "Pris dans un éclatement";
                case DeathCause.BombBlast:      return "Soufflé par une bombe";
                case DeathCause.Enemy:          return "Mordu par un Crawler";
                case DeathCause.Suffocation:    return "À court d'air";
                default:                        return "Mort mystérieuse";
            }
        }

        /// <summary>
        /// One sentence per cause, each pointing at the telegraph or the counter-play the game already
        /// gives (design rule 4: no unfair deaths — the recap's job is to show the warning was there).
        /// </summary>
        public static string CauseTip(DeathCause cause)
        {
            switch (cause)
            {
                case DeathCause.ChunkCrush:
                    return "un bloc qui tremble va tomber. Sors de sous lui pendant qu'il tremble.";
                case DeathCause.Collapse:
                    return "quand une rangée complète se vide, tout ce qui est au-dessus descend d'un cran.";
                case DeathCause.BurstShockwave:
                    return "un gros bloc qui tombe de 2 rangées ou plus éclate à l'impact — ne reste pas dessous.";
                case DeathCause.BombBlast:
                    return "une bombe armée clignote 1,5 s avant d'exploser en croix. Sors de sa ligne et de sa colonne.";
                case DeathCause.Enemy:
                    return "un Crawler réveillé patrouille sa rangée. Écrase-le avec un bloc ou change de rangée.";
                case DeathCause.Suffocation:
                    return "chaque forage redonne de l'air. Continue de forer vers le bas et ramasse les capsules.";
                default:
                    return "continue de descendre !";
            }
        }

        private static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        private static string Seconds(float seconds) => seconds.ToString("0.0", Fr) + " s";
    }
}
