using System;
using GTA;
using GTA.Native;
using BareMinimum.Core;

namespace BareMinimum.Vitals
{
    /// <summary>
    /// The game's own bars, and how they are made to go away.
    ///
    /// The health and armour strip is not a HUD component; it is a clip inside the minimap's
    /// own scaleform movie, and HIDE_HUD_COMPONENT_THIS_FRAME has no number for it. What the
    /// movie does have is a method, SETUP_HEALTH_ARMOUR, that the game calls to pick a layout
    /// -- and layout 3 is the one it uses on the golf course, which has no bars in it at all.
    /// Ask for that every frame and the strip is gone. The FiveM crowd has run this for years.
    ///
    /// The special ability bar has a native of its own, SET_ABILITY_BAR_VISIBILITY, and it
    /// is asked as well, every frame, because a mission script can set it back.
    ///
    /// THE BIG MAP IS NEVER OPENED FROM HERE. The FiveM version of this trick flicks the big
    /// map open and shut a frame apart so the minimap re-reads its layout, and the first
    /// build of Vitals did the same -- and on this game the shut arrived while the map was
    /// still opening and was ignored, which left the player with a minimap the size of the
    /// screen. So: the map is collapsed once at start-up in case anything left it open, the
    /// flick survives only as an off-by-default setting for a strip that will not go, and
    /// nothing here ever asks for the big map on its own account.
    ///
    /// EVERYTHING HERE IS UNDONE ON THE WAY OUT. A hidden strip is a change to the game's
    /// state, not to ours, and a script reload that left it hidden would be a bug in the mod
    /// that was no longer running.
    /// </summary>
    internal sealed class Stock
    {
        private const string Movie = "minimap";
        private const string Method = "SETUP_HEALTH_ARMOUR";

        private int _handle;
        private int _retryAt;

        /// <summary>-1 when not flicking; otherwise which step of the two-frame flick is next.</summary>
        private int _flickStep = -1;

        /// <summary>What the strip was last asked to be. Starts as shown, which is what the game has it as.</summary>
        private bool _hidden;
        private bool _ever;

        /// <summary>Until when the big map is kept shut after start-up. See Update.</summary>
        private int _shrinkUntil;

        private bool _saidHidden;

        /// <summary>
        /// Called every frame. <paramref name="show"/> asks for the game's bars back -- the
        /// compare view, or the vitals switched off.
        /// </summary>
        public void Update(Settings cfg, bool show)
        {
            var wantHidden = !show && (cfg.VitalsHideHealthArmour || cfg.VitalsHideSpecial);

            if (!_ever)
            {
                _ever = true;

                // FOR THREE SECONDS, NOT ONCE. Whatever left the big map open -- an earlier
                // build on its way out, most likely -- this puts it back to the small, zooming
                // one the game starts with. Asked every frame for a moment rather than a
                // single time, because a single "shut" that lands while the map is still
                // opening is ignored: that is the exact failure that made the flick go wrong,
                // and a script reload is the exact moment the map is mid-opening.
                if (cfg.VitalsShrinkMapOnStart) _shrinkUntil = Game.GameTime + 3000;

                if (wantHidden && cfg.VitalsMapFlick) _flickStep = 0;
            }

            if (_shrinkUntil != 0)
            {
                if (Game.GameTime < _shrinkUntil) BigMap(false);
                else _shrinkUntil = 0;
            }

            if (wantHidden != _hidden)
            {
                _hidden = wantHidden;

                if (!wantHidden) Show(cfg);
                if (cfg.VitalsMapFlick) _flickStep = 0;
            }

            if (_hidden)
            {
                if (cfg.VitalsHideSpecial) AbilityBar(false);
                if (cfg.VitalsHideHealthArmour) Setup(cfg.VitalsHideType);
            }

            Flick();
        }

        /// <summary>Puts the game's bars back, once. Used on the way out as well as for the compare view.</summary>
        public void Show(Settings cfg)
        {
            AbilityBar(true);
            Setup(cfg.VitalsShowType);
        }

        /// <summary>Everything back the way it was found, and the big map shut in case a flick left it open.</summary>
        public void Restore(Settings cfg)
        {
            if (!_ever) return;

            try
            {
                Show(cfg);
                BigMap(false);
            }
            catch (Exception ex)
            {
                Log.Error("Could not put the game's bars back", ex);
            }

            _hidden = false;
            _flickStep = -1;
        }

        /// <summary>The optional FiveM flick: open this frame, shut the next. See the class comment for why it is off.</summary>
        private void Flick()
        {
            if (_flickStep < 0) return;

            if (_flickStep == 0)
            {
                BigMap(true);
                _flickStep = 1;
                return;
            }

            BigMap(false);
            _flickStep = -1;
        }

        private static void BigMap(bool open)
        {
            try
            {
                Function.Call(Hash.SET_BIGMAP_ACTIVE, open, false);
            }
            catch (Exception ex)
            {
                Log.Once("vitals-bigmap", "Could not " + (open ? "open" : "shut") + " the big map: " + ex.Message);
            }
        }

        private void Setup(int type)
        {
            if (!Ready()) return;

            try
            {
                Function.Call(Hash.BEGIN_SCALEFORM_MOVIE_METHOD, _handle, Method);
                Function.Call(Hash.SCALEFORM_MOVIE_METHOD_ADD_PARAM_INT, type);
                Function.Call(Hash.END_SCALEFORM_MOVIE_METHOD);

                if (!_saidHidden)
                {
                    _saidHidden = true;
                    Log.Info("Minimap movie handle " + _handle + "; asked for layout " + type + ".");
                }
            }
            catch (Exception ex)
            {
                Log.Once("vitals-setup", "Could not call " + Method + " on the minimap: " + ex.Message);
            }
        }

        private static void AbilityBar(bool visible)
        {
            try
            {
                Function.Call(Hash.SET_ABILITY_BAR_VISIBILITY, visible);
            }
            catch (Exception ex)
            {
                Log.Once("vitals-ability-bar", "Could not set the ability bar's visibility: " + ex.Message);
            }
        }

        /// <summary>
        /// The minimap movie's handle, requested when needed and re-requested if the game has
        /// let it go. Rate-limited, because a request that keeps failing is not improved by
        /// being made sixty times a second.
        /// </summary>
        private bool Ready()
        {
            try
            {
                if (_handle != 0 && Function.Call<bool>(Hash.HAS_SCALEFORM_MOVIE_LOADED, _handle)) return true;

                var now = Game.GameTime;
                if (now < _retryAt) return false;
                _retryAt = now + 2000;

                _handle = Function.Call<int>(Hash.REQUEST_SCALEFORM_MOVIE, Movie);

                if (_handle == 0)
                {
                    Log.Once("vitals-movie", "The game handed back no handle for the minimap movie; the stock bars " +
                                             "will stay until it does.");
                    return false;
                }

                return Function.Call<bool>(Hash.HAS_SCALEFORM_MOVIE_LOADED, _handle);
            }
            catch (Exception ex)
            {
                Log.Once("vitals-movie-ex", "Could not request the minimap movie: " + ex.Message);
                return false;
            }
        }
    }
}
