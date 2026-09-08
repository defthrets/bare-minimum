using System;
using GTA;
using GTA.Native;

namespace BareMinimum.Core
{
    /// <summary>
    /// The game's own clock, read through the natives underneath it.
    /// </summary>
    ///
    /// <remarks>
    /// THIS EXISTS SO THE MOD LOADS ON SCRIPTHOOKVDOTNET 3.6. It used GTA.Chrono.GameClock,
    /// which is a fine API and does not exist before 3.7 -- and the cost of that is not a
    /// missing feature. SHVDN resolves a script's references in its own AppDomain and DECLINES
    /// one newer than itself, so a dll compiled against 3.9 does not load on 3.6 or on a 3.7
    /// nightly AT ALL: "Could not load file or assembly", and nothing in the mod runs.
    ///
    /// Fumes shipped four releases claiming 3.6 support with the same fault, and every report
    /// that it worked came from somebody on the 3.9 fork. Not learned twice.
    ///
    /// THE NATIVES ARE THE ONE THING THAT DOES NOT MOVE. World.CurrentDate would also work on
    /// both, and is obsolete on 3.9 -- which is what pushed this code to GameClock in the first
    /// place, and would push it somewhere else again next year. GET_CLOCK_HOURS has been
    /// GET_CLOCK_HOURS since 2013.
    ///
    /// MINUTES SINCE AN EPOCH, NOT A DATE TYPE. Every caller wants one of two things: what hour
    /// is it, and how much game time has passed since I last looked. A long of minutes answers
    /// the second with a subtraction and needs no type of its own on either side of the bridge.
    /// </remarks>
    internal static class Clock
    {
        /// <summary>
        /// The last reading that made sense, handed back when one does not.
        ///
        /// A clock that cannot be read must not look like a clock that has jumped: the needs
        /// step on the DIFFERENCE, so a zero here would drain a man by two thousand years the
        /// first time a native hiccupped.
        /// </summary>
        private static long _last;

        private static bool _ever;

        /// <summary>Game-clock minutes since year zero. Only differences mean anything.</summary>
        public static long Minutes
        {
            get
            {
                try
                {
                    var y = Function.Call<int>(Hash.GET_CLOCK_YEAR);
                    var mo = Function.Call<int>(Hash.GET_CLOCK_MONTH);        // 0-11, not 1-12
                    var d = Function.Call<int>(Hash.GET_CLOCK_DAY_OF_MONTH);
                    var h = Function.Call<int>(Hash.GET_CLOCK_HOURS);
                    var mi = Function.Call<int>(Hash.GET_CLOCK_MINUTES);

                    if (y < 1 || y > 9998 || mo < 0 || mo > 11 || d < 1 ||
                        h < 0 || h > 23 || mi < 0 || mi > 59)
                    {
                        return _last;
                    }

                    // CLAMPED RATHER THAN TRUSTED. The day and the month are read separately
                    // and a mission can set either; the thirty-first of February throws out of
                    // a DateTime, and a throw here is a mod that stops eating.
                    var days = DateTime.DaysInMonth(y, mo + 1);
                    if (d > days) d = days;

                    _last = new DateTime(y, mo + 1, d, h, mi, 0).Ticks / TimeSpan.TicksPerMinute;
                    _ever = true;

                    return _last;
                }
                catch
                {
                    return _last;
                }
            }
        }

        /// <summary>Whether the clock has ever been read successfully.</summary>
        public static bool Ready
        {
            get { var _ = Minutes; return _ever; }
        }

        /// <summary>The hour, 0-23. Falls back to the middle of the day rather than to zero.</summary>
        public static int Hour
        {
            get
            {
                try
                {
                    var h = Function.Call<int>(Hash.GET_CLOCK_HOURS);
                    return h < 0 || h > 23 ? 12 : h;
                }
                catch
                {
                    // Midday, because most of what asks the hour is asking whether a shop is
                    // open, and "shut" is the more annoying thing to be wrong about.
                    return 12;
                }
            }
        }

        /// <summary>Moves the clock forward. What a night in a bed does.</summary>
        public static void Add(int hours, int minutes)
        {
            try
            {
                Function.Call(Hash.ADD_TO_CLOCK_TIME, hours, minutes, 0);
            }
            catch (Exception ex)
            {
                Log.Once("clock-add", "Could not move the clock: " + ex.Message);
            }
        }

        /// <summary>Minutes, as whole game hours. For anything that thinks in hours.</summary>
        public static float HoursBetween(long from, long to)
        {
            return (to - from) / 60f;
        }
    }
}
