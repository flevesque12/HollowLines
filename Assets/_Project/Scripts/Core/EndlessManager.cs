using System;

namespace HollowLines.Core
{
    /// <summary>
    /// Endless mode progression. The counterpart of <see cref="CampaignManager"/>: no levels, no
    /// win condition — the run ends when the player suffocates or runs out of hearts, and the
    /// score that matters is how deep they got (design rule 6, GDD §7).
    ///
    /// <para><b>Why segments.</b> A <see cref="GridModel"/> has a fixed height, so "infinite"
    /// descent is played as a chain of finite boards. This class owns the bookkeeping that makes
    /// that chain read as one continuous well:</para>
    /// <list type="bullet">
    ///   <item>absolute <see cref="Depth"/> accumulates ACROSS segments (a per-board
    ///         <see cref="DepthTracker"/> resets every board, so it cannot be the run metric);</item>
    ///   <item>the generator's difficulty ramp resumes at <see cref="SegmentStartDepth"/> instead
    ///         of restarting, via <see cref="StrateGenerator.EndlessSegment"/>;</item>
    ///   <item>the air drain ramps with depth (GDD §7), so deep runs demand faster play.</item>
    /// </list>
    ///
    /// Pure C# — no MonoBehaviour, no UnityEngine. The view (GameBootstrap) listens to
    /// <see cref="SegmentExhausted"/> and loads <see cref="BuildCurrentSegment"/> after calling
    /// <see cref="AdvanceSegment"/>.
    /// </summary>
    public sealed class EndlessManager
    {
        // ── Tunables ─────────────────────────────────────────────────
        /// <summary>Content rows generated per segment (excludes spawn zone and floor row).</summary>
        public const int DefaultSegmentRows = 60;

        /// <summary>
        /// Rows above the bedrock floor at which the current segment is considered exhausted.
        /// Same threshold as the campaign win line — the player "reaches the bottom", except here
        /// the bottom just hands them the next segment.
        /// </summary>
        public const int ExitDepthFromFloor = CampaignManager.WinDepthFromFloor;

        // Air drain ramp (GDD §7): drain = 5.0 + (depth / 20) × 0.5, capped at 10 %/s.
        // At depth 0 that is the 20 s tank of a campaign level 1-3; at depth 100 it is 13.3 s.
        // Endless has no level structure to carry difficulty, so the clock IS the difficulty curve
        // (terrain rates ramp too, but the drain is what forces increasingly aggressive play).
        public const float BaseDrainRate   = AirSystem.DefaultDrainRate; // 5 %/s at the surface
        public const float DrainStepRows   = 20f;
        public const float DrainStepAmount = 0.5f;
        public const float MaxDrainRate    = 10f;

        /// <summary>
        /// Wobble telegraph for endless, in seconds. Matches campaign level 4+ (design rule 4):
        /// endless is unlocked after the campaign, so the player has already learned to read it.
        /// </summary>
        public const float WobbleDuration = 0.6f;

        // ── State ────────────────────────────────────────────────────
        /// <summary>Run seed. Same seed → same well, which is what Daily Dig (R3.4) needs.</summary>
        public int Seed { get; private set; }

        /// <summary>Content rows per segment for this run.</summary>
        public int SegmentRows { get; }

        /// <summary>0-based index of the segment currently being played.</summary>
        public int SegmentIndex { get; private set; }

        /// <summary>Absolute depth of the current segment's FIRST content row.</summary>
        public int SegmentStartDepth { get; private set; }

        /// <summary>Deepest absolute row reached this run — the leaderboard metric.</summary>
        public int Depth { get; private set; }

        /// <summary>Current air drain in % per second, derived from <see cref="Depth"/>.</summary>
        public float DrainRate { get; private set; }

        // ── Events ───────────────────────────────────────────────────
        /// <summary>Fired when the run reaches a new deepest absolute row. Arg = that depth.</summary>
        public event Action<int> DepthChanged;

        /// <summary>Fired when the depth ramp changes the drain. Arg = the new rate in %/s.</summary>
        public event Action<float> DrainRateChanged;

        /// <summary>
        /// Fired once when the avatar reaches the bottom of the current segment.
        /// Arg = the index of the segment that was exhausted. The view transitions, then calls
        /// <see cref="AdvanceSegment"/> + <see cref="BuildCurrentSegment"/>.
        /// </summary>
        public event Action<int> SegmentExhausted;

        private bool _exhausted; // guard: fire SegmentExhausted once per segment

        public EndlessManager(int seed, int segmentRows = DefaultSegmentRows)
        {
            if (segmentRows <= 0)
                throw new ArgumentOutOfRangeException(nameof(segmentRows));

            SegmentRows = segmentRows;
            Reset(seed);
        }

        // ── Depth ↔ drain ────────────────────────────────────────────

        /// <summary>
        /// Air drain in % per second at the given absolute depth (GDD §7).
        /// Continuous, not stepped: depth 0 → 5 %/s, depth 100 → 7.5 %/s, capped at 10 %/s
        /// (reached at depth 200) so the tank never becomes unplayably short.
        /// </summary>
        public static float DrainRateForDepth(int depth)
        {
            if (depth <= 0)
                return BaseDrainRate;

            float rate = BaseDrainRate + depth / DrainStepRows * DrainStepAmount;
            return Math.Min(MaxDrainRate, rate);
        }

        /// <summary>
        /// Absolute depth of a local board row: the segment's first CONTENT row is
        /// <see cref="SegmentStartDepth"/>.
        ///
        /// The spawn zone maps ABOVE that (negative offsets), not clamped to it — on segment 2+
        /// the avatar respawns at the top of a fresh board, and clamping would credit the seam
        /// itself as new depth. Only the very surface of the run is floored at 0.
        /// </summary>
        public int DepthForLocalRow(int localRow) =>
            Math.Max(0, SegmentStartDepth + localRow - StrateGenerator.SpawnRows);

        // ── Board ────────────────────────────────────────────────────

        /// <summary>
        /// Builds the board for the current segment. Deterministic: the same run seed always
        /// produces the same chain of segments.
        /// </summary>
        public string[] BuildCurrentSegment(int width = StrateGenerator.DefaultWidth) =>
            StrateGenerator.EndlessSegment(SegmentSeed(Seed, SegmentIndex), width, SegmentRows, SegmentStartDepth);

        /// <summary>
        /// Per-segment seed. Multiplied by a large prime so consecutive segments of one run look
        /// nothing alike (same reason CampaignBoard spreads its level seeds).
        /// </summary>
        public static int SegmentSeed(int runSeed, int segmentIndex) =>
            unchecked(runSeed + segmentIndex * (int)0x9E3779B9);

        // ── Progression ──────────────────────────────────────────────

        /// <summary>
        /// Call once per Update after AvatarModel.Tick(). Updates depth + drain, and fires
        /// <see cref="SegmentExhausted"/> when the avatar reaches the bottom of the board.
        /// boardHeight = grid.Height of the segment currently loaded.
        /// </summary>
        public void NotifyAvatarPosition(GridPos avatarPos, int boardHeight)
        {
            int depth = DepthForLocalRow(avatarPos.Y);
            if (depth > Depth)
            {
                Depth = depth;
                DepthChanged?.Invoke(Depth);

                float rate = DrainRateForDepth(Depth);
                if (rate != DrainRate)
                {
                    DrainRate = rate;
                    DrainRateChanged?.Invoke(DrainRate);
                }
            }

            if (_exhausted) return;

            if (avatarPos.Y >= boardHeight - ExitDepthFromFloor)
            {
                _exhausted = true;
                SegmentExhausted?.Invoke(SegmentIndex);
            }
        }

        /// <summary>
        /// Move on to the next segment. The new segment's first content row continues exactly
        /// where the old segment's last content row ended, so depth never jumps or repeats.
        /// </summary>
        public void AdvanceSegment()
        {
            SegmentIndex++;
            SegmentStartDepth += SegmentRows;
            _exhausted = false;
        }

        /// <summary>Starts a fresh run on the given seed.</summary>
        public void Reset(int seed)
        {
            Seed              = seed;
            SegmentIndex      = 0;
            SegmentStartDepth = 0;
            Depth             = 0;
            DrainRate         = BaseDrainRate;
            _exhausted        = false;
        }
    }
}
