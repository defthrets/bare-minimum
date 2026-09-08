using System;
using System.Globalization;
using GTA;
using GTA.Math;
using GTA.Native;

using BareMinimum.Core;
using BareMinimum.UI;

using Hud = BareMinimum.UI.Draw;

namespace BareMinimum.Venues
{
    /// <summary>
    /// The card that names a shop when the cursor is over its marker on the pause map.
    /// </summary>
    ///
    /// <remarks>
    /// THE PAUSE MAP HAS ONE HOVER HOOK, AND IT IS ONLINE'S. The legend on the right is the
    /// only thing the game itself shows about a blip under the cursor, and with the shops
    /// grouped it says "Food 34/160" -- the count, not the shop. Which blip the cursor is over
    /// is told to scripts through exactly one pair of natives, IS_HOVERING_OVER_MISSION_CREATOR_BLIP
    /// and GET_NEW_SELECTED_MISSION_CREATOR_BLIP, and only for blips flagged as "mission
    /// creator" blips: it is how the freemode script knows which job to describe when you hover
    /// one on the Online map. Story mode never runs that script and its map has no job card,
    /// so the flag costs nothing there and the hook is ours. Vendors flags every shop marker
    /// and MachineBlips every machine; this reads the hook and draws the card.
    ///
    /// DRAWN BY US, OVER THE MENU. Script drawing renders on top of the pause menu unless a
    /// script asks for it to go behind, which is why every other panel in this mod checks
    /// Game.IsPaused and stands down. This one wants the opposite and says so every frame.
    ///
    /// IT FOLLOWS THE MOUSE. The pointer comes off INPUT_CURSOR_X/Y, which the frontend and
    /// the world share, so the card sits just off it like a tooltip and flips to the other
    /// side at the edge of the screen. On a pad the cursor is a stick and those inputs say
    /// nothing, so the card takes a fixed place at the top left of the map instead.
    ///
    /// ON FRAMES, NOT THE CLOCK. The game timer is not to be trusted inside the pause menu,
    /// so the card's arrival is counted in ticks; a card timed off a frozen clock would be
    /// forever arriving and never seen.
    /// </remarks>
    internal sealed class MapCard
    {
        /// <summary>Ticks over which it arrives. About a sixth of a second.</summary>
        private const int RiseTicks = 9;

        /// <summary>How far it rises into place, as a fraction of screen height.</summary>
        private const float Climb = 0.008f;

        private const float TitleScale = 0.36f;
        private const float TitleTrack = 0.0020f;
        private const float LineScale = 0.26f;

        private const float PadY = 0.011f;
        private const float Gap = 0.004f;
        private const float RailW = 0.004f;

        /// <summary>Off the pointer by this much, so the arrow never covers the first letter.</summary>
        private const float LeadX = 0.016f;
        private const float LeadY = 0.022f;

        /// <summary>Where it sits when there is no mouse to sit beside: under the header, left.</summary>
        private const float FixedX = 0.014f;
        private const float FixedY = 0.170f;

        private readonly Settings _cfg;
        private readonly Vendors _vendors;
        private readonly MachineBlips _machines;

        /// <summary>The blip the map last said the cursor was over, or 0.</summary>
        private int _hover;
        private int _ticks;
        private int _unknownTicks;

        private string _title;
        private string _line;

        public MapCard(Settings cfg, Vendors vendors, MachineBlips machines)
        {
            _cfg = cfg;
            _vendors = vendors;
            _machines = machines;
        }

        // ======================================================================

        public void Update()
        {
            if (!_cfg.ShopBlipHoverCard) { Drop(); return; }

            try
            {
                if (!Function.Call<bool>(Hash.IS_PAUSE_MENU_ACTIVE)) { Drop(); return; }

                Log.Once("map-card-ticks", "Ticking inside the pause menu; the shop card can draw.");

                var over = Function.Call<bool>(Hash.IS_HOVERING_OVER_MISSION_CREATOR_BLIP);
                var fresh = Function.Call<int>(Hash.GET_NEW_SELECTED_MISSION_CREATOR_BLIP);

                // Either the map says so once, when the selection changes, or it says so every
                // frame; both are the same to this. A blip that is not one of ours is nothing.
                if (fresh != 0 && fresh != _hover)
                {
                    _hover = fresh;
                    _ticks = 0;
                    Resolve();
                }

                if (!over) { Drop(); return; }

                if (_hover == 0 || _title == null)
                {
                    // The map says the cursor is on one of ours and has not said which. Asked
                    // again now and then in case the marker was rebuilt under it.
                    _unknownTicks++;
                    if (_hover != 0 && _unknownTicks % 30 == 0) Resolve();
                    if (_unknownTicks == 90)
                    {
                        Log.Once("map-card-unknown",
                                 "The pause map says the cursor is over a shop marker but has not said which one.");
                    }
                    return;
                }

                _unknownTicks = 0;
                _ticks++;

                Function.Call(Hash.SET_SCRIPT_GFX_DRAW_BEHIND_PAUSEMENU, false);
                Paint();
            }
            catch (Exception ex)
            {
                Drop();
                Log.Once("map-card", "Could not draw the map card: " + ex.Message);
            }
        }

        /// <summary>Which of ours the hovered blip is, and what the card should say about it.</summary>
        private void Resolve()
        {
            _title = null;
            _line = null;

            var v = _vendors.ByBlip(_hover);

            if (v != null)
            {
                _title = v.Name;
                _line = _vendors.Hours(v) + Away(v.Position);

                Log.Once("map-card-hover", "The pause map put the cursor over " + v.Name + "'s marker; the card works.");
                return;
            }

            Vector3 at;
            if (_machines.Owns(_hover, out at))
            {
                _title = "Vending machine";
                _line = "Snacks and drinks, day and night" + Away(at);
            }
        }

        private void Drop()
        {
            _hover = 0;
            _ticks = 0;
            _unknownTicks = 0;
            _title = null;
            _line = null;
        }

        // ======================================================================

        private void Paint()
        {
            var arrive = Theme.Motion ? Math.Min(1f, _ticks / (float)RiseTicks) : 1f;
            arrive = 1f - (1f - arrive) * (1f - arrive);

            var title = _title.ToUpperInvariant();
            var hasLine = !string.IsNullOrEmpty(_line);

            var titleW = Hud.WidthTracked(title, TitleScale, Hud.FontLabel, TitleTrack);
            var titleH = Hud.Height(TitleScale, Hud.FontLabel);
            var lineW = hasLine ? Hud.Width(_line, LineScale, Hud.FontBody) : 0f;
            var lineH = hasLine ? Hud.Height(LineScale, Hud.FontBody) : 0f;

            var padX = Hud.ToX(PadY);
            var rail = Hud.ToX(RailW);

            var w = rail + padX + Math.Max(titleW, lineW) + padX;
            var h = PadY + titleH + (hasLine ? Gap + lineH : 0f) + PadY;

            float x, y;
            Where(w, h, out x, out y);

            // Up into place from a little below, the way the hint chip does.
            y += Climb * (1f - arrive);

            Theme.Card(x, y, w, h,
                       Palette.Alpha(Theme.Body, (int)(228f * arrive)),
                       Palette.Alpha(Palette.Brand, (int)(235f * arrive)), rail);

            var tx = x + rail + padX;
            var ty = y + PadY;

            // The weight under the name, then the name: the same lit lettering the shop heads use.
            Hud.TextTracked(title, tx + 0.0012f, ty + 0.0014f, TitleScale,
                            Palette.Alpha(Palette.BrandDeep, (int)(135f * arrive)),
                            Hud.FontLabel, TitleTrack, false);
            Hud.TextTracked(title, tx, ty, TitleScale,
                            Palette.Alpha(Palette.Text, (int)(250f * arrive)),
                            Hud.FontLabel, TitleTrack);

            if (!hasLine) return;

            Hud.Text(_line, tx, ty + titleH + Gap, LineScale,
                     Palette.Alpha(Palette.TextDim, (int)(225f * arrive)), Hud.FontBody);
        }

        /// <summary>Beside the pointer when there is one, flipping at the edges; else the fixed place.</summary>
        private static void Where(float w, float h, out float x, out float y)
        {
            float cx, cy;

            if (!Hud.OnPad && Cursor(out cx, out cy))
            {
                x = cx + Hud.ToX(LeadX);
                y = cy + LeadY;

                if (x + w > 0.985f) x = cx - w - Hud.ToX(LeadX * 0.75f);
                if (y + h > 0.940f) y = cy - h - LeadY * 0.6f;
                return;
            }

            x = FixedX;
            y = FixedY;
        }

        /// <summary>The pointer as fractions of the screen. False when the inputs read as nothing.</summary>
        private static bool Cursor(out float x, out float y)
        {
            x = 0f;
            y = 0f;

            try
            {
                x = Function.Call<float>(Hash.GET_DISABLED_CONTROL_NORMAL, 0, 239);
                y = Function.Call<float>(Hash.GET_DISABLED_CONTROL_NORMAL, 0, 240);

                if (x <= 0f && y <= 0f)
                {
                    x = Function.Call<float>(Hash.GET_DISABLED_CONTROL_NORMAL, 2, 239);
                    y = Function.Call<float>(Hash.GET_DISABLED_CONTROL_NORMAL, 2, 240);
                }

                return x > 0f || y > 0f;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>" - 850 m" or " - 2.3 km" from where he stands; nothing if that cannot be read.</summary>
        private static string Away(Vector3 at)
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return "";

                var m = me.Position.DistanceTo(at);

                return m < 1000f
                    ? " - " + ((int)Math.Round(m / 10f) * 10) + " m"
                    : " - " + (m / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " km";
            }
            catch
            {
                return "";
            }
        }
    }
}
