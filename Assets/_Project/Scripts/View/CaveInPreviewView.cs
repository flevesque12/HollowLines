using System.Collections.Generic;
using HollowLines.Core;
using UnityEngine;

namespace HollowLines.View
{
    /// <summary>
    /// TEMPORARY placeholder visuals for the `previewCaveIn` debug toggle (ceiling-gas.md) — lets the dev
    /// play the Cave-in + gas before the real View pass (R8.8) replaces this file.
    ///
    /// Draws, in board-local space (parented under the BoardView, torn down with it):
    ///   • the front — a hot orange line across the well at CeilingRow, plus a dark band over the row
    ///     it is crumbling through (the rubble itself is a BoardView tile colour);
    ///   • gas puffs — translucent green squares, gently pulsing;
    ///   • hissing pockets — a fast yellow-green flash (the 0.6 s telegraph);
    ///   • a corner readout of the lead (rows, %), the front speed and the Danger Zone.
    /// No Core logic here — it only reads CeilingSystem / GasSystem.
    /// </summary>
    public sealed class CaveInPreviewView : MonoBehaviour
    {
        private static readonly Color FrontColor  = new Color(1f, 0.45f, 0.12f, 1f);
        private static readonly Color DustColor   = new Color(0.10f, 0.08f, 0.07f, 0.75f);
        private static readonly Color GasColor    = new Color(0.45f, 0.95f, 0.30f, 0.55f);
        private static readonly Color HissColor   = new Color(0.85f, 1f, 0.30f, 0.65f);

        private CeilingSystem _ceiling;
        private GasSystem     _gas;
        private GridModel     _grid;

        private Sprite         _unit;
        private SpriteRenderer _front;
        private SpriteRenderer _dust;
        private readonly List<SpriteRenderer> _puffs  = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _hisses = new List<SpriteRenderer>();
        private readonly List<GridPos> _cells = new List<GridPos>();
        private GUIStyle _style;

        public void Init(CeilingSystem ceiling, GasSystem gas, GridModel grid)
        {
            _ceiling = ceiling;
            _gas     = gas;
            _grid    = grid;

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
            float frontY = -_ceiling.CeilingRow + 0.5f;
            _front.transform.localPosition = new Vector3(centerX, frontY, 0f);
            _front.transform.localScale    = new Vector3(width, 0.12f, 1f);
            _front.color = Color.Lerp(FrontColor, Color.white, 0.25f + 0.25f * Mathf.Sin(Time.time * 9f));

            // Dust over the row being eaten: from the rubble's bottom edge down to the front.
            float crumble = _ceiling.CeilingRow - _ceiling.SwallowedRows;
            _dust.enabled = crumble > 0.01f;
            if (_dust.enabled)
            {
                float top = -_ceiling.SwallowedRows + 0.5f;
                _dust.transform.localPosition = new Vector3(centerX, top - crumble / 2f, 0f);
                _dust.transform.localScale    = new Vector3(_grid.Width, crumble, 1f);
            }

            _cells.Clear();
            _gas.CopyGasCells(_cells);
            float puffPulse = 0.8f + 0.2f * Mathf.Sin(Time.time * 4f);
            Sync(_puffs, _cells, "GasPuff", new Color(GasColor.r, GasColor.g, GasColor.b, GasColor.a * puffPulse), 0.92f);

            _cells.Clear();
            _gas.CopyHissingCells(_cells);
            float hissPulse = 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(Time.time * 14f));
            Sync(_hisses, _cells, "GasHiss", new Color(HissColor.r, HissColor.g, HissColor.b, HissColor.a * hissPulse), 0.7f);
        }

        private void OnGUI()
        {
            if (_ceiling == null)
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

        private void Sync(List<SpriteRenderer> pool, List<GridPos> cells, string name, Color color, float size)
        {
            while (pool.Count < cells.Count)
                pool.Add(NewRenderer(name, color, 9));

            for (int i = 0; i < pool.Count; i++)
            {
                bool on = i < cells.Count;
                pool[i].enabled = on;
                if (!on)
                    continue;
                pool[i].transform.localPosition = BoardView.ToLocal(cells[i]);
                pool[i].transform.localScale    = new Vector3(size, size, 1f);
                pool[i].color = color;
            }
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
