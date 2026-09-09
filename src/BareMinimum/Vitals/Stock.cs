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
    /// THE MINIMAP GOES BLANK UNTIL IT IS RE-INITIALISED. Told to use the golf layout, the
    /// map did not draw at all until the player opened the pause menu once -- which is what
    /// rebuilds the minimap. Switching the radar off and on for a frame did not do it. The FiveM
    /// crowd's answer to this exact symptom is to flick the big map open and shut, which makes
    /// the game rebuild the minimap; the first build of Vitals did that a frame apart and on
    /// this game the shut arrived while the map was still opening and was ignored, leaving a
    /// minimap the size of the screen. So the flick here is HELD -- open, a moment, shut, and
    /// shut again twice more over the next second in case a shut is dropped -- and it runs once,
    /// after the first layout call has actually gone into the movie, and again whenever the
    /// strip is re-hidden after the compare view. Expect the big map for a fraction of a second
    /// when the script loads.
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

        /// <summary>Whether the one-time shut of the big map at start-up has been done.</summary>
        private bool _shrunk;

        private bool _saidHidden;

        /// <summary>
        /// The radar blink: -1 when not running; 0 = switch the radar off this frame; 1 = back
        /// on the next. The alternative refresh, for an ini that asks for it.
        /// </summary>
        private int _refreshStep = -1;

        /// <summary>Whether the minimap still needs rebuilding after the layout call. True at the start and after every re-hide.</summary>
        private bool _needRefresh = true;

        /// <summary>When the flick's next shut is due. See Flick.</summary>
        private int _flickAt;

        /// <summary>How long the big map is held open before the first shut, and the two insurance shuts after it.</summary>
        private const int FlickHoldMs = 150;
        private static readonly int[] FlickAgainMs = { 500, 1200 };

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

                // ONCE, NOT FOR THREE SECONDS. It used to be asked every frame for three
                // seconds after a load, in case a shut that landed mid-opening was ignored --
                // and a radar told to change state sixty times a second for three seconds
                // came up BLANK for those three seconds, which read as no minimap at all. This
                // mod never opens the big map, so one shut for whatever an earlier build left
                // open is all the protection there is any case for.
                if (cfg.VitalsShrinkMapOnStart && !_shrunk)
                {
                    _shrunk = true;
                    BigMap(false);
                }

                if (wantHidden && cfg.VitalsMapFlick) _flickStep = 0;
            }

            if (wantHidden != _hidden)
            {
                // PUTTING IT BACK IS ALLOWED TO FAIL, AND USED TO BE FORGOTTEN WHEN IT DID.
                // Setup does nothing and says so when the minimap's scaleform is not to hand,
                // and it backs off for two seconds after a miss -- but the flag had already
                // been flipped, so the branch never came round again and the game's own health
                // strip stayed missing for the session with the mod switched off. The flag now
                // moves only when the work actually happened, so the next tick tries again.
                if (!wantHidden)
                {
                    if (Show(cfg)) _hidden = false;
                }
                else
                {
                    _hidden = true;
                    _needRefresh = true;
                }
            }

            if (_hidden)
            {
                if (cfg.VitalsHideSpecial) AbilityBar(false);

                if (cfg.VitalsHideHealthArmour && Setup(cfg.VitalsHideType) && _needRefresh)
                {
                    // REBUILT ONCE THE LAYOUT CALL IS IN. The flick by default -- see the class
                    // comment -- or the radar blink for an ini that asks for it instead.
                    _needRefresh = false;

                    if (cfg.VitalsMapFlick) _flickStep = 0;
                    else if (cfg.VitalsRefreshRadar) _refreshStep = 0;
                }
            }

            Flick();
            Refresh();
        }

        /// <summary>The radar off for a frame and on again, so the minimap redraws in its new layout.</summary>
        private void Refresh()
        {
            if (_refreshStep < 0) return;

            try
            {
                if (_refreshStep == 0)
                {
                    Function.Call(Hash.DISPLAY_RADAR, false);
                    _refreshStep = 1;
                    return;
                }

                Function.Call(Hash.DISPLAY_RADAR, true);
                _refreshStep = -1;
                Log.Info("Radar blinked off and on so the minimap redraws in its new layout.");
            }
            catch (Exception ex)
            {
                _refreshStep = -1;
                Log.Once("vitals-refresh", "Could not blink the radar: " + ex.Message);
            }
        }

        /// <summary>Puts the game's bars back, once. Used on the way out as well as for the compare view.</summary>
        /// <summary>
        /// The game's own strip back. FALSE when the scaleform was not to hand and nothing was
        /// done, so the caller knows to come round again rather than assuming it worked.
        /// </summary>
        public bool Show(Settings cfg)
        {
            AbilityBar(true);
            return Setup(cfg.VitalsShowType);
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

            // A refresh caught half way would leave the radar off.
            if (_refreshStep == 1) { try { Function.Call(Hash.DISPLAY_RADAR, true); } catch { /* teardown */ } }
            _refreshStep = -1;

            _hidden = false;
            _flickStep = -1;
        }

        /// <summary>
        /// The optional FiveM flick, off by default: the big map open, held a quarter of a
        /// second, shut, and shut once more a second later.
        ///
        /// HELD, NOT A FRAME APART. The first cut opened and shut on consecutive frames, the way
        /// the FiveM snippet does, and on this game the shut arrived while the map was still
        /// opening and was ignored -- the minimap stayed the size of the screen. A quarter of a
        /// second lets the open land first, and the second shut is insurance against the first
        /// being dropped anyway. Expect a blink.
        /// </summary>
        private void Flick()
        {
            if (_flickStep < 0) return;

            var now = Game.GameTime;

            if (_flickStep == 0)
            {
                BigMap(true);
                _flickAt = now + FlickHoldMs;
                _flickStep = 1;
                return;
            }

            if (now < _flickAt) return;

            BigMap(false);

            // Steps 1 and 2 are followed by another shut; step 3 is the last.
            if (_flickStep <= FlickAgainMs.Length)
            {
                _flickAt = now + FlickAgainMs[_flickStep - 1];
                _flickStep++;
                return;
            }

            Log.Info("Big map flicked open and shut so the minimap rebuilds in its new layout.");
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

        /// <summary>Asks the minimap movie for a layout. True when the call went in.</summary>
        private bool Setup(int type)
        {
            if (!Ready()) return false;

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

                return true;
            }
            catch (Exception ex)
            {
                Log.Once("vitals-setup", "Could not call " + Method + " on the minimap: " + ex.Message);
                return false;
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
