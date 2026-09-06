using System;
using System.Drawing;
using GTA;
using GTA.Native;

using Hud = BareMinimum.UI.Draw;

namespace BareMinimum.UI
{
    /// <summary>
    /// A short card at the bottom of the screen: a mark, a headline, a line under it, and a
    /// meter that fills to where the thing it is reporting ended up.
    ///
    /// IT REPLACES THE GAME'S TICKER FOR THE THINGS THAT ARE OURS. PostTicker puts a grey box
    /// in the corner above the minimap, in the game's own font, alongside the messages about
    /// your car being impounded. That is the right place for a message FROM THE GAME and the
    /// wrong one for a mod that has its own panels, its own icons and its own colour -- a mod
    /// that draws a whole shop menu and then reports the result of it in somebody else's
    /// furniture reads as two mods.
    ///
    /// AT THE BOTTOM MIDDLE, which is the one part of the screen the game keeps clear. The
    /// minimap owns the bottom left, the money and the wanted stars the top right, our own
    /// two marks the area beside the minimap, and the ticker the top left. The middle is
    /// where subtitles go and there are none while you are walking about.
    ///
    /// ONE AT A TIME, AND A SECOND REPLACES THE FIRST. A queue would mean a message arriving
    /// seconds after the thing it is about, which is worse than losing it -- and nothing here
    /// is important enough to be worth reading late.
    ///
    /// STATIC, like Sheen and Menu's control suppression, because there is one screen and one
    /// of these on it. The alternative is threading a reference through the four classes that
    /// have something to say.
    /// </summary>
    internal static class Toast
    {
        /// <summary>
        /// How long it sits FULLY UP, once it has finished arriving and before it starts to go.
        ///
        /// This is the number that means anything to a reader, which is why it is the one
        /// that is set. The old single figure covered the rise and the fall as well, so
        /// "three seconds" bought about two and a half seconds of readable card and the
        /// setting quietly under-delivered on its own name.
        /// </summary>
        public const int HoldMs = 4000;

        /// <summary>The rise on the way in and the fall on the way out.</summary>
        private const int RiseMs = 220;
        private const int FallMs = 340;

        /// <summary>
        /// The whole life, which is the hold plus what it costs to arrive and to leave.
        ///
        /// DERIVED, NOT SET. Paint reaches full opacity at RiseMs and starts dropping at
        /// LifeMs - FallMs, so this expression is what makes the gap between those two
        /// exactly HoldMs. Typed as a separate number the two would drift apart the first
        /// time either half of the animation was retimed.
        /// </summary>
        public const int LifeMs = RiseMs + HoldMs + FallMs;

        /// <summary>How long the meter takes to sweep from where it was to where it is.</summary>
        private const int SweepMs = 900;

        /// <summary>Where the card's FOOT sits. Clear of the subtitle line and the minimap.</summary>
        private const float Foot = 0.925f;

        /// <summary>How far it climbs on the way in, as a fraction of the screen's height.</summary>
        private const float Climb = 0.020f;

        private const float Pad = 0.013f;
        private const float MarkH = 0.040f;
        private const float Gap = 0.011f;

        private const float HeadScale = 0.40f;
        private const float SubScale = 0.28f;

        private const float TrackH = 0.0055f;
        private const float MinTextW = 0.115f;

        // ---- what is on screen right now --------------------------------------
        private static string _mark;
        private static string _head;
        private static string _sub;
        private static Color _accent;
        private static float _from;
        private static float _to;

        /// <summary>When it started being SEEN. Zero while it is still waiting for a frame.</summary>
        private static int _shownAt;

        /// <summary>Raised and not yet seen, and when it was raised so it can be given up on.</summary>
        private static bool _waiting;
        private static int _raisedAt;

        /// <summary>
        /// How long a card will wait for the screen before giving up on itself.
        ///
        /// A wake-up is worth holding through a fade. It is not worth holding through a
        /// twenty-minute cutscene and then announcing a night's sleep from before it.
        /// </summary>
        private const int PatienceMs = 20000;

        /// <summary>
        /// Puts a card up. <paramref name="mark"/> is a file in data\icons, and
        /// <paramref name="from"/> and <paramref name="to"/> are the meter's ends -- pass the
        /// same number twice for something with nothing to measure.
        /// </summary>
        public static void Show(string mark, string head, string sub, Color accent,
                                float from, float to)
        {
            _mark = mark;
            _head = head ?? "";
            _sub = sub ?? "";
            _accent = accent;
            _from = Clamp01(from);
            _to = Clamp01(to);

            _shownAt = 0;
            _waiting = true;

            try { _raisedAt = Game.GameTime; }
            catch { _waiting = false; }
        }

        /// <summary>Takes it off early. For a shutdown, or anything that owns the screen.</summary>
        public static void Clear()
        {
            _shownAt = 0;
            _waiting = false;
        }

        /// <summary>
        /// Draws it, if there is one and this is a frame it belongs on.
        ///
        /// THE CLOCK STARTS ON THE FIRST FRAME IT CAN BE SEEN, not when it was raised.
        ///
        /// That is the whole difference between a card that works and one nobody ever sees.
        /// The case it is for is somebody else's sleep mod: it fades the screen to black,
        /// moves the clock, and fades back. We notice the jump DURING the black, and a card
        /// whose clock had already started would spend its whole life behind a fade and be
        /// gone by the time the picture returned. Waiting costs nothing and the card arrives
        /// with the world -- with its full four seconds still ahead of it.
        ///
        /// It gives up after PatienceMs, so a card caught behind something long does not
        /// announce a night's sleep from before it.
        /// </summary>
        public static void Draw(bool suspended)
        {
            if (!_waiting && _shownAt == 0) return;

            int now;

            try { now = Game.GameTime; }
            catch { Clear(); return; }

            if (_waiting)
            {
                // Still behind a fade, a menu or a cutscene. Hold it, up to a point.
                if (suspended || !Visible())
                {
                    if (now - _raisedAt > PatienceMs) Clear();
                    return;
                }

                _waiting = false;
                _shownAt = now;
            }

            var age = now - _shownAt;

            if (age < 0 || age >= LifeMs) { _shownAt = 0; return; }

            // Gone behind something mid-card. The rest of its life runs down anyway: it has
            // been read by now, and a card that pauses and resumes reads as a card that is
            // stuck.
            if (suspended || !Visible()) return;

            try { Paint(age); }
            catch (Exception ex)
            {
                Clear();
                Core.Log.Once("toast", "Could not draw a card: " + ex.Message);
            }
        }

        private static void Paint(int age)
        {
            // ---- coming and going ----
            var arrive = Theme.Motion && RiseMs > 0
                ? Math.Min(1f, age / (float)RiseMs)
                : 1f;

            arrive = 1f - (1f - arrive) * (1f - arrive);

            var left = LifeMs - age;

            var leaving = Theme.Motion && FallMs > 0
                ? Math.Min(1f, left / (float)FallMs)
                : 1f;

            var show = arrive * leaving;
            if (show <= 0.01f) return;

            // Up on the way in and back down on the way out, so it belongs to the bottom edge
            // rather than appearing in the middle of the screen.
            var slide = Climb * (1f - arrive) + Climb * 0.6f * (1f - leaving);

            // ---- how big ----
            var headW = Hud.Width(_head, HeadScale, Hud.FontLabel);
            var subW = string.IsNullOrEmpty(_sub) ? 0f : Hud.Width(_sub, SubScale, Hud.FontBody);

            var textW = Math.Max(MinTextW, Math.Max(headW, subW));

            var markW = string.IsNullOrEmpty(_mark) ? 0f : Hud.ToX(MarkH);
            var markGap = markW > 0f ? Gap : 0f;

            var w = Pad + markW + markGap + textW + Pad;

            var headH = Hud.Height(HeadScale, Hud.FontLabel);
            var subH = string.IsNullOrEmpty(_sub) ? 0f : Hud.Height(SubScale, Hud.FontBody);

            var h = Pad + headH + (subH > 0f ? 0.001f + subH : 0f) + 0.008f + TrackH + Pad;

            var x = 0.5f - w * 0.5f;
            var top = Foot - h + slide;

            // ---- the card ----
            Theme.Panel(x, top, w, h, show);

            var tx = x + Pad;

            // ---- the mark ----
            //
            // Sized and placed against the WHOLE card rather than the text, so a moon and an
            // apple sit in the same spot whatever the wording under them is.
            if (markW > 0f)
            {
                var icon = IconCache.Get(_mark);

                if (icon != null && !icon.Missing)
                {
                    icon.DrawSized(tx + markW * 0.5f, top + h * 0.5f - 0.004f, markW, MarkH,
                                   Palette.Alpha(_accent, (int)(255f * show)));
                }

                tx += markW + markGap;
            }

            var y = top + Pad;

            Hud.Text(_head, tx, y, HeadScale,
                     Palette.Alpha(Palette.Text, (int)(255f * show)), Hud.FontLabel);

            y += headH;

            if (subH > 0f)
            {
                Hud.Text(_sub, tx, y + 0.001f, SubScale,
                         Palette.Alpha(Palette.TextDim, (int)(225f * show)), Hud.FontBody);

                y += 0.001f + subH;
            }

            Meter(tx, y + 0.008f, x + w - Pad - tx, age, show);
        }

        /// <summary>
        /// The meter, sweeping from where the need WAS to where it is now.
        ///
        /// That sweep is the whole reason this is a card rather than a line of text. "Rested
        /// 100%" is a number you read; a bar climbing from a quarter to full is the night you
        /// just had, and it costs three rectangles.
        /// </summary>
        private static void Meter(float x, float y, float w, int age, float show)
        {
            if (w <= 0f) return;

            var swept = !Theme.Motion || SweepMs <= 0
                ? 1f
                : Math.Min(1f, age / (float)SweepMs);

            // Eased out, so it arrives rather than stopping.
            swept = 1f - (1f - swept) * (1f - swept) * (1f - swept);

            var at = _from + (_to - _from) * swept;

            Hud.Bar(x, y, w, TrackH, Color.FromArgb((int)(150f * show), 30, 26, 22));

            if (at > 0.001f)
            {
                var fill = w * at;

                Hud.Bar(x, y, fill, TrackH, Palette.Alpha(_accent, (int)(240f * show)));

                // A bright tip, so the end of it is a point rather than an edge.
                var tip = Math.Min(fill, Hud.ToX(0.0026f));

                Hud.Bar(x + fill - tip, y, tip, TrackH,
                        Color.FromArgb((int)(160f * show), 255, 255, 255));
            }

            Hud.Bar(x, y + TrackH, w, 0.0008f, Palette.Alpha(_accent, (int)(70f * show)));
        }

        /// <summary>
        /// Whether this is a frame anything of ours belongs on. The same rule the gauge uses:
        /// not over a pause menu, a fade or a character switch, and not while the player has
        /// asked for the radar to be gone.
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

        private static float Clamp01(float v)
        {
            return v < 0f ? 0f : v > 1f ? 1f : v;
        }
    }
}
