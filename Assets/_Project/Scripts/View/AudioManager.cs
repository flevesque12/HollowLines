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
        [Tooltip("Designed SFX mix level. The player's Effets slider (R6.9) scales this — 100 % = this value.")]
        [Range(0f, 1f)] [SerializeField] private float sfxVolume = 0.6f;
        [Tooltip("Designed music mix level. The player's Musique slider (R6.9) scales this — 100 % = this value.")]
        [Range(0f, 1f)] [SerializeField] private float musicVolume = 0.18f;

        // R6.9 (F10): every SFX source, so the player's Effets level reaches all of them at once.
        private readonly System.Collections.Generic.List<AudioSource> _sfxSources =
            new System.Collections.Generic.List<AudioSource>();

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
        private AudioClip _crawlerWakeClip;
        private AudioClip _crawlerDeathClip;
        private AudioClip _boomerWakeClip;
        private AudioClip _boomerBoomClip;
        private AudioClip _enemyAlertClip;   // R6.4: an active Crawler is lining up on the avatar
        private AudioClip _streakBreakClip;  // R6.7: the streak counter cracks
        private float     _lastEnemyAlertTime = -10f;
        private const float EnemyAlertCooldown = 0.8f;
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
        private AudioSource _enemySource;
        private AudioSource _uiSource; // R6.9 options preview — its own source, so no streak pitch leaks in

        // ── Persistent systems ──────────────────────────────────────────────────
        private StreakTracker _streak;

        // ── Grid-dependent systems, re-pointed every LoadLevel via Rewire() ─────
        private AvatarModel    _avatar;
        private CollapseSystem _collapse;
        private ChainTracker   _chain;
        private BombSystem     _bombs;
        private GravitySystem  _gravity;
        private EnemySystem    _enemies;

        // Last emitted beep index per armed bomb, so each fuse only ticks forward (never repeats).
        private readonly System.Collections.Generic.Dictionary<GridPos, int> _fuseBeepIndex =
            new System.Collections.Generic.Dictionary<GridPos, int>();

        private void Awake()
        {
            BuildClips();
            BuildSources();

            _musicSource.clip = _musicLoopClip;
            _musicSource.loop = true;
            _sfxSources.Remove(_musicSource); // music follows its own slider
            ApplyVolumes(VolumeSettings.Load());
            _musicSource.Play();
        }

        /// <summary>
        /// R6.9: apply the player's levels. Général goes on the AudioListener (everything, in one
        /// place); Musique and Effets scale the designed mix levels — never above them.
        /// </summary>
        public void ApplyVolumes(VolumeSettings v)
        {
            AudioListener.volume = Mathf.Clamp01(v.Master);
            _musicSource.volume  = musicVolume * Mathf.Clamp01(v.Music);
            float sfx = sfxVolume * Mathf.Clamp01(v.Sfx);
            foreach (AudioSource src in _sfxSources)
                src.volume = sfx;
        }

        /// <summary>R6.9: a short blip at the current Effets level, so moving the slider is heard.</summary>
        public void PlaySfxPreview() => _uiSource.PlayOneShot(_drillClip);

        /// <summary>Hook the systems that live for the whole session (called once from GameBootstrap.Awake).</summary>
        public void InitPersistent(AirSystem air, HealthSystem health, CampaignManager campaign,
                                   StreakTracker streak = null)
        {
            _streak = streak;
            // R6.7: StreakTracker is persistent (Reset per board, never rebuilt), so this is wired once.
            if (_streak != null) _streak.StreakBroken += OnStreakBroken;
            air.AirDepleted += PlayGameOver;
            health.HealthDepleted += PlayGameOver;
            campaign.LevelCompleted += _ => PlayLevelComplete();
            campaign.CampaignCompleted += PlayLevelComplete;
        }

        /// <summary>Re-point at the fresh grid-dependent systems GameBootstrap rebuilds every LoadLevel().</summary>
        public void Rewire(AvatarModel avatar, CollapseSystem collapse, ChainTracker chain, BombSystem bombs,
                           GravitySystem gravity = null, EnemySystem enemies = null)
        {
            Unwire();

            _avatar   = avatar;
            _collapse = collapse;
            _chain    = chain;
            _bombs    = bombs;
            _gravity  = gravity;
            _enemies  = enemies;

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

            if (_enemies != null)
            {
                _enemies.EnemyActivated  += OnEnemyActivated;
                _enemies.EnemyKilled     += OnEnemyKilled;
                _enemies.BoomerDetonated += OnBoomerDetonated;
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
            if (_enemies != null)
            {
                _enemies.EnemyActivated  -= OnEnemyActivated;
                _enemies.EnemyKilled     -= OnEnemyKilled;
                _enemies.BoomerDetonated -= OnBoomerDetonated;
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
        private void OnDrilled(GridPos cell, CellType oldType, DrillDirection direction)
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

        // ── Enemies (R5.16) ─────────────────────────────────────────────

        /// <summary>
        /// A buried enemy woke up — high chirp for a Crawler, low boop for a Boomer.
        ///
        /// EnemyActivated carries only the id, so the type has to be looked up. Unlike the VFX case
        /// (§5.10, R5.15) no cache is needed here: the enemy is necessarily ALIVE at the moment it
        /// activates, so GetAllAlive() finds it, and activation is a rare one-shot rather than a
        /// per-frame concern. Audio also doesn't care where it happened, only what it was.
        /// </summary>
        private void OnEnemyActivated(int id)
        {
            if (_enemies == null)
                return;

            foreach (EnemyEntity e in _enemies.GetAllAlive())
            {
                if (e.Id != id)
                    continue;

                _enemySource.PlayOneShot(e.Type == EnemyType.Boomer ? _boomerWakeClip : _crawlerWakeClip);
                return;
            }
        }

        /// <summary>
        /// A Crawler died — short dry crunch. A Boomer's death is voiced by OnBoomerDetonated
        /// instead (it has the boom), EXCEPT when a plain crush killed it: §6.5 says a crushed
        /// Boomer never detonates, so without this branch it would die silently.
        /// </summary>
        private void OnEnemyKilled(int id, EnemyType type, KillMethod method, int bonus)
        {
            if (type == EnemyType.Crawler)
                _enemySource.PlayOneShot(_crawlerDeathClip);
            else if (method == KillMethod.Crush)
                _enemySource.PlayOneShot(_boomerBoomClip, 0.6f); // quieter: it popped, it didn't detonate
        }

        private void OnBoomerDetonated(GridPos pos, int parentBonus, int blocksDestroyed)
        {
            _enemySource.PlayOneShot(_boomerBoomClip);
        }

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

        /// <summary>
        /// R6.7: a dry crack when a real streak (×2+) breaks — the audible half of the HUD counter
        /// shattering. A broken ×1 is just "a new colour started", not a loss, so it stays silent.
        /// </summary>
        private void OnStreakBroken(int lostCount)
        {
            if (lostCount >= 2)
                _drillSource.PlayOneShot(_streakBreakClip);
        }

        /// <summary>
        /// R6.4: EnemyView.DangerStarted — a Crawler just became a threat. Rate-limited so two
        /// Crawlers lining up together (or one wobbling across the threshold) give one warning.
        /// </summary>
        public void PlayEnemyAlert()
        {
            if (Time.time - _lastEnemyAlertTime < EnemyAlertCooldown) return;
            _lastEnemyAlertTime = Time.time;
            _enemySource.PlayOneShot(_enemyAlertClip);
        }

        private void PlayGameOver() => _stingerSource.PlayOneShot(_gameOverClip);

        public void PlayLevelComplete() => _stingerSource.PlayOneShot(_levelCompleteClip);

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

            // R5.16 — enemies. The two activation cues sit at opposite ends of the register on
            // purpose: a Crawler chirps high (something small just started moving toward you), a
            // Boomer boops low (something heavy just woke up and is now standing there).
            // R6.4 (F04): both are SWEEPS now, not pure tones. The old Crawler chirp (1500 Hz sine)
            // was a near-twin of the bomb fuse beep (1200 Hz sine) and testers filed it as "a bomb";
            // no other sound in the game sweeps up quickly, so the rising "bwip!" is unmistakable.
            _crawlerWakeClip   = SfxSynth.Sweep(650f, 1700f, 0.13f, 0.32f);
            _boomerWakeClip    = SfxSynth.Sweep(260f, 110f, 0.28f, 0.4f);

            // R6.4 — danger alert: a DESCENDING two-note "uh-oh". Every arpeggio in the game rises
            // (capsule, diamond, chain, fanfare = good news), so the falling one reads as a warning.
            _enemyAlertClip    = SfxSynth.Arpeggio(new[] { 987.77f, 698.46f }, 0.075f, 0.34f);

            // R6.7 — streak break: a short, bright, mostly-noise crack (like glass), well above the
            // crush thud and much shorter than the chunk shatter, so it reads as "your bonus broke",
            // not "you got hit" or "a block burst".
            _streakBreakClip   = SfxSynth.Shatter(0.12f, 1400f, 0.4f, noiseMix: 0.8f, lowPassFactor: 0.6f);

            // Crawler death: a short dry crunch. Higher low-pass than the crush clip so it reads
            // as something small breaking, not as the player getting hit.
            _crawlerDeathClip  = SfxSynth.Noise(0.15f, 0.4f, 0.35f);

            // Boomer detonation: noise + tone layered like the chunk shatter, but tuned the other
            // way — mostly TONE (noiseMix 0.35) at 70 Hz with a heavy low-pass, so it lands round
            // and bass-heavy instead of crackly. That is what separates it from the bomb blast,
            // which is pure Noise at a much brighter low-pass (0.25).
            _boomerBoomClip    = SfxSynth.Shatter(0.3f, 70f, 0.6f, noiseMix: 0.35f, lowPassFactor: 0.06f);
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
            _enemySource     = NewSource();
            _uiSource        = NewSource();
        }

        private AudioSource NewSource()
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.volume = sfxVolume;
            _sfxSources.Add(src);
            return src;
        }
    }
}
