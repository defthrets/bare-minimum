using System;
using System.Drawing;
using GTA;

using Hud = BareMinimum.UI.Draw;

namespace BareMinimum.UI
{
    /// <summary>
    /// The parts every screen in this mod is built from: the head, the key caps, the tags, the
    /// meters. Hoodrich's UiKit, in this mod's amber, so the two mods' panels are one family.
    ///
    /// The three screens here had each grown their own letterhead, their own idea of a footer
    /// -- "BACKSPACE to close" as a run of words in one corner, "LEFT / RIGHT cross over" in
    /// another -- and the same fact looked like three different things in three rooms.
    /// Everything here is one thing drawn one way, and the screens compose it.
    ///
    /// EVERY ANIMATION IS A FUNCTION OF TIME, never of frames. Nothing here has to be ticked,
    /// so nothing here can stall.
    /// </summary>
    internal static class Kit
    {
        // ======================================================================
        // The head
        // ======================================================================

        /// <summary>How tall the head is: one title row and a rule.</summary>
        public const float HeadH = 0.052f;

        /// <summary>
        /// The title row: a mark, the room in capitals, what it is in small dim words after it,
        /// a second mark and a figure on the right, and a rule under the lot. Returns the y
        /// under the rule.
        ///
        /// ONE ROW WHERE THERE WERE TWO. The panels used to stack a big centred title over a
        /// centred line of small print with an amber bar under both. The small print rides the
        /// title now and the figure sits on the same line, so the first thing under the head is
        /// the thing you came for.
        ///
        /// The marks breathe with the same shimmer the HUD icons use, because a mark drawn
        /// still next to two that move reads as printed on.
        /// </summary>
        public static float Head(float left, float top, float w, float pad, Icon mark, string title,
                                 string blurb, Icon tail, string right, float arrive, float shimmer)
        {
            var x = left + pad;
            var edge = left + w - pad;
            var y = top + 0.011f;

            var tx = x;

            if (mark != null && !mark.Missing)
            {
                var wide = Hud.ToX(HeadIcon);

                mark.DrawSized(x + wide * 0.5f, y + 0.010f, wide, HeadIcon,
                               Sheen.On(Palette.Alpha(Palette.Brand, (int)(235f * arrive)), 0f, shimmer));

                if (!mark.Missing) tx = x + wide + 0.006f;
            }

            Hud.Text(title, tx, y, 0.34f, Palette.Alpha(Palette.Text, (int)(255f * arrive)), Hud.FontLabel);

            if (!string.IsNullOrEmpty(blurb))
            {
                var after = tx + Hud.Width(title, 0.34f, Hud.FontLabel) + 0.010f;

                Hud.Text(blurb, after, y + 0.0045f, 0.25f,
                         Palette.Alpha(Palette.TextDim, (int)(175f * arrive)), Hud.FontBody);
            }

            var rx = edge;

            if (!string.IsNullOrEmpty(right))
            {
                Hud.TextRight(right, edge, y + 0.003f, 0.26f,
                              Palette.Alpha(Palette.TextDim, (int)(200f * arrive)), Hud.FontLabel);

                rx = edge - Hud.Width(right, 0.26f, Hud.FontLabel) - 0.008f;
            }

            if (tail != null && !tail.Missing)
            {
                var wide = Hud.ToX(HeadIcon);

                // A third of a cycle behind the first mark, so the pair never brightens
                // together -- in step they read as one wide ornament rather than two marks.
                tail.DrawSized(rx - wide * 0.5f, y + 0.010f, wide, HeadIcon,
                               Sheen.On(Palette.Alpha(Palette.Brand, (int)(235f * arrive)), 0.33f, shimmer));
            }

            Theme.Rule(x, top + HeadH - 0.006f, w - pad * 2f, arrive);

            return top + HeadH;
        }

        private const float HeadIcon = 0.022f;

        /// <summary>
        /// The head with a shop's own sign in place of the title: the LTD mark over its
        /// counter. A brand mark is already a piece of typography, so nothing else goes on the
        /// row with it but the rule underneath.
        ///
        /// SIZED OFF THE PANEL, NOT OFF THE TEXT. A mark should take the same share of the head
        /// whatever the title scale happens to be. Height comes back out of the width through
        /// the artwork's own ratio and the screen's, so the mark is never stretched.
        /// </summary>
        public static float HeadLogo(float left, float top, float w, float pad, Icon logo, float ratio,
                                     string right, float arrive)
        {
            var shape = Math.Max(0.1f, ratio);

            var logoW = w * 0.40f;
            var logoH = logoW * Hud.Aspect / shape;

            var maxH = HeadH - 0.016f;

            if (logoH > maxH)
            {
                logoH = maxH;
                logoW = logoH * shape / Hud.Aspect;
            }

            // Tinted WHITE, which is the identity for a sprite: CustomSprite multiplies its
            // colour with the texture, so white leaves the brand's own reds and greys exactly
            // as they were painted. Every other icon in this mod relies on the opposite.
            logo.DrawSized(left + w * 0.5f, top + 0.005f + maxH * 0.5f, logoW, logoH,
                           Color.FromArgb((int)(255f * arrive), 255, 255, 255));

            if (!string.IsNullOrEmpty(right))
            {
                Hud.TextRight(right, left + w - pad, top + 0.014f, 0.26f,
                              Palette.Alpha(Palette.TextDim, (int)(200f * arrive)), Hud.FontLabel);
            }

            Theme.Rule(left + pad, top + HeadH - 0.006f, w - pad * 2f, arrive);

            return top + HeadH;
        }

        // ======================================================================
        // The meter
        // ======================================================================

        /// <summary>A meter is a label line and a track under it, this tall together.</summary>
        public const float MeterH = 0.034f;

        /// <summary>Amber while there is room, warning as it fills, red when it is full.</summary>
        public static Color MeterTint(float full)
        {
            if (full >= 0.9f) return Palette.Danger;
            if (full >= 0.7f) return Palette.Warn;
            return Palette.Brand;
        }

        /// <summary>
        /// A capacity meter: a picture and a name on the left, the figure on the right, and a
        /// track under both that the fill slides along. Four rectangles.
        ///
        /// SHOWN IS WHERE THE NEEDLE IS, FULL IS WHERE IT IS GOING. The caller eases shown
        /// toward full, so the bar moves when something changes and holds still when nothing
        /// does. The tint follows the true figure, not the eased one, or a bar crossing into
        /// the red would go red a beat late.
        /// </summary>
        public static void Meter(float x, float y, float w, string icon, string label,
                                 string figure, float shown, float full, float arrive)
        {
            var tx = x;

            if (!string.IsNullOrEmpty(icon) &&
                Hud.Sprite(icon, x + Hud.ToX(MeterIcon) * 0.5f, y + 0.0075f, MeterIcon,
                           Palette.Alpha(Palette.Text, (int)(220f * arrive))))
            {
                tx = x + Hud.ToX(MeterIcon) + 0.005f;
            }

            Hud.Text(label, tx, y, 0.26f, Palette.Alpha(Palette.Text, (int)(240f * arrive)), Hud.FontLabel);

            var tint = MeterTint(full);

            Hud.TextRight(figure, x + w, y + 0.001f, 0.25f,
                          Palette.Alpha(full >= 0.7f ? tint : Palette.TextDim, (int)(230f * arrive)),
                          Hud.FontBody);

            var barY = y + 0.021f;

            if (shown < 0f) shown = 0f;
            if (shown > 1f) shown = 1f;

            Hud.Bar(x, barY, w, MeterTrack, Color.FromArgb((int)(160f * arrive), 30, 26, 22));

            if (shown > 0.001f)
            {
                var fillW = w * shown;

                Hud.Bar(x, barY, fillW, MeterTrack, Palette.Alpha(tint, (int)(235f * arrive)));

                var tipW = Math.Min(fillW, Hud.ToX(0.0028f));

                Hud.Bar(x + fillW - tipW, barY, tipW, MeterTrack,
                        Color.FromArgb((int)(150f * arrive), 255, 255, 255));
            }

            Hud.Bar(x, barY + MeterTrack, w, 0.0008f, Palette.Alpha(tint, (int)(70f * arrive)));
        }

        private const float MeterTrack = 0.0065f;
        private const float MeterIcon = 0.015f;

        // ======================================================================
        // Key caps
        // ======================================================================

        /// <summary>The footer: a rule and one line of key caps, this tall together.</summary>
        public const float FootH = 0.040f;

        /// <summary>
        /// One key and what it does: the key on a small dark cap, the errand in dim words
        /// after it. Two rectangles. Returns where the next one starts.
        ///
        /// A CAP, NOT A WORD. "BACKSPACE  DONE" is two words in the same colour and the same
        /// face, and which of them is the key is something you work out. A key drawn as a key
        /// -- a little block with a letter on it -- is read as a key before it is read at all.
        ///
        /// An icon instead of a cap label draws the picture on the cap: the arrows. Either
        /// way the cap is the same height, so a row of them is a row.
        /// </summary>
        public static float Key(float x, float y, string cap, string icon, string words, float arrive)
        {
            var ink = (int)(255f * arrive);

            const float capH = 0.0175f;

            var capW = string.IsNullOrEmpty(icon)
                ? Hud.Width(cap, 0.22f, Hud.FontLabel) + 0.008f
                : Hud.ToX(capH) + 0.004f;

            var capTop = y - 0.0015f;

            Hud.Bar(x, capTop, capW, capH, Color.FromArgb((int)(36f * arrive), 255, 255, 255));
            Hud.Bar(x, capTop + capH - 0.0012f, capW, 0.0012f,
                    Palette.Alpha(Palette.BrandDeep, (int)(200f * arrive)));

            if (!string.IsNullOrEmpty(icon))
            {
                Hud.Sprite(icon, x + capW * 0.5f, capTop + capH * 0.5f, capH * 0.72f,
                           Palette.Alpha(Palette.Text, ink));
            }
            else
            {
                Hud.Text(cap, x + capW * 0.5f, y, 0.22f, Palette.Alpha(Palette.Text, ink),
                         Hud.FontLabel, true, false, false);
            }

            var wx = x + capW + 0.005f;

            Hud.Text(words, wx, y, 0.23f, Palette.Alpha(Palette.TextDim, (int)(215f * arrive)),
                     Hud.FontLabel, false, false, false);

            return wx + Hud.Width(words, 0.23f, Hud.FontLabel) + 0.014f;
        }

        /// <summary>The same, ending at a right edge: the way out, in the same corner on every screen.</summary>
        public static void KeyRight(float right, float y, string cap, string words, float arrive)
        {
            var capW = Hud.Width(cap, 0.22f, Hud.FontLabel) + 0.008f;
            var wordsW = Hud.Width(words, 0.23f, Hud.FontLabel);

            Key(right - capW - 0.005f - wordsW, y, cap, null, words, arrive);
        }

        /// <summary>What this player calls the buttons. Xbox letters, because that is what the game prints.</summary>
        public static string Confirm { get { return Hud.OnPad ? "A" : "ENTER"; } }
        public static string Back { get { return Hud.OnPad ? "B" : "BACKSPACE"; } }
        public static string Drop { get { return Hud.OnPad ? "X" : "SPACE"; } }
        public static string Pages { get { return Hud.OnPad ? "LB RB" : "Q E"; } }

        // ======================================================================
        // Tags and words
        // ======================================================================

        /// <summary>
        /// A small label on a dark chip: SALE, CLOSED, COLD. One rectangle. Returns its width.
        /// A tag is a different kind of thing from a name and looks like one.
        /// </summary>
        public static float Tag(float x, float y, string text, Color ink, float arrive)
        {
            var w = Hud.Width(text, 0.19f, Hud.FontLabel) + 0.007f;

            Hud.Bar(x, y + 0.0015f, w, 0.0145f, Palette.Alpha(ink, (int)(34f * arrive)));

            Hud.Text(text, x + w * 0.5f, y + 0.0005f, 0.19f, Palette.Alpha(ink, (int)(225f * arrive)),
                     Hud.FontLabel, true, false, false);

            return w;
        }

        /// <summary>
        /// Trims text until it fits a width, with an ellipsis if anything was lost.
        ///
        /// Binary search rather than a character at a time: measuring is a native call, and a
        /// long line trimmed one letter per pass is fifty of them for one row.
        /// </summary>
        public static string Fit(string text, float maxWidth, float scale, int font)
        {
            if (string.IsNullOrEmpty(text) || maxWidth <= 0f) return text;
            if (Hud.Width(text, scale, font) <= maxWidth) return text;

            const string Ellipsis = "...";

            var lo = 0;
            var hi = text.Length;

            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                var candidate = text.Substring(0, mid).TrimEnd() + Ellipsis;

                if (Hud.Width(candidate, scale, font) <= maxWidth) lo = mid;
                else hi = mid - 1;
            }

            return lo <= 0 ? Ellipsis : text.Substring(0, lo).TrimEnd() + Ellipsis;
        }

        // ======================================================================
        // Motion
        // ======================================================================

        /// <summary>
        /// Chevrons that run one way: a transfer crossing the gap. Three glyphs, each lit a
        /// little after the last, so the eye reads a direction rather than three marks. Text,
        /// so it costs no rectangles at all. Strength is nought to one; dir is plus for right.
        /// </summary>
        public static void Flow(float x, float y, float w, int dir, float strength, Color ink,
                                float scale = 0.30f)
        {
            if (strength <= 0.01f || w <= 0f) return;

            const int Count = 3;
            const int LapMs = 620;

            var glyph = dir >= 0 ? ">" : "<";
            var glyphW = Hud.Width(glyph, scale, Hud.FontBody);

            var span = glyphW * Count * 1.05f;
            var start = x + (w - span) * 0.5f;

            var t = Theme.Motion ? (Game.GameTime % LapMs) / (float)LapMs : 0f;

            for (var i = 0; i < Count; i++)
            {
                var order = dir >= 0 ? i : Count - 1 - i;
                var phase = t - order / (float)Count;
                phase -= (float)Math.Floor(phase);

                var lit = 0.35f + 0.65f * (float)Math.Pow(1f - phase, 2.2);

                Hud.Text(glyph, start + i * glyphW * 1.05f, y, scale,
                         Palette.Alpha(ink, (int)(ink.A * lit * strength)), Hud.FontBody);
            }
        }

        /// <summary>How far into a short flash we are, one at the moment and nought when it is over.</summary>
        public static float Flash(int at, int lengthMs)
        {
            if (at <= 0) return 0f;

            var since = Game.GameTime - at;
            if (since < 0 || since >= lengthMs) return 0f;

            return 1f - since / (float)lengthMs;
        }

        /// <summary>
        /// A tile has landed this far, nought to one, for the staggered entrance every strip
        /// of tiles makes: a frame or two apart, eased out so each settles rather than stops.
        /// </summary>
        public static float Landed(int age, int lead, int over)
        {
            if (!Theme.Motion || over <= 0) return 1f;

            var land = age <= lead ? 0f : (age - lead) / (float)over;
            if (land > 1f) land = 1f;

            return 1f - (1f - land) * (1f - land);
        }
    }
}
