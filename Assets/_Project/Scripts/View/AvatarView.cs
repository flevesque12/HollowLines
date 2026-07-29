using HollowLines.Core;
using UnityEngine;

namespace HollowLines.View
{
    /// <summary>
    /// Visual for the driller: snaps the model's grid position, renders as a colored circle-ish tile,
    /// and eases toward the target so one-cell steps read as movement instead of teleports.
    /// (DOTween can replace the Lerp later; M1 avoids the dependency.)
    /// </summary>
    public sealed class AvatarView : MonoBehaviour
    {
        [Header("— Motion —")]
        [Tooltip("Approach speed toward the current cell, higher = snappier.")]
        [Range(4f, 40f)]
        [SerializeField] private float followSpeed = 18f;

        [Header("— Look —")]
        [SerializeField] private Color avatarColor = new Color(0.85f, 0.35f, 0.19f); // coral

        private AvatarModel _model;
        private Vector3 _target;
        private SpriteRenderer _renderer;

        public void Init(AvatarModel model)
        {
            _model = model;
            _model.Moved += OnMoved;
            _target = BoardView.ToLocal(model.Position);
            transform.localPosition = _target;

            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sprite = BoardView.GetUnitSprite();
            _renderer.color = avatarColor;
            _renderer.sortingOrder = 10;
            transform.localScale = Vector3.one * 0.8f;
        }

        /// <summary>
        /// Blend the driller toward a streak color. VfxManager drives this from ×3 up so the
        /// avatar itself shows what color run you are on. <paramref name="t"/> is 0 → base color.
        /// </summary>
        public void SetStreakTint(Color streakColor, float t)
        {
            if (_renderer == null) return;
            _renderer.color = Color.Lerp(avatarColor, streakColor, Mathf.Clamp01(t));
        }

        /// <summary>Back to the driller's own color (streak broken or reset).</summary>
        public void ClearStreakTint()
        {
            if (_renderer == null) return;
            _renderer.color = avatarColor;
        }

        private void OnDestroy()
        {
            if (_model != null)
                _model.Moved -= OnMoved;
        }

        private void OnMoved(GridPos position)
        {
            _target = BoardView.ToLocal(position);
        }

        private void Update()
        {
            transform.localPosition = Vector3.Lerp(transform.localPosition, _target, followSpeed * Time.deltaTime);
        }
    }
}
