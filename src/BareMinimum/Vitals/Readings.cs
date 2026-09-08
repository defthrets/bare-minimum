using System;
using GTA;
using GTA.Native;
using BareMinimum.Core;

namespace BareMinimum.Vitals
{
    /// <summary>
    /// The three numbers, 0 to 1, read fresh every frame.
    ///
    /// Health and armour come straight from the game. The third bar is ENERGY -- the sprint
    /// meter this mod keeps, see Energy -- or, with that switched off, the special ability
    /// meter worked out by the second class in this file, because the game will not say how
    /// full that one is.
    /// </summary>
    internal sealed class Readings
    {
        public float Health;
        public float Armour;

        /// <summary>The third bar's level: energy, or the special ability's charge.</summary>
        public float Third;

        /// <summary>Whether a third bar belongs on screen at all this frame.</summary>
        public bool HasThird;

        /// <summary>Whether the third bar is the energy meter rather than the special ability.</summary>
        public bool ThirdIsEnergy;

        /// <summary>Energy: whether he has run himself out and cannot sprint yet.</summary>
        public bool Tired;

        /// <summary>The special ability's own state, read for whoever has one, whatever the third bar shows.</summary>
        public bool SpecialActive;
        public bool SpecialFull;
        public bool SpecialEnabled;

        /// <summary>This frame's change in health and armour, as fractions. Negative is damage.</summary>
        public float HealthDelta;
        public float ArmourDelta;

        /// <summary>This frame's change in the third bar. Negative while energy drains; the streaks read it.</summary>
        public float ThirdDelta;

        /// <summary>Which of the three the player is, 0 to 2, or -1 for anybody else.</summary>
        public int Character = -1;

        private int _ped;
        private int _lastHealth = -1;
        private int _lastArmour = -1;
        private float _lastThird = -1f;

        private int _michael, _franklin, _trevor;
        private bool _hashed;

        private readonly SpecialMeter _meter = new SpecialMeter();

        public void Update(Settings cfg, float dt, Energy energy)
        {
            var ped = Game.Player.Character;
            var handle = ped.Handle;

            if (handle != _ped)
            {
                // A new body -- a switch, a respawn, a model change. Its first reading is not a
                // change from the last one, so nothing is allowed to jolt on it.
                _ped = handle;
                _lastHealth = -1;
                _lastArmour = -1;
                _lastThird = -1f;
            }

            // ---- health ----
            //
            // A ped is dead at 100, not at 0: the bottom hundred points are not on the bar.
            // (health - 100) / (max - 100) is the fraction the stock strip shows.
            var health = Function.Call<int>(Hash.GET_ENTITY_HEALTH, handle);
            var maxHealth = Function.Call<int>(Hash.GET_PED_MAX_HEALTH, handle);
            if (maxHealth <= 100) maxHealth = 200;

            var span = maxHealth - 100f;

            Health = Ink.Clamp01((health - 100f) / span);
            HealthDelta = _lastHealth >= 0 ? (health - _lastHealth) / span : 0f;
            _lastHealth = health;

            // ---- armour ----
            var armour = Function.Call<int>(Hash.GET_PED_ARMOUR, handle);
            var maxArmour = Function.Call<int>(Hash.GET_PLAYER_MAX_ARMOUR, Game.Player.Handle);
            if (maxArmour <= 0) maxArmour = 100;

            Armour = Ink.Clamp01(armour / (float)maxArmour);
            ArmourDelta = _lastArmour >= 0 ? (armour - _lastArmour) / (float)maxArmour : 0f;
            _lastArmour = armour;

            // ---- the special ability's state, for the colour cues ----
            Character = WhichCharacter(ped);

            if (Character >= 0)
            {
                var player = Game.Player.Handle;

                SpecialActive = Function.Call<bool>(Hash.IS_SPECIAL_ABILITY_ACTIVE, player, 0);
                SpecialFull = Function.Call<bool>(Hash.IS_SPECIAL_ABILITY_METER_FULL, player, 0);
                SpecialEnabled = Function.Call<bool>(Hash.IS_SPECIAL_ABILITY_ENABLED, player, 0);
            }
            else
            {
                SpecialActive = SpecialFull = SpecialEnabled = false;
            }

            // ---- the third bar ----
            if (cfg.VitalsEnergy)
            {
                // Everybody has legs, so everybody has the bar.
                ThirdIsEnergy = true;
                HasThird = true;
                Third = energy.Level;
                Tired = energy.Tired;
                Trend();
                return;
            }

            ThirdIsEnergy = false;
            Tired = false;

            switch (cfg.VitalsSpecial)
            {
                case SpecialMode.Always: HasThird = true; break;
                case SpecialMode.Never: HasThird = false; break;
                default: HasThird = Character >= 0; break;
            }

            Third = HasThird ? _meter.Update(cfg, dt, Character, SpecialActive, SpecialFull) : 0f;
            Trend();
        }

        private void Trend()
        {
            ThirdDelta = _lastThird >= 0f ? Third - _lastThird : 0f;
            _lastThird = Third;
        }

        /// <summary>0 Michael, 1 Franklin, 2 Trevor -- the SP0, SP1, SP2 of the stat names -- or -1.</summary>
        private int WhichCharacter(Ped ped)
        {
            if (!_hashed)
            {
                _hashed = true;
                _michael = Game.GenerateHash("player_zero");
                _franklin = Game.GenerateHash("player_one");
                _trevor = Game.GenerateHash("player_two");
            }

            var model = ped.Model.Hash;

            if (model == _michael) return 0;
            if (model == _franklin) return 1;
            if (model == _trevor) return 2;
            return -1;
        }
    }

    /// <summary>
    /// How full the special ability meter is, worked out, because the game will not say.
    ///
    /// THERE IS NO NATIVE FOR THE NUMBER. The native list has IS_SPECIAL_ABILITY_ACTIVE,
    /// IS_SPECIAL_ABILITY_METER_FULL, a dozen ways to charge the meter and two to drain it, and
    /// nothing that reads it -- the stock bar knows because it is drawn by the same code that
    /// owns the meter. So this keeps its own copy and corrects it whenever the game lets slip
    /// something certain:
    ///
    ///   - the game says FULL:                              the meter is 1.
    ///   - the ability stops and nobody pressed the button: it ran dry, the meter is 0.
    ///   - the ability starts:                              it held at least the minimum.
    ///
    /// Between those it runs down while the ability is on, at a rate set by how long a full
    /// meter lasts for this character's Special skill, and back up while it is off, at the
    /// refill rate. What it cannot see is a charge the game hands out for a headshot or a
    /// near miss, so between corrections it can read low. The next full meter squares it.
    ///
    /// One meter per character, because the game keeps three. Only used with the energy bar
    /// switched off; the energy bar is the reason it usually is.
    /// </summary>
    internal sealed class SpecialMeter
    {
        private readonly float[] _charge = { 1f, 1f, 1f };
        private readonly int[] _skill = { 100, 100, 100 };
        private int _skillAt;

        private float _shown = 1f;
        private int _shownFor = -1;

        private bool _wasActive;
        private int _pressedAt = -100000;

        public float Update(Settings cfg, float dt, int character, bool active, bool full)
        {
            if (character < 0 || character > 2) character = 0;

            if (character != _shownFor)
            {
                _shownFor = character;
                _shown = _charge[character];
            }

            ReadSkill(character);
            WatchButton();

            var charge = _charge[character];
            var now = Game.GameTime;

            if (full && !active)
            {
                charge = 1f;
            }
            else if (active)
            {
                if (!_wasActive && charge < cfg.SpecialMinimumCharge) charge = cfg.SpecialMinimumCharge;

                var seconds = cfg.SpecialDurationSeconds *
                              (cfg.SpecialMinDurationFraction + (1f - cfg.SpecialMinDurationFraction) * _skill[character] / 100f);
                if (seconds < 1f) seconds = 1f;

                charge -= dt / seconds;

                // Not below a sliver while the game still says it is running: the drain rate
                // was guessed a little fast, and an empty bar under a live ability is a lie.
                if (charge < 0.02f) charge = 0.02f;
            }
            else
            {
                if (_wasActive)
                {
                    // Stopped. By the player, within the last moment, or by running dry.
                    var pressed = now - _pressedAt < 400;
                    if (!pressed) charge = 0f;
                }

                charge += dt / Math.Max(1f, cfg.SpecialRechargeSeconds);
                if (charge > 1f) charge = 1f;
            }

            _wasActive = active;
            _charge[character] = charge;

            // Eased on screen, so a correction is a quick fill rather than a jump.
            var k = 1f - (float)Math.Pow(0.002, dt);
            _shown += (charge - _shown) * k;

            if (Math.Abs(charge - _shown) < 0.002f) _shown = charge;

            return _shown;
        }

        /// <summary>The Special skill, 0 to 100, from the stat the game keeps it in. Read every few seconds.</summary>
        private void ReadSkill(int character)
        {
            var now = Game.GameTime;
            if (now < _skillAt) return;
            _skillAt = now + 5000;

            try
            {
                var hash = Game.GenerateHash("SP" + character + "_SPECIAL_ABILITY_UNLOCKED");

                using (var value = new OutputArgument())
                {
                    if (Function.Call<bool>(Hash.STAT_GET_INT, hash, value, -1))
                    {
                        var v = value.GetResult<int>();
                        if (v >= 0 && v <= 100) _skill[character] = v;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Once("vitals-skill", "Could not read the Special skill stat: " + ex.Message +
                                         " - assuming it is maxed.");
            }
        }

        /// <summary>Remembers the last press of any special-ability control, to tell a stop from a run-out.</summary>
        private void WatchButton()
        {
            try
            {
                if (Game.IsControlJustPressed(Control.SpecialAbility) ||
                    Game.IsControlJustPressed(Control.SpecialAbilitySecondary) ||
                    Game.IsControlJustPressed(Control.SpecialAbilityPC) ||
                    Game.IsControlJustPressed(Control.VehicleSpecialAbilityFranklin))
                {
                    _pressedAt = Game.GameTime;
                }
            }
            catch
            {
                // A control that cannot be read reads as never pressed, which errs toward
                // "it ran dry" -- the more common way for an ability to stop.
            }
        }
    }
}
