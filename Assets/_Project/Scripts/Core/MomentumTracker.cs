using System;

namespace HollowLines.Core
{
    /// <summary>
    /// v3.2 Drill Momentum (drill-momentum.md §M1/§M2) — replaces the color-based StreakTracker.
    ///
    /// Time-based: every drill, in any direction and of any drillable type, refreshes a 0.8 s window
    /// and adds momentum. Let the window run out and everything resets to Tier 0. Momentum climbs
    /// through 4 tiers (×1 / ×2 / ×4 / ×6); reaching Tier 3 fires PowerDrillActivated, and the caller
    /// (GameBootstrap) orchestrates the burst then calls CompletePowerDrill, which drops back to Tier 1.
    ///
    /// ⚡D2: tiers compare against a float accumulator, so the optional color bonus can add ×1.5
    /// per same-color drill without lying about DrillCount. Only real colors (CanFuse) earn the bonus —
    /// Hard, HardCracked, AirCapsule and Diamond add a plain +1 and break the color chain.
    ///
    /// Pure counting/reporting — ScoreSystem turns Multiplier into points.
    /// </summary>
    public sealed class MomentumTracker
    {
        // ── Tuning (§M1) ─────────────────────────────────────────────
        public const float MomentumWindow = 0.8f;
        public const int   Tier1Threshold = 3;
        public const int   Tier2Threshold = 7;
        public const int   Tier3Threshold = 10;
        public const float ColorBonusRate = 1.5f;

        private static readonly float[] TierMultipliers = { 1f, 2f, 4f, 6f };

        private float    _momentumProgress;
        private float    _timer;
        private CellType _lastColor = CellType.Empty;

        // ── State ────────────────────────────────────────────────────
        /// <summary>0-3, derived from the internal progress accumulator (⚡D2).</summary>
        public int CurrentTier { get; private set; }

        /// <summary>Real number of drills in the current momentum (display only — tiers use progress).</summary>
        public int DrillCount { get; private set; }

        /// <summary>Consecutive same-color drills in the current momentum (0 after a non-color drill).</summary>
        public int ColorChain { get; private set; }

        /// <summary>×1 / ×2 / ×4 / ×6 for the current tier. Stays ×6 for the whole Power Drill burst (⚡D1).</summary>
        public float Multiplier => TierMultipliers[CurrentTier];

        /// <summary>True between PowerDrillActivated and CompletePowerDrill (⚡D1).</summary>
        public bool IsInPowerDrill { get; private set; }

        /// <summary>Seconds left before the momentum is lost. 0 when idle.</summary>
        public float TimeRemaining => _timer;

        /// <summary>Raw accumulator — exposed for HUD progress bars and tests.</summary>
        public float Progress => _momentumProgress;

        // ── Events ───────────────────────────────────────────────────
        /// <summary>(oldTier, newTier) on every tier change, up or down.</summary>
        public event Action<int, int> TierChanged;

        /// <summary>Fired when built-up momentum drops back to Tier 0 (timeout or Reset).</summary>
        public event Action MomentumLost;

        /// <summary>Tier 3 reached. Carries the multiplier that covers the whole burst (⚡D1).</summary>
        public event Action<float> PowerDrillActivated;

        // ── API ──────────────────────────────────────────────────────

        /// <summary>Call after every successful drill, any direction (§M9), with the type that was drilled.</summary>
        public void NotifyDrill(CellType drilled)
        {
            _timer = MomentumWindow;
            DrillCount++;

            // The Power Drill's own blocks keep the window alive but never re-trigger it —
            // the cycle only restarts once the caller completes the burst.
            if (IsInPowerDrill)
                return;

            if (drilled.CanFuse() && drilled == _lastColor)
            {
                ColorChain++;
                _momentumProgress += ColorBonusRate;
            }
            else
            {
                ColorChain = drilled.CanFuse() ? 1 : 0;
                _momentumProgress += 1f;
            }
            _lastColor = drilled;

            SetTier(TierFor(_momentumProgress));

            if (CurrentTier == 3)
            {
                IsInPowerDrill = true;
                PowerDrillActivated?.Invoke(Multiplier);
            }
        }

        /// <summary>
        /// ⚡D1: call once the Power Drill burst is fully resolved. Partial reset to Tier 1 —
        /// the cycle starts again instead of falling to zero. No-op outside a Power Drill.
        /// </summary>
        public void CompletePowerDrill()
        {
            if (!IsInPowerDrill)
                return;

            IsInPowerDrill    = false;
            _momentumProgress = Tier1Threshold;
            DrillCount        = Tier1Threshold;
            SetTier(1);
        }

        /// <summary>Graze reward (+0.3 s). Ignored when there is no momentum to extend.</summary>
        public void ExtendTimer(float seconds)
        {
            if (DrillCount == 0 || seconds <= 0f)
                return;
            _timer += seconds;
        }

        /// <summary>Counts the window down. The caller skips this during freefall (§M3.3).</summary>
        public void Tick(float dt)
        {
            if (DrillCount == 0 || dt <= 0f)
                return;

            _timer -= dt;
            if (_timer <= 0f)
                Reset();
        }

        /// <summary>Back to Tier 0. Fires TierChanged / MomentumLost only if there was momentum to lose.</summary>
        public void Reset()
        {
            bool hadMomentum = DrillCount > 0;
            int  oldTier     = CurrentTier;

            _momentumProgress = 0f;
            _timer            = 0f;
            _lastColor        = CellType.Empty;
            DrillCount        = 0;
            ColorChain        = 0;
            IsInPowerDrill    = false;
            CurrentTier       = 0;

            if (oldTier != 0)
                TierChanged?.Invoke(oldTier, 0);
            if (hadMomentum)
                MomentumLost?.Invoke();
        }

        // ── Internals ────────────────────────────────────────────────

        private static int TierFor(float progress)
        {
            if (progress >= Tier3Threshold) return 3;
            if (progress >= Tier2Threshold) return 2;
            if (progress >= Tier1Threshold) return 1;
            return 0;
        }

        private void SetTier(int newTier)
        {
            if (newTier == CurrentTier)
                return;
            int oldTier = CurrentTier;
            CurrentTier = newTier;
            TierChanged?.Invoke(oldTier, newTier);
        }
    }
}
