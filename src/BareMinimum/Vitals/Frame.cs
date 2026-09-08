using System;
using System.Drawing;
using GTA;
using GTA.Native;
using BareMinimum.Core;
using BareMinimum.UI;
using Hud = BareMinimum.UI.Draw;

namespace BareMinimum.Vitals
{
    /// <summary>
    /// A frame round the minimap in the bars' own black, and the street and suburb written
    /// into its foot.
    ///
    /// TIED TO THE ROW. The edge is the bars' surround -- the same width, the same alpha, at
    /// the same opacity -- so the map and the five columns beside it read as one instrument
    /// rather than as a map with some bars near it. Where the game's own health strip used to
    /// sit under the map there is now a plate, again the bars' plate, and the plate says where
    /// you are: the street on the left, the suburb on the right, always, not only for the
    /// three seconds the game shows them when you get into a car.
    ///
    /// THE MAP IS ASKED WHERE IT IS, through the same alignment maths the strip uses -- see
    /// Layout.Map -- so the frame lands on the map on any screen and any safe-zone setting.
    /// The plate has to be tall enough to read, which is taller than the strip's slot was, so
    /// it climbs into the foot of the map by the difference rather than below the safe-zone
    /// line: the bars' plates end on that line and the frame keeps to it.
    ///
    /// The game's own area and street names are hidden while the label is on; the same words
    /// twice, once in the corner and once above the corner, is a stutter.
    /// </summary>
    internal sealed class Frame
    {
        /// <summary>How often the street and suburb are looked up. They do not change faster than this.</summary>
        private const int NamesEveryMs = 400;

        /// <summary>The label plate's height, as a fraction of screen height. Enough for one line.</summary>
        private const float LabelH = 0.021f;

        private const float StreetScale = 0.245f;
        private const float ZoneScale = 0.225f;

        /// <summary>IS_BIGMAP_ACTIVE, by hash: the vendored 3.6.0 enum has only the setter.</summary>
        private static readonly Hash IsBigMapActive = (Hash)0xFFF65C63UL;

        /// <summary>The game's own HUD components for the area and street names.</summary>
        private const int AreaName = 7;
        private const int StreetName = 9;

        private string _street = "";
        private string _zone = "";
        private int _namesAt;
        private bool _measured;

        public void Draw(Settings cfg, float strength)
        {
            if (!cfg.MinimapFrame && !cfg.MinimapLabel) return;
            if (BigMap()) return;

            float l, t, r, b, safe;
            if (!Layout.Map(out l, out t, out r, out b, out safe)) return;

            var aspect = Ink.Aspect;

            // THE BARS' EDGE, from the same expression Gauge uses for the surround.
            var edge = Math.Max(0.0005f, cfg.HudBarWidth * 0.22f);
            var edgeH = edge * aspect;

            var ink = Ink.Alpha(Color.FromArgb(205, 0, 0, 0), (int)(205f * cfg.HudOpacity * strength + 0.5f));

            // The plate: the strip's old slot under the map, made tall enough to read and
            // climbing into the foot of the map by the difference. Without the label it is the
            // slot alone, filled, so the frame closes under the map.
            var plateTop = cfg.MinimapLabel ? Math.Min(b, safe - LabelH) : b;

            if (cfg.MinimapFrame)
            {
                Ink.Bar(l - edge, t - edgeH, (r - l) + edge * 2f, edgeH, ink);    // the top, corners included
                Ink.Bar(l - edge, t, edge, plateTop - t, ink);                     // the left side
                Ink.Bar(r, t, edge, plateTop - t, ink);                            // the right side
            }

            Ink.Bar(l - edge, plateTop, (r - l) + edge * 2f, safe - plateTop, ink);

            if (!cfg.MinimapLabel) return;

            Hide(AreaName);
            Hide(StreetName);

            Names();
            Words(cfg, l, r, plateTop, safe - plateTop, edge, strength);

            if (!_measured)
            {
                _measured = true;
                Log.Info("Minimap frame: map " + (l * Ink.ScreenWidth).ToString("0") + ".." + (r * Ink.ScreenWidth).ToString("0") +
                         " x " + (t * Ink.ScreenHeight).ToString("0") + ".." + (b * Ink.ScreenHeight).ToString("0") +
                         " px, label plate " + ((safe - plateTop) * Ink.ScreenHeight).ToString("0") + " px tall.");
            }
        }

        /// <summary>The street on the left, the suburb on the right, on one line in the plate.</summary>
        private void Words(Settings cfg, float l, float r, float top, float h, float edge, float strength)
        {
            var padX = edge * 2f;
            var k = cfg.HudOpacity * strength;

            var street = _street;
            var zone = _zone;

            var zoneW = string.IsNullOrEmpty(zone) ? 0f : Hud.Width(zone, ZoneScale, Hud.FontLabel);
            var room = (r - l) - padX * 2f - (zoneW > 0f ? zoneW + padX : 0f);

            if (!string.IsNullOrEmpty(street) && room > 0f)
            {
                street = Kit.Fit(street, room, StreetScale, Hud.FontLabel);
            }

            var textH = Hud.Height(StreetScale, Hud.FontLabel);
            var y = top + (h - textH) / 2f - 0.0015f;

            if (!string.IsNullOrEmpty(street))
            {
                Hud.Text(street, l + padX, y, StreetScale,
                         Palette.Alpha(Palette.Text, (int)(240f * k)), Hud.FontLabel, false, false, false);
            }

            if (zoneW > 0f)
            {
                Hud.Text(zone, r - padX, y + 0.001f, ZoneScale,
                         Palette.Alpha(Palette.TextDim, (int)(215f * k)), Hud.FontLabel, false, true, false);
            }
        }

        /// <summary>Where he is, looked up a few times a second rather than every frame.</summary>
        private void Names()
        {
            var now = Game.GameTime;
            if (now < _namesAt) return;
            _namesAt = now + NamesEveryMs;

            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                var at = me.Position;

                var street = World.GetStreetName(at);
                var zone = World.GetZoneLocalizedName(at);

                _street = string.IsNullOrEmpty(street) ? "" : street.Trim();
                _zone = string.IsNullOrEmpty(zone) || zone == "NULL" ? "" : zone.Trim();
            }
            catch (Exception ex)
            {
                Log.Once("minimap-names", "Could not read the street and suburb: " + ex.Message);
            }
        }

        private static void Hide(int component)
        {
            try { Function.Call(Hash.HIDE_HUD_COMPONENT_THIS_FRAME, component); }
            catch { /* then the game says it too */ }
        }

        private static bool BigMap()
        {
            try { return Function.Call<bool>(IsBigMapActive); }
            catch { return false; }
        }
    }
}
