using System;
using System.Collections.Generic;
using System.Text;

namespace HollowLines.Core
{
    /// <summary>
    /// Procedurally builds board maps from layered StrateDescriptor configs.
    ///
    /// A "strate" is a horizontal band with its own density and block-type rules.
    /// Stack several strates to produce the full well — the generator adds the empty
    /// spawn zone at the top and a solid floor at the bottom automatically.
    ///
    /// Output is string[] where each element is one row, suitable for GridModel.FromStringMap().
    ///
    /// Cell placement priority (highest wins): Empty → Steel → Bomb → Hard → Air → Diamond → color.
    /// Remaining probability after all special rates is filled with color blocks.
    ///
    /// Bomb adjacency rule: no two bombs are placed side-by-side in the same row
    /// (prevents trivial same-tick sympathetic chains at game start).
    /// </summary>
    public static class StrateGenerator
    {
        public const int SpawnRows    = 3;
        public const int FloorRows    = 1;
        public const int DefaultWidth = 7;

        // Campaign porosity. v3 needs MORE holes than v2.1 did: a chunk only bursts if it can
        // fall 2+ rows, which requires two stacked gaps under it. Roughly, the odds a given
        // column offers such a gap scale with EmptyRate², so the v2.1 values (0.20/0.30) made
        // bursts rare. Levels 1-3 stay a little denser so onboarding has fewer falling hazards.
        public const float EarlyEmptyRate = 0.28f; // levels 1-3
        public const float LateEmptyRate  = 0.36f; // levels 4-10

        // Color cohesion (v3 §15.3). Rolling every cell's color independently made a same-color
        // run of 4+ almost impossible, so the Color Streak system had no material to work with —
        // measured streak share of score fell to 0%. Colors now grow in veins instead: each color
        // cell copies a neighbor rather than re-rolling.
        //
        // Vertical is weighted higher because drilling straight down is the natural instinct, and
        // that is the streak the player will actually build. Expected vein length ≈ 1/(1-p), so
        // 0.75 gives runs of ~4 — a routine ×4 streak. Cohesion also grows the chunks, which
        // feeds Chunk Burst (§15.4).
        public const float VerticalColorCohesion   = 0.75f;
        public const float HorizontalColorCohesion = 0.55f;

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Build a full board from an array of strate descriptors.
        /// Prepends <see cref="SpawnRows"/> empty rows and one solid floor row automatically.
        /// </summary>
        public static string[] Build(StrateDescriptor[] strates, int width, Random rng)
        {
            if (strates == null || strates.Length == 0)
                throw new ArgumentException("At least one strate required.", nameof(strates));
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (rng == null)
                throw new ArgumentNullException(nameof(rng));

            int totalRows = SpawnRows + FloorRows;
            foreach (var s in strates)
                totalRows += s.Rows;

            var rows = new string[totalRows];
            int cursor = 0;

            // Empty spawn zone — avatar must start in clear air.
            string emptyRow = new string('.', width);
            for (int i = 0; i < SpawnRows; i++)
                rows[cursor++] = emptyRow;

            // Content layers. Each row inherits the last color seen in each column — NOT the
            // literal row above. A vein must survive the gaps and Hard/Steel cells between its
            // segments, exactly like the player's streak survives streak-neutral cells. Passing
            // the literal row above instead lets a 36% empty rate shred every vein (§15.3).
            var columnColors = new char[width]; // '\0' = no color placed in this column yet
            foreach (var desc in strates)
            {
                for (int r = 0; r < desc.Rows; r++)
                {
                    string row = GenerateRow(desc, width, rng, new string(columnColors));
                    rows[cursor++] = row;

                    for (int x = 0; x < width; x++)
                    {
                        if (IsUsableColor(row[x], 3))
                            columnColors[x] = row[x];
                    }
                }
            }

            // Solid floor — unbreakable bedrock keeps the board finite.
            rows[cursor] = new string('A', width);

            return rows;
        }

        /// <summary>
        /// Generate a single strate row. Exposed for unit tests.
        /// </summary>
        /// <param name="columnColors">
        ///   The last color placed in each column (NOT the literal row above), so a vein continues
        ///   through gaps and Hard/Steel cells. Use '\0' for "no color yet". Pass null for a
        ///   standalone row — colors then only cohere horizontally.
        /// </param>
        public static string GenerateRow(StrateDescriptor desc, int width, Random rng, string columnColors = null)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            var sb = new StringBuilder(width);
            bool prevWasBomb = false;

            for (int x = 0; x < width; x++)
            {
                char cell = PickCellType(desc, rng);

                // Horizontal adjacency guard: no two bombs side-by-side.
                if (cell == 'X' && prevWasBomb)
                    cell = ColorSlot;

                prevWasBomb = (cell == 'X');

                if (cell == ColorSlot)
                    cell = PickColor(desc, rng, Above(columnColors, x), Left(sb, x));

                sb.Append(cell);
            }

            // Row must contain at least one solid block — a completely void row would
            // collapse immediately on the first frame (CollapseSystem detects it).
            bool hasSolid = false;
            for (int i = 0; i < sb.Length; i++)
            {
                if (sb[i] != '.') { hasSolid = true; break; }
            }

            if (!hasSolid)
            {
                int x = rng.Next(width);
                sb[x] = PickColor(desc, rng, Above(columnColors, x), '\0');
            }

            return sb.ToString();
        }

        private static char Above(string columnColors, int x) =>
            columnColors != null && x < columnColors.Length ? columnColors[x] : '\0';

        private static char Left(StringBuilder sb, int x) =>
            x > 0 ? sb[x - 1] : '\0';

        /// <summary>
        /// Preset board for the given campaign level (1–10).
        /// Uses a deterministic seed so the same level always produces the same board.
        /// </summary>
        public static string[] CampaignBoard(int level)
        {
            if (level < 1 || level > 10)
                throw new ArgumentOutOfRangeException(nameof(level), "Level must be between 1 and 10.");

            // Multiply by a large prime so adjacent levels have very different boards.
            var rng = new Random(unchecked(level * (int)0x9E3779B9));
            string[] board = Build(CampaignStrates(level), DefaultWidth, rng);

            // v3 teaching moments: streak on level 2, burst on level 3.
            if (level == 2) InjectStreakTutorial(board);
            if (level == 3) InjectBurstTutorial(board);

            // R4: diamonds place last, after the tutorials, so they never land on an authored
            // vein/chunk cell. Same rng stream — CampaignBoard stays deterministic per level.
            int diamonds = DiamondCountForLevel(level);
            if (diamonds > 0)
                PlaceDiamonds(board, diamonds, rng);

            return board;
        }

        /// <summary>
        /// R4 (§9): exact diamond count per campaign level. Levels 1-3 are pure tutorial (0).
        /// v3.1: diamonds no longer gate the level — each is a +150 pt bonus toward the score
        /// minimum CampaignManager.ScoreMinimumForLevel gates on (§5.7).
        /// </summary>
        public static int DiamondCountForLevel(int level)
        {
            switch (level)
            {
                case 1:
                case 2:
                case 3:  return 0;
                case 4:  return 2;
                case 5:  return 3;
                case 6:  return 3;
                case 7:  return 4;
                case 8:  return 4;
                case 9:  return 5;
                default: return 5; // level 10
            }
        }

        /// <summary>
        /// R4: scatter exactly <paramref name="count"/> diamonds across the board's content rows
        /// (never the spawn zone, never the floor — design rule 8: always below spawn). Mutates
        /// <paramref name="board"/> in place. Exposed public for unit tests (mirrors GenerateRow).
        ///
        /// Candidates are existing drillable color/Hard cells only — never Empty (would float, and
        /// while GravitySystem.Settle() at load would resolve it harmlessly, an existing solid cell
        /// is simpler and guaranteed reachable), never Steel/Bomb (§9: "never behind walls" — a
        /// diamond must never require a bomb just to reach). Scattering across the full width, not
        /// just the spawn column, is deliberate: §9 wants a routing choice ("drill sideways for it,
        /// which costs air"), not a freebie on the straight-down path.
        ///
        /// If the board has fewer eligible cells than <paramref name="count"/> (defensive — real
        /// campaign strates never run this short), it places as many as it can rather than throwing.
        /// </summary>
        public static void PlaceDiamonds(string[] board, int count, Random rng)
        {
            if (board == null || board.Length == 0 || count <= 0)
                return;
            if (rng == null)
                throw new ArgumentNullException(nameof(rng));

            int width    = board[0].Length;
            int firstRow = SpawnRows;
            int lastRow  = board.Length - FloorRows - 1;

            var candidates = new List<GridPos>();
            for (int y = firstRow; y <= lastRow; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    char c = board[y][x];
                    if (c == 'A' || c == 'B' || c == 'C' || c == 'H')
                        candidates.Add(new GridPos(x, y));
                }
            }

            // Partial Fisher-Yates: pick up to `count` distinct candidates uniformly at random.
            int pool = candidates.Count;
            int toPlace = Math.Min(count, pool);
            for (int i = 0; i < toPlace; i++)
            {
                int j = i + rng.Next(pool - i);
                GridPos tmp = candidates[i];
                candidates[i] = candidates[j];
                candidates[j] = tmp;

                GridPos pos = candidates[i];
                var row = board[pos.Y].ToCharArray();
                row[pos.X] = 'D';
                board[pos.Y] = new string(row);
            }
        }

        // ── Enemy placement (🆕R5.13, §9) ────────────────────────────────────

        /// <summary>Minimum row gap between any two enemies — keeps them scattered, never clustered.</summary>
        public const int MinEnemyRowSpacing = 3;

        /// <summary>A Crawler prefers a row with at least this many empty cells to shuffle along.</summary>
        public const int CrawlerLateralSpace = 3;

        /// <summary>A Boomer prefers to sit within this many rows of a buried bomb.</summary>
        public const int BoomerBombProximity = 2;

        /// <summary>Crawlers per campaign level (§9). None before level 6, where bombs already taught burst kills.</summary>
        public static int CrawlerCountForLevel(int level)
        {
            switch (level)
            {
                case 6:
                case 7:  return 3;
                case 8:  return 4;
                case 9:  return 5;
                case 10: return 6;
                default: return 0; // levels 1-5
            }
        }

        /// <summary>Boomers per campaign level (§9). None before level 7 — the player has to have seen bomb chains first.</summary>
        public static int BoomerCountForLevel(int level)
        {
            switch (level)
            {
                case 7:  return 2;
                case 8:  return 3;
                case 9:  return 4;
                case 10: return 5;
                default: return 0; // levels 1-6
            }
        }

        /// <summary>
        /// R5.13 (§9): pick the enemy placements for a campaign board. Returns (type, position)
        /// pairs; the enemy count per level comes from <see cref="CrawlerCountForLevel"/> /
        /// <see cref="BoomerCountForLevel"/>. Levels 1-5 return an empty list.
        ///
        /// **The board is NOT mutated** — read-only input. Enemies are ACTORS, not CellTypes (§6.5):
        /// they have no map character and cannot live in the `rows` at all (GridModel.FromStringMap
        /// would throw on one). An enemy's position is the coordinate of an existing solid cell,
        /// which is exactly what "buried/dormant" means — the cell keeps its own type, and the enemy
        /// sits on top of it as a separate entity. That is also what makes the Crawler work: it is
        /// walled in until the player drills next to it, which both wakes it (§6.5 activation) and
        /// opens the tunnel it then walks into.
        ///
        /// Placement rules, in order of how hard they bind:
        ///   HARD — content rows only (never the spawn zone, never the floor), and only on an
        ///          existing Color/Hard cell. Never Empty (an enemy hanging in mid-air reads as a
        ///          bug), never Steel or Bomb (those cells have their own meaning to the player).
        ///   HARD — at least <see cref="MinEnemyRowSpacing"/> rows between any two enemies, of
        ///          either type. Enforced by construction: each enemy claims a whole row.
        ///   SOFT — Boomers prefer rows within <see cref="BoomerBombProximity"/> of a buried bomb,
        ///          so their death blast has a real chance of setting one off (design rule 9).
        ///   SOFT — Crawlers prefer rows with at least <see cref="CrawlerLateralSpace"/> empty
        ///          cells, so there is somewhere to shuffle once woken.
        /// Soft rules are preferences, not filters: each type takes its preferred rows first and
        /// falls back to any legal row, so the per-level count is always met when geometry allows.
        /// </summary>
        public static List<(EnemyType type, GridPos pos)> PlaceEnemies(string[] board, int level, Random rng)
        {
            var placements = new List<(EnemyType type, GridPos pos)>();

            if (board == null || board.Length == 0)
                return placements;
            if (rng == null)
                throw new ArgumentNullException(nameof(rng));

            int crawlers = CrawlerCountForLevel(level);
            int boomers  = BoomerCountForLevel(level);
            if (crawlers + boomers == 0)
                return placements;

            int width    = board[0].Length;
            int firstRow = SpawnRows;
            int lastRow  = board.Length - FloorRows - 1;
            if (lastRow < firstRow)
                return placements;

            // One pass over the content rows collects everything both preferences need.
            var legalRows     = new List<int>();
            var rowColumns    = new Dictionary<int, List<int>>(); // row → columns holding a Color/Hard cell
            var rowEmptyCount = new Dictionary<int, int>();
            var bombRows      = new List<int>();

            for (int y = firstRow; y <= lastRow; y++)
            {
                var columns = new List<int>();
                int empty   = 0;
                bool hasBomb = false;

                for (int x = 0; x < width; x++)
                {
                    char c = board[y][x];
                    if (c == 'A' || c == 'B' || c == 'C' || c == 'H') columns.Add(x);
                    else if (c == '.') empty++;
                    else if (c == 'X') hasBomb = true;
                }

                if (hasBomb)
                    bombRows.Add(y);
                if (columns.Count == 0)
                    continue; // nowhere solid to bury an enemy in this row

                legalRows.Add(y);
                rowColumns[y]    = columns;
                rowEmptyCount[y] = empty;
            }

            var usedRows = new List<int>();

            // Boomers go first: "near a bomb" is the narrower preference, so it gets first pick of
            // the rows before Crawler placement starts blocking them out.
            PlaceEnemyType(EnemyType.Boomer, boomers,
                           OrderRowsByPreference(legalRows, y => IsNearBombRow(y, bombRows), rng),
                           rowColumns, usedRows, placements, rng);

            PlaceEnemyType(EnemyType.Crawler, crawlers,
                           OrderRowsByPreference(legalRows, y => rowEmptyCount[y] >= CrawlerLateralSpace, rng),
                           rowColumns, usedRows, placements, rng);

            return placements;
        }

        private static void PlaceEnemyType(EnemyType type, int count, List<int> orderedRows,
                                           Dictionary<int, List<int>> rowColumns, List<int> usedRows,
                                           List<(EnemyType type, GridPos pos)> placements, Random rng)
        {
            int placed = 0;
            foreach (int y in orderedRows)
            {
                if (placed >= count)
                    return;
                if (!IsFarEnough(y, usedRows))
                    continue;

                List<int> columns = rowColumns[y];
                int x = columns[rng.Next(columns.Count)];

                placements.Add((type, new GridPos(x, y)));
                usedRows.Add(y);
                placed++;
            }
            // Falls through short only if the board genuinely has no legal spacing left — defensive,
            // real campaign boards are far taller than count × MinEnemyRowSpacing.
        }

        /// <summary>Preferred rows first (shuffled), then everything else (shuffled) as fallback.</summary>
        private static List<int> OrderRowsByPreference(List<int> rows, Func<int, bool> isPreferred, Random rng)
        {
            var preferred = new List<int>();
            var rest      = new List<int>();

            foreach (int y in rows)
            {
                if (isPreferred(y)) preferred.Add(y);
                else                rest.Add(y);
            }

            Shuffle(preferred, rng);
            Shuffle(rest, rng);
            preferred.AddRange(rest);
            return preferred;
        }

        private static bool IsNearBombRow(int row, List<int> bombRows)
        {
            foreach (int bombRow in bombRows)
            {
                if (Math.Abs(row - bombRow) <= BoomerBombProximity)
                    return true;
            }
            return false;
        }

        private static bool IsFarEnough(int row, List<int> usedRows)
        {
            foreach (int used in usedRows)
            {
                if (Math.Abs(row - used) < MinEnemyRowSpacing)
                    return false;
            }
            return true;
        }

        private static void Shuffle(List<int> items, Random rng)
        {
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int tmp  = items[i];
                items[i] = items[j];
                items[j] = tmp;
            }
        }

        // ── Tutorial showcase (intro board, played before campaign level 1) ──

        /// <summary>The avatar's spawn column on the tutorial board — the natural straight-down path.</summary>
        public const int TutorialSpawnColumn = DefaultWidth / 2;

        /// <summary>Row of the on-path diamond in the new Diamonds chamber (R4).</summary>
        public const int TutorialDiamondRow = 9;

        /// <summary>Row of the full-width ColorC slab that hides the optional Perfect Clear secret.</summary>
        public const int TutorialPerfectClearRow = 11;

        /// <summary>Top row of the two-row ColorC chunk in the burst chamber.</summary>
        public const int TutorialBurstChunkTop = 13;

        /// <summary>Row holding the three buried bombs of the chain chamber.</summary>
        public const int TutorialBombRow = 21;

        /// <summary>
        /// Hand-authored, fully deterministic showcase level that teaches every scoring method in
        /// isolated chambers, top to bottom. Unlike <see cref="CampaignBoard"/> it uses NO RNG and
        /// NO <see cref="Build"/> — every cell is placed on purpose so each lesson is guaranteed,
        /// not emergent.
        ///
        /// The whole structure hangs off a continuous ColorA frame (side walls at col 0 / col 6 plus
        /// the solid divider rows) that fuses down to the bedrock floor, so <c>GravitySystem.Settle()</c>
        /// at load finds everything supported and drops nothing. Movable lesson pieces are a DIFFERENT
        /// color (ColorB vein, ColorC chunk/slab) so they never fuse into the frame and only move when
        /// the player acts. See CLAUDE.md §5.8 for why an unanchored authored structure self-destructs
        /// at load — this frame is exactly the countermeasure.
        ///
        /// The R4 Diamond chamber (rows 9-10) follows the SAME rule even though Diamond isn't part of
        /// the frame color: each diamond cell sits directly on a solid cell of its own (col 3 rests on
        /// the ColorA divider at row 10; the row-10 diamond at col 1 rests on the Perfect Clear slab at
        /// row 11) — never on empty air — so both are grounded at Settle() instead of dropping onto the
        /// slab below and destroying the deliberate layout (§5.2's "unanchored structure" trap applies
        /// to any solid cell, not just fused chunks).
        ///
        ///   rows 0-2   spawn zone (avatar @ col 3)
        ///   rows 3-8   CHAMBER 1 — Streak + Depth: a 6-block ColorB vein straight down → ×6
        ///   rows 9-10  🆕R4 CHAMBER — Diamonds: one free pickup on the straight-down path (col 3),
        ///              one just off to the side (col 1) rewarding a one-step detour (§6.4)
        ///   row  11    ★ Perfect Clear SECRET: a full-width ColorC slab; clear all 7 → +500 (optional)
        ///   row  12    air capsule (P) on the main path — shows the air restore
        ///   rows 13-18 CHAMBER 2 — Chunk Burst: a 10-cell ColorC chunk on ONE support over a drop zone
        ///   rows 19-23 CHAMBER 3 — Bomb chain: drilling in arms a bomb → ×3 sympathetic chain that
        ///              blasts the floor open and drops the player to the win line (design rule 3).
        ///              🔄v3.2 (§5.3, CLAUDE.md): the bomb the player drills directly only blasts at
        ///              BombSystem.DirectBlastRadius (1) — every OTHER bomb in the chain (sympathetic
        ///              detonation, always) keeps BombSystem.ChainBlastRadius (2). So the layout is
        ///              now a straight vertical stack: the player-armed bomb sits directly above a
        ///              second bomb (distance 1 — reachable even at DirectBlastRadius), and THAT
        ///              bomb's wider ChainBlastRadius reaches sideways to ignite a third.
        ///   rows 24-25 finale + bedrock (reaching row 24 wins: depth = height - WinDepthFromFloor)
        /// </summary>
        public static string[] TutorialBoard()
        {
            return new[]
            {
                ".......", //  0  spawn
                ".......", //  1  spawn
                ".......", //  2  spawn — avatar starts here (col 3)

                "A..B..A", //  3  ┐ CHAMBER 1: ColorB streak vein, drill straight down for ×6
                "A..B..A", //  4  │
                "A..B..A", //  5  │
                "A..B..A", //  6  │
                "A..B..A", //  7  │
                "A..B..A", //  8  ┘

                "A..D..A", //  9  ┐ CHAMBER — Diamonds (R4): free pickup on the straight-down path…
                "AD.A..A", // 10  ┘ …plus one off to the side (col 1) — a one-step detour for +150 (§6.4)

                "CCCCCCC", // 11  ★ Perfect Clear secret slab — clear the whole row → +500
                "AAAPAAA", // 12  divider + air capsule (P), on the straight-down path

                "ACCCCCA", // 13  ┐ CHAMBER 2: 10-cell ColorC chunk…
                "ACCCCCA", // 14  ┘
                "A....AA", // 15  …resting on ONE support (col 5, fused to the right wall)
                "A.....A", // 16  ┐ drop zone: 3 clear rows → fall distance ≥ 2 → burst
                "A.....A", // 17  ┘
                "AAAAAAA", // 18  burst landing floor

                "A.....A", // 19  ┐ CHAMBER 3: landing
                "ACCCCCA", // 20  │ ColorC filler the blast will score
                "ACCXCCA", // 21  │ bomb 1 (col 3) — armed by the player, DirectBlastRadius = 1
                "AXCXCCA", // 22  │ bomb 2 (col 3, distance 1 below bomb 1) + bomb 3 (col 1, distance
                           // │   2 left of bomb 2 — reachable only at bomb 2's ChainBlastRadius = 2)
                "AAAAAAA", // 23  ┘ divider — bombs 2 and 3 each punch straight through it below them

                "A.....A", // 24  finale: reaching this row wins (depth = height - 2)
                "AAAAAAA", // 25  bedrock
            };
        }

        /// <summary>Rows of the authored streak vein. 5 same-color blocks = a guaranteed ×5 streak.</summary>
        public const int StreakTutorialRows = 5;

        /// <summary>Rows of the authored burst band (chunk + support + drop zone + hard floor).</summary>
        public const int BurstTutorialRows = 7;

        /// <summary>
        /// Top row of the burst tutorial band on a board of this height.
        /// The band sits directly on the board's bedrock floor ON PURPOSE: an authored solid row
        /// placed mid-board is not anchored, and with porous terrain beneath it the whole band
        /// settles downward — falling 2+ rows, which makes it burst on load and destroys the
        /// setup before the player ever sees it (§15.1 regression). The bedrock never moves.
        /// </summary>
        public static int BurstTutorialTop(int boardHeight) =>
            boardHeight - FloorRows - BurstTutorialRows;

        /// <summary>
        /// Level 2 — Color Streak. Overwrite the spawn column for the first rows below the spawn
        /// zone with one unbroken vein of ColorA:
        ///
        ///   ...A...
        ///   ...A...   ← 5 blocks, same color, straight down
        ///   ...A...
        ///   ...A...
        ///   ...A...
        ///
        /// Digging straight down (the natural first instinct) never breaks the color, so the streak
        /// climbs to ×5 with no explanation needed. Mutates <paramref name="board"/> in place, so the
        /// row count from CampaignStrates is preserved exactly.
        /// </summary>
        private static void InjectStreakTutorial(string[] board)
        {
            int width = board[0].Length;
            int col   = width / 2; // the avatar's spawn column
            int last  = SpawnRows + StreakTutorialRows - 1;

            if (last + 1 >= board.Length)
                return; // board too short to host the vein (defensive — campaign boards are not)

            for (int y = SpawnRows; y <= last; y++)
            {
                var row = board[y].ToCharArray();
                row[col] = 'A';
                board[y] = new string(row);
            }

            // The vein must rest on something at load, or it falls before the player reaches it.
            var below = board[last + 1].ToCharArray();
            if (below[col] == '.')
            {
                below[col] = 'B'; // different color so it does not extend the streak
                board[last + 1] = new string(below);
            }
        }

        /// <summary>
        /// Level 3 — Chunk Burst. Overwrite an authored band below the spawn zone (width 7 shown):
        ///
        ///   .CCCCC.   ← 10-cell chunk…
        ///   .CCCCC.
        ///   .....AA   ← …resting on ONE block (col 5), grounded by the right-hand pillar
        ///   ......A
        ///   ......A   ← 4 empty rows under the overhang: the drop zone
        ///   ......A
        ///   AAAAAAA   ← authored floor, so the pillar is grounded whatever was generated below
        ///
        /// Drill the lone support and all 10 cells fall 4 rows → guaranteed burst. A player who
        /// instead digs straight down splits the chunk and undermines the left half, which also
        /// falls 4 rows and bursts — both routes teach the same lesson. Mutates in place.
        /// </summary>
        private static void InjectBurstTutorial(string[] board)
        {
            int width = board[0].Length;
            int top   = BurstTutorialTop(board.Length);
            if (width < 4 || top < SpawnRows)
                return; // defensive: not enough room to author the setup

            int supportCol = width - 2; // the chunk's only footing
            int pillarCol  = width - 1; // grounds the support without blocking the drop zone

            string chunkRow = "." + new string('C', width - 2) + ".";

            var supportRow = new string('.', width).ToCharArray();
            supportRow[supportCol] = 'A';
            supportRow[pillarCol]  = 'A';

            var pillarRow = new string('.', width).ToCharArray();
            pillarRow[pillarCol] = 'A';

            int y = top;
            board[y++] = chunkRow;
            board[y++] = chunkRow;
            board[y++] = new string(supportRow);
            board[y++] = new string(pillarRow);
            board[y++] = new string(pillarRow);
            board[y++] = new string(pillarRow);
            board[y]   = new string('A', width); // merges with the bedrock row directly below
        }

        /// <summary>
        /// Procedural endless board for the given seed and well depth (content rows only,
        /// not counting spawn zone and floor). Difficulty ramps from scratch — this is the
        /// first segment of a run.
        /// </summary>
        public static string[] EndlessBoard(int seed, int width, int depth) =>
            EndlessSegment(seed, width, depth, startDepth: 0);

        /// <summary>
        /// One segment of an endless run. Identical to <see cref="EndlessBoard"/> except the
        /// difficulty ramp CONTINUES from <paramref name="startDepth"/> (the absolute depth of
        /// this segment's first content row) instead of restarting at 0.
        ///
        /// Endless is played as a chain of finite boards (a GridModel has a fixed height), so
        /// without this the player would fall back to 2-color, hazard-free terrain every segment
        /// — the descent would get easier the deeper they went, which inverts the whole mode.
        /// </summary>
        public static string[] EndlessSegment(int seed, int width, int rows, int startDepth)
        {
            if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (startDepth < 0) throw new ArgumentOutOfRangeException(nameof(startDepth));

            // Seeds arrive from arithmetic (EndlessManager.SegmentSeed wraps unchecked), so
            // int.MinValue is reachable — and it is the one value some System.Random
            // implementations choke on (no positive counterpart to abs).
            var rng = new Random(seed == int.MinValue ? int.MaxValue : seed);
            return Build(EndlessStrates(rng, rows, startDepth), width, rng);
        }

        // ── Campaign level presets ───────────────────────────────────────────

        /// <summary>
        /// Campaign ramp (CLAUDE.md §5.8 mechanic order, §9 row counts).
        /// Content rows = total rows from §9 minus SpawnRows + FloorRows, so level 1 = 24 total.
        /// One new mechanic per level; nothing appears before the level that teaches it.
        ///
        /// Depths were tripled for v3 (§15.1). The v2.1 line gate was the run's time sink; with
        /// depth as the only win condition, a 14-26 row board is over before the air clock does
        /// anything — even a human-paced player spent only 9-25% of their air. Raising drain
        /// instead would need ~20%/s to bite in 2 s, which would make exploring lethal. The board
        /// has to be long enough for air to be a resource you manage.
        /// </summary>
        private static StrateDescriptor[] CampaignStrates(int level)
        {
            switch (level)
            {
                case 1: // 24 rows — movement + drilling. Two colors only, readable fusion.
                    return new[]
                    {
                        new StrateDescriptor(rows: 10, maxColors: 2, emptyRate: EarlyEmptyRate),
                        new StrateDescriptor(rows: 10, maxColors: 2, emptyRate: EarlyEmptyRate),
                    };

                case 2: // 30 rows — Color Streak. Third color arrives; authored vein teaches it.
                    return new[]
                    {
                        new StrateDescriptor(rows: 13, maxColors: 3, emptyRate: EarlyEmptyRate),
                        new StrateDescriptor(rows: 13, maxColors: 3, emptyRate: EarlyEmptyRate),
                    };

                case 3: // 32 rows — Chunks + Chunk Burst. Extra porosity so wild bursts also happen.
                    return new[]
                    {
                        new StrateDescriptor(rows: 14, maxColors: 3, emptyRate: EarlyEmptyRate + 0.04f),
                        new StrateDescriptor(rows: 14, maxColors: 3, emptyRate: EarlyEmptyRate + 0.04f),
                    };

                case 4: // 40 rows — air capsules + real drain pressure.
                    return new[]
                    {
                        new StrateDescriptor(rows: 12, maxColors: 3, airRate: 0.08f, emptyRate: LateEmptyRate),
                        new StrateDescriptor(rows: 12, maxColors: 3, airRate: 0.10f, emptyRate: LateEmptyRate),
                        new StrateDescriptor(rows: 12, maxColors: 3, airRate: 0.07f, emptyRate: LateEmptyRate),
                    };

                case 5: // 44 rows — Hard blocks: the 2-hit drill.
                    return new[]
                    {
                        new StrateDescriptor(rows: 13, maxColors: 3, airRate: 0.07f, emptyRate: LateEmptyRate),
                        new StrateDescriptor(rows: 13, maxColors: 3, hardRate: 0.08f, airRate: 0.05f, emptyRate: LateEmptyRate),
                        new StrateDescriptor(rows: 14, maxColors: 3, hardRate: 0.12f, emptyRate: LateEmptyRate),
                    };

                case 6: // 50 rows — single bombs: arm one, survive the fuse, enjoy the payout.
                    return new[]
                    {
                        new StrateDescriptor(rows: 15, maxColors: 3, airRate: 0.08f, emptyRate: LateEmptyRate),
                        new StrateDescriptor(rows: 15, maxColors: 3, hardRate: 0.08f, bombRate: 0.05f, emptyRate: LateEmptyRate),
                        new StrateDescriptor(rows: 16, maxColors: 3, hardRate: 0.10f, bombRate: 0.06f, airRate: 0.05f, emptyRate: LateEmptyRate),
                    };

                case 7: // 54 rows — bomb chains: denser bombs so sympathetic detonations happen.
                    // R2.8c: bombRate was 0.12/0.14 — the densest of any level — but measured, that
                    // WALLED the player in (bombs are not drillable) and produced FEWER chains than
                    // levels 8-10, the opposite of the intent (§15.4 R2.8e). Bombs blast radius 2 and
                    // detonate each other, so ~0.09-0.10 already clusters enough to chain without the
                    // undrilllable field blocking every descent path.
                    return new[]
                    {
                        new StrateDescriptor(rows: 16, maxColors: 3, airRate: 0.08f, bombRate: 0.06f, emptyRate: LateEmptyRate),
                        new StrateDescriptor(rows: 17, maxColors: 3, hardRate: 0.08f, bombRate: 0.09f, emptyRate: LateEmptyRate),
                        new StrateDescriptor(rows: 17, maxColors: 3, hardRate: 0.10f, bombRate: 0.10f, airRate: 0.05f, emptyRate: LateEmptyRate),
                    };

                case 8: // 60 rows — Steel: bomb-only walls force routing decisions.
                    return new[]
                    {
                        new StrateDescriptor(rows: 18, maxColors: 3, airRate: 0.08f, bombRate: 0.06f, emptyRate: LateEmptyRate),
                        new StrateDescriptor(rows: 19, maxColors: 3, steelRate: 0.12f, hardRate: 0.08f, bombRate: 0.08f, emptyRate: LateEmptyRate),
                        new StrateDescriptor(rows: 19, maxColors: 3, steelRate: 0.15f, hardRate: 0.08f, bombRate: 0.08f, airRate: 0.05f, emptyRate: LateEmptyRate),
                    };

                case 9: // 68 rows — full mix, high pressure.
                    return new[]
                    {
                        new StrateDescriptor(rows: 21, maxColors: 3, airRate: 0.10f, bombRate: 0.06f, emptyRate: LateEmptyRate),
                        new StrateDescriptor(rows: 21, maxColors: 3, hardRate: 0.10f, steelRate: 0.08f, bombRate: 0.08f, emptyRate: LateEmptyRate),
                        new StrateDescriptor(rows: 22, maxColors: 3, hardRate: 0.08f, steelRate: 0.10f, bombRate: 0.10f, airRate: 0.05f, emptyRate: LateEmptyRate),
                    };

                default: // 10 — 76 rows, dense final board.
                    return new[]
                    {
                        new StrateDescriptor(rows: 24, maxColors: 3, airRate: 0.10f, bombRate: 0.08f, emptyRate: LateEmptyRate),
                        new StrateDescriptor(rows: 24, maxColors: 3, hardRate: 0.12f, steelRate: 0.12f, bombRate: 0.08f, airRate: 0.06f, emptyRate: LateEmptyRate),
                        new StrateDescriptor(rows: 24, maxColors: 3, hardRate: 0.10f, steelRate: 0.12f, bombRate: 0.10f, airRate: 0.04f, emptyRate: LateEmptyRate),
                    };
            }
        }

        // ── Endless mode ─────────────────────────────────────────────────────

        /// <summary>
        /// Content rows over which the endless difficulty ramp climbs from 0 to 1 (GDD §7).
        /// Past that the board is at steady-state max difficulty.
        /// </summary>
        public const float EndlessRampRows = 80f;

        // Endless steady-state caps (R3.1, measured 2026-07-26). These were originally 0.15 hard /
        // 0.12 steel / 0.36 empty, set by estimate before endless was playable. Measured against the
        // campaign — the only balance yardstick this game has evidence for — that terrain was worse
        // than ANY authored board:
        //
        //                       color cells   hard+steel   burst-ready
        //   campaign 10 (hardest)   32 %         13 %         13 %
        //   endless steady-state    22 %         27 %         11 %
        //
        // Color blocks are the material of the core loop (design rule 1: every drill pays ×streak),
        // and at 22 % two thirds of the solid cells were hazard — the streak starved exactly where
        // the run is supposed to peak, while steel (not drillable) forced ever more air-costly
        // detours. Endless is meant to END harder than the campaign, not to stop being the game.
        //
        // Retuned to land on campaign-10 terrain and let the DRAIN ramp carry late difficulty
        // instead (§15.1: the air clock is the tax on score-farming). Endless-only — campaign
        // strates are untouched.
        public const float EndlessMaxHardRate   = 0.10f;
        public const float EndlessMaxSteelRate  = 0.08f;
        public const float EndlessMaxBombRate   = 0.08f; // kept: campaign 10 ships ~9 % bomb cells
        public const float EndlessLateEmptyRate = 0.40f; // > LateEmptyRate: buys back burst opportunity

        /// <summary>
        /// R4 (§6.4): diamonds in Endless are a rare bonus drop, not a gated objective — a flat
        /// per-cell roll (unlike the campaign's exact per-level count) since there is nothing to
        /// guarantee. Does not ramp with difficulty; it is a constant trickle throughout the well.
        /// </summary>
        public const float EndlessDiamondRate = 0.02f;

        private static StrateDescriptor[] EndlessStrates(Random rng, int totalRows, int startDepth = 0)
        {
            var strates = new List<StrateDescriptor>();
            int remaining = totalRows;

            // Ramps from 0 to 1 over EndlessRampRows content rows — resumed at startDepth so a
            // mid-run segment keeps the difficulty the player already dug down to.
            float difficulty = Math.Min(1f, startDepth / EndlessRampRows);

            while (remaining > 0)
            {
                int h = Math.Min(3 + rng.Next(3), remaining); // 3–5 rows per strate

                float hard  = difficulty * EndlessMaxHardRate;
                float steel = difficulty * EndlessMaxSteelRate;
                float bomb  = difficulty * EndlessMaxBombRate;
                float air   = 0.06f + rng.Next(5) * 0.01f; // 6–10 %
                int colors  = difficulty < 0.10f ? 2 : 3;

                // Porosity. NOT the GDD §7 "empty rate" column (25%→12%): those are pre-balance
                // numbers, and §15.3 established they are too low for Chunk Burst to work — a burst
                // needs TWO stacked gaps under a chunk, so opportunity scales ~EmptyRate². We ramp
                // the two v3-calibrated campaign constants instead (0.28 → 0.36 with difficulty), so
                // deeper endless stays porous enough for bursts while hard/steel/bomb rates carry the
                // difficulty. (Passing 0f here was a bug: a zero-porosity board never falls or bursts.)
                // R3.1 raised the deep end to EndlessLateEmptyRate: measured, the 0.36 target left
                // burst opportunity at ~11 % against the campaign's 12-15 %, i.e. the deepest boards
                // offered the FEWEST bursts — backwards for the mode whose whole payoff is spectacle.
                float empty = EarlyEmptyRate + (EndlessLateEmptyRate - EarlyEmptyRate) * difficulty;

                strates.Add(new StrateDescriptor(h, hard, steel, bomb, air, empty, colors, EndlessDiamondRate));

                remaining  -= h;
                difficulty  = Math.Min(1f, difficulty + h / EndlessRampRows);
            }

            return strates.ToArray();
        }

        // ── Cell generation helpers ──────────────────────────────────────────

        /// <summary>Placeholder meaning "a color belongs here" — resolved by PickColor.</summary>
        private const char ColorSlot = '';

        /// <summary>
        /// Decides the cell's TYPE only. Color choice is deferred to PickColor so it can look at
        /// its neighbors — the type roll must not consume the color's randomness.
        /// </summary>
        private static char PickCellType(StrateDescriptor desc, Random rng)
        {
            double roll   = rng.NextDouble();
            double cursor = 0;

            cursor += desc.EmptyRate;
            if (roll < cursor) return '.';

            cursor += desc.SteelRate;
            if (roll < cursor) return 'S';

            cursor += desc.BombRate;
            if (roll < cursor) return 'X';

            cursor += desc.HardRate;
            if (roll < cursor) return 'H';

            cursor += desc.AirRate;
            if (roll < cursor) return 'P';

            cursor += desc.DiamondRate;
            if (roll < cursor) return 'D';

            return ColorSlot;
        }

        /// <summary>
        /// Pick a color, preferring to continue a neighbor's vein (§15.3). Vertical wins over
        /// horizontal: the straight-down driller is the streak we most want to feed.
        /// A neighbor outside the current MaxColors is ignored, so a 2-color strate never
        /// inherits a 'C' from the 3-color strate above it.
        /// </summary>
        private static char PickColor(StrateDescriptor desc, Random rng, char above, char left)
        {
            if (IsUsableColor(above, desc.MaxColors) && rng.NextDouble() < VerticalColorCohesion)
                return above;

            if (IsUsableColor(left, desc.MaxColors) && rng.NextDouble() < HorizontalColorCohesion)
                return left;

            return RandomColor(desc.MaxColors, rng);
        }

        private static bool IsUsableColor(char c, int maxColors)
        {
            switch (c)
            {
                case 'A': return true;
                case 'B': return maxColors >= 2;
                case 'C': return maxColors >= 3;
                default:  return false;
            }
        }

        private static char RandomColor(int maxColors, Random rng)
        {
            switch (rng.Next(maxColors < 1 ? 1 : maxColors > 3 ? 3 : maxColors))
            {
                case 0:  return 'A';
                case 1:  return 'B';
                default: return 'C';
            }
        }
    }
}
