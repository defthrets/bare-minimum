using System;
using GTA;
using GTA.Native;

using BareMinimum.Core;

namespace BareMinimum.Venues
{
    /// <summary>
    /// The soda machines, which this mod does NOT sell from.
    ///
    /// IT WATCHES INSTEAD OF SELLING, and that distinction is the whole design. The game
    /// already sells a drink out of a soda machine, plays its own animation and restores a
    /// little health for it. Putting a second, different purchase on top of that would be two
    /// mods fighting over one prop -- ours quietly ignoring the health the vanilla one gave,
    /// and the player paying twice for one bottle. That was the reason machines were left
    /// alone in the first place and it is still a good reason.
    ///
    /// So the vanilla purchase stays the only purchase, and this notices it happened. He
    /// drank something; the hunger meter should know. That is the entire feature.
    ///
    /// THE SNACK MACHINES ARE A DIFFERENT CASE and are handled the other way, by Counters --
    /// the game sells nothing out of those, so there is nothing there to fight with.
    ///
    /// WATCHED BY ANIMATION, NOT BY PROXIMITY. Standing next to a machine is not drinking
    /// from one, and the animation is the only thing that is actually true: the game plays
    /// mini@sprunk when and only when it has sold you a drink.
    /// </summary>
    internal sealed class Sipping
    {
        private const string Dict = "mini@sprunk";
        private const string Clip = "plyr_buy_drink_pt1";

        /// <summary>
        /// How long after one sip before another can count.
        ///
        /// The clip runs about a second and this is asked every frame, so without a lockout
        /// one drink would credit sixty times. Comfortably longer than the animation, and
        /// shorter than anybody can queue two purchases at one machine.
        /// </summary>
        private const int AgainMs = 4000;

        private readonly Settings _cfg;
        private readonly Needs.Needs _needs;

        private bool _was;
        private int _lastAt;

        public Sipping(Settings cfg, Needs.Needs needs)
        {
            _cfg = cfg;
            _needs = needs;
        }

        public void Update()
        {
            if (_cfg.VendingSipHunger <= 0f) return;

            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists() || me.IsDead) { _was = false; return; }

                var now = Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM,
                                              me.Handle, Dict, Clip, 3);

                // THE EDGE, not the level. The clip is true for its whole run.
                var started = now && !_was;
                _was = now;

                if (!started) return;

                var time = Game.GameTime;
                if (time - _lastAt < AgainMs) return;

                _lastAt = time;

                _needs.Hunger.Restore(_cfg.VendingSipHunger);

                Log.Info("Drank from a vending machine: hunger +" +
                         (_cfg.VendingSipHunger * 100f).ToString("0") + "%.");
            }
            catch (Exception ex)
            {
                Log.Once("sipping", "Could not watch the vending machines: " + ex.Message);
            }
        }
    }
}
