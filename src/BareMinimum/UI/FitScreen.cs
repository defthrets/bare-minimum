using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using BareMinimum.Core;
using BareMinimum.Food;

namespace BareMinimum.UI
{
    /// <summary>
    /// The bench: one model at a time, in his hand, in the pose he eats it in, until it sits
    /// right. Then it is written down against THAT MODEL and nothing else ever moves it.
    ///
    /// WHY THIS EXISTS AND WHY THE ROWS ON THE SETTINGS PAGE WERE NOT ENOUGH.
    ///
    ///   1. A NUMBER PER KIND CANNOT BE RIGHT. The forty drinks hold twelve different models
    ///      between them -- a plastic cup, a mug, a beer bottle, a shot glass -- and every
    ///      model has its own origin. The roll that stands a cup up lays a mug on its side.
    ///      Nothing tuned against one of them can be right for the other eleven, so the unit
    ///      here is the MODEL, and fitting one cannot move another.
    ///
    ///   2. A THING HELD BY A MAN STANDING STILL IS NOT THE THING YOU ARE FITTING. The hand
    ///      is at his side when he is idle and at his mouth when he eats, and a cup that sits
    ///      perfectly in the first looks wrong in the second. So this plays the item's real
    ///      animation, on a loop, the whole time the screen is open: you are fitting the pose
    ///      it will actually be seen in.
    ///
    ///   3. IT HAS TO BE ABLE TO SAVE. Every version of this before it was a dial with
    ///      nowhere to put the answer -- the numbers were read out of the ini afterwards and
    ///      baked in by hand, into a kind rather than a model, and the next model undid them.
    ///      LOCK writes six numbers against one model name and they stay there.
    ///
    /// NOTHING HERE TOUCHES ANYTHING THAT IS NOT ON SCREEN. One model at a time, and the only
    /// key that writes anything is LOCK.
    /// </summary>
    internal sealed class FitScreen
    {
        private readonly Core.Settings _cfg;
        private readonly Catalogue _menu;
        private readonly Peek _hand = new Peek();

        /// <summary>The six, in the order the rows show them.</summary>
        private static readonly string[] Axes =
        {
            "Left / right", "Forward / back", "Up / down",
            "Roll", "Pitch", "Yaw"
        };

        /// <summary>Metres for the first three, degrees for the last three.</summary>
        private static readonly float[] Steps = { 0.005f, 0.005f, 0.005f, 5f, 5f, 5f };
        private static readonly float[] Fine = { 0.001f, 0.001f, 0.001f, 1f, 1f, 1f };

        private readonly Menu _ui = new Menu();

        private string[] _props;
        private int _prop;

        /// <summary>What is being worked on, before it is locked. Six numbers, live.</summary>
        private float[] _now = new float[6];

        private bool _loaded;
        private int _animAt;

        public bool IsOpen => _ui.IsOpen;

        public FitScreen(Core.Settings cfg, Catalogue menu)
        {
            _cfg = cfg;
            _menu = menu;

            _ui.Title = "FITTING";
            _ui.LeftRightAdjusts = true;
            _ui.ConfirmWord = "DO IT";
        }

        // ======================================================================

        public void Open()
        {
            _props = _menu.PropsByUse();
            _prop = 0;
            _loaded = false;

            Load();
            Refill();

            _ui.Open();
        }

        public void Close()
        {
            _ui.Close();

            _hand.Clear();
            Release();
        }

        public void Update(bool suspended)
        {
            try
            {
                if (suspended)
                {
                    if (IsOpen) Close();
                    return;
                }

                if (!IsOpen) return;

                _ui.Update();

                if (_ui.JustClosed) { _hand.Clear(); Release(); return; }

                if (_ui.Adjusted != null)
                {
                    var tag = _ui.Adjusted.Tag as string;
                    Adjust(tag, _ui.AdjustBy, _ui.Fine);
                    Refill();
                }

                if (_ui.Activated != null)
                {
                    var tag = _ui.Activated.Tag as string;
                    Do(tag);
                    Refill();
                }

                if (!IsOpen) return;

                Show();
                _ui.Draw();
            }
            catch (Exception ex)
            {
                Log.Once("fit", "The fitting screen failed: " + ex.Message);
                Close();
            }
        }

        // ======================================================================
        // The model in his hand, in the pose he eats it in
        // ======================================================================

        private string Prop
        {
            get
            {
                if (_props == null || _props.Length == 0) return "";

                if (_prop < 0) _prop = _props.Length - 1;
                if (_prop >= _props.Length) _prop = 0;

                return _props[_prop];
            }
        }

        private Item Item_ => _menu.ItemWithProp(Prop);

        /// <summary>
        /// Holds it, and keeps the right animation running underneath it.
        ///
        /// LOOPING, AND RE-ASKED EVERY FEW SECONDS. A clip played once ends, his hand drops
        /// to his side, and the last half of the fitting is done against the wrong pose. The
        /// flag is 49 -- upper body, secondary, LOOPING -- so his legs are still his and he
        /// can be turned on the spot while the hand stays where it will be.
        /// </summary>
        private void Show()
        {
            var prop = Prop;

            _hand.Sits = () => new Vector3(_now[0], _now[1], _now[2]);
            _hand.Spin = () => new Vector3(_now[3], _now[4], _now[5]);

            _hand.Show(prop);
            _hand.Tick();

            var now = Game.GameTime;
            if (now < _animAt) return;

            _animAt = now + 2000;

            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists() || me.IsDead) return;

                var item = Item_;

                var anim = item != null && item.Smoke ? _menu.Smoke
                         : item != null && item.Drink ? _menu.Sip
                         : (item != null ? item.Eat : null) ?? _menu.Eat;

                anim.Resolve("Fitting animation");
                if (!anim.Usable || !anim.Valid) return;

                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, anim.Dict))
                {
                    Function.Call(Hash.REQUEST_ANIM_DICT, anim.Dict);
                    return;
                }

                var clip = anim.Cycle.Length > 0 ? anim.Cycle[0] : anim.Clip;

                Function.Call(Hash.TASK_PLAY_ANIM, me.Handle, anim.Dict, clip,
                              4f, -4f, -1, 49, 0f, false, false, false);
            }
            catch
            {
                // Fitting against a still pose is worse than fitting against none.
            }
        }

        private void Release()
        {
            _animAt = 0;

            try
            {
                var me = Game.Player.Character;
                if (me != null && me.Exists()) Function.Call(Hash.CLEAR_PED_TASKS, me.Handle);
            }
            catch
            {
                // He shakes it off on his own.
            }
        }

        // ======================================================================
        // The numbers
        // ======================================================================

        /// <summary>What this model has now: its own fitted six, or its kind's as a start.</summary>
        private void Load()
        {
            var prop = Prop;

            float[] fit;
            if (prop.Length > 0 && _cfg.Fit.TryGetValue(prop, out fit))
            {
                _now = new[] { fit[0], fit[1], fit[2], fit[3], fit[4], fit[5] };
                _loaded = true;
                return;
            }

            var item = Item_;
            var drink = item != null && item.Drink;

            var hold = _menu.HoldFor(item, drink);
            var turn = _menu.TurnFor(item, drink);

            _now = new[] { hold[0], hold[1], hold[2], turn[0], turn[1], turn[2] };
            _loaded = false;
        }

        private void Adjust(string tag, int by, bool fine)
        {
            if (tag == "prop")
            {
                _prop += by >= 0 ? 1 : -1;
                Load();
                return;
            }

            int axis;
            if (!int.TryParse(tag, out axis) || axis < 0 || axis > 5) return;

            _now[axis] += (by >= 0 ? 1f : -1f) * (fine ? Fine[axis] : Steps[axis]);
        }

        private void Do(string tag)
        {
            var prop = Prop;
            if (prop.Length == 0) return;

            switch (tag)
            {
                case "lock":
                    _cfg.Fit[prop] = new[] { _now[0], _now[1], _now[2], _now[3], _now[4], _now[5] };
                    _loaded = true;

                    Save();
                    Toast("fitted");
                    break;

                case "clear":
                    _cfg.Fit.Remove(prop);
                    _loaded = false;

                    Save();
                    Load();
                    Toast("back to the default");
                    break;

                case "zero":
                    _now = new float[6];
                    break;
            }
        }

        /// <summary>
        /// Writes the whole table to the ini, now, rather than on a settle timer.
        ///
        /// A FITTING SESSION IS A SEQUENCE OF SMALL DECISIONS and losing the third of them to
        /// a crash while the fourth is being made would be the most annoying possible failure
        /// of this screen. It is one line in a file.
        /// </summary>
        private void Save()
        {
            try
            {
                IniFile.SetValue(Paths.Ini, "Eating", "Fit", _cfg.FitLine);
            }
            catch (Exception ex)
            {
                Log.Once("fit-save", "Could not write the fit: " + ex.Message);
            }
        }

        private void Toast(string what)
        {
            var item = Item_;

            Notify("~g~" + (item != null ? item.Name : Prop) + "~s~ - " + what + ". " +
                   _cfg.Fit.Count + " model(s) fitted.");
        }

        private static void Notify(string text)
        {
            try
            {
                Function.Call(Hash.BEGIN_TEXT_COMMAND_THEFEED_POST, "STRING");
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
                Function.Call(Hash.END_TEXT_COMMAND_THEFEED_POST_TICKER, false, false);
            }
            catch
            {
                // A ticker nobody saw.
            }
        }

        // ======================================================================
        // The rows
        // ======================================================================

        private void Refill()
        {
            var keep = _ui.Index;

            _ui.Rows.Clear();

            var item = Item_;
            var prop = Prop;

            var uses = 0;
            foreach (var i in _menu.Items) if (string.Equals(i.Prop, prop, StringComparison.OrdinalIgnoreCase)) uses++;

            _ui.Rows.Add(new Row
            {
                Left = "Model",
                Right = (item != null ? item.Name : prop.Length > 0 ? prop : "-") +
                        (_loaded ? "  *" : ""),
                Note = prop.Length == 0
                    ? "Nothing in the catalogue has a model yet."
                    : prop + "  --  " + uses + " item(s) hold this one" +
                      (_loaded ? ", and it has been fitted." : ", not fitted yet."),
                Tag = "prop"
            });

            for (var i = 0; i < 6; i++)
            {
                _ui.Rows.Add(new Row
                {
                    Left = Axes[i],
                    Right = _now[i].ToString(i < 3 ? "0.000" : "0"),
                    Note = i < 3
                        ? "Metres, out of the palm, towards the fingers, up through the back of the hand. Hold CTRL for a finer step."
                        : "Degrees. Hold CTRL for one at a time.",
                    Tag = i.ToString()
                });
            }

            _ui.Rows.Add(new Row
            {
                Left = "LOCK IT IN",
                Right = _cfg.Fit.Count + " done",
                Note = "Writes these six against this model, in the ini, now. Everything that " +
                       "holds this model moves with it and nothing else moves at all.",
                Tag = "lock"
            });

            _ui.Rows.Add(new Row
            {
                Left = "Forget this one",
                Right = _loaded ? "fitted" : "-",
                Enabled = _loaded,
                Note = "Takes this model back out of the table, so it falls back to the kind's " +
                       "own numbers again.",
                Tag = "clear"
            });

            _ui.Rows.Add(new Row
            {
                Left = "Zero the six",
                Right = "",
                Note = "Puts all six back to nought, which is the middle of the grip point. A " +
                       "place to start from when a model is badly out.",
                Tag = "zero"
            });

            _ui.Index = keep < _ui.Rows.Count ? keep : 0;
        }
    }
}
