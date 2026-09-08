using System;
using System.Drawing;
using GTA;
using GTA.Native;
using BareMinimum.Core;
using BareMinimum.UI;
using Hud = BareMinimum.UI.Draw;
using Icon = BareMinimum.UI.Icon;

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

            // WHERE THE MAP IS. The game's alignment maths for the left edge and the safe-zone
            // line, the radar's own height-based size for the rest -- see Layout.Map. The
            // gauge's minimap numbers are the fallback: GET_HUD_COMPONENT_POSITION(13) put the
            // map at four pixels from the edge on a screen where it stands at five hundred,
            // and the frame sat in the corner with nothing in it.
            float l, mapTop, r, mapBottom, safeLine;

            if (!Layout.Map(out l, out mapTop, out r, out mapBottom, out safeLine))
            {
                l = mapLeft;
                r = mapLeft + Layout.MapWide / aspect;
                safeLine = row.Foot + row.Breath + row.PlateH;
                mapBottom = safeLine - Layout.StockThick - Layout.StockGap;
                mapTop = safeLine - Layout.MapTall;
            }

            // The plate stands on the safe-zone line the map does -- and reaches down to the
            // bars' foot when the bars have been put lower than that, so the two bottoms stay
            // level, which is the whole point of the frame.
            var foot = Math.Max(safeLine, row.Foot + row.Breath + row.PlateH);
            foot = Math.Min(foot, 1f - 1f / Math.Max(720f, Ink.ScreenHeight));

            // OUTSIDE THE BLIPS. The game clamps a far-off blip to the edge of the map and half
            // of it pokes over; a frame flush to the map has that half under its line. The gap
            // puts the line clear of them, and a translucent mat fills the gap so the map's soft
            // edge is buried rather than showing the world through a seam.
            var gap = Math.Max(0f, cfg.MinimapFrameGap);
            var gapW = gap / aspect;

            var ink = Ink.Alpha(Color.FromArgb(228, 0, 0, 0), (int)(228f * cfg.HudOpacity * strength + 0.5f));
            var mat = Ink.Alpha(Color.FromArgb(120, 0, 0, 0), (int)(120f * cfg.HudOpacity * strength + 0.5f));

            // ON SCREEN, WHATEVER THE SAFE ZONE. With the safe zone at its widest the map sits
            // four pixels from the left edge, and a frame drawn outside it falls off the
            // screen. The gap on that side gives way first, then the line sits flush; the
            // right-hand side keeps its gap because it has the room.
            var leftGapW = Math.Min(gapW, Math.Max(0f, l - edge));
            var outerL = Math.Max(0f, l - leftGapW - edge);
            var outerR = r + gapW + edge;

            var topGap = Math.Min(gap, Math.Max(0f, mapTop - edgeH));

            // The plate: as tall as the bars' plates or as tall as a line of text, whichever is
            // more, standing on the bars' foot line and climbing into the foot of the map. It
            // carries the speed and the dash lights now, and the words when there is no band.
            var wantPlate = cfg.MinimapLabel || (cfg.MinimapFrame && (cfg.MinimapSpeedo || cfg.MinimapDash));
            var plateTop = wantPlate ? Math.Min(mapBottom, foot - Math.Max(LabelH, row.PlateH)) : mapBottom;

            // A HARD TOP EDGE. The radar fades out at the top and lets the world through; the
            // top band comes down over that so the map ends against the frame.
            var cover = mapTop + (mapBottom - mapTop) * Ink.Clamp(cfg.MinimapTopCover, 0f, 0.5f);
            if (cover > plateTop) cover = plateTop;

            if (cfg.MinimapLabel)
            {
                Hide(AreaName);
                Hide(StreetName);
                Names();
            }

            if (cfg.MinimapFrame)
            {
                var bandTop = mapTop - topGap - edgeH;

                Ink.Bar(outerL, bandTop, outerR - outerL, cover - bandTop, ink);
                Ink.Bar(outerL, cover, edge, plateTop - cover, ink);
                Ink.Bar(r + gapW, cover, edge, plateTop - cover, ink);

                // THE BAND: the compass in the middle, the street at the left, the suburb at the right.
                Band(cfg, l, r, outerL, outerR, bandTop, cover, edge + leftGapW, strength);

                if (leftGapW > 0f) Ink.Bar(l - leftGapW, cover, leftGapW, plateTop - cover, mat);
                if (gapW > 0f) Ink.Bar(r, cover, gapW, plateTop - cover, mat);
            }

            Ink.Bar(outerL, plateTop, outerR - outerL, foot - plateTop, ink);

            // THE PLATE: the dash lights at the left and the speed at the right, in a car -- or,
            // with no band to hold them, the words, where they used to be.
            if (cfg.MinimapFrame) Plate(cfg, outerL, outerR, plateTop, foot, edge + leftGapW, strength);
            else if (cfg.MinimapLabel) Words(cfg, outerL, outerR, plateTop, foot - plateTop, edge + gapW, strength);

            if (!cfg.MinimapLabel) return;

            if (!_measured)
            {
                _measured = true;
                Log.Info("Minimap frame: map " + (l * Ink.ScreenWidth).ToString("0") + ".." + (r * Ink.ScreenWidth).ToString("0") +
                         " x " + (mapTop * Ink.ScreenHeight).ToString("0") + ".." + (mapBottom * Ink.ScreenHeight).ToString("0") +
                         " px, safe line " + (safeLine * Ink.ScreenHeight).ToString("0") + ", foot " + (foot * Ink.ScreenHeight).ToString("0") +
                         " px, frame " + (outerL * Ink.ScreenWidth).ToString("0") + ".." + (outerR * Ink.ScreenWidth).ToString("0") +
                         ", plate " + ((foot - plateTop) * Ink.ScreenHeight).ToString("0") + " px tall, top band " +
                         ((cover - mapTop) * Ink.ScreenHeight).ToString("0") + " px, gap " + (gap * Ink.ScreenHeight).ToString("0.0") + " px; " +
                         "the game says the minimap is " + (Rendering() ? "rendering" : "NOT rendering") + ".");
            }
        }

        // ======================================================================
        // The band's corners
        // ======================================================================

        /// <summary>How wide the compass tape is, as a share of the map's width, and how many degrees it shows.</summary>
        private const float TapeShare = 0.36f;
        private const float TapeSpan = 120f;

        /// <summary>The rev bar's low colour: green, then amber, then red -- a tachometer's, not the bars' cold blue.</summary>
        private static readonly Color RevGreen = Color.FromArgb(255, 96, 200, 110);

        /// <summary>The rev bar: how many segments, and how wide each is as a fraction of screen height (squared up on screen).</summary>
        private const int RevSegments = 7;
        private const float RevSegment = 0.0036f;
        private const float RevGap = 0.0012f;

        private static readonly string[] Cardinals = { "N", "E", "S", "W" };

        /// <summary>The dashboard lights, held open; a CustomSprite keeps a texture handle.</summary>
        private readonly Icon _lamp = new Icon("dash_lamp.png");
        private readonly Icon _oil = new Icon("dash_oil.png");
        private readonly Icon _brake = new Icon("dash_brake.png");

        /// <summary>
        /// The top band: the compass in the middle, and the words either side of it -- the
        /// street at the left, the suburb at the right, each cut to the room it has. The
        /// compass is a tape, ticks every fifteen degrees and the cardinals scrolling under a
        /// pointer as you turn, and it reads from its centre, which is why it keeps the middle.
        ///
        /// THE SPEED AND THE DASH LIGHTS, WHICH HAD THE BAND'S CORNERS, ARE ON THE PLATE NOW,
        /// and the words, which had the plate, are up here. Asked for; and it puts the two
        /// things you read while driving together at the foot, nearest the bars.
        /// </summary>
        private void Band(Settings cfg, float l, float r, float outerL, float outerR, float top, float bottom, float pad, float strength)
        {
            if (!cfg.MinimapCompass && !cfg.MinimapLabel) return;

            var h = bottom - top;
            if (h < 0.012f) return;

            Ped me;
            try { me = Game.Player.Character; if (me == null || !me.Exists()) return; }
            catch { return; }

            var k = cfg.HudOpacity * strength;

            // A text scale that fits the band -- a step up from the plate's, on request -- shrunk
            // if the band is thinner than that needs.
            var scale = 0.235f;
            var th = Hud.Height(scale, Hud.FontLabel);
            if (th > h * 0.92f) { scale *= h * 0.92f / th; th = Hud.Height(scale, Hud.FontLabel); }

            var midY = top + h * 0.5f;

            Vehicle car = null;
            try { if (me.IsInVehicle()) car = me.CurrentVehicle; } catch { car = null; }

            // The compass's span, drawn or not: the words keep clear of it.
            var tapeW = (r - l) * TapeShare;
            var tapeL = (l + r) * 0.5f - tapeW * 0.5f;
            var tapeR = tapeL + tapeW;

            if (cfg.MinimapCompass)
            {
                Compass(me, car, tapeL, midY, tapeW, h, scale, k);
            }

            if (cfg.MinimapLabel)
            {
                // From the frame's outer edges, half its thickness in; up to a thickness short
                // of the compass, or to the middle when there is none.
                var inset = pad * 0.5f;
                var mid = (l + r) * 0.5f;
                var streetR = cfg.MinimapCompass ? tapeL - pad : mid - inset;
                var zoneL = cfg.MinimapCompass ? tapeR + pad : mid + inset;

                Label(outerL + inset, streetR, zoneL, outerR - inset, top, h, scale, k);
            }
        }

        /// <summary>
        /// The plate under the map: the dash lights at the left; the speed with its unit, gear
        /// and revs at the right. In a vehicle only -- on foot the plate is the bars' plate and
        /// nothing more.
        /// </summary>
        private void Plate(Settings cfg, float outerL, float outerR, float top, float bottom, float pad, float strength)
        {
            if (!cfg.MinimapSpeedo && !cfg.MinimapDash) return;

            var h = bottom - top;
            if (h < 0.012f) return;

            Vehicle car;
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists() || !me.IsInVehicle()) return;
                car = me.CurrentVehicle;
                if (car == null || !car.Exists()) return;
            }
            catch { return; }

            var k = cfg.HudOpacity * strength;

            var scale = 0.235f;
            var th = Hud.Height(scale, Hud.FontLabel);
            if (th > h * 0.92f) { scale *= h * 0.92f / th; th = Hud.Height(scale, Hud.FontLabel); }

            var midY = top + h * 0.5f;

            // Each as far into its corner as the frame goes: from the frame's own outer edge,
            // a quarter of its thickness in.
            if (cfg.MinimapSpeedo) Speedo(cfg, car, outerR - pad * 0.25f, midY, h, scale, th, k);
            if (cfg.MinimapDash) Dash(car, outerL + pad * 0.25f, midY, h, k);
        }

        /// <summary>The street, left-aligned in its room; the suburb, right-aligned in its, dimmer and a touch smaller. Each cut to fit.</summary>
        private void Label(float streetL, float streetR, float zoneL, float zoneR, float top, float h, float scale, float k)
        {
            var street = _street;
            var zone = _zone;

            var zoneScale = scale * 0.92f;
            var textH = Hud.Height(scale, Hud.FontLabel);
            var y = top + (h - textH) / 2f - 0.0015f;

            var streetRoom = streetR - streetL;
            if (!string.IsNullOrEmpty(street) && streetRoom > 0.004f)
            {
                street = Kit.Fit(street, streetRoom, scale, Hud.FontLabel);
                Hud.Text(street, streetL, y, scale,
                         Palette.Alpha(Palette.Text, (int)(240f * k)), Hud.FontLabel, false, false, false);
            }

            var zoneRoom = zoneR - zoneL;
            if (!string.IsNullOrEmpty(zone) && zoneRoom > 0.004f)
            {
                zone = Kit.Fit(zone, zoneRoom, zoneScale, Hud.FontLabel);
                Hud.Text(zone, zoneR, y + 0.001f, zoneScale,
                         Palette.Alpha(Palette.TextDim, (int)(215f * k)), Hud.FontLabel, false, true, false);
            }
        }

        /// <summary>
        /// The dashboard lights, left to right from the corner: oil, headlamp, handbrake.
        ///
        /// A DASH SHOWS ITS LIGHTS DIM, NOT ABSENT. Every symbol is there whenever you are in a
        /// car, faint, and comes up in colour when it has something to say: the oil can carries
        /// the engine's health, warming from its resting grey through amber to red as the
        /// engine and the body take damage, slowly, and flashing once the engine smokes; the
        /// lamp lights white when the headlights are on and blue-white on high beam; the
        /// handbrake is red for as long as it is pulled. A light that appears from nowhere is
        /// a fault the eye has to find; a light that changes colour is read in place.
        /// </summary>
        private void Dash(Vehicle car, float x, float midY, float h, float k)
        {
            var size = h * 0.74f;
            var wide = size / Ink.Aspect;
            // TIGHT, so the row hugs the frame's left end: a sliver of air between the lamps, no more.
            var step = wide * 1.12f;

            float engine, body;
            bool lights, beams, brake;

            try
            {
                engine = Ink.Clamp01(car.EngineHealth / 1000f);
                body = Ink.Clamp01(car.BodyHealth / 1000f);
                lights = car.AreLightsOn;
                beams = car.AreHighBeamsOn;
                brake = Game.IsControlPressed(Control.VehicleHandbrake);
            }
            catch
            {
                return;
            }

            var rest = Palette.Alpha(Color.FromArgb(255, 200, 205, 200), (int)(70f * k));

            // ---- oil, standing for the engine: grey at rest, red by the time the car is wrecked ----
            var hurt = 1f - Math.Min(engine, body);
            var oilTint = hurt < 0.05f
                ? rest
                : Ink.Alpha(Ink.Mix(Palette.Warn, Palette.Danger, Ink.Clamp01((hurt - 0.35f) / 0.5f)),
                            (int)((90f + 150f * Ink.Clamp01(hurt / 0.6f)) * k));

            // AND IT FLASHES ONCE THE ENGINE SMOKES. The game starts the smoke at two fifths of
            // engine health and thickens it at one fifth; the light blinks from there, quicker
            // as the smoke thickens, so the moment you would smell it is the moment it starts.
            // A sine rather than a switch, so it beats rather than stutters.
            if (engine < 0.4f)
            {
                var rate = engine < 0.2f ? 8.0 : 5.0;
                var beat = 0.45f + 0.55f * (float)Math.Abs(Math.Sin(Game.GameTime * 0.001 * rate));
                oilTint = Ink.Alpha(Ink.Mix(oilTint, Color.FromArgb(255, 255, 120, 100), 0.35f * beat),
                                    (int)(oilTint.A * (0.45f + 0.55f * beat)));
            }

            // ---- the lamp: lit when the lights are on ----
            var lampTint = !lights ? rest
                : beams ? Palette.Alpha(Color.FromArgb(255, 200, 232, 255), (int)(245f * k))
                        : Palette.Alpha(Color.FromArgb(255, 255, 246, 214), (int)(225f * k));

            // ---- handbrake: red while it is on ----
            var brakeTint = brake ? Palette.Alpha(Palette.Danger, (int)(240f * k)) : rest;

            var cx = x + wide * 0.5f;

            Lamp(_oil, cx, midY, wide, size, oilTint); cx += step;
            Lamp(_lamp, cx, midY, wide, size, lampTint); cx += step;
            Lamp(_brake, cx, midY, wide, size, brakeTint);
        }

        private static void Lamp(Icon icon, float cx, float cy, float w, float h, Color tint)
        {
            if (icon == null || icon.Missing) return;
            icon.DrawSized(cx, cy, w, h, tint);
        }

        /// <summary>The tape: ticks every fifteen degrees, letters at the cardinals, a pointer in the middle.</summary>
        private static void Compass(Ped me, Vehicle car, float x, float midY, float w, float h, float scale, float k)
        {
            float heading;
            try { heading = car != null && car.Exists() ? car.Heading : me.Heading; }
            catch { return; }

            // GTA's heading runs the wrong way for a compass: 0 is north and it grows to the
            // WEST. A bearing grows to the east.
            var bearing = (360f - heading) % 360f;
            if (bearing < 0f) bearing += 360f;

            var tickH = h * 0.26f;
            var tickW = Math.Max(1f / Ink.ScreenWidth, 0.0008f / Ink.Aspect);
            var letterScale = scale * 0.78f;
            var letterH = Hud.Height(letterScale, Hud.FontLabel);

            var tickY = midY + h * 0.5f - tickH - h * 0.06f;
            var letterY = midY - h * 0.5f + h * 0.02f;

            var ink = Palette.Alpha(Palette.Text, (int)(210f * k));
            var dim = Palette.Alpha(Palette.TextDim, (int)(150f * k));

            // Every fifteen degrees within the span, each at its place along the tape and
            // fading toward the ends so the tape has no hard edge.
            var first = (float)Math.Floor((bearing - TapeSpan / 2f) / 15f) * 15f;

            for (var b = first; b <= bearing + TapeSpan / 2f; b += 15f)
            {
                var off = b - bearing;
                var u = 0.5f + off / TapeSpan;
                if (u < 0.03f || u > 0.97f) continue;

                var fade = 1f - (float)Math.Pow(Math.Abs(u - 0.5f) * 2f, 2.2);
                var px = x + w * u;

                var abs = ((b % 360f) + 360f) % 360f;
                var cardinal = Math.Abs(abs % 90f) < 0.5f;
                var half = Math.Abs(abs % 45f) < 0.5f;

                var tall = cardinal ? tickH * 1.6f : half ? tickH * 1.2f : tickH;

                Ink.Bar(px - tickW / 2f, tickY + tickH - tall, tickW, tall,
                        Palette.Alpha(cardinal ? Palette.Text : Palette.TextDim, (int)((cardinal ? 200f : 130f) * fade * k)));

                if (!cardinal) continue;

                var letter = Cardinals[(int)Math.Round(abs / 90f) % 4];
                Hud.Text(letter, px, letterY, letterScale, Palette.Alpha(ink, (int)(ink.A * fade)), Hud.FontLabel, true, false, false);
            }

            // The pointer: where you are heading, in the bars' amber.
            var pw = Math.Max(1.5f / Ink.ScreenWidth, 0.0012f / Ink.Aspect);
            Ink.Bar(x + w / 2f - pw / 2f, midY - h * 0.5f + h * 0.08f, pw, h * 0.84f, Palette.Alpha(Palette.Brand, (int)(230f * k)));
        }

        /// <summary>
        /// From the corner inward: the gear at the number's size but the unit's faintness, the
        /// unit stood on end -- its letters stacked down the band -- the number, the rev bar.
        /// </summary>
        private static void Speedo(Settings cfg, Vehicle car, float right, float midY, float h, float scale, float th, float k)
        {
            float speed, rpm;
            int gear;
            try { speed = car.Speed; rpm = car.CurrentRPM; gear = car.CurrentGear; }
            catch { return; }

            var mph = string.Equals(cfg.MinimapSpeedUnits, "MPH", StringComparison.OrdinalIgnoreCase);
            var shown = (int)Math.Round(speed * (mph ? 2.23694f : 3.6f));
            var unit = mph ? "MPH" : "KPH";

            var numW = Hud.Width(shown.ToString(), scale, Hud.FontLabel);

            var gap = 0.0012f / Ink.Aspect;
            var y = midY - th * 0.5f - 0.001f;

            var dim = Palette.Alpha(Palette.TextDim, (int)(190f * k));
            var bright = Palette.Alpha(Palette.Text, (int)(240f * k));

            // THE GEAR, in the corner: the number's size, the unit's faintness -- a figure you
            // can read at a glance that does not compete with the speed. A number, or R backing up.
            var gearText = gear <= 0 ? "R" : gear.ToString();
            var x = right - Hud.Width(gearText, scale, Hud.FontLabel);
            Hud.Text(gearText, x, y, scale, dim, Hud.FontLabel, false, false, false);

            // THE UNIT, STOOD ON END: its letters stacked down the band beside the number, one
            // to a third of the height, centred in their column, in the same faint face. Small,
            // but it is a label, not a reading -- the number does the talking. Sized off the
            // band so the three always fit it exactly.
            var cell = th / unit.Length;
            var lineH = Hud.Height(1f, Hud.FontLabel);
            // The line box overruns its cell by a quarter -- a glyph is about two thirds of its
            // line, so the letters still clear each other -- which is the "slightly bigger" asked for.
            var letterScale = lineH > 0.0001f ? Math.Max(0.06f, cell * 1.25f / lineH) : scale * 0.5f;
            var letterH = Hud.Height(letterScale, Hud.FontLabel);

            var colW = 0f;
            for (var i = 0; i < unit.Length; i++)
            {
                colW = Math.Max(colW, Hud.Width(unit.Substring(i, 1), letterScale, Hud.FontLabel));
            }

            x -= gap * 3f + colW;

            for (var i = 0; i < unit.Length; i++)
            {
                var letter = unit.Substring(i, 1);
                var lx = x + (colW - Hud.Width(letter, letterScale, Hud.FontLabel)) * 0.5f;
                var ly = y + i * cell + (cell - letterH) * 0.5f;
                Hud.Text(letter, lx, ly, letterScale, dim, Hud.FontLabel, false, false, false);
            }

            // THE NUMBER, bright, before the unit.
            x -= gap + numW;
            Hud.Text(shown.ToString(), x, y, scale, bright, Hud.FontLabel, false, false, false);

            // ---- the revs ----
            var segW = RevSegment / Ink.Aspect;
            var segGap = RevGap / Ink.Aspect;
            var barW = RevSegments * segW + (RevSegments - 1) * segGap;
            var segH = h * 0.34f;
            var segY = midY - segH * 0.5f;

            x -= gap * 3f + barW;

            rpm = Ink.Clamp01(rpm);

            for (var i = 0; i < RevSegments; i++)
            {
                var lit = rpm >= (i + 0.35f) / RevSegments;
                var sx = x + i * (segW + segGap);

                Color c;
                if (!lit) c = Color.FromArgb((int)(70f * k), 200, 205, 200);
                else if (i >= RevSegments - 1) c = Palette.Alpha(Palette.Danger, (int)(235f * k));
                else if (i >= RevSegments - 3) c = Palette.Alpha(Palette.Warn, (int)(230f * k));
                else c = Palette.Alpha(RevGreen, (int)(220f * k));

                Ink.Bar(sx, segY, segW, segH, c);
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

        /// <summary>Whether the game is drawing its minimap at all. For the log: a blank radar is not this mod's rectangle.</summary>
        private static bool Rendering()
        {
            try { return Function.Call<bool>(Hash.IS_MINIMAP_RENDERING); }
            catch { return true; }
        }

        private static void Hide(int component)
        {
            try { Function.Call(Hash.HIDE_HUD_COMPONENT_THIS_FRAME, component); }
            catch { /* then the game says it too */ }
        }

    }
}
