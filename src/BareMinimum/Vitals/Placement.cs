using System;
using System.Drawing;
using System.Globalization;
using GTA;
using GTA.Native;
using BareMinimum.Core;
using Hud = BareMinimum.UI.Draw;

namespace BareMinimum.Vitals
{
    /// <summary>
    /// The cash readout, beside the bars.
    ///
    /// THE GAME'S OWN CANNOT BE BROUGHT DOWN. SET_HUD_COMPONENT_POSITION does move HUD_CASH --
    /// sideways. Its Y is thrown away, and frontend.xml says why: the cash, the wanted stars,
    /// the weapon icon and the cash change are all members of list 1, a stack the game lays
    /// out from the top of the safe zone downward in priority order, and a member of a stack
    /// has no Y of its own. (Subtitles and the street name, listId -1, take a Y offset fine,
    /// which is the one Rockstar's own scripts nudge.) So the first version put it in the right
    /// place across the screen and left it stuck at the top, and this one does what is left:
    /// hides the game's readout every frame it might show and draws the same thing -- the
    /// total in the game's money face, the change under it, counting up, holding, fading --
    /// off the end of the row of bars, where it was wanted.
    ///
    /// IT COMES UP WHEN THE GAME'S WOULD. When the money changes, for a few seconds; and for as
    /// long as the game is showing its own -- in a shop, say -- read off IS_HUD_COMPONENT_ACTIVE
    /// before the hide goes in, so a till still tells you what you have.
    ///
    /// THE MINIMAP ITSELF CANNOT BE MOVED FROM HERE. The native that does it in FiveM,
    /// SET_MINIMAP_COMPONENT_POSITION, is FiveM's own -- it is not in the game's native table
    /// any more than IS_BIGMAP_ACTIVE was, and that one crashed the game when called by hash.
    /// The map sits where the safe-zone slider puts it; the frame follows the map, and the
    /// group offset moves the things that are ours -- the bars and this -- as one, because
    /// this hangs off the row.
    /// </summary>
    internal sealed class Placement
    {
        private const int Cash = 3;
        private const int CashChange = 13;

        /// <summary>The game's money face: Pricedown, font 7, the one the top-right total is set in.</summary>
        private const int MoneyFont = 7;

        /// <summary>How long the fade at the end takes, in seconds.</summary>
        private const float FadeSeconds = 0.6f;

        /// <summary>The game's colours for it: HUD_COLOUR_GREEN for the total and a gain, HUD_COLOUR_RED for a loss.</summary>
        private static readonly Color Green = Color.FromArgb(255, 114, 204, 114);
        private static readonly Color Red = Color.FromArgb(255, 224, 50, 50);

        private bool _reset;
        private bool _have;

        /// <summary>The last total read, the total as drawn (rolling toward it), and the change since the readout came up.</summary>
        private int _money;
        private float _shown;
        private long _delta;

        /// <summary>Our own clock, in seconds, and when the money last moved on it. Below zero while the readout is down.</summary>
        private float _wall;
        private float _since = -1f;

        /// <summary>Once a frame. <paramref name="visible"/> is whether the HUD is being drawn at all -- dead, arrested, hidden.</summary>
        public void Update(Settings cfg, UI.Gauge gauge, bool visible, bool on, float dt)
        {
            // A BUILD BEFORE THIS ONE MOVED THE GAME'S READOUT ACROSS THE SCREEN, and a moved
            // component stays moved until it is reset -- across a reload, not across a restart.
            if (!_reset)
            {
                _reset = true;
                Restore();
            }

            // NOT WHILE THE MOD IS OFF. This holds the game's own notification feed out of
            // the way of our frame, and a frame we are not drawing needs nothing held out of
            // its way. Passing 0 is what puts the feed back, and it is what Restore calls.
            Notifications(on ? cfg.HudNotifyLift : 0f);

            if (!cfg.MoveCash)
            {
                _since = -1f;
                return;
            }

            // NOWHERE TO PUT IT. With the HUD off, or on its icon style, there is no row of
            // bars, and the game's own readout is left to do its job.
            if (gauge == null || !cfg.ShowHud || cfg.Style != HudStyle.Bars)
            {
                _since = -1f;
                return;
            }

            if (dt < 0f) dt = 0f;
            if (dt > 0.1f) dt = 0.1f;
            _wall += dt;

            int money;
            var gameShowing = false;

            try
            {
                money = Game.Player.Money;
                gameShowing = Function.Call<bool>(Hash.IS_HUD_COMPONENT_ACTIVE, Cash);
            }
            catch (Exception ex)
            {
                Log.Once("cash-read", "Could not read the cash: " + ex.Message);
                return;
            }

            if (!_have)
            {
                _have = true;
                _money = money;
                _shown = money;
            }
            else if (money != _money)
            {
                // A fresh showing starts its change from nought; one already up adds to it.
                if (_since < 0f) _delta = 0;

                _delta += money - _money;
                _money = money;
                _since = _wall;
            }
            else if (gameShowing)
            {
                // The game has its readout up on its own account. Ours stays up with it, and
                // with no change to report if there has not been one.
                if (_since < 0f) _delta = 0;
                _since = _wall;
            }

            // THE GAME'S IS HIDDEN EVERY FRAME IT MIGHT SHOW, so the two never stand together.
            try
            {
                Function.Call(Hash.HIDE_HUD_COMPONENT_THIS_FRAME, Cash);
                Function.Call(Hash.HIDE_HUD_COMPONENT_THIS_FRAME, CashChange);
            }
            catch (Exception ex)
            {
                Log.Once("cash-hide", "Could not hide the game's cash readout: " + ex.Message);
            }

            if (_since < 0f || !visible) return;

            var age = _wall - _since;
            var hold = Math.Max(0.5f, cfg.CashSeconds);

            if (age > hold + FadeSeconds)
            {
                _since = -1f;
                _shown = _money;
                return;
            }

            var k = age < hold ? 1f : 1f - (age - hold) / FadeSeconds;

            // THE TOTAL COUNTS TO ITS NEW VALUE rather than jumping, as the game's does.
            var diff = _money - _shown;
            if (Math.Abs(diff) < 0.5f) _shown = _money;
            else _shown += diff * Math.Min(1f, dt * 7f);

            Draw(cfg, gauge, (long)Math.Round(_shown), _delta, k);
        }

        /// <summary>
        /// The game's notifications, lifted clear of the frame.
        ///
        /// THEY SIT JUST ABOVE THE RADAR and stack upward, which was clear of everything until
        /// the frame put a band above the map with the street name in it. The map cannot be
        /// moved and the band is where the writing lives, so the notifications give way.
        ///
        /// EVERY FRAME, because it is a per-frame value the game and every other script are
        /// free to set as well -- a menu that opens and reserves its own height would otherwise
        /// leave the feed wherever it put it. Nought puts it back, which is what Rockstar's own
        /// menus call on the way out and what Restore does here.
        /// </summary>
        private void Notifications(float lift)
        {
            if (Math.Abs(lift) < 0.0005f)
            {
                if (!_lifted) return;
                lift = 0f;
            }

            try
            {
                Function.Call(Hash.THEFEED_SET_SCRIPTED_MENU_HEIGHT, lift);
                _lifted = Math.Abs(lift) > 0.0005f;

                if (_lifted && !_saidLift)
                {
                    _saidLift = true;
                    Log.Info("Notifications lifted by " + lift.ToString("0.000") +
                             " of the screen. [HUD] NotifyLift, and 0 hands them back to the game.");
                }
            }
            catch (Exception ex)
            {
                Log.Once("notify-lift", "Could not move the notifications: " + ex.Message);
            }
        }

        /// <summary>Whether the feed is currently being held clear, so it is only put back once.</summary>
        private bool _lifted;
        private bool _saidLift;

        /// <summary>The readout itself: off the end of the row, level with the tops of the bars, the change under the total.</summary>
        private static void Draw(Settings cfg, UI.Gauge gauge, long total, long delta, float k)
        {
            UI.Gauge.Row row;

            try { row = gauge.Rack(); }
            catch { return; }

            if (row == null || row.Names.Count == 0) return;

            // One bar's gap past the last bar's surround, then the dials. The row already
            // carries the group offset, so this moves with the whole lot.
            var slots = row.Names.Count;
            var right = row.X + row.BarW + (slots - 1) * row.Pitch + row.Edge;
            var gap = Math.Max(0f, row.Pitch - row.BarW);

            var x = right + gap + cfg.CashX;
            var y = row.Top + cfg.CashY;

            var scale = Math.Max(0.1f, cfg.CashScale);
            var alpha = (int)(255f * Ink.Clamp01(k) * Ink.Clamp01(row.Opacity));
            if (alpha <= 0) return;

            Hud.Text("$" + total.ToString("N0", CultureInfo.InvariantCulture), x, y, scale,
                     Ink.Alpha(Green, alpha), MoneyFont, false, false, true);

            if (delta != 0)
            {
                var line = Hud.Height(scale, MoneyFont);
                var sign = delta > 0 ? "+$" : "-$";

                Hud.Text(sign + Math.Abs(delta).ToString("N0", CultureInfo.InvariantCulture),
                         x, y + line * 0.95f, scale * 0.72f,
                         Ink.Alpha(delta > 0 ? Green : Red, alpha), MoneyFont, false, false, true);
            }
        }

        /// <summary>
        /// The game's own positions back. Nothing here moves them any more, but a build before
        /// this one did and the game keeps a moved component where it was put; so once on the
        /// way in and once on the way out, and the switch going off does nothing else.
        /// </summary>
        public void Restore()
        {
            try
            {
                Function.Call(Hash.RESET_HUD_COMPONENT_VALUES, Cash);
                Function.Call(Hash.RESET_HUD_COMPONENT_VALUES, CashChange);

                // And the notifications back where the game had them, which is what nought
                // means to this native.
                Function.Call(Hash.THEFEED_SET_SCRIPTED_MENU_HEIGHT, 0f);
                _lifted = false;
            }
            catch
            {
                // Nothing more to try.
            }
        }
    }
}
