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

        /// <summary>Every model anything in the catalogue holds. The fallback set.</summary>
        private string[] _models;

        /// <summary>
        /// EVERY MODEL IN THE GAME YOU COULD PUT IN A HAND, out of data/props.txt.
        ///
        /// THE SIXTY THIS MOD ALREADY USES WERE NOT ENOUGH AND THE PIZZA PROVED IT. Swapping
        /// a burger for a taco is fine off that list; finding the right model for a slice of
        /// pizza is not, because the six pizza models this game has are not on it -- nothing
        /// in the shops holds one. So the list is the game's own, filtered down to what reads
        /// as food, drink or packaging by tools/props.py, and searched by word.
        ///
        /// Fifteen hundred names is far too many to walk one at a time, which is what the
        /// search row is for: type nothing, choose a word. "pizza" is eleven of them.
        /// </summary>
        private string[] _all_models;

        /// <summary>The words offered, built from the item being fitted. See Words.</summary>
        private string[] _words = new string[0];
        private int _word;

        /// <summary>What the current word matches, or the catalogue's own set for "in use".</summary>
        private string[] _matches = new string[0];

        /// <summary>The items, and which one is on the bench.</summary>
        private Item[] _all;
        private int _at;

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
            _models = _menu.PropsByUse();
            _all_models = Models.All();

            var list = new List<Item>();
            foreach (var i in _menu.Items) if (!string.IsNullOrEmpty(i.Prop)) list.Add(i);

            // The order of the shop's own file, so walking the list is walking the menus.
            _all = list.ToArray();
            _at = 0;
            _loaded = false;

            // OFF TO ONE SIDE. Centred, this panel stands exactly on top of the man whose
            // hand you are trying to see into, which is the one thing this screen is for.
            _ui.PanelX = _cfg.FitPanelX;

            Words();
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

        /// <summary>The item on the bench.</summary>
        private Item Item_
        {
            get
            {
                if (_all == null || _all.Length == 0) return null;

                if (_at < 0) _at = _all.Length - 1;
                if (_at >= _all.Length) _at = 0;

                return _all[_at];
            }
        }

        /// <summary>And the model it is holding, which is what gets fitted.</summary>
        private string Prop
        {
            get
            {
                var item = Item_;
                return item == null ? "" : item.Prop;
            }
        }

        /// <summary>
        /// The words this item can be searched by, and what each one matches.
        ///
        /// FROM THE ITEM'S OWN NAME AND ITS OWN MODEL, because that is what somebody standing
        /// at this screen is thinking: the Pizza Slice wants "pizza", the Bottle of Wine
        /// wants "wine" or "bottle". "In use" is the sixty the shops already hold, which is
        /// the right list for swapping one known-good model for another, and "everything" is
        /// there for when none of it helps.
        /// </summary>
        private void Words()
        {
            var item = Item_;
            var words = new List<string>();

            if (item != null)
            {
                foreach (var raw in item.Name.ToLowerInvariant()
                                        .Split(' ', '-', '\'', ',', '.', '(', ')'))
                {
                    var w = raw.Trim();

                    if (w.Length < 3) continue;
                    if (w == "the" || w == "and" || w == "with" || w == "of") continue;
                    if (words.Contains(w)) continue;

                    words.Add(w);
                }

                foreach (var bit in (item.Prop ?? "").ToLowerInvariant().Split('_'))
                {
                    if (bit.Length < 4 || words.Contains(bit)) continue;
                    if (bit == "prop" || bit == "proc") continue;

                    words.Add(bit);
                }
            }

            words.Add("in use");
            words.Add("everything");

            _words = words.ToArray();
            _word = 0;

            Matches();
        }

        /// <summary>What the chosen word matches, in the game's list.</summary>
        private void Matches()
        {
            if (_words.Length == 0) { _matches = _models ?? new string[0]; return; }

            if (_word < 0) _word = _words.Length - 1;
            if (_word >= _words.Length) _word = 0;

            var word = _words[_word];

            if (word == "in use") { _matches = _models ?? new string[0]; return; }

            var found = new List<string>();
            var all = _all_models ?? new string[0];

            foreach (var name in all)
            {
                if (word == "everything" || name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found.Add(name);
                }
            }

            // A word that finds nothing is worse than no word: fall back rather than showing
            // an empty list somebody cannot get out of.
            _matches = found.Count > 0 ? found.ToArray() : (_models ?? new string[0]);
        }

        /// <summary>
        /// Puts a different model in this item's hand, now and for good.
        ///
        /// FROM WHAT THE SHOPS ALREADY USE, which is the only list where every name is
        /// guaranteed to exist in this build -- something else is holding it. Written to the
        /// ini on the press, the same as a fit, and the catalogue is told immediately so the
        /// bench is holding the new one before the row has finished redrawing.
        /// </summary>
        private void Pick(int by)
        {
            var item = Item_;
            if (item == null || _models == null || _models.Length == 0) return;

            var list = _matches != null && _matches.Length > 0 ? _matches : _models;
            if (list == null || list.Length == 0) return;

            var now = Array.IndexOf(list, item.Prop);
            if (now < 0) now = by >= 0 ? -1 : 0;

            var next = now + (by >= 0 ? 1 : -1);
            while (next < 0) next += list.Length;
            next %= list.Length;

            var model = list[next];

            _cfg.Props[item.Id] = model;
            item.Prop = model;

            try { IniFile.SetValue(Paths.Ini, "Eating", "Props", _cfg.PropsLine); }
            catch (Exception ex) { Log.Once("fit-props", "Could not write the model: " + ex.Message); }

            // A different model is a different shape, so what was fitted for the last one
            // means nothing here. Whatever this one has, or its kind's numbers.
            Load();
        }

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

            // THE HAND THE ANIMATION USES, not the one this class happens to prefer. See
            // Peek.Lefty: fitting in one hand while the game holds it in the other is a day
            // of somebody's work thrown away, and it happened.
            var held = Item_;

            var using_ = held != null && held.Smoke ? _menu.Smoke
                       : held != null && held.Drink ? _menu.Sip
                       : (held != null ? held.Eat : null) ?? _menu.Eat;

            _hand.Lefty = using_ != null && using_.LeftHanded;

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
            if (tag == "item")
            {
                _at += by >= 0 ? 1 : -1;
                Words();
                Load();
                return;
            }

            if (tag == "word")
            {
                _word += by >= 0 ? 1 : -1;
                Matches();
                return;
            }

            if (tag == "model")
            {
                Pick(by);
                return;
            }

            if (tag == "hand")
            {
                _cfg.RightHand = !_menu.RightHand;
                _menu.Hand(_cfg.RightHand);

                try { IniFile.SetValue(Paths.Ini, "Eating", "RightHand",
                                       _cfg.RightHand ? "true" : "false"); }
                catch (Exception ex) { Log.Once("fit-hand", "Could not write the hand: " + ex.Message); }

                // The clip changed, so the one running underneath is the old hand's. Dropped
                // here rather than waited out: Show re-asks within the frame.
                _animAt = 0;
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

                case "mirror":
                    // M = diag(-1, 1, 1). An offset reflects componentwise, so left/right
                    // flips. A rotation maps to M R M, which leaves the turn about the mirror
                    // axis alone and negates the other two. Pressing it twice is the identity,
                    // which is the test that it is a reflection and not an adjustment.
                    _now = new[] { -_now[0], _now[1], _now[2], _now[3], -_now[4], -_now[5] };
                    break;

                case "guess":
                    Guess();
                    break;

                case "unpick":
                    var item = Item_;
                    if (item == null) break;

                    _cfg.Props.Remove(item.Id);

                    try { IniFile.SetValue(Paths.Ini, "Eating", "Props", _cfg.PropsLine); }
                    catch { /* said once by Pick */ }

                    // The ladder in the file again, first rung this build has.
                    foreach (var name in item.Props)
                    {
                        try
                        {
                            if (!Function.Call<bool>(Hash.IS_MODEL_VALID, new Model(name).Hash)) continue;
                            item.Prop = name;
                            break;
                        }
                        catch { }
                    }

                    Load();
                    break;
            }
        }

        /// <summary>How many models the shops use that nobody has fitted yet.</summary>
        private int Unfitted()
        {
            var n = 0;

            foreach (var name in _models ?? new string[0])
            {
                if (!_cfg.Fit.ContainsKey(name)) n++;
            }

            return n;
        }

        /// <summary>
        /// Gives every unfitted model the numbers off the fitted one it most resembles.
        ///
        /// TWENTY-EIGHT HONEST ANSWERS ARE WORTH MORE THAN TWENTY-EIGHT. A cup is a cup and a
        /// tin is a tin: prop_plastic_cup_02 fitted by hand is a far better starting point
        /// for p_ing_coffeecup_02 than nought is, and nought is what every unfitted model has.
        /// So the name is the evidence -- the words in it, minus the maker's prefixes and the
        /// numbers on the end -- and the best match wins if it shares a real word.
        ///
        /// IT IS A GUESS AND IT IS WRITTEN AS ONE. Every model filled in this way can still be
        /// walked to and fixed, and fixing one does not touch the others. What it cannot do is
        /// leave a model at nought, which is the only answer that is certainly wrong.
        /// </summary>
        private void Guess()
        {
            var fitted = new List<string>(_cfg.Fit.Keys);
            if (fitted.Count == 0) { Notify("~y~Fit one first."); return; }

            var done = 0;

            foreach (var name in _models ?? new string[0])
            {
                if (_cfg.Fit.ContainsKey(name)) continue;

                var best = "";
                var score = 0;

                foreach (var other in fitted)
                {
                    var s = Alike(name, other);
                    if (s <= score) continue;

                    score = s;
                    best = other;
                }

                if (score <= 0 || best.Length == 0) continue;

                var six = _cfg.Fit[best];
                _cfg.Fit[name] = new[] { six[0], six[1], six[2], six[3], six[4], six[5] };
                done++;
            }

            if (done == 0) { Notify("~y~Nothing close enough to guess from."); return; }

            Save();
            Load();

            Notify("~g~" + done + "~s~ model(s) filled in from the " + fitted.Count +
                   " you fitted. " + _cfg.Fit.Count + " have numbers now.");
        }

        /// <summary>
        /// How alike two model names are: shared words, longest first.
        ///
        /// The prefixes every maker puts on the front -- prop, ng, proc, v, ret, res, cs, amb,
        /// p, and the trailing 01a -- carry no meaning and would match everything to
        /// everything, so they are dropped before anything is compared.
        /// </summary>
        private static int Alike(string a, string b)
        {
            var one = Words_(a);
            var two = Words_(b);

            var score = 0;

            foreach (var w in one)
            {
                foreach (var v in two)
                {
                    if (w == v) { score += w.Length; continue; }

                    // A cup and a coffeecup are the same thing with a word stuck on the front.
                    if (w.Length >= 4 && v.Length >= 4 && (w.Contains(v) || v.Contains(w))) score += 2;
                }
            }

            return score;
        }

        private static readonly string[] Noise =
        {
            "prop", "proc", "ret", "res", "amb", "ing", "cs", "ng", "sf", "apa", "ba", "bkr",
            "ch", "ex", "gr", "h4", "hei", "lux", "m23", "m24", "m25", "vw", "xm3", "xs",
            "int", "ext", "mp", "sh", "tt", "fa", "fh", "lng", "kitch", "247", "61"
        };

        private static List<string> Words_(string name)
        {
            var out_ = new List<string>();

            foreach (var raw in (name ?? "").ToLowerInvariant().Split('_'))
            {
                var w = raw.Trim();

                // The 01a on the end of half of them.
                while (w.Length > 0 && (char.IsDigit(w[w.Length - 1]) ||
                                        (w.Length > 1 && char.IsDigit(w[w.Length - 2]))))
                {
                    w = w.Substring(0, w.Length - 1);
                }

                if (w.Length < 3) continue;
                if (Array.IndexOf(Noise, w) >= 0) continue;

                out_.Add(w);
            }

            return out_;
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
                Left = "Item",
                Right = item != null ? item.Name : "-",
                Note = item == null
                    ? "Nothing in the catalogue has a model."
                    : (_at + 1) + " of " + _all.Length + "  --  " + item.Category +
                      ", " + (item.Drink ? "a drink" : item.Smoke ? "a smoke" : "food") + ".",
                Tag = "item"
            });

            _ui.Rows.Add(new Row
            {
                Left = "Search",
                Right = _words.Length > 0 ? _words[_word < _words.Length ? _word : 0] : "-",
                Note = _matches.Length + " model(s) match. The words come from this item's own " +
                       "name and its model; \"in use\" is what the shops already hold, and " +
                       "\"everything\" is every holdable model in the game.",
                Tag = "word"
            });

            _ui.Rows.Add(new Row
            {
                Left = "Model",
                Right = (prop.Length > 0 ? prop : "-") + (_loaded ? "  *" : ""),
                Note = prop.Length == 0
                    ? "This one has no model."
                    : uses + " item(s) hold this one" +
                      (_loaded ? ", and it has been fitted. " : ", not fitted yet. ") +
                      (_cfg.Props.ContainsKey(item.Id) ? "Chosen here." : "From foods.json.") +
                      "  Left and right change it.",
                Tag = "model"
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
                Left = "Hand",
                Right = _menu.RightHand ? "RIGHT" : "LEFT",
                Note = "Which hand he eats and drinks with. This moves the ANIMATION and the " +
                       "prop together -- they cannot be split, or he mimes it with an empty " +
                       "fist. Nothing in the game's name lists says which arm a clip raises, " +
                       "so look at him and pick. Saved, and live the moment you press it.",
                Tag = "hand"
            });

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
                Left = "Back to the file's model",
                Right = item != null && _cfg.Props.ContainsKey(item.Id) ? "chosen" : "-",
                Enabled = item != null && _cfg.Props.ContainsKey(item.Id),
                Note = "Forgets the model chosen here and goes back to the first one in " +
                       "foods.json that this build has.",
                Tag = "unpick"
            });

            _ui.Rows.Add(new Row
            {
                Left = "Guess the rest from these",
                Right = Unfitted() + " to go",
                Enabled = _cfg.Fit.Count > 0 && Unfitted() > 0,
                Note = "Gives every model that has not been done the numbers off the fitted " +
                       "model whose NAME is most like it -- a cup from a cup, a tin from a " +
                       "tin. A guess, and a much better starting point than nought; every one " +
                       "of them can still be walked to and fixed.",
                Tag = "guess"
            });

            _ui.Rows.Add(new Row
            {
                Left = "Mirror to the other hand",
                Right = "",
                Note = "Flips these six as if the model had been fitted in the other hand. " +
                       "The two hand bones are mirror images, so this is a reflection rather " +
                       "than a guess: left/right flips sign, and so do pitch and yaw.",
                Tag = "mirror"
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
