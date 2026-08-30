using System;

namespace BareMinimum.UI
{
    /// <summary>
    /// One or more Icons shown as a sequence: a picture that can blink.
    ///
    /// WHY NOT JUST AN Icon. An Icon is a texture handle and a path, and it draws itself; it
    /// has no idea what time it is and should not learn. This owns the schedule and hands back
    /// whichever frame is due, so every menu mark can be an animation without the menu, the
    /// icon or the panel that built it knowing anything about frames.
    ///
    /// A ONE-FRAME FLIPBOOK IS THE NORMAL CASE and costs nothing: Current returns the only
    /// frame and never looks at the clock. That is what lets TitleLeft and TitleRight take the
    /// same type whether they are a burger that sits still or an eye that blinks, instead of
    /// the menu carrying two ways of holding a picture.
    ///
    /// ON THE WALL CLOCK, like everything else in this mod that moves, so it runs at a human
    /// rate whatever the framerate is doing. TickCount is masked positive because it wraps
    /// after about twenty-five days of uptime and a negative remainder would quietly stop the
    /// animation for the rest of the session with nothing to say why.
    /// </summary>
    internal sealed class Flipbook
    {
        private readonly Icon[] _frames;

        /// <summary>How often the sequence runs, in real milliseconds.</summary>
        public int EveryMs = 5200;

        /// <summary>How long one run of it takes.</summary>
        public int SpanMs = 260;

        public Flipbook(params string[] files)
        {
            _frames = new Icon[files.Length];

            for (var i = 0; i < files.Length; i++) _frames[i] = new Icon(files[i]);
        }

        /// <summary>The frame to draw right now. Never null unless there are none at all.</summary>
        public Icon Current
        {
            get
            {
                if (_frames.Length == 0) return null;
                if (_frames.Length == 1) return _frames[0];

                var frame = At(Phase());

                // A missing file falls back to the resting frame rather than to nothing: an
                // icon that vanishes for a fifth of a second reads as a fault, and a mark that
                // simply does not blink reads as a mark that does not blink.
                if (frame == null || frame.Missing) frame = _frames[0];

                return frame;
            }
        }

        /// <summary>How far through the current run we are, 0 to 1, or -1 while resting.</summary>
        private float Phase()
        {
            if (EveryMs <= 1 || SpanMs <= 1) return -1f;

            var into = (Environment.TickCount & int.MaxValue) % EveryMs;
            if (into >= SpanMs) return -1f;

            return into / (float)SpanMs;
        }

        /// <summary>
        /// Which frame that phase lands on: out through the set and back again.
        ///
        /// THERE AND BACK, not a loop. The frames are a movement -- an eye closing -- and
        /// playing them 0,1,2,0 snaps the lid open in a single frame. Playing 0,1,2,1,0 is the
        /// eye opening again, which is the other half of a blink.
        /// </summary>
        private Icon At(float phase)
        {
            if (phase < 0f) return _frames[0];

            var last = _frames.Length - 1;

            // Out over the first half, back over the second.
            var t = phase < 0.5f ? phase * 2f : (1f - phase) * 2f;

            var i = (int)(t * last + 0.5f);

            if (i < 0) i = 0;
            if (i > last) i = last;

            return _frames[i];
        }
    }
}
