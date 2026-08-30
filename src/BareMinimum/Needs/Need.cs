using System;

namespace BareMinimum.Needs
{
    /// <summary>
    /// Which of the two this is. Kept as an enum rather than a bool so the log, the save file
    /// and the HUD all name the same thing the same way.
    /// </summary>
    internal enum Kind
    {
        Hunger,
        Sleep
    }

    /// <summary>
    /// One need: a number between 0 and 1, and the rules for moving it.
    ///
    /// 1 IS GOOD AND 0 IS BAD, for both of them. That is worth stating because the English
    /// runs the other way for one and not the other -- "hunger" going up sounds like getting
    /// hungrier, and here it means the opposite. Everything downstream (the icon stage, the
    /// colour ramp, the thresholds) reads "low is trouble", and having one need inverted
    /// against the other would put a sign error in every one of those places.
    ///
    /// So the field is how FULL you are and how RESTED you are. The class is called Need
    /// because that is what it models; the number is what remains of it.
    /// </summary>
    internal sealed class Need
    {
        public readonly Kind Kind;

        private float _value = 1f;

        public Need(Kind kind)
        {
            Kind = kind;
        }

        /// <summary>0 = empty and in trouble, 1 = full and fine.</summary>
        public float Value
        {
            get { return _value; }
            set { _value = Clamp01(value); }
        }

        public bool Empty => _value <= 0.0001f;
        public bool Full => _value >= 0.9999f;

        /// <summary>
        /// Which of the five drawn states this is, 4 = best.
        ///
        /// The bands are NOT five equal fifths. An icon that changes at 80/60/40/20 spends its
        /// first two states saying "fine" in two different ways, and its last state -- the one
        /// that matters -- covers a fifth of the range, so the player is shown a core or a shut
        /// eye long before anything is actually wrong. Weighted low, the top two states cover
        /// most of the range and the bottom two are narrow and urgent, which is where the
        /// information actually is.
        /// </summary>
        public int Stage
        {
            get
            {
                if (_value >= 0.80f) return 4;
                if (_value >= 0.58f) return 3;
                if (_value >= 0.34f) return 2;
                if (_value >= 0.14f) return 1;
                return 0;
            }
        }

        /// <summary>Drains by a whole-meter-per-hours rate, over a span of GAME hours.</summary>
        public void Drain(float gameHours, float hoursToEmpty, float multiplier = 1f)
        {
            if (gameHours <= 0f || hoursToEmpty <= 0.0001f) return;

            Value = _value - (gameHours / hoursToEmpty) * multiplier;
        }

        /// <summary>Puts some back. Returns how much actually landed, after the clamp at full.</summary>
        public float Restore(float amount)
        {
            if (amount <= 0f) return 0f;

            var before = _value;
            Value = _value + amount;
            return _value - before;
        }

        public override string ToString()
        {
            return Kind + " " + (_value * 100f).ToString("0") + "%";
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return float.IsNaN(v) ? 0f : v;
        }
    }
}
