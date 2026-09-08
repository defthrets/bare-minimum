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

            var ink = Ink.Alpha(Color.FromArgb(205, 0, 0, 0), (int)(205f * cfg.HudOpacity * strength + 0.5f));
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
            // more, standing on the bars' foot line and climbing into the foot of the map.
            var plateTop = cfg.MinimapLabel ? Math.Min(mapBottom, foot - Math.Max(LabelH, row.PlateH)) : mapBottom;

            // A HARD TOP EDGE. The radar fades out at the top and lets the world through; the
            // top band comes down over that so the map ends against the frame.
            var cover = mapTop + (mapBottom - mapTop) * Ink.Clamp(cfg.MinimapTopCover, 0f, 0.5f);
            if (cover > plateTop) cover = plateTop;

            if (cfg.MinimapFrame)
            {
                var bandTop = mapTop - topGap - edgeH;

                Ink.Bar(outerL, bandTop, outerR - outerL, cover - bandTop, ink);
                Ink.Bar(outerL, cover, edge, plateTop - cover, ink);
                Ink.Bar(r + gapW, cover, edge, plateTop - cover, ink);

                // THE CORNERS OF THE BAND: a compass at the left, the speed and revs at the right.
                Corners(cfg, l, r, bandTop, cover, edge + leftGapW, strength);

                if (leftGapW > 0f) Ink.Bar(l - leftGapW, cover, leftGapW, plateTop - cover, mat);
                if (gapW > 0f) Ink.Bar(r, cover, gapW, plateTop - cover, mat);
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

        /// <summary>The rev bar: how many segments, and how wide each is as a fraction of screen height (squared up on screen).</summary>
        private const int RevSegments = 7;
        private const float RevSegment = 0.0036f;
        private const float RevGap = 0.0012f;

        private static readonly string[] Cardinals = { "N", "E", "S", "W" };

        /// <summary>The dashboard lights, held open; a CustomSprite keeps a texture handle.</summary>
        private readonly Icon _engine = new Icon("dash_engine.png");
        private readonly Icon _lamp = new Icon("dash_lamp.png");
        private readonly Icon _oil = new Icon("dash_oil.png");
        private readonly Icon _brake = new Icon("dash_brake.png");

        /// <summary>
        /// The compass in the middle of the band and the speedo at the right, in the band the top
        /// of the frame makes. Nothing if the band is too thin to hold a line of text: TopCover
        /// at nought is a plain line, and a plain line has no corners worth writing in.
        ///
        /// THE COMPASS IS A TAPE, NOT A LETTER. A letter says "NW" and changes eight times a
        /// circle; a tape of ticks and letters scrolling under a fixed pointer moves with every
        /// degree, which is what "telling what direction you are going" looks like on a dash.
        /// The bearing is the thing you are IN: the car's when driving, yours on foot.
        ///
        /// THE SPEED IS IN A VEHICLE ONLY. A man walking at 6 KPH is not information, and a
        /// zero standing still is a fault light. The revs beside it are seven segments that
        /// light with the engine, the last two warm and the last red, so the redline reads
        /// without a number.
        /// </summary>
        private void Corners(Settings cfg, float l, float r, float top, float bottom, float pad, float strength)
        {
            if (!cfg.MinimapCompass && !cfg.MinimapSpeedo && !cfg.MinimapDash) return;

            var h = bottom - top;
            if (h < 0.012f) return;

            Ped me;
            try { me = Game.Player.Character; if (me == null || !me.Exists()) return; }
            catch { return; }

            var k = cfg.HudOpacity * strength;
            var padX = pad * 1.5f;

            // A text scale that fits the band -- a step up from the plate's, on request -- shrunk
            // if the band is thinner than that needs.
            var scale = 0.235f;
            var th = Hud.Height(scale, Hud.FontLabel);
            if (th > h * 0.92f) { scale *= h * 0.92f / th; th = Hud.Height(scale, Hud.FontLabel); }

            var midY = top + h * 0.5f;

            Vehicle car = null;
            try { if (me.IsInVehicle()) car = me.CurrentVehicle; } catch { car = null; }

            if (cfg.MinimapCompass)
            {
                // IN THE MIDDLE OF THE BAND, not the corner: a compass reads from its centre
                // pointer, and the middle of the map's top edge is where a pointer for "the way
                // you are facing" belongs. The speedo keeps the right-hand corner.
                var tapeW = (r - l) * TapeShare;
                Compass(me, car, (l + r) * 0.5f - tapeW * 0.5f, midY, tapeW, h, scale, k);
            }

            if (cfg.MinimapSpeedo && car != null && car.Exists())
            {
                Speedo(cfg, car, r - padX, midY, h, scale, th, k);
            }

            if (cfg.MinimapDash && car != null && car.Exists())
            {
                // AS FAR LEFT AS THE FRAME GOES: from the frame's own outer edge, not the map's,
                // with a quarter of the frame's thickness of breathing room. Asked for.
                var farLeft = Math.Max(0f, l - pad) + pad * 0.25f;
                Dash(car, farLeft, midY, h, k);
            }
        }

        /// <summary>
        /// The dashboard lights, left to right from the corner: engine, headlamp, oil, handbrake.
        ///
        /// A DASH SHOWS ITS LIGHTS DIM, NOT ABSENT. Every symbol is there whenever you are in a
        /// car, faint, and comes up in colour when it has something to say: the engine warms
        /// from its resting grey through amber to red as the engine and the body take damage,
        /// slowly, the way it was asked for; the lamp lights white when the headlights are on
        /// and blue-white on high beam; the oil comes up amber when the engine is down to two
        /// fifths and red when it is nearly gone or the oil has run out; the handbrake is red
        /// for as long as it is pulled. A light that appears from nowhere is a fault the eye has
        /// to find; a light that changes colour is read in place.
        /// </summary>
        private void Dash(Vehicle car, float x, float midY, float h, float k)
        {
            var size = h * 0.74f;
            var wide = size / Ink.Aspect;
            var step = wide * 1.45f;

            float engine, body, oil;
            bool lights, beams, brake;

            try
            {
                engine = Ink.Clamp01(car.EngineHealth / 1000f);
                body = Ink.Clamp01(car.BodyHealth / 1000f);
                oil = car.OilLevel;
                lights = car.AreLightsOn;
                beams = car.AreHighBeamsOn;
                brake = Game.IsControlPressed(Control.VehicleHandbrake);
            }
            catch
            {
                return;
            }

            var rest = Palette.Alpha(Color.FromArgb(255, 200, 205, 200), (int)(70f * k));

            // ---- engine: grey at rest, red by the time the car is wrecked ----
            var hurt = 1f - Math.Min(engine, body);
            var engineTint = hurt < 0.05f
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
                engineTint = Ink.Alpha(Ink.Mix(engineTint, Color.FromArgb(255, 255, 120, 100), 0.35f * beat),
                                       (int)(engineTint.A * (0.45f + 0.55f * beat)));
            }

            // ---- the lamp: lit when the lights are on ----
            var lampTint = !lights ? rest
                : beams ? Palette.Alpha(Color.FromArgb(255, 200, 232, 255), (int)(245f * k))
                        : Palette.Alpha(Color.FromArgb(255, 255, 246, 214), (int)(225f * k));

            // ---- oil: pressure goes with the engine, and out with the oil ----
            var oilTint = rest;
            if (oil <= 0.05f || engine < 0.2f) oilTint = Palette.Alpha(Palette.Danger, (int)(235f * k));
            else if (engine < 0.4f) oilTint = Palette.Alpha(Palette.Warn, (int)(225f * k));

            // ---- handbrake: red while it is on ----
            var brakeTint = brake ? Palette.Alpha(Palette.Danger, (int)(240f * k)) : rest;

            var cx = x + wide * 0.5f;

            Lamp(_engine, cx, midY, wide, size, engineTint); cx += step;
            Lamp(_lamp, cx, midY, wide, size, lampTint); cx += step;
            Lamp(_oil, cx, midY, wide, size, oilTint); cx += step;
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

        /// <summary>The number right-aligned to the corner, the unit small and dim after it, the rev bar before it.</summary>
        private static void Speedo(Settings cfg, Vehicle car, float right, float midY, float h, float scale, float th, float k)
        {
            float speed, rpm;
            try { speed = car.Speed; rpm = car.CurrentRPM; }
            catch { return; }

            var mph = string.Equals(cfg.MinimapSpeedUnits, "MPH", StringComparison.OrdinalIgnoreCase);
            var shown = (int)Math.Round(speed * (mph ? 2.23694f : 3.6f));
            var unit = mph ? "MPH" : "KPH";

            var unitScale = scale * 0.62f;
            var unitW = Hud.Width(unit, unitScale, Hud.FontLabel);
            var numW = Hud.Width(shown.ToString(), scale, Hud.FontLabel);

            var gap = 0.0012f / Ink.Aspect;
            var y = midY - th * 0.5f - 0.001f;

            // Unit, then number, then the revs, each to the left of the last.
            var x = right - unitW;
            Hud.Text(unit, x, y + (th - Hud.Height(unitScale, Hud.FontLabel)) * 0.85f, unitScale,
                     Palette.Alpha(Palette.TextDim, (int)(190f * k)), Hud.FontLabel, false, false, false);

            x -= gap + numW;
            Hud.Text(shown.ToString(), x, y, scale, Palette.Alpha(Palette.Text, (int)(240f * k)), Hud.FontLabel, false, false, false);

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
                else c = Palette.Alpha(Palette.Cold, (int)(220f * k));

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
