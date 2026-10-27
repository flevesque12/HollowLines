using HollowLines.Core;
using UnityEngine;

namespace HollowLines.View
{
    /// <summary>
    /// TEMPORARY placeholder visuals for the Cave-in (cave-in.md) — born as the `previewCaveIn` debug view,
    /// always on since the R8.6 switch, until the real View pass (R8.8) replaces this file.
    /// The front lives in ABSOLUTE rows; `rowOffset` maps them back onto this board (Endless segments).
    ///
    /// Draws, in board-local space (parented under the BoardView, torn down with it):
    ///   • the front — a hot orange line across the well at CeilingRow, plus a dark band over the row
    ///     it is crumbling through (the rubble itself is a BoardView tile colour);
    ///   • a corner readout of the lead (rows, %), the front speed and the Danger Zone.
    /// No Core logic here — it only reads CeilingSystem.
    /// </summary>
    public sealed class CaveInPreviewView : MonoBehaviour
    {
        private static readonly Color FrontColor  = new Color(1f, 0.45f, 0.12f, 1f);
        private static readonly Color DustColor   = new Color(0.10f, 0.08f, 0.07f, 0.75f);

        private CeilingSystem _ceiling;
        private GridModel     _grid;
        private int           _rowOffset;

        private Sprite         _unit;
        private SpriteRenderer _front;
        private SpriteRenderer _dust;
        private GUIStyle _style;

        public void Init(CeilingSystem ceiling, GridModel grid, int rowOffset)
        {
            _ceiling   = ceiling;
            _grid      = grid;
            _rowOffset = rowOffset;

            var tex = new Texture2D(1, 1) { filterMode = FilterMode.Point };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            _unit = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);

            _dust  = NewRenderer("CaveInDust",  DustColor,  7);
            _front = NewRenderer("CaveInFront", FrontColor, 9);
        }

        private void LateUpdate()
        {
            if (_ceiling == null)
                return;

            float centerX = (_grid.Width - 1) / 2f;
            float width   = _grid.Width + 0.4f;

            // The front: world y = -row, a row's top edge is +0.5 above its centre.
            float frontRow = _ceiling.CeilingRow - _rowOffset;
            float frontY   = -frontRow + 0.5f;
            _front.transform.localPosition = new Vector3(centerX, frontY, 0f);
            _front.transform.localScale    = new Vector3(width, 0.12f, 1f);
            _front.color = Color.Lerp(FrontColor, Color.white, 0.25f + 0.25f * Mathf.Sin(Time.time * 9f));

            // Dust over the row being eaten: from the rubble's bottom edge down to the front.
            float swallowed = _ceiling.SwallowedRows - _rowOffset;
            float crumble   = frontRow - swallowed;
            _dust.enabled = crumble > 0.01f;
            if (_dust.enabled)
            {
                float top = -swallowed + 0.5f;
                _dust.transform.localPosition = new Vector3(centerX, top - crumble / 2f, 0f);
                _dust.transform.localScale    = new Vector3(_grid.Width, crumble, 1f);
            }
        }

        private void OnGUI()
        {
            // Menus, pause and game over freeze time — keep the debug readout off their cards.
            if (_ceiling == null || Time.timeScale == 0f)
                return;

            if (_style == null)
                _style = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };

            _style.normal.textColor = _ceiling.IsDangerZone ? new Color(1f, 0.3f, 0.25f) : new Color(1f, 0.8f, 0.45f);
            string text = $"ÉBOULEMENT  marge {_ceiling.Lead:0.0} / {_ceiling.MaxLead:0} rangées ({_ceiling.LeadPercent:0} %)" +
                          $"   vitesse {_ceiling.Speed:0.00} r/s" +
                          (_ceiling.InStartGrace ? $"   attente {_ceiling.StartGraceRemaining:0.0} s" : "") +
                          (_ceiling.IsDangerZone ? "   DANGER ×2" : "");
            GUI.Label(new Rect(16, Screen.height - 64, Screen.width - 32, 28), text, _style);
        }

        private SpriteRenderer NewRenderer(string name, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite       = _unit;
            sr.color        = color;
            sr.sortingOrder = order;
            return sr;
        }

        private void OnDestroy()
        {
            if (_unit != null)
            {
                Destroy(_unit.texture);
                Destroy(_unit);
            }
        }
    }
}
