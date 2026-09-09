using System;
using GTA;
using GTA.Native;

using BareMinimum.Core;

namespace BareMinimum.Needs
{
    /// <summary>
    /// What a psychedelic does to the picture, for as long as it lasts.
    /// </summary>
    ///
    /// <remarks>
    /// IT IS NOT AN UPPER AND IT IS NOT A DOWNER, and that is the whole reason it needs its own
    /// class. Every other drug in the mod is a number on a meter: the stimulants fill the sleep
    /// bar and pin the energy, the sedatives empty it. Acid does almost nothing to either -- you
    /// are neither rested nor tired, you are somewhere else -- so a drug modelled only as
    /// arithmetic would come out as the one that did nothing at all.
    ///
    /// WHAT IT DOES IS TO THE SCREEN, and the game already owns the look. drug_flying_base is
    /// the timecycle the peyote trips run on: the colour goes wrong, the light goes soft and
    /// wide, and it holds for as long as it is asked to. Nothing here is drawn by this mod, so
    /// there is nothing to keep in step with a graphics setting or a time of day.
    ///
    /// IT BREATHES RATHER THAN SITTING. A timecycle held at one strength is a filter, and a
    /// filter is something you stop seeing after a minute -- which is the opposite of the point.
    /// The strength swells and falls on two periods that do not divide into each other, so it
    /// never quite settles and never repeats. It also comes UP slowly at the start and lets go
    /// slowly at the end, because the one thing a trip is not is sudden.
    ///
    /// ONE TIMECYCLE SLOT, AND THIS WINS IT. Effects.Wobble asks for the same slot when the
    /// sleep meter bottoms out, and a man who has taken acid and not slept should be shown the
    /// acid: it is the thing he did, it is the louder of the two, and being shown a tired
    /// shimmer instead would read as the tab having been a dud.
    ///
    /// EVERYTHING IS PUT BACK. On the way out, on a reload, and the moment the drug is done.
    /// </remarks>
    internal sealed class Trip
    {
        /// <summary>The peyote trip's own timecycle. Verified in this build's modifier list.</summary>
        private const string Cycle = "drug_flying_base";

        /// <summary>
        /// How long it takes to come up and how long to come down, in real seconds.
        ///
        /// LONGER GOING THAN COMING, which is the shape the drug has. Neither is quick: the
        /// whole character of it is that you cannot say when it started.
        /// </summary>
        private const float RiseSeconds = 25f;
        private const float FallSeconds = 45f;

        private readonly Settings _cfg;
        private readonly Food.Speech _speech;
        private readonly Random _rng = new Random();

        private bool _on;
        private int _nextWord;

        public Trip(Settings cfg, Food.Speech speech)
        {
            _cfg = cfg;
            _speech = speech;
        }

        /// <summary>Whether the picture is bent on this class's account. Effects.Wobble stands down for it.</summary>
        public bool Running => _on;

        public void Update(bool suspended)
        {
            try
            {
                var left = suspended ? 0f : Food.Dope.TripLeft;

                if (left <= 0f) { Clear(); return; }

                var strength = Strength(left);

                if (strength <= 0.01f) { Clear(); return; }

                if (!_on)
                {
                    Function.Call(Hash.SET_TRANSITION_TIMECYCLE_MODIFIER, Cycle, RiseSeconds * 0.5f);
                    _on = true;

                    Log.Info("Trip: the picture is going.");
                }

                Function.Call(Hash.SET_TIMECYCLE_MODIFIER_STRENGTH, strength);

                Talk();
            }
            catch (Exception ex)
            {
                Log.Once("trip", "Could not bend the picture: " + ex.Message);
                Clear();
            }
        }

        /// <summary>
        /// How hard it is riding: up over the first half minute, down over the last three
        /// quarters of one, and never still in between.
        ///
        /// TWO PERIODS THAT DO NOT DIVIDE INTO EACH OTHER, so the swell has no beat you could
        /// count. A trip that pulsed on a rhythm would be a strobe, and a strobe is a completely
        /// different and much worse thing to do to somebody.
        /// </summary>
        private float Strength(float left)
        {
            var total = Math.Max(1f, Food.Dope.TripTotal);
            var age = total - left;

            var up = age < RiseSeconds ? age / RiseSeconds : 1f;
            var down = left < FallSeconds ? left / FallSeconds : 1f;

            var hold = Math.Min(up, down);

            var t = Game.GameTime / 1000.0;

            var swell = 0.5 + 0.5 * Math.Sin(t * (2.0 * Math.PI / 17.0));
            var wander = 0.5 + 0.5 * Math.Sin(t * (2.0 * Math.PI / 6.7));

            var deep = 0.55f + 0.30f * (float)swell + 0.15f * (float)wander;

            // AND THE DIAL OVER ALL OF IT. "How much of this do I want" is the first thing
            // anybody asks of a full-screen effect, and 0 is a legitimate answer -- the drug
            // still works, it just stops being something you look at.
            var dial = _cfg.TripStrength;
            if (dial < 0f) dial = 0f;
            if (dial > 1f) dial = 1f;

            return hold * deep * dial;
        }

        /// <summary>
        /// Something said, now and again, and rarely.
        ///
        /// THE BAD TRIP SET, WHICH IS THE ONLY ONE THAT FITS. The game has no line for a man
        /// enjoying himself quietly; what it has is shock and confusion, which is half of what
        /// acid actually sounds like out loud and the whole of what it sounds like from outside.
        /// Rare on purpose -- a man narrating his own trip every twenty seconds is a comedy.
        /// </summary>
        private void Talk()
        {
            if (_speech == null || !_cfg.SpeechEnabled) return;

            var now = Game.GameTime;

            if (_nextWord == 0) { _nextWord = now + Gap(); return; }
            if (now < _nextWord) return;

            _nextWord = now + Gap();

            _speech.Say(_rng.Next(100) < 40 ? "badtrip" : "weed");
        }

        private int Gap()
        {
            return 45000 + _rng.Next(60000);
        }

        /// <summary>The picture handed back. Safe to call when it was never taken.</summary>
        public void Clear()
        {
            _nextWord = 0;

            if (!_on) return;

            _on = false;

            try { Function.Call(Hash.CLEAR_TIMECYCLE_MODIFIER); }
            catch { /* nothing else to try */ }
        }
    }
}
