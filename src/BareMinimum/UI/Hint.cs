using System;
using System.Drawing;
using GTA;
using GTA.Native;

using Hud = BareMinimum.UI.Draw;

namespace BareMinimum.UI
{
    /// <summary>
    /// A small line at the bottom of the screen, and a thin fill under it when something is
    /// being held down.
    ///
    /// IT REPLACES THE GAME'S HELP BOX FOR THE SLEEP OFFER. That box is pinned to the top
    /// left, in the game's own furniture, next to the messages about your car being
    /// impounded -- which is the right place for the game to talk to you and the wrong one
    /// for a mod that draws its own panels everywhere else. It is also the loudest thing on
    /// the screen for a prompt that should be the quietest.
    ///
    /// SMALLER AND FAINTER THAN Toast ON PURPOSE. Toast is a result -- you slept, here is
    /// what it did -- and it is allowed to be read. This is an offer, shown while you happen
    /// to be sat in a car, and most of the time the answer is no. It sits below Toast's line
    /// and at about two thirds of its opacity, so the two can never be confused for each
    /// other even though they share the bottom of the screen. They also cannot appear
    /// together: the offer is gone by the time the sleep starts, and the card arrives after
    /// it ends.
    ///
    /// THE CALLER DRIVES IT, FRAME BY FRAME. There is no Hide. Whoever wants it up calls
    /// Show every frame, and it fades itself out when the calls stop -- which means the
    /// thing that knows whether the offer still stands never has to remember to take it
    /// down, and cannot leave it stuck on screen by returning early.
    /// </summary>
    internal static class Hint
    {
        /// <summary>How long after the last Show it starts going. One frame of slack.</summary>
        private const int GraceMs = 90;

        private const int RiseMs = 160;
        private const int FallMs = 260;

        /// <summary>Where its FOOT sits. Below Toast, above the very bottom edge.</summary>
        private const float Foot = 0.958f;

        /// <summary>How far it climbs on the way in, as a fraction of screen height.</summary>
        private const float Climb = 0.012f;

        private const float Pad = 0.009f;
        private const float Scale = 0.27f;

        /// <summary>The fill under the text. Thin: it is a hint, not a loading bar.</summary>
        private const float TrackH = 0.0032f;

        /// <summary>
        /// Everything is drawn through this, so "barely noticeable" is one number rather than
        /// a decision repeated at every call.
        /// </summary>
        private const float Quiet = 0.66f;

        private static string _text;
        private static string _cap;
        private static float _at = -1f;

        /// <summary>The keycap's own geometry, as fractions of screen height.</summary>
        private const float CapPadX = 0.006f;
        private const float CapScale = 0.24f;
        private const float CapGap = 0.008f;

        private static int _askedAt;
        private static int _since;

        /// <summary>Puts a line up for this frame. Call it again next frame to keep it.</summary>
        public static void Show(string text)
        {
            Show(text, null, -1f);
        }

        /// <summary>With a key drawn on a cap in front of the words.</summary>
        public static void Show(string text, string cap)
        {
            Show(text, cap, -1f);
        }

        /// <summary>
        /// As above, with a fill from 0 to 1 under the text. A negative number draws no fill.
        /// </summary>
        public static void Show(string text, float at)
        {
            Show(text, null, at);
        }

        /// <summary>
        /// The whole of it: a line, an optional key on a cap, an optional fill.
        ///
        /// THE CAP IS DRAWN BY US RATHER THAN ASKED FOR. ~INPUT_CONTEXT~ resolves to a button
        /// picture inside the game's HELP box and NOWHERE ELSE -- everywhere else the text
        /// renderer emits the raw token, so this chip was showing "t_E" and "b__7" to people.
        /// The key from the ini is the thing the player actually presses, and it stays right
        /// however they have rebound it.
        /// </summary>
        public static void Show(string text, string cap, float at)
        {
            if (string.IsNullOrEmpty(text)) return;

            int now;
            try { now = Game.GameTime; }
            catch { return; }

            // A gap since the last request means this is a new hint rather than the same one
            // continuing, so the rise starts again.
            if (_since == 0 || now - _askedAt > GraceMs) _since = now;

            _text = text;
            _cap = string.IsNullOrEmpty(cap) ? null : cap;
            _at = at > 1f ? 1f : at;
            _askedAt = now;
        }

        /// <summary>Takes it off at once. For a shutdown.</summary>
        public static void Clear()
        {
            _text = null;
            _cap = null;
            _askedAt = 0;
            _since = 0;
            _at = -1f;
        }

        /// <summary>Draws it, if anything asked for it recently enough.</summary>
        public static void Draw(bool suspended)
        {
            if (string.IsNullOrEmpty(_text) || _since == 0) return;

            int now;
            try { now = Game.GameTime; }
            catch { Clear(); return; }

            var stale = now - _askedAt;

            // Long gone. Forget it rather than fading from a value nobody can see.
            if (stale > GraceMs + FallMs) { Clear(); return; }

            if (suspended || !Visible()) return;

            try { Paint(now, stale); }
            catch (Exception ex)
            {
                Clear();
                Core.Log.Once("hint", "Could not draw a hint: " + ex.Message);
            }
        }

        private static void Paint(int now, int stale)
        {
            var arrive = Theme.Motion && RiseMs > 0
                ? Math.Min(1f, (now - _since) / (float)RiseMs)
                : 1f;

            arrive = 1f - (1f - arrive) * (1f - arrive);

            var leaving = stale <= GraceMs || !Theme.Motion || FallMs <= 0
                ? 1f
                : Math.Max(0f, 1f - (stale - GraceMs) / (float)FallMs);

            var show = arrive * leaving * Quiet;
            if (show <= 0.01f) return;

            // Up on the way in and back down on the way out, so it belongs to the bottom edge
            // rather than appearing in the middle of nothing.
            var slide = Climb * (1f - arrive) + Climb * 0.5f * (1f - leaving);

            var textW = Hud.Width(_text, Scale, Hud.FontBody);
            var textH = Hud.Height(Scale, Hud.FontBody);

            // The cap is as wide as the letters on it plus a margin either side, so E and
            // NumPad0 both sit in one that fits them.
            var capText = _cap == null ? 0f : Hud.Width(_cap, CapScale, Hud.FontLabel);
            var capW = _cap == null ? 0f : capText + Hud.ToX(CapPadX) * 2f;
            var capLead = _cap == null ? 0f : capW + Hud.ToX(CapGap);

            var fill = _at >= 0f;

            var w = Pad + capLead + textW + Pad;
            var h = Pad * 0.85f + textH + (fill ? 0.004f + TrackH : 0f) + Pad * 0.85f;

            var x = 0.5f - w * 0.5f;
            var top = Foot - h + slide;

            Theme.Panel(x, top, w, h, show);

            if (_cap != null)
            {
                var capH = Hud.Height(CapScale, Hud.FontLabel) + 0.004f;
                var capY = top + Pad * 0.85f + (textH - capH) * 0.5f - 0.001f;

                Hud.RoundRect(x + Pad, capY, capW, capH, 0.0022f,
                              Palette.Alpha(Palette.Brand, (int)(58f * show)));

                Hud.Text(_cap, x + Pad + (capW - capText) * 0.5f, capY + 0.002f, CapScale,
                         Palette.Alpha(Palette.Brand, (int)(255f * show)), Hud.FontLabel);
            }

            Hud.Text(_text, x + Pad + capLead, top + Pad * 0.85f, Scale,
                     Palette.Alpha(Palette.Text, (int)(232f * show)), Hud.FontBody);

            if (!fill) return;

            var barY = top + h - Pad * 0.85f - TrackH;
            var barW = w - Pad * 2f;

            Hud.Bar(x + Pad, barY, barW, TrackH, Color.FromArgb((int)(120f * show), 30, 26, 22));

            var got = barW * (_at < 0f ? 0f : _at);
            if (got <= 0.0005f) return;

            Hud.Bar(x + Pad, barY, got, TrackH, Palette.Alpha(Palette.Brand, (int)(235f * show)));
        }

        /// <summary>
        /// Whether this is a frame anything of ours belongs on. The same rule the gauge and
        /// the card use.
        /// </summary>
        private static bool Visible()
        {
            try
            {
                if (Game.IsPaused) return false;
                if (!Function.Call<bool>(Hash.IS_SCREEN_FADED_IN)) return false;
                if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return false;

                return !Function.Call<bool>(Hash.IS_RADAR_HIDDEN);
            }
            catch
            {
                return true;
            }
        }
    }
}
