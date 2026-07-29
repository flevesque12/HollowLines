using HollowLines.Core;
using UnityEngine;

namespace HollowLines.View
{
    /// <summary>
    /// M4 step 4: procedural SFX + music. No audio assets — every clip is synthesized once in
    /// Awake() via SfxSynth. Persistent like HUDView: created once in GameBootstrap.Awake(),
    /// survives level transitions, and re-subscribes to fresh grid-dependent systems through
    /// Rewire() (same pattern as HUDView.RewireChain()).
    ///
    /// One dedicated AudioSource per sound category so a pitch change on one (the chain SFX)
    /// never bleeds into another that's overlapping it.
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        [Header("— Volumes —")]
        [Range(0f, 1f)] [SerializeField] private float sfxVolume = 0.6f;
        [Range(0f, 1f)] [SerializeField] private float musicVolume = 0.18f;

        [Header("— Chain Pitch —")]
        [Tooltip("Chain step at which the rising pitch caps out.")]
        [SerializeField] private int maxChainForPitch = 6;
        [Tooltip("Semitones added per chain step above 1.")]
        [SerializeField] private float semitonesPerStep = 2f;

        [Header("— Streak Pitch (v3) —")]
        [Tooltip("Pitch added per streak step above 1 (CLAUDE.md §5.11).")]
        [SerializeField] private float pitchPerStreakStep = 0.05f;
        [Tooltip("Streak step at which the rising drill pitch caps out.")]
        [SerializeField] private int maxStreakForPitch = 10;

        [Header("— Burst Shatter (v3) —")]
        [Tooltip("Chunk size at which the shatter reaches its deepest pitch.")]
        [SerializeField] private int burstCellsForLowestPitch = 12;
        [SerializeField] private float shatterPitchSmall = 1.20f;
        [SerializeField] private float shatterPitchLarge = 0.65f;

        [Header("— Bomb Fuse Beep (v3) —")]
        [Tooltip("Number of beeps over a full fuse. Packed toward the end so the ticks accelerate.")]
        [SerializeField] private int fuseBeepSteps = 7;

        // ── Pre-baked clips (generated once) ───────────────────────────────────
        private AudioClip _drillClip;
        private AudioClip _capsuleClip;
        private AudioClip _bombArmClip;
        private AudioClip _bombExplodeClip;
        private AudioClip _bombChainClip;
        private AudioClip _fuseBeepClip;
        private AudioClip _shatterClip;
        private AudioClip _crushClip;
        private AudioClip _gameOverClip;
        private AudioClip _levelCompleteClip;
        private AudioClip _perfectClearClip;
        private AudioClip _diamondClip;
        private AudioClip _musicLoopClip;

        // ── One source per category ─────────────────────────────────────────────
        private AudioSource _drillSource;
        private AudioSource _clearSource;
        private AudioSource _bombSource;
        private AudioSource _bombChainSource;
        private AudioSource _fuseSource;
        private AudioSource _burstSource;
        private AudioSource _crushSource;
        private AudioSource _stingerSource;
        private AudioSource _musicSource;
        private AudioSource _diamondSource;

        // ── Persistent systems ──────────────────────────────────────────────────
        private StreakTracker _streak;

        // ── Grid-dependent systems, re-pointed every LoadLevel via Rewire() ─────
        private AvatarModel    _avatar;
        private CollapseSystem _collapse;
        private ChainTracker   _chain;
        private BombSystem     _bombs;
        private GravitySystem  _gravity;

        // Last emitted beep index per armed bomb, so each fuse only ticks forward (never repeats).
        private readonly System.Collections.Generic.Dictionary<GridPos, int> _fuseBeepIndex =
            new System.Collections.Generic.Dictionary<GridPos, int>();

        private void Awake()
        {
            BuildClips();
            BuildSources();

            _musicSource.clip = _musicLoopClip;
            _musicSource.loop = true;
            _musicSource.volume = musicVolume;
            _musicSource.Play();
        }

        /// <summary>Hook the systems that live for the whole session (called once from GameBootstrap.Awake).</summary>
        public void InitPersistent(AirSystem air, HealthSystem health, CampaignManager campaign,
                                   StreakTracker streak = null)
        {
            _streak = streak;
            air.AirDepleted += PlayGameOver;
            health.HealthDepleted += PlayGameOver;
            campaign.LevelCompleted += _ => PlayLevelComplete();
            campaign.CampaignCompleted += PlayLevelComplete;
        }

        /// <summary>Re-point at the fresh grid-dependent systems GameBootstrap rebuilds every LoadLevel().</summary>
        public void Rewire(AvatarModel avatar, CollapseSystem collapse, ChainTracker chain, BombSystem bombs,
                           GravitySystem gravity = null)
        {
            Unwire();

            _avatar   = avatar;
            _collapse = collapse;
            _chain    = chain;
            _bombs    = bombs;
            _gravity  = gravity;

            _avatar.Drilled        += OnDrilled;
            _collapse.PerfectClear += OnPerfectClear;
            _bombs.BombArmed       += OnBombArmed;
            _bombs.BombExploded    += OnBombExploded;
            _bombs.BombScored      += OnBombScored;
            _bombs.FuseProgress    += OnFuseProgress;
            _bombs.DiamondLiberated += OnDiamondLiberated;

            if (_gravity != null)
            {
                _gravity.ChunkBurst       += OnChunkBurst;
                _gravity.DiamondLiberated += OnDiamondLiberated;
            }
        }

        private void Unwire()
        {
            if (_avatar != null)   _avatar.Drilled -= OnDrilled;
            if (_collapse != null) _collapse.PerfectClear -= OnPerfectClear;
            if (_bombs != null)
            {
                _bombs.BombArmed -= OnBombArmed;
                _bombs.BombExploded -= OnBombExploded;
                _bombs.BombScored -= OnBombScored;
                _bombs.FuseProgress -= OnFuseProgress;
                _bombs.DiamondLiberated -= OnDiamondLiberated;
            }
            if (_gravity != null)
            {
                _gravity.ChunkBurst       -= OnChunkBurst;
                _gravity.DiamondLiberated -= OnDiamondLiberated;
            }
            _fuseBeepIndex.Clear();
        }

        private void OnDestroy() => Unwire();

        // ── Event handlers ──────────────────────────────────────────────

        /// <summary>
        /// The drill note climbs with the color streak (§5.11): pitch = 1 + (step - 1) × 0.05.
        /// Reads StreakTracker.CurrentStreak, which GameBootstrap's own Drilled handler has already
        /// updated — it subscribes in LoadLevel() before Rewire() runs, so this sees the fresh value.
        /// </summary>
        private void OnDrilled(GridPos cell, CellType oldType)
        {
            if (oldType == CellType.AirCapsule)
            {
                _drillSource.pitch = 1f;
                _drillSource.PlayOneShot(_capsuleClip);
                return;
            }

            if (oldType == CellType.Diamond)
            {
                OnDiamondLiberated(cell);
                return;
            }

            int step = Mathf.Clamp(_streak?.CurrentStreak ?? 1, 1, maxStreakForPitch);
            _drillSource.pitch = 1f + (step - 1) * pitchPerStreakStep;
            _drillSource.PlayOneShot(_drillClip);
        }

        /// <summary>Bigger chunk = deeper shatter. Mass should sound like mass.</summary>
        private void OnChunkBurst(System.Collections.Generic.List<GridPos> cells, int fallDistance, CellType color)
        {
            float t = Mathf.InverseLerp(1f, burstCellsForLowestPitch, cells.Count);
            _burstSource.pitch = Mathf.Lerp(shatterPitchSmall, shatterPitchLarge, t);
            _burstSource.PlayOneShot(_shatterClip);
        }

        private void OnPerfectClear(int row)
        {
            int step = Mathf.Clamp(_chain.CurrentChain, 1, maxChainForPitch);
            _clearSource.pitch = Mathf.Pow(2f, (step - 1) * semitonesPerStep / 12f);
            _clearSource.PlayOneShot(_perfectClearClip);
        }

        private void OnBombArmed(GridPos pos) => _bombSource.PlayOneShot(_bombArmClip);

        /// <summary>R4: shared by drill, bomb blast and burst shockwave — a bright chime, higher
        /// register than everything else so a diamond always reads as the precious pickup it is.</summary>
        private void OnDiamondLiberated(GridPos pos) => _diamondSource.PlayOneShot(_diamondClip);

        private void OnBombExploded(GridPos pos)
        {
            _fuseBeepIndex.Remove(pos);
            _bombSource.PlayOneShot(_bombExplodeClip);
        }

        /// <summary>
        /// The audible half of the fuse telegraph (GDD §4.8): beeps whose cadence and pitch climb as
        /// the fuse burns down. Beep indices are packed quadratically (frac²), so the ticks start slow
        /// and accelerate toward detonation — matching VfxManager's accelerating flash.
        /// </summary>
        private void OnFuseProgress(GridPos pos, float frac)
        {
            int idx = Mathf.FloorToInt(frac * frac * fuseBeepSteps);
            if (!_fuseBeepIndex.TryGetValue(pos, out int last)) last = -1;
            if (idx <= last) return;

            _fuseBeepIndex[pos] = idx;
            _fuseSource.pitch = Mathf.Lerp(1f, 2f, frac);
            _fuseSource.PlayOneShot(_fuseBeepClip);
        }

        /// <summary>
        /// Sympathetic detonations layer a rising arpeggio over the unchanged blast clip —
        /// the chain is a reward, so it should sound like one (design rule 3).
        /// </summary>
        private void OnBombScored(int destroyed, int chainMult)
        {
            if (chainMult <= 1) return;

            int step = Mathf.Clamp(chainMult, 2, maxChainForPitch);
            _bombChainSource.pitch = Mathf.Pow(2f, (step - 1) * semitonesPerStep / 12f);
            _bombChainSource.PlayOneShot(_bombChainClip);
        }

        /// <summary>Called by GameBootstrap.OnAvatarCrushed() — not a Core event, since crush already
        /// funnels through one handler there for hearts + camera shake.</summary>
        public void PlayCrush() => _crushSource.PlayOneShot(_crushClip);

        private void PlayGameOver() => _stingerSource.PlayOneShot(_gameOverClip);

        private void PlayLevelComplete() => _stingerSource.PlayOneShot(_levelCompleteClip);

        // ── Setup ───────────────────────────────────────────────────────

        private void BuildClips()
        {
            _drillClip         = SfxSynth.Tone(880f, 0.05f, 0.35f, 0.002f, 0.03f);
            _capsuleClip       = SfxSynth.Arpeggio(new[] { 660f, 990f }, 0.06f, 0.3f);
            _bombArmClip       = SfxSynth.Tone(220f, 0.05f, 0.25f, 0.002f, 0.02f);
            _fuseBeepClip      = SfxSynth.Tone(1200f, 0.035f, 0.3f, 0.001f, 0.02f);
            _bombExplodeClip   = SfxSynth.Noise(0.35f, 0.5f, 0.25f);
            _crushClip         = SfxSynth.Noise(0.2f, 0.45f, 0.5f);
            _gameOverClip      = SfxSynth.Sweep(440f, 110f, 0.8f, 0.4f);
            _levelCompleteClip = SfxSynth.Arpeggio(new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.12f, 0.35f);
            _musicLoopClip     = SfxSynth.Arpeggio(new[] { 220f, 261.63f, 329.63f, 392f, 329.63f, 261.63f }, 0.4f, 0.2f);

            // v3 — chunk burst: noise + tone layered, 0.3 s (§5.11).
            _shatterClip       = SfxSynth.Shatter(0.3f, 320f, 0.5f);

            // v3 — bomb chain overlay: ascending arpeggio, played over the unchanged blast clip.
            _bombChainClip     = SfxSynth.Arpeggio(new[] { 440f, 587.33f, 739.99f }, 0.07f, 0.28f);

            // v3 — Perfect Clear fanfare: C5-E5-G5-C6, snappier and louder than level complete.
            _perfectClearClip  = SfxSynth.Arpeggio(new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.09f, 0.5f);

            // R4 — diamond chime: fast, bright, higher register (C6-E6-G6) than anything else in
            // the game, so it always reads as a bonus pickup rather than a scoring action.
            _diamondClip       = SfxSynth.Arpeggio(new[] { 1046.5f, 1318.51f, 1567.98f }, 0.045f, 0.4f);
        }

        private void BuildSources()
        {
            _drillSource     = NewSource();
            _clearSource     = NewSource();
            _bombSource      = NewSource();
            _bombChainSource = NewSource();
            _fuseSource      = NewSource();
            _burstSource     = NewSource();
            _crushSource     = NewSource();
            _stingerSource   = NewSource();
            _musicSource     = NewSource();
            _diamondSource   = NewSource();
        }

        private AudioSource NewSource()
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.volume = sfxVolume;
            return src;
        }
    }
}
