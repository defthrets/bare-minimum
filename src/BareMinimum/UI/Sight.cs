using System;
using GTA;
using GTA.Native;
using BareMinimum.Core;

namespace BareMinimum.UI
{
    /// <summary>
    /// WHETHER THERE IS A HUD ON SCREEN WORTH DRAWING OVER. Asked once a frame, answered the
    /// same way for everybody, and LATCHED.
    ///
    /// THE WHOLE ROW WAS BLINKING OFF AND ON, everywhere, all the time.
    ///
    /// Four files each had their own Visible(), each asked the game fresh, and each hid
    /// everything it drew the instant the answer came back no. IS_HUD_HIDDEN and
    /// IS_RADAR_HIDDEN are the twitchy ones: HIDE_HUD_AND_RADAR_THIS_FRAME is how the game --
    /// and how ANY of the dozen other scripts on this machine -- takes the HUD away, and it is
    /// a THIS FRAME native. One script calling it on one frame in ten took our bars down on
    /// one frame in ten. Nothing was wrong with the bars; they were being asked a question
    /// that changes sixty times a second and answering it honestly every time.
    ///
    /// So the hard states hide at once and the twitchy ones have to MEAN it. Steady frames of
    /// "hidden" before anything goes, and the moment it comes back it comes back. A tenth of a
    /// second is nothing against a cutscene or a phone call, which hold the native down for as
    /// long as they last, and it is everything against a single frame.
    ///
    /// AND THE FOUR ANSWERS WERE NOT THE SAME ANSWER. The vitals asked about the pause menu
    /// and the radar preference; the gauge did not; the hint and the toast asked a shorter
    /// question again. So on a frame where the tests disagreed, half the row drew and half did
    /// not -- which is not a blink, it is the instrument coming apart. One question now, one
    /// answer, everybody.
    ///
    /// THE HARD STATES ARE NOT LATCHED, on purpose. A bar floating over WASTED for a tenth of
    /// a second is worse than a bar that blinked, and those states -- dead, arrested, paused,
    /// switching, faded out -- do not chatter.
    /// </summary>
    internal static class Sight
    {
        /// <summary>
        /// How many frames in a row the game has to say the HUD is hidden before it is. Six is
        /// about a tenth of a second: shorter than anything that means it, longer than
        /// anything that does not.
        /// </summary>
        private const int Steady = 6;

        /// <summary>How often the flicker warning may be said again.</summary>
        private const int SayEveryMs = 20000;

        /// <summary>A tenth of a second's worth of flips in ten seconds is somebody fighting us.</summary>
        private const int Noisy = 4;
        private const int WindowMs = 10000;

        private static int _frame = -1;
        private static bool _clear = true;
        private static int _hidden;
        private static string _why = "";

        private static int _flips;
        private static int _windowAt;
        private static int _saidAt;
        private static bool _said;

        /// <summary>
        /// True when the row should draw. Every caller in a frame gets the same answer, however
        /// many times it is asked.
        /// </summary>
        public static bool Clear
        {
            get
            {
                Sync();
                return _clear;
            }
        }

        private static void Sync()
        {
            int now;
            try { now = Function.Call<int>(Hash.GET_FRAME_COUNT); }
            catch { now = _frame + 1; }

            if (now == _frame) return;
            _frame = now;

            string why;

            // ---- the states that mean it, and go at once ----
            if (Hard(out why))
            {
                _hidden = Steady;
                Flip(false, why);
                return;
            }

            // ---- and the states that have to hold ----
            if (!Soft(out why))
            {
                _hidden = 0;
                Flip(true, "");
                return;
            }

            if (_hidden < Steady) _hidden++;
            if (_hidden >= Steady) Flip(false, why);
        }

        /// <summary>Records the change and counts how often it is happening. See the class note.</summary>
        private static void Flip(bool clear, string why)
        {
            if (clear == _clear)
            {
                if (!clear) _why = why;
                return;
            }

            _clear = clear;
            if (!clear) _why = why;

            int wall;
            try { wall = Game.GameTime; }
            catch { return; }

            if (_windowAt == 0 || wall - _windowAt > WindowMs)
            {
                _windowAt = wall;
                _flips = 0;
            }

            _flips++;

            if (_flips < Noisy || (_said && wall - _saidAt < SayEveryMs)) return;

            _said = true;
            _saidAt = wall;

            Log.Warn("The HUD went away and came back " + _flips + " time(s) in " +
                     ((wall - _windowAt) / 1000) + "s, the last of them because " + _why + ". " +
                     "Something on this machine is calling HIDE_HUD_AND_RADAR_THIS_FRAME on and " +
                     "off -- the game does it for cutscenes and phone calls, and so do other " +
                     "scripts. Six frames of it are already ignored; this is more than that.");
        }

        /// <summary>Dead, arrested, paused, switching, faded. TRUE means gone, now.</summary>
        private static bool Hard(out string why)
        {
            why = "";

            try
            {
                if (Game.IsPaused) { why = "the game is paused"; return true; }

                if (Function.Call<bool>(Hash.IS_PAUSE_MENU_ACTIVE)) { why = "the pause menu is up"; return true; }
                if (!Function.Call<bool>(Hash.IS_SCREEN_FADED_IN)) { why = "the screen is not faded in"; return true; }

                if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS))
                {
                    why = "a character switch is running";
                    return true;
                }

                var me = Game.Player == null ? 0 : Game.Player.Handle;

                if (Function.Call<bool>(Hash.IS_PLAYER_DEAD, me)) { why = "he is dead"; return true; }

                if (Function.Call<bool>(Hash.IS_PLAYER_BEING_ARRESTED, me, true))
                {
                    why = "he is being arrested";
                    return true;
                }

                return false;
            }
            catch
            {
                // A question we cannot ask is not a reason to take the HUD away.
                return false;
            }
        }

        /// <summary>The this-frame ones. TRUE means hidden, but only counts if it holds.</summary>
        private static bool Soft(out string why)
        {
            why = "";

            try
            {
                if (Function.Call<bool>(Hash.IS_HUD_HIDDEN)) { why = "the game's HUD is hidden"; return true; }
                if (Function.Call<bool>(Hash.IS_RADAR_HIDDEN)) { why = "the radar is hidden"; return true; }

                if (!Function.Call<bool>(Hash.IS_RADAR_PREFERENCE_SWITCHED_ON))
                {
                    why = "the radar is switched off in the game's own settings";
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
