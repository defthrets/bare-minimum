using System;
using GTA;
using GTA.Native;

using BareMinimum.Core;

namespace BareMinimum.Needs
{
    /// <summary>
    /// Going out for a second at a time, when there is too much of a sedative in him.
    /// </summary>
    ///
    /// <remarks>
    /// TWO BARS IS THE POINT, NOT ONE. A single xanax is a heavy head and the walk to match, and
    /// the mod already says that with the drunk clipset and a sleep meter that drops a third.
    /// The second one is where it stops being a state and starts being a thing that HAPPENS to
    /// you -- the lights going out for a beat with no warning, and coming back with you still
    /// walking. Which is what people describe.
    ///
    /// THE GAME'S OWN FADE, NOT A BLACK RECTANGLE. A script rectangle over the screen is not a
    /// blackout: the game draws its blips over anything a script draws, so the minimap and every
    /// marker on it would float in front of the black and the whole thing would read as a bug.
    /// DO_SCREEN_FADE_OUT takes the lot.
    ///
    /// IT NEVER TAKES CONTROL. Sleeping does, because there the player is meant to be somewhere
    /// else when it ends; here he is meant to be exactly where he would have got to, and finding
    /// out where that is IS the mechanic. It also means nothing here can wedge him -- the worst
    /// this class can do is leave a screen dark, and the ceiling below covers that.
    ///
    /// AND IT CHECKS THE SCREEN IS HIS BEFORE IT TAKES IT. A fade started over a cutscene, a
    /// loading screen or somebody else's fade is a fight between two scripts that both think
    /// they own the same one thing, and the loser is whoever fades in first. So it only ever
    /// starts from a screen that is fully faded IN, and it gives up on its own if something else
    /// has taken over by the time it wants to come back.
    /// </remarks>
    internal sealed class Blackout
    {
        /// <summary>Down, held and back. Short: this is a lapse, not a nap.</summary>
        private const int OutMs = 320;
        private const int DarkMs = 700;
        private const int InMs = 650;

        /// <summary>
        /// A CEILING, NOT A SCHEDULE. The phase ends when the clock says so; this is here so a
        /// fade that never reports itself finished cannot leave the player on a black screen for
        /// the rest of the session. Sleeping keeps the same guard for the same reason.
        /// </summary>
        private const int GiveUpMs = 4000;

        private readonly Settings _cfg;
        private readonly Random _rng = new Random();

        private int _nextAt;
        private int _startedAt;
        private bool _dark;

        public Blackout(Settings cfg)
        {
            _cfg = cfg;
        }

        /// <summary>Whether the screen is out on this class's account right now.</summary>
        public bool Dark => _dark;

        /// <param name="busy">
        /// Whether anything else owns the screen or the player -- a sleep, a menu, a walk-in.
        /// Composed by Main from the same list every other system here is handed.
        /// </param>
        public void Update(bool busy)
        {
            int now;
            try { now = Game.GameTime; }
            catch { return; }

            if (_dark)
            {
                // SOMETHING ELSE TOOK OVER MID-LAPSE. A sleep, a shop, a walk-in: each of those
                // fades the screen itself, and this one carrying on to fade back IN over the top
                // of it would leave the player looking at a room that is meant to be dark, or at
                // black that is meant to be a room. Whoever owns it now owns bringing it back.
                if (busy) { _dark = false; _nextAt = 0; return; }

                Run(now);
                return;
            }

            // NOTHING WHILE SOMETHING ELSE OWNS THE SCREEN. Sleeping, a shop, a walk-in, a
            // cutscene: all of them are somebody else's fade and none wants a second in it.
            if (busy || !_cfg.BlackoutsEnabled) { _nextAt = 0; return; }

            if (!Food.Dope.Blacking) { _nextAt = 0; return; }

            var me = Game.Player.Character;
            if (me == null || !me.Exists() || me.IsDead) { _nextAt = 0; return; }

            // ARMED ON THE FIRST TICK IT IS TRUE rather than fired on it: the dose that put him
            // over should not black him out in the same instant he swallows it.
            if (_nextAt == 0) { _nextAt = now + Wait(); return; }

            if (now < _nextAt) return;

            if (!FadedIn()) return;

            Start(now);
        }

        /// <summary>
        /// How long until the next one.
        ///
        /// RANDOM, AND WIDELY SO. A lapse on a timer you can feel coming is a mechanic you play
        /// around; one you cannot is a thing that happens to you, which is the whole difference
        /// between this and a cooldown. The spread is deliberately more than double.
        /// </summary>
        private int Wait()
        {
            var least = Math.Max(3f, _cfg.BlackoutEverySeconds);
            var most = least * 2.6f;

            return (int)((least + (float)_rng.NextDouble() * (most - least)) * 1000f);
        }

        private void Start(int now)
        {
            try
            {
                Function.Call(Hash.DO_SCREEN_FADE_OUT, OutMs);

                _dark = true;
                _startedAt = now;

                Log.Debug("Blackout.");
            }
            catch (Exception ex)
            {
                Log.Once("blackout", "Could not black the screen out: " + ex.Message);
                _dark = false;
                _nextAt = now + Wait();
            }
        }

        private void Run(int now)
        {
            var age = now - _startedAt;

            if (age < OutMs + DarkMs && age < GiveUpMs) return;

            try
            {
                Function.Call(Hash.DO_SCREEN_FADE_IN, InMs);
            }
            catch (Exception ex)
            {
                Log.Once("blackout-in", "Could not bring the screen back: " + ex.Message);
            }

            _dark = false;
            _nextAt = now + Wait();
        }

        /// <summary>
        /// Everything undone, now, with the screen given back.
        ///
        /// FOR THE WAY OUT OF THE SCRIPT, and it is not optional. A reload while the screen is
        /// out would otherwise leave it out with nothing left running that knows how to bring it
        /// back -- the one failure this class must not have.
        /// </summary>
        public void Shutdown()
        {
            if (!_dark) return;

            _dark = false;

            try { Function.Call(Hash.DO_SCREEN_FADE_IN, 200); }
            catch { /* nothing else to try */ }
        }

        private static bool FadedIn()
        {
            try { return Function.Call<bool>(Hash.IS_SCREEN_FADED_IN); }
            catch { return false; }
        }
    }
}
