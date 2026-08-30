using System;
using System.Collections.Generic;
using GTA;
using GTA.Native;
using BareMinimum.Core;

namespace BareMinimum.Food
{
    /// <summary>
    /// Gets the player to say something out loud.
    ///
    /// THE GAME'S OWN LINES, not new audio. Every playable character has a full ambient
    /// speech bank already recorded in their own voice, and PLAY_PED_AMBIENT_SPEECH_NATIVE
    /// picks a take out of it. That is why buying a hot dog can get a Franklin line, a
    /// Michael line or a Trevor line without this mod shipping a single wav -- and why it is
    /// in character for each of them, which nothing I could write would be.
    ///
    /// NAMES COME FROM THE JSON. A speech name that does not exist in the bank plays nothing
    /// and returns nothing to check, so there is no validating these the way the model lists
    /// are validated -- the failure is silent by nature. Keeping them in foods.json at least
    /// means a wrong guess is a text edit rather than a rebuild.
    ///
    /// SPARINGLY. A character who thanks the cashier every single time is a character with a
    /// tic, and the whole point is that it lands as a person rather than as a trigger.
    /// </summary>
    internal sealed class Speech
    {
        private readonly Core.Settings _cfg;
        private readonly Random _rng = new Random();

        private readonly Dictionary<string, string[]> _lines =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        private int _nextAllowed;

        public Speech(Core.Settings cfg)
        {
            _cfg = cfg;
        }

        /// <summary>Replaces the line sets. Called by the catalogue when it reads its file.</summary>
        public void Load(Dictionary<string, string[]> sets)
        {
            _lines.Clear();

            if (sets == null) return;

            foreach (var pair in sets) _lines[pair.Key] = pair.Value;

            Log.Info("Speech: " + _lines.Count + " set(s) loaded.");
        }

        /// <summary>
        /// Says one line from a set, if the dice, the clock and the situation allow it.
        ///
        /// Returns quietly on every refusal. This is garnish: there is no state that depends
        /// on it and nothing downstream should ever have to know whether it happened.
        /// </summary>
        public void Say(string set)
        {
            if (!_cfg.SpeechEnabled || _lines.Count == 0) return;

            try
            {
                var now = Game.GameTime;
                if (now < _nextAllowed) return;

                if (_rng.Next(100) >= Math.Max(0, _cfg.SpeechChance)) return;

                string[] lines;
                if (!_lines.TryGetValue(set, out lines) || lines.Length == 0) return;

                var me = Game.Player.Character;
                if (me == null || !me.Exists() || me.IsDead) return;

                // NOT OVER THE TOP OF SOMETHING ELSE. The game talks to itself constantly --
                // phone calls, mission dialogue, the character muttering at traffic -- and
                // cutting a line of that off to say "thanks" is worse than staying quiet.
                if (Function.Call<bool>(Hash.IS_ANY_SPEECH_PLAYING, me.Handle)) return;

                var line = lines[_rng.Next(lines.Length)];

                // SPEECH_PARAMS_FORCE rather than the shouted or standard variants: standard
                // can be dropped when the audio engine is busy, which for one line every few
                // minutes means it mostly never plays, and shouted is a man yelling THANKS
                // across a shop.
                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, me.Handle, line,
                              "SPEECH_PARAMS_FORCE");

                _nextAllowed = now + Math.Max(5, _cfg.SpeechGapSeconds) * 1000;

                Log.Debug("Said " + line + " (" + set + ").");
            }
            catch (Exception ex)
            {
                Log.Once("speech", "Could not say anything: " + ex.Message);
            }
        }
    }
}
