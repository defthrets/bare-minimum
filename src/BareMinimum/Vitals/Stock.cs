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
    /// Ask for that and the strip is gone. The FiveM crowd has run this for years.
    ///
    /// ONCE. NOT EVERY FRAME -- AND EVERY FRAME IS WHAT MADE THE GPS FLICKER.
    ///
    /// The method is not a flag being set. It is a RE-LAYOUT of the movie, which the rest of
    /// this comment is the proof of: the first one leaves the map BLANK until the game rebuilds
    /// it. The map's own texture survives a relayout, so nothing looked wrong. The GPS does not
    /// survive one: the route line and the turn arrow are built by this movie, and only while a
    /// route exists, so sixty relayouts a second tore them down and rebuilt them sixty times a
    /// second and they strobed. Only ever with a route set, which is why it looked like a fault
    /// in the GPS rather than in the thing rebuilding the map underneath it.
    ///
    /// So it is asked once, and asked again only when the game has had a chance to undo it --
    /// see Upheaval for which moments those are, and VitalsHideRepeatMs for the way back to the
    /// old behaviour if the strip ever returns and stays.
    ///
    /// The special ability bar is different and is still asked every frame:
    /// SET_ABILITY_BAR_VISIBILITY is a plain native setting a flag, it lays nothing out, and a
    /// mission script can set it back.
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

        /// <summary>Whether the layout call has ever actually gone into the movie.</summary>
        private bool _asked;

        /// <summary>Set when something has happened that could have put the game's own strip back.</summary>
        private bool _askAgain;

        /// <summary>When the optional heartbeat is next due. Only read when HideRepeatMs is not 0.</summary>
        private int _repeatAt;

        /// <summary>Last frame's answers to the questions in Upheaval. The ped starts at -1 so the first frame is not a change.</summary>
        private bool _wasPaused, _wasFaded, _wasSwitching, _wasCutscene;
        private int _wasPed = -1;

        /// <summary>How many times the layout has had to be asked for again. The number that says whether the list in Upheaval is long enough.</summary>
        private int _reasks;

        /// <summary>How long the big map is held open before the first shut, and the two insurance shuts after it.</summary>
        private const int FlickHoldMs = 150;
        private static readonly int[] FlickAgainMs = { 500, 1200 };

        /// <summary>
        /// Called every frame. <paramref name="show"/> asks for the game's bars back -- the
        /// compare view, or the vitals switched off.
        /// </summary>
        public void Update(Settings cfg, bool show)
        {
            // Every frame, whatever else happens: it is watching for EDGES, and an edge missed
            // is an edge gone.
            var upheaval = Upheaval();

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
                    _askAgain = true;
                }
            }

            if (_hidden)
            {
                if (cfg.VitalsHideSpecial) AbilityBar(false);

                if (cfg.VitalsHideHealthArmour && Due(cfg, upheaval) && Setup(cfg.VitalsHideType))
                {
                    Landed(cfg);

                    if (_needRefresh)
                    {
                        // REBUILT ONCE THE LAYOUT CALL IS IN. The flick by default -- see the
                        // class comment -- or the radar blink for an ini that asks for it.
                        _needRefresh = false;

                        if (cfg.VitalsMapFlick) _flickStep = 0;
                        else if (cfg.VitalsRefreshRadar) _refreshStep = 0;
                    }
                }
            }

            Flick();
            Refresh();
        }

        /// <summary>
        /// WHETHER TO PUSH THE LAYOUT INTO THE MOVIE THIS FRAME.
        ///
        /// Not while the game is rebuilding its own HUD -- behind a fade or a pause menu there
        /// is no strip for anyone to see, and a relayout pushed into a movie that is already
        /// being rebuilt is the whole of what went wrong here.
        ///
        /// Every frame until it has landed once, because the movie is not always to hand at a
        /// load and Setup says so by coming back false. After that, only when something has
        /// undone it -- or on the heartbeat, for an ini that has asked for one.
        /// </summary>
        private bool Due(Settings cfg, bool upheaval)
        {
            if (upheaval) return false;
            if (!_asked || _askAgain) return true;

            return cfg.VitalsHideRepeatMs > 0 && Game.GameTime >= _repeatAt;
        }

        /// <summary>The layout call went in. Counted, because the count is the evidence.</summary>
        private void Landed(Settings cfg)
        {
            if (_asked && _askAgain)
            {
                _reasks++;

                // Loud for the first few and then thinned: if this climbs steadily with nobody
                // touching anything, the list in Upheaval is catching something that is not an
                // edge, and the fix is there rather than in a heartbeat.
                if (_reasks <= 5 || _reasks % 50 == 0)
                {
                    Log.Debug("Asked the minimap for layout " + cfg.VitalsHideType + " again (" +
                              _reasks + " since the load). The game had a chance to put its own " +
                              "strip back.");
                }
            }

            _asked = true;
            _askAgain = false;
            _repeatAt = Game.GameTime + Math.Max(1, cfg.VitalsHideRepeatMs);
        }

        /// <summary>
        /// WHEN THE GAME HAS HAD A CHANCE TO PUT THE STRIP BACK -- the only time the layout is
        /// worth asking for twice.
        ///
        /// Four states and a body, and each of them is the game rebuilding its HUD. The pause
        /// menu, which is the very thing that used to be needed to get the minimap back at all.
        /// A screen fade, which missions, cutscenes, respawns, interiors and fast travel all go
        /// through. A character switch. A cutscene. And the player's ped handle, for a respawn
        /// that hands back a different body.
        ///
        /// They are read as EDGES: the rebuild happens as each one ENDS, so the ask is queued
        /// for when it has finished rather than spent while it is going on.
        ///
        /// Returns true while any of them is running, and the caller stands down for as long as
        /// it is: there is nothing to hide behind a fade, and nothing to gain from pushing a
        /// relayout at a movie the game is already rebuilding.
        /// </summary>
        private bool Upheaval()
        {
            bool paused = false, faded = false, switching = false, cutscene = false;
            var ped = _wasPed;

            try
            {
                paused = Function.Call<bool>(Hash.IS_PAUSE_MENU_ACTIVE);

                faded = Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT) ||
                        Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT) ||
                        Function.Call<bool>(Hash.IS_SCREEN_FADING_IN);

                switching = Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS);
                cutscene = Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE);

                var me = Game.Player == null ? null : Game.Player.Character;
                ped = me == null ? 0 : me.Handle;
            }
            catch (Exception ex)
            {
                // Every one of these is in the vendored 3.6.0 enum, so this is not expected --
                // but a state we cannot read must not be read as an edge, so nothing moves.
                Log.Once("vitals-upheaval", "Could not read the game's HUD state; the minimap layout " +
                                            "will be asked for on HideRepeatMs alone: " + ex.Message);
                return false;
            }

            if ((_wasPaused && !paused) || (_wasFaded && !faded) ||
                (_wasSwitching && !switching) || (_wasCutscene && !cutscene) ||
                (_wasPed != -1 && ped != _wasPed))
            {
                _askAgain = true;
            }

            _wasPaused = paused;
            _wasFaded = faded;
            _wasSwitching = switching;
            _wasCutscene = cutscene;
            _wasPed = ped;

            return paused || faded || switching || cutscene;
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

            // A reload starts over: the movie handle goes with the script and the next run has
            // to get its layout in before it can stop asking for it.
            _asked = false;
            _askAgain = false;
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
