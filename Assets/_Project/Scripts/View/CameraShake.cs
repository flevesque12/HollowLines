using UnityEngine;

namespace HollowLines.View
{
    /// <summary>
    /// M4 step 3: additive camera shake, layered on top of the follow position GameBootstrap drives.
    /// Lives on Camera.main for the whole session (created once, survives level transitions —
    /// unlike BoardView/AvatarView which get rebuilt every LoadLevel).
    /// Owns <see cref="BasePosition"/> (the un-shaken camera position); LateUpdate lays the transient
    /// shake offset on top of it every frame, so the follow and the shake never fight each other.
    /// </summary>
    public sealed class CameraShake : MonoBehaviour
    {
        private float _duration;
        private float _elapsed;
        private float _amplitude;

        /// <summary>
        /// The un-shaken camera position. GameBootstrap drives this each frame to follow the avatar
        /// down the well; LateUpdate adds the shake offset on top of it.
        /// </summary>
        public Vector3 BasePosition { get; set; }

        /// <summary>
        /// Start (or extend) a shake. If a stronger or longer shake is already running, this call
        /// is ignored so a big hit isn't cut short by a smaller one landing right after.
        /// </summary>
        public void Shake(float amplitude, float duration)
        {
            float remaining = _duration - _elapsed;
            if (amplitude < _amplitude && duration <= remaining)
                return;

            _amplitude = amplitude;
            _duration = duration;
            _elapsed = 0f;
        }

        /// <summary>Cut any shake in progress immediately — e.g. on game over, so it doesn't keep
        /// running under a screen the player can no longer act on.</summary>
        public void StopShake()
        {
            _amplitude = 0f;
            _duration = 0f;
            _elapsed = 0f;
            transform.position = BasePosition;
        }

        private void LateUpdate()
        {
            Vector3 offset = Vector3.zero;
            if (_elapsed < _duration)
            {
                _elapsed += Time.deltaTime;
                float falloff = 1f - (_elapsed / _duration);
                offset = (Vector3)Random.insideUnitCircle * _amplitude * falloff;
            }

            transform.position = BasePosition + offset;
        }
    }
}
