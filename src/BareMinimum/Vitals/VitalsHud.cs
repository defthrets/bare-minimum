using System;
using GTA;
using GTA.Native;
using BareMinimum.Core;
using BareMinimum.UI;

namespace BareMinimum.Vitals
{
    /// <summary>
    /// Health, armour and energy: the vitals. Once a mod of their own, now the left of the row.
    ///
    /// WHAT CAME ACROSS, AND WHAT CHANGED. Vitals was the health, armour and special ability
    /// bars redrawn -- the game's own strip hidden through the minimap's golf layout, the
    /// three drawn as liquid with a spring in each, upright beside Bare Minimum's two or lying
    /// under the minimap. All of that is here, file for file. What changed is what the merge
    /// made possible: the upright columns are drawn through the gauge's own frame and plate
    /// rather than a copy of them, so the five bars are one row by construction; the row's
    /// order is the ini's, [HUD] RowOrder; and the third bar is ENERGY, a sprint
    /// meter this mod runs, with the special ability showing through it as colour.
    ///
    /// THIS RUNS BEFORE THE MOD'S OWN ENABLED GATE. The game's bars are hidden by it and have
    /// to be put back when the mod is switched off; a pass that stopped running with them
    /// hidden would be a mod that had broken the game's HUD while not even running. So the
    /// stock bars are managed every frame whatever else is on, and everything else here waits
    /// on the switch.
    /// </summary>
    internal sealed class VitalsHud
    {
        private readonly Settings _cfg;

        private readonly Stock _stock = new Stock();
        private readonly Readings _readings = new Readings();
        private readonly Energy _energy = new Energy();
        private readonly Momentum _motion = new Momentum();
        private readonly Strip _strip = new Strip();
        private readonly Columns _columns = new Columns();
        private readonly Frame _frame = new Frame();
        private readonly Placement _placement = new Placement();

        private Layout _layout;
        private int _layoutAt;
        private bool _layoutThird;

        /// <summary>This frame: the columns stand in the gauge's row, which will ask for them.</summary>
        private bool _upright;

        /// <summary>How strong everything of ours is drawn: half in the compare view.</summary>
        private float _strength = 1f;

        private bool _measuredStrip;
        private bool _measuredUpright;

        public VitalsHud(Settings cfg)
        {
            _cfg = cfg;
        }

        /// <summary>Whether the three stand in the gauge's row this frame. The gauge asks before it lays the row out.</summary>
        public bool Upright => _upright;

        /// <summary>Whether there is a third bar this frame -- energy, or a special ability.</summary>
        public bool HasThird => _readings.HasThird;

        /// <summary>How many columns stand in the row this frame. For the log.</summary>
        public int ColumnsShown => _upright ? (_readings.HasThird ? 2 : 1) : 0;

        /// <summary>
        /// Holds the energy bar full for this many minutes. Wired from Eating and from Dope by
        /// Main. <paramref name="wired"/> says it was a stimulant, which sets the bar shimmering.
        /// </summary>
        public void HoldEnergy(float minutes, bool wired = false)
        {
            _energy.Hold(minutes * 60f, wired);
        }

        /// <summary>Whether he has run himself out of breath. For anything else that cares.</summary>
        public bool Tired => _cfg.VitalsEnabled && _energy.Tired;

        /// <summary>The gauge, for the row's geometry: the minimap's frame lines up with it. Set by Main.</summary>
        public Gauge Gauge { get; set; }

        // ======================================================================

        /// <summary>Once a frame, from Main, before the gauge draws. <paramref name="on"/> is the mod's switch and ours together.</summary>
        public void Update(bool on)
        {
            _upright = false;

            var dt = Game.LastFrameTime;

            // A paused or hitching game hands back a dt of zero or of several seconds.
            // Anything past a tenth of a second is a hitch, not animation.
            if (dt < 0f || dt > 0.1f) dt = 0f;

            var compare = _cfg.VitalsCompare;

            // The cash readout is part of the HUD's layout, not of the vitals, and is drawn
            // whether the vitals are on or off.
            _placement.Update(_cfg, Gauge, Visible(), dt);

            // THE GAME'S BARS ARE MANAGED WHETHER OR NOT OURS ARE DRAWN. During a fade or a
            // switch nothing of ours is on screen, and that is exactly when the minimap is
            // most likely to be rebuilt -- so the hide has to keep being asked for.
            _stock.Update(_cfg, compare || !on);

            if (!on)
            {
                _motion.Rest();
                return;
            }

            if (!Visible())
            {
                _motion.Rest();
                return;
            }

            _energy.Update(_cfg, dt);
            _readings.Update(_cfg, dt, _energy);
            _motion.Update(_cfg, dt, _readings, _energy);

            _strength = compare ? 0.5f : 1f;

            if (Gauge != null)
            {
                _frame.Draw(_cfg, Gauge.Rack(), UI.Gauge.MinimapLeft(), UI.Gauge.MinimapWidth(), _strength);
            }

            // UPRIGHT NEEDS A ROW TO STAND IN. With the gauge on its icon style, or hidden,
            // there is none, and the strip under the minimap is what the three become.
            if (_cfg.VitalsStyle == VitalsStyle.Upright && _cfg.Style == HudStyle.Bars && _cfg.ShowHud)
            {
                _upright = true;
                return;
            }

            var lay = LayoutFor(_readings.HasThird);
            _strip.Draw(_cfg, lay, _readings, _motion, _strength);

            if (!_measuredStrip)
            {
                _measuredStrip = true;
                Log.Info("Vitals: " + lay.Describe());
            }
        }

        /// <summary>The three columns, in the gauge's row. Called by the gauge with the row it built.</summary>
        public void DrawColumns(Gauge gauge, Gauge.Row row)
        {
            if (!_upright) return;

            _columns.Draw(_cfg, gauge, row, _readings, _motion, _strength);

            if (!_measuredUpright)
            {
                _measuredUpright = true;
                Log.Info("Vitals: upright, " + ColumnsShown + " columns at the left of the row, pitch " +
                         (row.Pitch * Ink.ScreenWidth).ToString("0.0") + " px, bar " +
                         (row.BarW * Ink.ScreenWidth).ToString("0") + " x " + (row.BarH * Ink.ScreenHeight).ToString("0") + " px.");
            }
        }

        /// <summary>Leaves the game's HUD exactly as it was found. On a reload as well as on shutdown.</summary>
        public void Shutdown()
        {
            try { _stock.Restore(_cfg); }
            catch (Exception ex) { Log.Error("Restoring the game's bars", ex); }

            // THE SPEED CAP OUTLIVES THE SCRIPT. A man left winded by a reload would jog for
            // the rest of the session.
            try { _energy.Release(); }
            catch (Exception ex) { Log.Error("Lifting the energy cap", ex); }

            try { _placement.Restore(); }
            catch (Exception ex) { Log.Error("Putting the cash readout back", ex); }
        }

        // ======================================================================

        /// <summary>
        /// The strip's position, recomputed twice a second rather than every frame.
        ///
        /// The safe zone and the resolution can change from the settings menu mid-session,
        /// and two natives twice a second is nothing; four natives a frame is not much more,
        /// but there is no reason to spend it.
        /// </summary>
        private Layout LayoutFor(bool third)
        {
            var now = Game.GameTime;

            if (_layout == null || now >= _layoutAt || third != _layoutThird)
            {
                _layoutAt = now + 500;
                _layoutThird = third;
                _layout = Layout.Compute(_cfg, third);
            }

            return _layout;
        }

        /// <summary>
        /// Whether there is a HUD on screen worth drawing over.
        ///
        /// The radar being hidden is the useful test: the game turns it off for cutscenes, for
        /// the pause menu, on the wasted screen and during a fade, which is exactly the set of
        /// moments a HUD bar should not be floating in.
        /// </summary>
        private static bool Visible()
        {
            try
            {
                if (Game.IsPaused) return false;
                if (Function.Call<bool>(Hash.IS_PAUSE_MENU_ACTIVE)) return false;
                if (!Function.Call<bool>(Hash.IS_SCREEN_FADED_IN)) return false;

                // THE WASTED AND BUSTED SCREENS. The radar stays up through the first moments of
                // both, so the radar alone does not say; a dead man has no vitals to show.
                if (Function.Call<bool>(Hash.IS_PLAYER_DEAD, Game.Player.Handle)) return false;
                if (Function.Call<bool>(Hash.IS_PLAYER_BEING_ARRESTED, Game.Player.Handle, true)) return false;
                if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return false;
                if (Function.Call<bool>(Hash.IS_HUD_HIDDEN)) return false;
                if (Function.Call<bool>(Hash.IS_RADAR_HIDDEN)) return false;
                if (!Function.Call<bool>(Hash.IS_RADAR_PREFERENCE_SWITCHED_ON)) return false;

                return true;
            }
            catch
            {
                return true;
            }
        }
    }
}
