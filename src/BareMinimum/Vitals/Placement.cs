using System;
using GTA;
using GTA.Math;
using GTA.Native;
using BareMinimum.Core;

namespace BareMinimum.Vitals
{
    /// <summary>
    /// The game's cash readout, moved from the top right corner to sit with the bars.
    ///
    /// SET_HUD_COMPONENT_POSITION takes a component's own posX and posY -- the numbers from
    /// frontend.xml, which GET_HUD_COMPONENT_POSITION reads back -- so the move is an OFFSET
    /// from wherever the game had it, on both the total and the "+$500" change that pops under
    /// it, and the two travel together. The defaults are read once and logged, the offsets are
    /// dials, and RESET_HUD_COMPONENT_VALUES puts both back on the way out.
    ///
    /// THE MINIMAP ITSELF CANNOT BE MOVED FROM HERE. The native that does it in FiveM,
    /// SET_MINIMAP_COMPONENT_POSITION, is FiveM's own -- it is not in the game's native table
    /// any more than IS_BIGMAP_ACTIVE was, and that one crashed the game when called by hash.
    /// The map sits where the safe-zone slider puts it; the frame follows the map, and the
    /// group offset moves the things that are ours -- the bars and the cash -- as one.
    /// </summary>
    internal sealed class Placement
    {
        private const int Cash = 3;
        private const int CashChange = 13;

        private Vector3 _cashDefault;
        private Vector3 _changeDefault;
        private bool _read;
        private bool _moved;
        private bool _said;

        public void Update(Settings cfg)
        {
            if (!cfg.MoveCash)
            {
                if (_moved) Restore();
                return;
            }

            try
            {
                if (!_read)
                {
                    _cashDefault = Function.Call<Vector3>(Hash.GET_HUD_COMPONENT_POSITION, Cash);
                    _changeDefault = Function.Call<Vector3>(Hash.GET_HUD_COMPONENT_POSITION, CashChange);
                    _read = true;

                    Log.Info("Cash readout: the game has it at " + _cashDefault.X.ToString("0.000") + "," +
                             _cashDefault.Y.ToString("0.000") + " and its change at " + _changeDefault.X.ToString("0.000") + "," +
                             _changeDefault.Y.ToString("0.000") + ".");
                }

                var dx = cfg.CashX + cfg.HudGroupX;
                var dy = cfg.CashY + cfg.HudGroupY;

                Function.Call(Hash.SET_HUD_COMPONENT_POSITION, Cash, _cashDefault.X + dx, _cashDefault.Y + dy);
                Function.Call(Hash.SET_HUD_COMPONENT_POSITION, CashChange, _changeDefault.X + dx, _changeDefault.Y + dy);
                _moved = true;

                if (!_said)
                {
                    _said = true;
                    Log.Info("Cash readout moved by " + dx.ToString("0.000") + "," + dy.ToString("0.000") +
                             " to " + (_cashDefault.X + dx).ToString("0.000") + "," + (_cashDefault.Y + dy).ToString("0.000") + ".");
                }
            }
            catch (Exception ex)
            {
                Log.Once("cash-move", "Could not move the cash readout: " + ex.Message);
            }
        }

        /// <summary>The game's own positions back. On the way out, and when the switch goes off.</summary>
        public void Restore()
        {
            try
            {
                Function.Call(Hash.RESET_HUD_COMPONENT_VALUES, Cash);
                Function.Call(Hash.RESET_HUD_COMPONENT_VALUES, CashChange);
            }
            catch
            {
                // Nothing more to try.
            }

            _moved = false;
            _said = false;
        }
    }
}
