using System;
using GTA;

using BareMinimum.Core;

namespace BareMinimum.Needs
{
    /// <summary>
    /// What being dry SOUNDS like: hard breathing, and every so often a word about it.
    /// </summary>
    ///
    /// <remarks>
    /// THERE IS NO "I'M THIRSTY" LINE IN GTA V. Every voice in the game's speech list was
    /// searched for THIRST, PARCH, DEHYD, WATER and SWEAT, and the single hit anywhere is
    /// DEATH_UNDERWATER, which is the opposite problem. That is worth writing down because it
    /// is the sort of thing somebody will otherwise go looking for again, twice.
    ///
    /// So this is two things the game does have, and the first is the better of them.
    ///
    /// THE BREATH IS REAL. PLAY_PAIN is misnamed -- it is the ped's whole non-verbal set, and
    /// inhale, exhale, wheeze and exhaustion are all in it, recorded in the same voice as
    /// everything else. So a parched Franklin actually pants in Franklin's voice, with no audio
    /// shipped by this mod and nothing that has to be checked against three voices. See
    /// Food.Speech.Noise, which the cigarette cough already goes through.
    ///
    /// THE LINE IS THE CLOSEST THE GAME HAS. GENERIC_DRINK, in among the out-of-breath race
    /// lines -- which is a man saying something about a drink while gasping, and reads as
    /// wanting one when he has not had one in a day. It is deliberately RARE; a stock line
    /// repeating is the fastest way to make a mod tiresome, and this one has to survive a
    /// player being dry for a long stretch of a session.
    ///
    /// IT GETS FASTER AS THE METER GOES DOWN and faster again while he is running, which is
    /// what turns it from an alarm into a state. An alarm tells you once; a rate tells you how
    /// bad it is for as long as you leave it, and the player never has to look at the bar.
    ///
    /// SILENT IN A CAR. Sitting down is not exertion and a man panting at the wheel is a man
    /// with a different problem.
    /// </remarks>
    internal sealed class Parched
    {
        private readonly Settings _cfg;
        private readonly Food.Speech _speech;
        private readonly Random _rng = new Random();

        private int _nextBreath;

        /// <summary>
        /// Which noise came last, so two of the same never land in a row.
        ///
        /// A man breathing hard is not making one sound repeatedly, and the set is small enough
        /// that random alone lands a pair often. This is the cheapest fix there is.
        /// </summary>
        private int _lastPick = -1;

        public Parched(Settings cfg, Food.Speech speech)
        {
            _cfg = cfg;
            _speech = speech;
        }

        public void Update(Needs needs, bool suspended)
        {
            if (suspended || needs == null) return;
            if (!_cfg.ThirstEnabled || !_cfg.SpeechEnabled) return;

            try
            {
                var dry = needs.Thirst.Value;

                // Above the threshold, nothing at all -- and the clock is pushed out so that
                // arriving at the threshold does not fire a breath on the same frame. Somebody
                // finishing a drink and immediately gasping reads as the drink not working.
                if (dry >= _cfg.ThirstDryAt) { _nextBreath = 0; return; }

                var me = Game.Player.Character;
                if (me == null || !me.Exists() || me.IsDead) return;

                // Not at the wheel, and not while he is already saying something -- Speech's own
                // guard would catch the second of those for a line, but a NOISE goes through a
                // lighter gate and can land on top of dialogue.
                if (me.IsInVehicle()) return;

                var now = Game.GameTime;

                if (_nextBreath == 0) { _nextBreath = now + Gap(dry, me); return; }
                if (now < _nextBreath) return;

                _nextBreath = now + Gap(dry, me);

                // The line, occasionally, INSTEAD of the breath rather than as well: the two
                // together is a man talking over his own gasping.
                if (_cfg.ThirstLineChance > 0 && _rng.Next(100) < _cfg.ThirstLineChance)
                {
                    _speech.Say("parched");
                    return;
                }

                _speech.Noise(Pick(dry));
            }
            catch (Exception ex)
            {
                Log.Once("parched", "Could not make him sound thirsty: " + ex.Message);
            }
        }

        /// <summary>
        /// How long until the next one, in milliseconds.
        ///
        /// FIVE TIMES THE GAP AT THE THRESHOLD, ONE AT THE BOTTOM, so the rate itself is the
        /// reading -- an occasional exhale when it first starts, hard steady breathing when
        /// there is nothing left. Halved again while he is running, because that is when a dry
        /// throat is worst and when the player is most likely to be paying attention to how his
        /// legs feel.
        ///
        /// And jittered, because a noise on an exact clock is a metronome.
        /// </summary>
        private int Gap(float dry, Ped me)
        {
            var t = _cfg.ThirstDryAt <= 0.0001f ? 0f : Clamp01(dry / _cfg.ThirstDryAt);

            var seconds = Math.Max(2f, _cfg.ThirstBreathSeconds) * (1f + 4f * t);

            try { if (me.IsSprinting || me.IsRunning) seconds *= 0.5f; }
            catch { /* a ped that will not say what it is doing is treated as standing */ }

            // Plus or minus a quarter.
            seconds *= 0.75f + (float)_rng.NextDouble() * 0.5f;

            return (int)(seconds * 1000f);
        }

        /// <summary>
        /// Which noise. The set narrows and gets worse as the meter empties.
        ///
        /// At the top of the range it is an exhale and the odd inhale -- somebody breathing a
        /// bit hard. Further down the wheeze arrives, and at the bottom it is exhaustion, which
        /// is the grunt the energy bar already uses when his legs go. That the two share a sound
        /// is the point: being dry and being winded are the same thing happening for different
        /// reasons, and the ear should not be asked to tell them apart.
        /// </summary>
        private Food.Speech.Pain Pick(float dry)
        {
            Food.Speech.Pain[] set;

            if (dry <= 0.05f)
            {
                set = new[]
                {
                    Food.Speech.Pain.Exhaustion, Food.Speech.Pain.Wheeze,
                    Food.Speech.Pain.Exhale, Food.Speech.Pain.Exhaustion
                };
            }
            else if (dry <= 0.10f)
            {
                set = new[]
                {
                    Food.Speech.Pain.Wheeze, Food.Speech.Pain.Exhale,
                    Food.Speech.Pain.Exhaustion, Food.Speech.Pain.Inhale
                };
            }
            else
            {
                set = new[]
                {
                    Food.Speech.Pain.Exhale, Food.Speech.Pain.Inhale,
                    Food.Speech.Pain.Exhale, Food.Speech.Pain.Wheeze
                };
            }

            var pick = _rng.Next(set.Length);

            // Never the same one twice running. One retry rather than a loop: the sets are four
            // long and a second collision is not worth a second roll's worth of anybody's time.
            if ((int)set[pick] == _lastPick) pick = (pick + 1) % set.Length;

            _lastPick = (int)set[pick];

            return set[pick];
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            return v > 1f ? 1f : v;
        }
    }
}
