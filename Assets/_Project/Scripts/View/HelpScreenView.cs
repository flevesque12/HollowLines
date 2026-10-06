using System;
using System.Globalization;
using HollowLines.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HollowLines.View
{
    /// <summary>
    /// R6.12 (F16): the in-game help — "AIDE", reachable from the main menu and the pause screen.
    ///
    /// Like OptionsMenu and DeathRecapView, a plain class that builds a subtree into
    /// UIScreenManager's single overlay panel. One page per mechanic, each a short list of
    /// icon + sentence lines; the icons are the game's own tiles and enemy sprites, so the player
    /// learns to recognise exactly what they will see on the board.
    ///
    /// Navigation: the page body is ONE focusable element — Left/Right flips pages (and is swallowed
    /// so it doesn't also move focus), Down reaches "Retour" through the default navigation. The
    /// ◀ ▶ buttons are mouse-only. Numbers come from the Core constants wherever one exists, so the
    /// help can't drift from the rules.
    /// </summary>
    public sealed class HelpScreenView
    {
        private static readonly Color ColMuted  = new Color(0.70f, 0.70f, 0.70f);
        private static readonly Color ColAmber  = new Color(0.94f, 0.62f, 0.15f);
        private static readonly Color ColDotOff = new Color(0.30f, 0.25f, 0.20f);
        private static readonly Color ColRowBg  = new Color(0.13f, 0.11f, 0.09f);
        private static readonly Color ColButton = new Color(0.20f, 0.17f, 0.14f);

        // Same defaults as BoardView / EnemyView, so the icons match the board.
        private static readonly Color TintAmber   = new Color(0.94f, 0.62f, 0.15f);
        private static readonly Color TintTeal    = new Color(0.11f, 0.62f, 0.46f);
        private static readonly Color TintPink    = new Color(0.83f, 0.33f, 0.49f);
        private static readonly Color TintDiamond = new Color(0.80f, 0.92f, 1.00f);
        private static readonly Color Crawler     = new Color(0.62f, 0.95f, 0.20f);
        private static readonly Color Boomer      = new Color(0.66f, 0.38f, 1.00f);

        private readonly struct Line
        {
            public readonly Sprite Icon;
            public readonly Color  Tint;
            public readonly string Text;
            public readonly float  IconScale; // the 16x16 enemy sprites carry transparent margin
            public Line(string text, Sprite icon = null, Color? tint = null, float iconScale = 1f)
            {
                Text = text; Icon = icon; Tint = tint ?? Color.white; IconScale = iconScale;
            }
        }

        private readonly struct Page
        {
            public readonly string Title;
            public readonly Line[] Lines;
            public Page(string title, params Line[] lines) { Title = title; Lines = lines; }
        }

        private readonly VisualElement _root;
        private readonly VisualElement _body;
        private readonly Label         _pageTitle;
        private readonly VisualElement _lines;
        private readonly VisualElement _dots;
        private readonly Page[]        _pages;
        private int _index;

        public HelpScreenView(VisualElement parent)
        {
            _pages = BuildPages();

            _root = new VisualElement();
            _root.style.width        = 680f;
            _root.style.marginBottom = 20f;
            _root.style.flexShrink   = 0f;
            _root.style.display      = DisplayStyle.None;
            parent.Add(_root);

            // ── Page body (the focus target) ─────────────────────────────────
            _body = new VisualElement { focusable = true };
            _body.style.minHeight               = 300f;
            _body.style.paddingTop              = 16f;
            _body.style.paddingBottom           = 16f;
            _body.style.paddingLeft             = 22f;
            _body.style.paddingRight            = 22f;
            _body.style.backgroundColor         = new StyleColor(ColRowBg);
            _body.style.borderTopLeftRadius     = 8f;
            _body.style.borderTopRightRadius    = 8f;
            _body.style.borderBottomLeftRadius  = 8f;
            _body.style.borderBottomRightRadius = 8f;
            _body.style.borderTopWidth    = 2f;
            _body.style.borderBottomWidth = 2f;
            _body.style.borderLeftWidth   = 2f;
            _body.style.borderRightWidth  = 2f;
            SetBorderColor(_body, Color.clear);
            _body.RegisterCallback<FocusInEvent>(_  => SetBorderColor(_body, new Color(1f, 1f, 1f, 0.6f)));
            _body.RegisterCallback<FocusOutEvent>(_ => SetBorderColor(_body, Color.clear));
            _body.RegisterCallback<NavigationMoveEvent>(evt =>
            {
                int delta = evt.direction == NavigationMoveEvent.Direction.Left  ? -1
                          : evt.direction == NavigationMoveEvent.Direction.Right ?  1 : 0;
                if (delta == 0) return; // Down: let the default navigation reach "Retour"
                Turn(delta);
                evt.StopPropagation();
                _body.focusController?.IgnoreEvent(evt);
            });
            _root.Add(_body);

            _pageTitle = new Label();
            _pageTitle.style.fontSize                = 22f;
            _pageTitle.style.color                   = new StyleColor(ColAmber);
            _pageTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _pageTitle.style.marginBottom            = 12f;
            _body.Add(_pageTitle);

            _lines = new VisualElement();
            _body.Add(_lines);

            // ── Pager: ◀  ● ○ ○ …  ▶ ────────────────────────────────────────
            var pager = new VisualElement();
            pager.style.flexDirection  = FlexDirection.Row;
            pager.style.justifyContent = Justify.Center;
            pager.style.alignItems     = Align.Center;
            pager.style.marginTop      = 12f;
            _root.Add(pager);

            pager.Add(PagerButton("◀", () => Turn(-1)));
            _dots = new VisualElement();
            _dots.style.flexDirection = FlexDirection.Row;
            _dots.style.marginLeft    = 12f;
            _dots.style.marginRight   = 12f;
            pager.Add(_dots);
            for (int i = 0; i < _pages.Length; i++)
            {
                int page = i;
                var dot = new VisualElement();
                dot.style.width                   = 10f;
                dot.style.height                  = 10f;
                dot.style.marginLeft              = 4f;
                dot.style.marginRight             = 4f;
                dot.style.borderTopLeftRadius     = 5f;
                dot.style.borderTopRightRadius    = 5f;
                dot.style.borderBottomLeftRadius  = 5f;
                dot.style.borderBottomRightRadius = 5f;
                dot.RegisterCallback<PointerDownEvent>(_ => ShowPage(page));
                _dots.Add(dot);
            }
            pager.Add(PagerButton("▶", () => Turn(1)));

            ShowPage(0);
        }

        /// <summary>Open on the first page with the focus on it, so ← → work at once.</summary>
        public void Show()
        {
            ShowPage(0);
            _root.style.display = DisplayStyle.Flex;
            _body.schedule.Execute(() => _body.Focus());
        }

        public void Hide() => _root.style.display = DisplayStyle.None;

        // ─────────────────────────────────────────────────────────────────────

        private void Turn(int delta) => ShowPage((_index + delta + _pages.Length) % _pages.Length);

        private void ShowPage(int index)
        {
            _index = Mathf.Clamp(index, 0, _pages.Length - 1);
            Page page = _pages[_index];

            _pageTitle.text = $"{_index + 1}/{_pages.Length}  ·  {page.Title}";
            _lines.Clear();
            foreach (Line line in page.Lines)
                _lines.Add(BuildLine(line));

            for (int i = 0; i < _dots.childCount; i++)
                _dots[i].style.backgroundColor = new StyleColor(i == _index ? ColAmber : ColDotOff);
        }

        private static VisualElement BuildLine(Line line)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.marginTop     = 7f;
            row.style.flexShrink    = 0f;

            // Fixed-width icon slot (empty when the line has no icon) keeps the text aligned.
            var icon = new VisualElement();
            icon.style.width       = 30f;
            icon.style.height      = 30f;
            icon.style.marginRight = 14f;
            icon.style.flexShrink  = 0f;
            if (line.Icon != null)
            {
                icon.style.backgroundImage               = new StyleBackground(line.Icon);
                icon.style.unityBackgroundImageTintColor = new StyleColor(line.Tint);
                icon.style.scale                         = new Scale(new Vector3(line.IconScale, line.IconScale, 1f));
            }
            row.Add(icon);

            var text = new Label(line.Text);
            text.style.fontSize   = 16f;
            text.style.color      = new StyleColor(Color.white);
            text.style.whiteSpace = WhiteSpace.Normal;
            text.style.flexShrink = 1f;
            text.style.flexGrow   = 1f;
            row.Add(text);
            return row;
        }

        private static Button PagerButton(string text, Action onClick)
        {
            var b = new Button(onClick) { text = text, focusable = false }; // mouse only: the page owns focus
            b.style.width                   = 36f;
            b.style.height                  = 28f;
            b.style.fontSize                = 14f;
            b.style.backgroundColor         = new StyleColor(ColButton);
            b.style.color                   = new StyleColor(Color.white);
            b.style.borderTopWidth    = 0f;
            b.style.borderBottomWidth = 0f;
            b.style.borderLeftWidth   = 0f;
            b.style.borderRightWidth  = 0f;
            return b;
        }

        private static void SetBorderColor(VisualElement e, Color c)
        {
            var sc = new StyleColor(c);
            e.style.borderTopColor    = sc;
            e.style.borderBottomColor = sc;
            e.style.borderLeftColor   = sc;
            e.style.borderRightColor  = sc;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Content — French, player-facing (§12). Numbers from Core wherever a constant exists.
        // ─────────────────────────────────────────────────────────────────────

        private static Page[] BuildPages()
        {
            Sprite neutral = Tile("tile_neutral");
            Sprite hard    = Tile("tile_hard");
            Sprite steel   = Tile("tile_steel");
            Sprite capsule = Tile("tile_capsule");
            Sprite bomb    = Tile("tile_bomb");
            Sprite crawler = EnemySprites.Crawler(Crawler, eyesOpen: true);
            Sprite boomer  = EnemySprites.Boomer(Boomer, eyesOpen: true);
            Sprite alert   = EnemySprites.Alert();

            return new[]
            {
                new Page("LE BUT",
                    new Line("Fore vers le bas jusqu'à la ligne d'arrivée en damier doré. Les flèches dorées sur les côtés te montrent où elle est."),
                    new Line("Campagne : à partir du niveau 4, il faut aussi un score minimum pour sortir. Pas assez de points en arrivant au fond ? Continue de marquer : la sortie s'ouvre dès que tu les as."),
                    new Line("Sans fin : pas de fond. Descends le plus creux possible avant de manquer d'air."),
                    new Line("Défi du jour : le même puits pour tout le monde, un nouveau chaque jour.")),

                new Page("CONTRÔLES",
                    new Line("Marcher : A / D (ou Q / D)  ·  manette : stick gauche ou croix ← →"),
                    new Line("Forer : flèches du clavier  ·  manette : Y ↑   A ↓   X ←   B →  (le bouton est placé du côté où tu fores)"),
                    new Line("Pause : Échap  ·  manette : Start"),
                    new Line("Menus : flèches ou croix pour choisir, Entrée ou A pour valider, Échap ou B pour revenir.")),

                new Page("FORER ET STREAK",
                    new Line($"Chaque bloc foré donne {ScoreSystem.DrillPoints} pts × ta streak, et {Fr(AirSystem.DrillRestoreAmount)} % d'air.", neutral, TintAmber),
                    new Line("Fore VERS LE BAS dans la même couleur : la streak monte (×2, ×3, ×4…).", neutral, TintTeal),
                    new Line("Une autre couleur vers le bas casse la streak. Forer de côté ou vers le haut ne la change pas.", neutral, TintPink),
                    new Line("Bloc dur : il faut deux coups pour le percer.", hard),
                    new Line("Acier : impossible à forer. Seule une explosion le fissure.", steel)),

                new Page("L'AIR ET LES CŒURS",
                    new Line("L'air baisse tout le temps, et plus vite en profondeur. Si la barre se vide, c'est fini."),
                    new Line("Forer redonne de l'air : continue de creuser, ne reste jamais immobile."),
                    new Line($"Capsule AIR : +{AirSystem.CapsuleRestoreAmount:0} % d'air. Les bursts, les chaînes de bombes et les Perfect Clear en redonnent aussi.", capsule),
                    new Line($"Tu as {HealthSystem.MaxHearts} cœurs par niveau. Écrasé, pris dans une explosion ou touché par un ennemi : un cœur de moins.")),

                new Page("LES BLOCS QUI TOMBENT",
                    new Line("Les blocs de même couleur qui se touchent forment un seul gros bloc.", neutral, TintPink),
                    new Line("Retire ce qui le tient : il tremble un instant, puis tombe. Ne reste pas dessous !"),
                    new Line($"S'il tombe de {GravitySystem.BurstFallThreshold} rangées ou plus, il ÉCLATE : {ScoreSystem.BurstPointsPerCell} pts par bloc, multipliés par la hauteur de chute.", neutral, TintAmber),
                    new Line("L'onde de choc casse les blocs autour, libère les capsules d'air et allume les bombes.")),

                new Page("BOMBES",
                    new Line($"Fore à côté d'une bombe : sa mèche s'allume ({Fr(BombSystem.FuseDuration)} s).", bomb),
                    new Line($"Elle détruit les blocs en croix ({ScoreSystem.BombPointsPerBlock} pts par bloc) et libère les capsules d'air."),
                    new Line("Une bombe en allume d'autres : chaque bombe de la chaîne multiplie les points."),
                    new Line($"Une bombe que TU allumes explose à {BombSystem.DirectBlastRadius} case : un pas de côté suffit. Les autres portent à {BombSystem.ChainBlastRadius} cases.")),

                new Page("ENNEMIS",
                    new Line($"Crawler : se réveille quand tu creuses à côté, puis se promène. Le toucher coûte un cœur. Écrase-le avec un bloc, un burst ou une bombe : +{ScoreSystem.CrawlerKillPoints} pts.", crawler, iconScale: 1.5f),
                    new Line($"Boomer : il ne te fait jamais mal. Tué par un burst ou une bombe, il explose et casse les blocs autour : +{ScoreSystem.BoomerKillPoints} pts, et plus en chaîne.", boomer, iconScale: 1.5f),
                    new Line("« ! » au-dessus d'un ennemi : il vient de se réveiller, ou un Crawler s'approche de toi.", alert, iconScale: 1.3f)),

                new Page("BONUS",
                    new Line($"Diamant : +{ScoreSystem.DiamondPoints} pts, foré ou libéré par une explosion. Prends-le s'il est sur ton chemin.", neutral, TintDiamond),
                    new Line($"Profondeur : +{ScoreSystem.DepthPoints} pts chaque fois que tu atteins une rangée plus creuse."),
                    new Line($"Perfect Clear : vide une rangée au complet pour +{ScoreSystem.PerfectClearBase} pts. Rare, mais payant !")),
            };
        }

        private static readonly CultureInfo FrCa = CultureInfo.GetCultureInfo("fr-CA");

        /// <summary>0.5 → "0,5", 1.5 → "1,5" (French decimal comma).</summary>
        private static string Fr(float v) => v.ToString("0.#", FrCa);

        private static Sprite Tile(string name) =>
            Resources.Load<Sprite>("Tiles/" + name) ?? BoardView.GetUnitSprite();
    }
}
