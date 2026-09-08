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
    /// THE MAP IS WHERE THE GAUGE SAYS IT IS -- the same left edge and measured width that
    /// place the bars, and the same foot line -- so the frame ends where the bars begin on any
    /// screen. The plate has to be tall enough to read, which is taller than the strip's slot
    /// was, so it climbs into the foot of the map by the difference rather than below the
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

        /// <summary>The game's own HUD components for the area and street names.</summary>
        private const int AreaName = 7;
        private const int StreetName = 9;

        private string _street = "";
        private string _zone = "";
        private int _namesAt;
        private bool _measured;

        /// <summary>
        /// Draws the frame and the plate. <paramref name="row"/> is the gauge's row as it stands,
        /// <paramref name="mapLeft"/> and <paramref name="mapWidth"/> the minimap as the gauge
        /// measures it.
        ///
        /// THE MAP FROM THE GAUGE'S OWN NUMBERS: its left edge asked of the game, its width the
        /// measured 0.2785 of the screen's height -- the same two that put the bars where they
        /// are, so the frame ends where the bars begin. The first cut took the map from the
        /// alignment box the strip uses, which comes out a third too wide on 21:9, and the plate
        /// ran under the bars with the suburb written across their marks.
        ///
        /// The line the bars' plates end on is the line the map stands on, and the edge is the
        /// bars' edge -- both read off the row rather than recomputed, so a change to either
        /// moves the frame with them.
        /// </summary>
        public void Draw(Settings cfg, Gauge.Row row, float mapLeft, float mapWidth, float strength)
        {
            if (!cfg.MinimapFrame && !cfg.MinimapLabel) return;
            if (row == null || mapWidth <= 0.001f) return;

            // NO BIG-MAP CHECK, ON PURPOSE. IS_BIGMAP_ACTIVE is not in the vendored 3.6.0 enum,
            // and calling it by hash crashed the game: ScriptHookV has no entry for that hash,
            // and a native it cannot find is a FATAL, not an exception -- no try/catch sees it.

            var aspect = Ink.Aspect;
            var edge = row.Edge;
            var edgeH = edge * aspect;

            var l = mapLeft;
            var r = mapLeft + mapWidth;

            var foot = row.Foot + row.Breath + row.PlateH;
            var mapBottom = foot - Layout.StockThick - Layout.StockGap;
            var mapTop = foot - Layout.MapTall;

            // OUTSIDE THE BLIPS. The game clamps a far-off blip to the edge of the map and half
            // of it pokes over; a frame flush to the map has that half under its line. The gap
            // puts the line clear of them, and a translucent mat fills the gap so the map's soft
            // edge is buried rather than showing the world through a seam.
            var gap = Math.Max(0f, cfg.MinimapFrameGap);
            var gapW = gap / aspect;

            var ink = Ink.Alpha(Color.FromArgb(205, 0, 0, 0), (int)(205f * cfg.HudOpacity * strength + 0.5f));
            var mat = Ink.Alpha(Color.FromArgb(120, 0, 0, 0), (int)(120f * cfg.HudOpacity * strength + 0.5f));

            var outerL = l - gapW - edge;
            var outerR = r + gapW + edge;

            // The plate: as tall as the bars' plates or as tall as a line of text, whichever is
            // more, standing on the bars' foot line and climbing into the foot of the map.
            var plateTop = cfg.MinimapLabel ? Math.Min(mapBottom, foot - Math.Max(LabelH, row.PlateH)) : mapBottom;

            // A HARD TOP EDGE. The radar fades out at the top and lets the world through; the
            // top band comes down over that so the map ends against the frame.
            var cover = mapTop + (mapBottom - mapTop) * Ink.Clamp(cfg.MinimapTopCover, 0f, 0.5f);
            if (cover > plateTop) cover = plateTop;

            if (cfg.MinimapFrame)
            {
                Ink.Bar(outerL, mapTop - gap - edgeH, outerR - outerL, (cover - mapTop) + gap + edgeH, ink);
                Ink.Bar(outerL, cover, edge, plateTop - cover, ink);
                Ink.Bar(r + gapW, cover, edge, plateTop - cover, ink);

                if (gapW > 0f)
                {
                    Ink.Bar(l - gapW, cover, gapW, plateTop - cover, mat);
                    Ink.Bar(r, cover, gapW, plateTop - cover, mat);
                }
            }

            Ink.Bar(outerL, plateTop, outerR - outerL, foot - plateTop, ink);

            if (!cfg.MinimapLabel) return;

            Hide(AreaName);
            Hide(StreetName);

            Names();
            Words(cfg, outerL, outerR, plateTop, foot - plateTop, edge + gapW, strength);

            if (!_measured)
            {
                _measured = true;
                Log.Info("Minimap frame: map " + (l * Ink.ScreenWidth).ToString("0") + ".." + (r * Ink.ScreenWidth).ToString("0") +
                         " x " + (mapTop * Ink.ScreenHeight).ToString("0") + ".." + (mapBottom * Ink.ScreenHeight).ToString("0") +
                         " px, frame " + (outerL * Ink.ScreenWidth).ToString("0") + ".." + (outerR * Ink.ScreenWidth).ToString("0") +
                         ", plate " + ((foot - plateTop) * Ink.ScreenHeight).ToString("0") + " px tall, top band " +
                         ((cover - mapTop) * Ink.ScreenHeight).ToString("0") + " px, gap " + (gap * Ink.ScreenHeight).ToString("0.0") + " px.");
            }
        }

        /// <summary>The street on the left, the suburb on the right, on one line in the plate.</summary>
        private void Words(Settings cfg, float l, float r, float top, float h, float edge, float strength)
        {
            // <paramref name="edge"/> here is the frame's full thickness, line and gap; the words
            // sit two of those in from the plate's ends.
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

    }
}
