using System;

namespace HollowLines.Core
{
    /// <summary>
    /// M2 step 2: counts collapse chains.
    ///
    /// Design note — chains are TEMPORAL, not instantaneous. With our rules, one drill can only
    /// complete one row, so a "chain" is never two rows finished by the same tap. Real chains happen
    /// through gravity: a collapse orphans a chunk → the chunk falls → the rows it VACATES become
    /// void → they collapse in turn, one gravity-step apart. So instead of a loop counter, we track
    /// a running chain that stays alive while the board is still moving, and completes once
    /// everything has settled for a beat.
    ///
    /// This class only counts and reports. Multipliers (x2/x3/x5) are ScoreSystem's job (step 4) —
    /// same separation of concerns as everywhere else in the core.
    /// </summary>
    public sealed class ChainTracker
    {
        /// <summary>How long the board must be fully settled (no wobble, no falls) before the chain closes.</summary>
        public float SettleDelay { get; set; } = 0.15f;

        /// <summary>The running chain count. 0 when no chain is in progress.</summary>
        public int CurrentChain { get; private set; }

        /// <summary>Fired at every collapse with the running count (1, 2, 3…). The view pops the combo counter here.</summary>
        public event Action<int> LinkAdded;

        /// <summary>Fired once the dust settles, with the final chain length. ScoreSystem converts this to points.</summary>
        public event Action<int> ChainCompleted;

        private readonly GravitySystem _gravity;
        private float _settleTimer;

        public ChainTracker(CollapseSystem collapse, GravitySystem gravity)
        {
            if (collapse == null) throw new ArgumentNullException(nameof(collapse));
            _gravity = gravity ?? throw new ArgumentNullException(nameof(gravity));

            collapse.PerfectClear += OnPerfectClear;
        }

        /// <summary>Call once per frame, AFTER CollapseSystem.Resolve and GravitySystem.Tick.</summary>
        public void Tick(float deltaTime)
        {
            if (CurrentChain == 0)
                return;

            // As long as something is still wobbling or falling, the chain might grow — keep it open.
            if (_gravity.IsBusy)
            {
                _settleTimer = 0f;
                return;
            }

            _settleTimer += deltaTime;
            if (_settleTimer >= SettleDelay)
            {
                int completed = CurrentChain;
                CurrentChain = 0;
                _settleTimer = 0f;
                ChainCompleted?.Invoke(completed);
            }
        }

        private void OnPerfectClear(int row)
        {
            CurrentChain++;
            _settleTimer = 0f;
            LinkAdded?.Invoke(CurrentChain);
        }
    }
}
