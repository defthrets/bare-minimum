using System;
using GTA;
using BareMinimum.Core;
using BareMinimum.Needs;
using BareMinimum.UI;

namespace BareMinimum
{
    /// <summary>
    /// Script entry point and the only owner of the update loop.
    ///
    /// ONE Script subclass, deliberately. SHVDN instantiates every Script it finds and ticks
    /// them in an order it does not define; a single entry point means the order our own
    /// subsystems run in is ours to decide, and there is exactly one place that has to be
    /// exception-safe.
    /// </summary>
    public sealed class Main : Script
    {
        /// <summary>Consecutive tick failures before the script parks itself rather than spamming.</summary>
        private const int MaxConsecutiveFailures = 10;

        /// <summary>
        /// Core.Settings, spelt out in full every time.
        ///
        /// Script -- the SHVDN base class -- has its own inherited Settings property, and it
        /// shadows our type in expression position. Written bare, Settings.Load() does not
        /// compile and the error points at something else entirely.
        /// </summary>
        private readonly Core.Settings _cfg;

        private readonly Needs.Needs _needs;
        private readonly Effects _effects;
        private readonly Gauge _gauge;

        private int _failures;
        private bool _parked;
        private bool _greeted;

        public Main()
        {
            _cfg = Core.Settings.Load();

            _needs = new Needs.Needs(_cfg);
            _effects = new Effects(_cfg);
            _gauge = new Gauge(_cfg);

            Interval = 0;
            Tick += OnTick;
            Aborted += OnAborted;

            Log.Info(Build.Name + " " + Build.Version + " loaded. Interact key " +
                     _cfg.InteractKey + ".");

            if (!_cfg.Enabled)
            {
                Log.Warn("[General] Enabled is false - nothing will run until it is turned on.");
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_parked || !_cfg.Enabled) return;

            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                Greet();

                var dt = Game.LastFrameTime;

                // A paused or hitching game hands back a dt of zero or of several seconds.
                // Only the save timer uses this -- the needs themselves run on the GAME clock,
                // which is the whole point of them -- but a wild value would still make the
                // state file get written on a stutter.
                if (dt < 0f || dt > 1f) dt = 0f;

                // Nothing owns the player yet. Sleeping will, once it exists, and both the
                // effects and the HUD have to stand down while it does.
                const bool suspended = false;

                _needs.Update(dt);
                _effects.Update(_needs, suspended);
                _gauge.Draw(_needs, suspended);

                _failures = 0;
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
        }

        /// <summary>Says hello once, after the game is running rather than in the constructor.</summary>
        private void Greet()
        {
            if (_greeted) return;
            _greeted = true;

            if (!_cfg.AnnounceOnLoad) return;

            try
            {
                GTA.UI.Notification.PostTicker(
                    "~b~" + Build.Name + "~s~ " + Build.Version + " by " + Build.By, false, false);
            }
            catch
            {
                // Not being able to say hello is not a reason to stop.
            }
        }

        private void Fail(Exception ex)
        {
            _failures++;
            Log.Error("Tick failed (" + _failures + "/" + MaxConsecutiveFailures + ")", ex);

            if (_failures < MaxConsecutiveFailures) return;

            _parked = true;
            Log.Error("Ten ticks in a row have failed. " + Build.Name +
                      " has stopped itself rather than keep throwing. See above for the cause.");

            try
            {
                GTA.UI.Notification.PostTicker(
                    "~r~" + Build.Name + " stopped~s~ - see " + Paths.Stem + ".log.", false, false);
            }
            catch
            {
                // Nothing further to try.
            }

            Cleanup();
        }

        private void OnAborted(object sender, EventArgs e)
        {
            Cleanup();
        }

        /// <summary>
        /// Leaves the player exactly as it found them, and writes the needs down.
        ///
        /// Runs on a reload as well as on shutdown, because SHVDN reloads scripts on a
        /// keypress -- and a mod that leaves somebody permanently drunk and limping every time
        /// a script is reloaded is a mod that ruins a save quietly.
        /// </summary>
        private void Cleanup()
        {
            try { _effects.Clear(); } catch (Exception ex) { Log.Error("Clearing effects", ex); }
            try { _needs.SaveNow(); } catch (Exception ex) { Log.Error("Final save", ex); }

            Log.Info(Build.Name + " stopped cleanly.");
        }
    }
}
