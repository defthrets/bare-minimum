using System;
using System.Collections.Generic;
using System.Globalization;
using GTA;
using GTA.Native;
using BareMinimum.Core;

namespace BareMinimum.Venues
{
    /// <summary>
    /// Writes down where you are standing, in the shape vendors.json wants.
    ///
    /// WHY THIS EXISTS. Every one of the fifty-odd places in vendors.json was added the same
    /// way: walk to a door, read the coordinates off a HUD belonging to another mod entirely,
    /// screenshot it, and type the numbers back in by hand. That is fine for one door. There
    /// are twenty-three petrol stations with no shop on them, and a lap of those is
    /// ninety-two numbers transcribed off screenshots -- which is both a tedious afternoon and
    /// a job with a silent failure mode, because a mistyped digit does not look wrong, it just
    /// puts a shop through a wall.
    ///
    /// COORDINATES ARE NOT AVAILABLE ANY OTHER WAY. A station's position can be had from the
    /// fuel mod's own list, but that is the forecourt: at both stations where the two can be
    /// compared, the shop door is about twenty-five metres from it, in a direction that
    /// differs per station. There is no list of doors, and guessing one is worse than not
    /// having it.
    ///
    /// It writes a FILE rather than only a log line, so the entries come back as text that can
    /// be pasted or handed over whole, rather than read out of a screenshot a second time.
    /// </summary>
    internal sealed class Capture
    {
        private readonly Core.Settings _cfg;

        private bool _down;
        private int _quietUntil;

        private readonly List<string> _taken = new List<string>();

        public Capture(Core.Settings cfg)
        {
            _cfg = cfg;
        }

        public void Update()
        {
            if (!Pressed()) return;

            try { Take(); }
            catch (Exception ex)
            {
                Log.Once("capture", "Could not write the spot down: " + ex.Message);
            }
        }

        private bool Pressed()
        {
            var down = false;

            try { down = Game.IsKeyPressed(_cfg.CaptureKey); }
            catch { /* a key that cannot be read is a key that is not pressed */ }

            var edge = down && !_down;
            _down = down;

            if (!edge) return false;
            if (Game.GameTime < _quietUntil) return false;

            _quietUntil = Game.GameTime + 250;
            return true;
        }

        private void Take()
        {
            var me = Game.Player.Character;
            if (me == null || !me.Exists()) return;

            var p = me.Position;

            // The heading you are FACING, which is what a vendor entry wants: it is the way a
            // seller looks out of the hatch, and it is read off the player because the player
            // is the one standing where the customer stands.
            var heading = me.Heading;

            var block =
                "    {\n" +
                "      \"_note\": \"" + Where(p) + "\",\n" +
                "      \"id\": \"\",\n" +
                "      \"name\": \"\",\n" +
                "      \"label\": \"\",\n" +
                "      \"item\": \"\",\n" +
                "      \"x\": " + N(p.X) + ",\n" +
                "      \"y\": " + N(p.Y) + ",\n" +
                "      \"z\": " + N(p.Z) + ",\n" +
                "      \"heading\": " + N(heading) + ",\n" +
                "      \"spawnRange\": 90.0,\n" +
                "      \"reach\": 2.4,\n" +
                "      \"blip\": true,\n" +
                "      \"blipSprite\": 52,\n" +
                "      \"blipColour\": 5\n" +
                "    }";

            _taken.Add(block);

            // REWRITTEN WHOLE EVERY TIME rather than appended to. An append has to know where
            // the closing bracket is and put itself before it, which is a parser; rewriting a
            // list this short is free and cannot corrupt the file.
            var doc = "[\n" + string.Join(",\n", _taken.ToArray()) + "\n]\n";

            System.IO.File.WriteAllText(Paths.CapturedFile, doc);

            Log.Info("Captured " + _taken.Count + ": " + Where(p) + "  " +
                     N(p.X) + ", " + N(p.Y) + ", " + N(p.Z) + "  heading " + N(heading));

            Notify("~g~Spot " + _taken.Count + " written~s~ - " + Where(p));
        }

        /// <summary>
        /// The neighbourhood, for the note.
        ///
        /// THE ZONE RATHER THAN THE STREET. GET_STREET_NAME_AT_COORD returns its answer
        /// through two out-parameters, which in this codebase means an unsafe block and the
        /// build turning on /unsafe for one label -- and a note saying "Sandy Shores" is worth
        /// about as much as one saying "Alhambra Drive" when the point is to find the entry
        /// again in a list.
        /// </summary>
        private static string Where(GTA.Math.Vector3 p)
        {
            try
            {
                var zone = World.GetZoneLocalizedName(p);
                return string.IsNullOrEmpty(zone) ? "somewhere" : zone;
            }
            catch { return "somewhere"; }
        }

        private static string N(float v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static void Notify(string text)
        {
            try
            {
                Function.Call(Hash.BEGIN_TEXT_COMMAND_THEFEED_POST, "STRING");
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
                Function.Call(Hash.END_TEXT_COMMAND_THEFEED_POST_TICKER, false, true);
            }
            catch { /* the log still has it */ }
        }
    }
}
