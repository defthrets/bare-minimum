using System;
using System.Collections.Generic;
using GTA;

using BareMinimum.Core;

namespace BareMinimum.Venues
{
    /// <summary>
    /// He says where he is, now and then, when he crosses into somewhere new.
    ///
    /// THE GAME ALREADY HAS A LINE FOR A HUNDRED AND ONE PLACES, in every protagonist's own
    /// voice -- LOCATION_CHAMBERLAIN_HILLS, LOCATION_SANDY_SHORES, LOCATION_VESPUCCI_BEACH and
    /// the rest. They were recorded for the friend-activity chatter and are otherwise never
    /// heard in single player. Nothing here is shipped audio; it is a name handed to the same
    /// native the eating and drinking lines go through.
    ///
    /// ON THE CROSSING, NOT ON A TIMER. A line about where you are only means anything at the
    /// moment where you are changes, so this watches the zone and speaks on the change -- and
    /// then only sometimes, because a man who announces every suburb he drives through is a
    /// satnav. [Speech] LocationChance is how often; the ordinary speech gap applies on top, so
    /// crossing four zones in a minute cannot get four lines out of him.
    ///
    /// THE NAME IS BUILT AND THEN CHECKED, never guessed. The zone's own display name uppercased
    /// with its spaces made underscores IS the speech name for every one of them -- "Chamberlain
    /// Hills" is LOCATION_CHAMBERLAIN_HILLS -- but a name the bank does not have plays nothing
    /// and reports nothing, so the built name is looked up in the list read out of the game's
    /// own speech file. Anywhere not on that list simply passes in silence.
    /// </summary>
    internal sealed class Whereabouts
    {
        /// <summary>How often the zone is asked for. It changes on the scale of streets, not frames.</summary>
        private const int EveryMs = 2000;

        /// <summary>
        /// How long he has to have been somewhere before it counts as having arrived.
        ///
        /// Zones meet along a line and driving the length of one puts you across it and back
        /// several times; without this, a road that runs the border is a man naming two
        /// suburbs at each other. He has to still be there a few seconds later.
        /// </summary>
        private const int SettleMs = 6000;

        private readonly Core.Settings _cfg;
        private readonly Food.Speech _speech;
        private readonly Random _rng = new Random();

        /// <summary>The names the game actually has, out of foods.json. Empty means say nothing.</summary>
        private readonly HashSet<string> _known =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Where he was last known to be, where he seems to be now, and since when.</summary>
        private string _settled = "";
        private string _pending = "";
        private int _pendingAt;
        private int _nextLook;

        public Whereabouts(Core.Settings cfg, Food.Speech speech)
        {
            _cfg = cfg;
            _speech = speech;
        }

        /// <summary>The line names the game has. Handed over by the catalogue with the rest of the speech.</summary>
        public void Load(string[] names)
        {
            _known.Clear();

            if (names == null) return;

            foreach (var n in names)
            {
                if (!string.IsNullOrEmpty(n)) _known.Add(n.Trim());
            }

            Log.Info("Whereabouts: " + _known.Count + " place(s) he has a line for.");
        }

        public void Update()
        {
            if (!_cfg.SpeechEnabled || !_cfg.SpeechLocations || _known.Count == 0) return;

            int now;
            try { now = Game.GameTime; }
            catch { return; }

            if (now < _nextLook) return;
            _nextLook = now + EveryMs;

            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists() || me.IsDead) return;

                var zone = World.GetZoneLocalizedName(me.Position);
                if (string.IsNullOrEmpty(zone) || zone == "NULL") return;

                zone = zone.Trim();

                // Somewhere new to what we were watching: start the clock again.
                if (!string.Equals(zone, _pending, StringComparison.OrdinalIgnoreCase))
                {
                    _pending = zone;
                    _pendingAt = now;
                    return;
                }

                if (now - _pendingAt < SettleMs) return;
                if (string.Equals(zone, _settled, StringComparison.OrdinalIgnoreCase)) return;

                // THE FIRST ONE IS NEVER SPOKEN. Loading a save puts him somewhere, and that is
                // not arriving anywhere -- it is the mod starting up.
                var first = _settled.Length == 0;
                _settled = zone;

                if (first) return;

                if (_rng.Next(100) >= Math.Max(0, Math.Min(100, _cfg.SpeechLocationChance))) return;

                var line = "LOCATION_" + zone.ToUpperInvariant().Replace(' ', '_');

                if (!_known.Contains(line))
                {
                    Log.Debug("No line for " + zone + " (" + line + ").");
                    return;
                }

                _speech.Line(line);
            }
            catch (Exception ex)
            {
                Log.Once("whereabouts", "Could not work out where he is: " + ex.Message);
            }
        }
    }
}
