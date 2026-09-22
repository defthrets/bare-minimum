using System;
using System.Drawing;
using GTA;
using GTA.Native;
using BareMinimum.Bodies;
using BareMinimum.Core;

using Hud = BareMinimum.UI.Draw;

namespace BareMinimum.UI
{
    /// <summary>
    /// What somebody had on them, and who they were.
    ///
    /// THE CARD IS HALF THE POINT. A grid of things you can take is a loot screen and there
    /// are a thousand of those; a photograph, a name, a date of birth and a set turns the same
    /// grid into somebody you have just gone through the pockets of. Nothing about the numbers
    /// changes. The reading does.
    ///
    /// THE PHOTOGRAPH IS THE ACTUAL MAN. The game will render a headshot of any ped that
    /// exists, so it is asked for the one lying in front of you rather than a stand-in --
    /// which means the face on the card is the face on the pavement, down to the hat. It
    /// takes a few frames and there is a plate behind it in the meantime.
    ///
    /// A GRID, THE SAME AS THE POCKET AND THE FRIDGE. Same tiles, same cursor frame, same
    /// caps along the foot, so a player who has opened his pocket has already learnt this.
    ///
    /// IT CAME FROM POSTED UP ON 2026-09-22 and it is a rebuild rather than a copy: that mod's
    /// panel kit, curtain, gun art and drug icons are all its own, and this one has its own of
    /// the first two, none of the third and reaches over a bridge for the fourth. What is the
    /// same is every decision about what the screen SAYS.
    /// </summary>
    internal sealed class LootScreen
    {
        /// <summary>How wide the panel is, as a fraction of screen HEIGHT. See Hud.ToX.</summary>
        private const float PanelWidthH = 0.62f;
        private const float PadH = 0.024f;

        /// <summary>
        /// The card, and the photograph on it.
        ///
        /// The photograph matches the text block beside it rather than falling short of it,
        /// which is the difference between a card and a picture with some writing next to it.
        /// </summary>
        private const float CardH = 0.104f;

        /// <summary>
        /// How tall the card is for THIS body: the base two rows, plus a row each for
        /// anything another mod told us.
        ///
        /// IT HAS TO BE MEASURED, NOT ASSUMED. The item grid starts at the bottom of the
        /// card and the panel is measured from the same number, so a card that drew an extra
        /// row without growing would print it straight through the icons - which is exactly
        /// the overlapping mess this integration exists to clear up. Both uses read this, so
        /// they cannot disagree.
        /// </summary>
        private float CardHeight
        {
            get
            {
                var h = CardH;
                if (_body != null && !string.IsNullOrEmpty(_body.Occupation)) h += FieldLine;
                if (_body != null && !string.IsNullOrEmpty(_body.Note)) h += FieldLine;
                return h;
            }
        }
        private const float PhotoH = 0.096f;

        /// <summary>One square of the grid, and how many across.</summary>
        private const float TileH = 0.082f;
        private const int Columns = 5;
        private const int MaxRows = 3;

        private const float GridPad = 0.006f;

        /// <summary>How much room "Nothing on him." gets. See Paint.</summary>
        private const float EmptyH = 0.044f;

        /// <summary>The line under the grid that names the chosen thing.</summary>
        private const float NoteH = 0.050f;

        /// <summary>The gap between tiles, as an x fraction. Turned into y through the aspect.</summary>
        private const float Gap = 0.0018f;

        /// <summary>How much the chosen picture swells as its plate comes up.</summary>
        private const float PickGrow = 0.10f;

        /// <summary>How long after opening before a button press counts. See Update.</summary>
        private const int OpenGraceMs = 220;

        private const int RepeatMs = 140;
        private const int TookFlashMs = 420;

        /// <summary>How long the take button has to be held before it means all of it.</summary>
        private const int HoldMs = 420;

        private readonly Glide _frame = new Glide();

        private Corpses _bodies;
        private Body _body;
        private Ped _who;

        /// <summary>Set by Bodies.Search: he can stand up again.</summary>
        public Action Done;

        /// <summary>
        /// The carry, so the footer can offer to move him. Null when nothing set it.
        ///
        /// HIM, OFF THIS SCREEN. This is the screen you are already looking at when you want
        /// the body gone, and closing it to hunt for a second prompt is the step nobody should
        /// have to take. When the two halves were in two mods this was a reflection call
        /// through three classes; both halves are here now.
        /// </summary>
        public Drag Carry;

        private int _selected, _lastSelected = -1, _page;
        private int _openedAt, _pickedAt, _nextRepeat;
        private int _tookAt;
        private string _tookName = "";

        /// <summary>When the take button went down, and whether the hold has already fired.</summary>
        private int _downSince;
        private bool _tookAll;

        /// <summary>The headshot of the man on the floor. See the note on the class.</summary>
        private int _shot;
        private string _shotTxd = "";
        private int _shotAskedAt;

        public bool IsOpen { get; private set; }

        public void Open(Corpses bodies, Body body, Ped who)
        {
            if (bodies == null || body == null) return;

            _bodies = bodies;
            _body = body;
            _who = who;

            _selected = 0;
            _lastSelected = -1;
            _page = 0;
            _tookName = "";
            _downSince = 0;
            _tookAll = false;
            _openedAt = _pickedAt = Game.GameTime;

            // KEPT SEPARATELY, because the refusal line below is written AFTER Close has let
            // go of the body. See Buttons.
            _tookFemale = body.Female;

            _frame.Reset();

            Photo();

            IsOpen = true;

            Sound("SELECT");
        }

        public void Close()
        {
            if (!IsOpen) return;

            IsOpen = false;

            DropPhoto();

            _bodies = null;
            _body = null;
            _who = null;

            if (Done != null) Done();
        }

        // ---- the photograph -----------------------------------------------------

        /// <summary>
        /// Asks the game for a picture of him.
        ///
        /// ITS OWN REGISTRATION, NOT A CACHE. A cache of faces made from MODELS is right for
        /// an account on a feed and wrong here -- two men of the same model are two different
        /// bodies and would share one photograph. This asks for the ped itself and hands the
        /// slot straight back when the screen shuts, so it never holds one for long.
        /// </summary>
        private void Photo()
        {
            DropPhoto();

            _shotAskedAt = Game.GameTime;

            try
            {
                if (_who != null && _who.Exists())
                {
                    _shot = Function.Call<int>(Hash.REGISTER_PEDHEADSHOT, _who.Handle);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Loot: no headshot of the body: " + ex.Message);
                _shot = 0;
            }
        }

        private void DropPhoto()
        {
            try
            {
                if (_shot != 0) Function.Call(Hash.UNREGISTER_PEDHEADSHOT, _shot);
            }
            catch
            {
                // The game takes it back on its own eventually.
            }

            _shot = 0;
            _shotTxd = "";
        }

        /// <summary>The texture once the game has finished making it, or "" while it has not.</summary>
        private string Made()
        {
            if (!string.IsNullOrEmpty(_shotTxd)) return _shotTxd;
            if (_shot == 0) return "";

            try
            {
                if (!Function.Call<bool>(Hash.IS_PEDHEADSHOT_READY, _shot)) return "";
                if (!Function.Call<bool>(Hash.IS_PEDHEADSHOT_VALID, _shot)) { _shot = 0; return ""; }

                _shotTxd = Function.Call<string>(Hash.GET_PEDHEADSHOT_TXD_STRING, _shot) ?? "";

                if (!string.IsNullOrEmpty(_shotTxd))
                {
                    Log.Debug("Loot: photographed the body in " +
                              (Game.GameTime - _shotAskedAt) + "ms.");
                }

                return _shotTxd;
            }
            catch
            {
                return "";
            }
        }

        // ---- input --------------------------------------------------------------

        public void Update()
        {
            if (!IsOpen) return;

            try
            {
                Menu.Suppress();

                if (Game.GameTime - _openedAt < OpenGraceMs) { Paint(); return; }

                // THE BODY IS STILL THERE, ISN'T IT. The game cleans corpses up on its own
                // clock and it does not care that somebody is looking through this one.
                if (_who == null || !_who.Exists() || _body == null)
                {
                    Close();
                    return;
                }

                if (Buttons()) return;

                Paint();
            }
            catch (Exception ex)
            {
                Log.Once("loot", "The body card failed: " + ex.Message);
                Close();
            }
        }

        /// <summary>The whole of the input. True when the screen has shut and there is nothing to draw.</summary>
        private bool Buttons()
        {
            // HIM, OFF THIS SCREEN. The card closes FIRST: the carry puts an animation on the
            // player and welds a body to his chest, and neither of those is something to start
            // underneath a full-screen panel.
            if (CanCarry && Pressed(GTA.Control.Jump))
            {
                var who = _who != null && _who.Exists() ? _who.Handle : 0;

                Close();

                if (who != 0 && !Carry.TakeByHandle(who))
                {
                    Hint.Show("Can't get hold of " + (_tookFemale ? "her" : "him") + " from there");
                }

                return true;
            }

            if (Pressed(GTA.Control.FrontendCancel) || Pressed(GTA.Control.FrontendPause))
            {
                Sound("BACK");
                Close();
                return true;
            }

            if (_body.Items.Count == 0)
            {
                _downSince = 0;
                _tookAll = false;

                // Nothing left. One press of the take button and it is over.
                if (Pressed(GTA.Control.FrontendAccept)) { Close(); return true; }

                return false;
            }

            if (Pressed(GTA.Control.FrontendUp)) Move(0, -1);
            else if (Pressed(GTA.Control.FrontendDown)) Move(0, 1);
            else if (Pressed(GTA.Control.FrontendLeft)) Move(-1, 0);
            else if (Pressed(GTA.Control.FrontendRight)) Move(1, 0);

            // ONE TAKES IT, AND HELD TAKES THE LOT.
            //
            // TAP AND HOLD RATHER THAN TWO BUTTONS. The lot is the same action done harder, so
            // it is the same button held -- nothing extra to learn and nothing extra to reach
            // for. The tap fires on RELEASE, because a tap that fired on the press would take
            // one and then take the lot a moment later while the finger was still down.
            //
            // AND ONLY THE CONFIRM BUTTON. In the mod this came from it also read the context
            // key, and the context key on a pad is D-PAD RIGHT -- the same button four lines
            // above moves the cursor right. So a direction both moved the selection and took
            // what was under it, which is not a list you can choose from, it is a list that
            // empties itself while you look at it.
            if (Held(GTA.Control.FrontendAccept))
            {
                if (_downSince == 0) _downSince = Game.GameTime;

                if (!_tookAll && Game.GameTime - _downSince >= HoldMs)
                {
                    _tookAll = true;

                    if (Game.GameTime >= _nextRepeat) Everything();
                }

                return false;
            }

            if (_downSince == 0) return false;

            var brief = !_tookAll;

            _downSince = 0;
            _tookAll = false;

            if (brief && Game.GameTime >= _nextRepeat) One();

            return false;
        }

        /// <summary>Kept so the refusal line still has a pronoun after the card has been let go of.</summary>
        private bool _tookFemale;

        /// <summary>Whether the carry is here, switched on, and free to take somebody.</summary>
        private bool CanCarry
        {
            get
            {
                try { return Carry != null && Carry.Allowed && !Carry.Holding; }
                catch { return false; }
            }
        }

        private void Move(int dx, int dy)
        {
            var count = _body.Items.Count;
            if (count == 0) return;

            var at = Clamp(_selected, 0, count - 1);

            var to = dy != 0 ? at + dy * Columns : at + dx;

            if (to < 0 || to >= count) return;

            _lastSelected = at;
            _selected = to;
            _pickedAt = Game.GameTime;

            var perPage = Columns * MaxRows;
            _page = perPage <= 0 ? 0 : to / perPage;

            Sound("NAV_UP_DOWN");
        }

        private void One()
        {
            if (_selected < 0 || _selected >= _body.Items.Count) return;

            var item = _body.Items[_selected];

            string why;

            if (!_bodies.Take(_body, item, out why))
            {
                Log.Info("Loot: would not take " + item.Name + " (" + why + ").");

                Hint.Show(Capital(why));

                Sound("ERROR");
                _nextRepeat = Game.GameTime + RepeatMs * 3;
                return;
            }

            _tookAt = Game.GameTime;
            _tookName = item.Name;
            _nextRepeat = Game.GameTime + RepeatMs;

            Sound("NAV_UP_DOWN");

            if (_selected >= _body.Items.Count) _selected = Math.Max(0, _body.Items.Count - 1);
            _lastSelected = -1;
            _pickedAt = Game.GameTime;
        }

        /// <summary>
        /// The lot, in one press.
        ///
        /// FROM THE END BACKWARDS, because taking one shortens the list under the cursor and
        /// walking forwards through a list that is shrinking skips every other entry. It also
        /// stops at the first refusal rather than grinding through fourteen "pockets are full"
        /// messages.
        /// </summary>
        private void Everything()
        {
            var moved = 0;

            for (var i = _body.Items.Count - 1; i >= 0; i--)
            {
                var item = _body.Items[i];

                string why;

                if (!_bodies.Take(_body, item, out why))
                {
                    if (moved == 0)
                    {
                        Hint.Show(Capital(why));
                        Sound("ERROR");
                    }

                    break;
                }

                moved++;
                _tookName = item.Name;
            }

            if (moved == 0)
            {
                _nextRepeat = Game.GameTime + RepeatMs * 3;
                return;
            }

            _tookAt = Game.GameTime;
            _nextRepeat = Game.GameTime + RepeatMs * 2;

            _selected = 0;
            _lastSelected = -1;
            _pickedAt = Game.GameTime;

            Sound("NAV_UP_DOWN");
        }

        private static string Capital(string words)
        {
            if (string.IsNullOrEmpty(words)) return words;

            return char.ToUpperInvariant(words[0]) + words.Substring(1);
        }

        // ---- drawing ------------------------------------------------------------

        /// <summary>
        /// Whoever he ran with, as a colour. Green for the Families, purple for the Ballas,
        /// yellow for the Vagos -- see Corpses.Tint.
        /// </summary>
        private Color Mine => _body == null ? Palette.Brand : _body.Colour;

        private void Paint()
        {
            if (!IsOpen || _body == null) return;

            var arrive = Theme.Arrive(_openedAt, Theme.EnterMs);

            var count = _body.Items.Count;

            var rows = count == 0 ? 1 : (count + Columns - 1) / Columns;
            if (rows > MaxRows) rows = MaxRows;

            var panelWidth = Hud.ToX(PanelWidthH);
            var pad = Hud.ToX(PadH);

            var tileW = (panelWidth - pad * 2f) / Columns;
            var tileH = TileH;

            // AN EMPTY BODY IS NOT A ROW OF NOTHING. A pocketful of air was given a whole
            // tile's worth of height to say "Nothing on him." in, so the one case with the
            // least to show got the biggest hole in the middle of it.
            var shelf = count == 0 ? EmptyH : rows * tileH;

            var height = Kit.HeadH + CardHeight + GridPad + shelf + GridPad + NoteH + Kit.FootH;

            var left = 0.5f - panelWidth * 0.5f;
            var top = 0.5f - height * 0.5f + Theme.EnterRise * (1f - arrive);

            Theme.Panel(left, top, panelWidth, height, arrive);

            var x = left + pad;
            var right = left + panelWidth - pad;
            var wide = right - x;

            // NO MARK BESIDE THE TITLE. Every other head in this mod has one -- a bag, a
            // fridge, a shop's sign -- and there is no picture of a corpse in data\icons, so
            // the row is the words, the set, and the rule under them. Kit.Head hands back
            // where it ended, which is top + HeadH, and that is what the panel was measured
            // with.
            var y = Kit.Head(left, top, panelWidth, pad, null, "THE BODY",
                             "what was on " + _body.Him, null,
                             _body.Affiliation.ToUpperInvariant(), arrive, 0f);

            Identity(x, y, wide, arrive);

            y += CardHeight + GridPad;

            _frame.Begin();

            Grid(x, y, tileW, tileH, rows, shelf, arrive);

            y += shelf + GridPad;

            Note(x, y, wide, arrive);

            Foot(x, right, top + height - Kit.FootH + 0.004f, arrive);

            _frame.Draw(arrive);
        }

        /// <summary>
        /// The card: his photograph, his name, and the four things a card carries.
        ///
        /// TWO COLUMNS OF LABELLED FIELDS rather than a sentence, because that is what an
        /// identity card looks like and the shape is doing as much work here as the words.
        /// </summary>
        private void Identity(float x, float y, float wide, float arrive)
        {
            var ink = Palette.Alpha(Palette.Text, (int)(252f * arrive));
            var dim = Palette.Alpha(Palette.TextDim, (int)(200f * arrive));

            // The quiet tone for a LABEL, which is a different job from a value nobody has
            // read yet. Below the dim grey the panel uses for text somebody is meant to read.
            var quiet = Color.FromArgb((int)(150f * arrive), 168, 172, 176);

            var photoW = Hud.ToX(PhotoH);

            // The plate the picture sits on, so the space reads as a photograph before there
            // is one in it -- and a hairline round it, because a photograph on a card has an
            // edge and a floating rectangle of face does not.
            Hud.Bar(x, y, photoW, PhotoH, Color.FromArgb((int)(30f * arrive), 255, 255, 255));

            var made = Made();

            if (!string.IsNullOrEmpty(made))
            {
                Face(made, x + photoW * 0.5f, y + PhotoH * 0.5f, photoW, PhotoH, arrive);
            }
            else
            {
                Hud.Text("NO PHOTO", x + photoW * 0.5f, y + PhotoH * 0.5f - 0.008f, 0.22f,
                         quiet, Hud.FontLabel, true);
            }

            Frame(x, y, photoW, PhotoH, Color.FromArgb((int)(46f * arrive), 255, 255, 255));

            var tx = x + photoW + 0.014f;
            var room = wide - photoW - 0.014f;

            // THE NAME IS THE CONDENSED FACE IN CAPITALS, which is how a name is printed on
            // every licence anybody has ever been handed. Long ones are stepped down rather
            // than cut, because a surname with the end trimmed off is worse than a smaller one.
            var name = _body.Name.ToUpperInvariant();
            var size = Sized(name, NameSize, room);

            Hud.Text(name, tx, y - 0.001f, size, ink, Hud.FontLabel);

            // A RULE UNDER IT, in whoever's colours he ran in. One line does most of the work
            // of making this read as a card rather than as four labels next to a photograph:
            // it separates the person from the particulars, which is what the box on a real
            // one is doing.
            Theme.Rule(tx, y + 0.029f, room, Mine, arrive * 0.75f);

            // The four fields, two to a line, on the same grid so the labels line up.
            var half = room * 0.52f;
            var line = y + 0.038f;

            Field(tx, line, "D.O.B.", _body.Born, quiet, dim);
            Field(tx + half, line, "HEIGHT", _body.Height, quiet, dim);

            line += FieldLine;

            Field(tx, line, "ETHNICITY", _body.Ethnicity, quiet, dim);
            Field(tx + half, line, "AFFILIATION", _body.Affiliation, quiet, dim);

            // ---- what another mod knows and this one cannot --------------------------
            // TWO ROWS THAT ONLY EXIST WHEN SOMEBODY FILLED THEM. Everything above is
            // invented here from the handle and the model - consistent, so the same man is
            // the same man, but never actually true. A mod that has SPOKEN to him knows what
            // he did for a living and how many times the two of you talked, and those are
            // the two facts that turn this from a receipt into an accusation. Empty means
            // nobody told us and the row is left out rather than padded with a guess.
            // CardHeight counts these, so they cannot print through the icons. See
            // Api.Bodies.WireIdentity.
            if (!string.IsNullOrEmpty(_body.Occupation))
            {
                line += FieldLine;
                Field(tx, line, "OCCUPATION", _body.Occupation, quiet, dim);
            }

            if (!string.IsNullOrEmpty(_body.Note))
            {
                line += FieldLine;
                // HIS OR HERS, from the body's own pronoun. The note is the one label on
                // this card that has to agree with the person lying on the pavement, and
                // this mod already had a bug where every line of copy said "him" over a
                // woman. Not repeating it two lines below the comment about it.
                Field(tx, line, "YOU AND " + _body.Him.ToUpperInvariant(), _body.Note, quiet, dim);
            }
        }

        /// <summary>
        /// The headshot, drawn straight off the game's own texture.
        ///
        /// NOT THROUGH Hud.Sprite, WHICH IS FOR THIS MOD'S PNGs. That path loads a file off
        /// disk into a CustomSprite; a pedheadshot is a texture the GAME made in memory, named
        /// by a dictionary string it hands back, and the only way to put it on screen is
        /// DRAW_SPRITE with that name. It is one sprite and it is not a rectangle, so it costs
        /// nothing against the shared DRAW_RECT budget the mods on this machine share.
        /// </summary>
        private static void Face(string txd, float cx, float cy, float w, float h, float arrive)
        {
            try
            {
                Function.Call(Hash.DRAW_SPRITE, txd, txd, cx, cy, w, h, 0f,
                              255, 255, 255, (int)(255f * arrive));
            }
            catch
            {
                // The plate behind it is still a photograph-shaped hole, which reads.
            }
        }

        /// <summary>One labelled particular: the caption above, the answer below.</summary>
        ///
        /// <remarks>
        /// THE TWO USED TO TOUCH. The label sat at y, the value eleven thousandths under it at
        /// a size thirteen thousandths tall, and the next line started twenty-four thousandths
        /// down -- so the bottom of every value was exactly where the next label began, and on
        /// a real screen ETHNICITY was printed through the date of birth. The gap is now bigger
        /// than the thing that goes in it, which is the only arrangement that cannot collide.
        /// </remarks>
        private static void Field(float x, float y, string label, string value, Color quiet, Color ink)
        {
            Hud.Text(label, x, y, 0.20f, quiet, Hud.FontLabel);
            Hud.Text(value, x, y + 0.0125f, 0.28f, ink, Hud.FontBody);
        }

        /// <summary>
        /// The size a name is set at: the display size, unless it is too wide for the card.
        ///
        /// Stepped down by exactly the amount it is over, in one measurement, because the
        /// game's text scale multiplies glyph widths. A title that runs off the edge is not a
        /// title.
        /// </summary>
        private static float Sized(string words, float want, float max)
        {
            if (string.IsNullOrEmpty(words) || max <= 0f) return want;

            var w = Hud.Width(words, want, Hud.FontLabel);

            return w <= max || w <= 0f ? want : want * (max / w);
        }

        /// <summary>A hairline round a rectangle: four thin bars, no fill.</summary>
        private static void Frame(float x, float y, float w, float h, Color ink)
        {
            const float t = 0.0011f;

            var tv = t * Hud.Aspect;

            Hud.Bar(x, y, w, tv, ink);
            Hud.Bar(x, y + h - tv, w, tv, ink);
            Hud.Bar(x, y, t, h, ink);
            Hud.Bar(x + w - t, y, t, h, ink);
        }

        /// <summary>How big the name is set, and the drop from one field line to the next.</summary>
        private const float NameSize = 0.62f;
        private const float FieldLine = 0.029f;

        private void Grid(float x, float y, float tileW, float tileH, int rows, float shelf, float arrive)
        {
            if (_body.Items.Count == 0)
            {
                Hud.Text("Nothing on " + _body.Him + ".", x + (tileW * Columns) * 0.5f,
                         y + shelf * 0.5f - 0.010f, 0.30f,
                         Palette.Alpha(Palette.TextDim, (int)(190f * arrive)), Hud.FontBody, true);
                return;
            }

            var perPage = Columns * rows;
            var first = _page * perPage;

            var age = Game.GameTime - _openedAt;
            var grown = Theme.Grown(_pickedAt);

            for (var i = 0; i < perPage; i++)
            {
                var at = first + i;
                if (at >= _body.Items.Count) break;

                var col = i % Columns;
                var row = i / Columns;

                var land = Kit.Landed(age, i * 35, Theme.EnterMs);

                var show = arrive * land;
                if (show <= 0.01f) continue;

                var tx = x + col * tileW;
                var ty = y + row * tileH + Theme.EnterRise * 0.5f * (1f - land);

                var picked = at == _selected;
                var lit = Theme.Lit(at, _selected, _lastSelected, grown);

                Square(_body.Items[at], tx, ty, tileW, tileH, lit, show, picked, grown);

                if (picked)
                {
                    _frame.Target(tx + Gap, ty + Gap * Hud.Aspect,
                                  tileW - Gap * 2f, tileH - Gap * 2f * Hud.Aspect);
                }
            }

            var pages = (_body.Items.Count + perPage - 1) / perPage;
            if (pages <= 1) return;

            Hud.TextRight((_page + 1) + " / " + pages, x + tileW * Columns - 0.004f,
                          y + rows * tileH - 0.018f, 0.22f,
                          Palette.Alpha(Palette.TextDim, (int)(190f * arrive)), Hud.FontLabel);
        }

        /// <summary>One square: the ground, the plate under the cursor, the picture, the chip.</summary>
        private void Square(LootItem item, float x, float y, float w, float h, float lit, float show,
                            bool picked, float grown)
        {
            var gx = Gap;
            var gy = Gap * Hud.Aspect;

            var tx = x + gx;
            var ty = y + gy;
            var tw = w - gx * 2f;
            var th = h - gy * 2f;

            Hud.Bar(tx, ty, tw, th, Color.FromArgb((int)(26f * show), 255, 255, 255));

            Theme.Plate(tx, ty, tw, th, lit * show);

            var swell = picked ? 1f + PickGrow * grown : 1f;
            var cx = tx + tw / 2f;
            var cy = ty + th * 0.44f;

            var ink = Theme.Ink(Palette.Alpha(item.Tint, (int)(230f * show)), lit);

            // A PICTURE WHERE THERE IS ONE, AND A WORD WHERE THERE IS NOT.
            //
            // THIS MOD SHIPS NO GUN ART AND NO PICTURE OF A ROLL OF NOTES. The three hundred
            // icons in data\icons are food, and the drugs are the other mod's files borrowed
            // over the bridge -- so a burger and a bag of meth have a shape and a pistol and
            // ninety dollars do not. They get the thing itself set in the middle of the tile
            // instead: the amount for cash, the gun's name for a gun. Not a placeholder --
            // "$96" in money green is a more useful tile than a picture of a banknote -- but
            // a drawn gun would still be better, and is the obvious next thing.
            if (!string.IsNullOrEmpty(item.Icon))
            {
                var icon = IconCache.Get(item.Icon);

                if (icon != null && !icon.Missing)
                {
                    var tall = th * 0.56f * swell;

                    icon.DrawSized(cx, cy, Hud.ToX(tall), tall, ink);
                }
            }
            else if (item.Kind == LootKind.Money)
            {
                var words = "$" + item.Count;
                var size = Sized(words, 0.52f * swell, tw - 0.008f);

                Hud.Text(words, cx, cy - Hud.Height(size, Hud.FontLabel) * 0.5f, size,
                         ink, Hud.FontLabel, true);
            }
            else
            {
                var words = Kit.Fit(item.Name.ToUpperInvariant(), tw - 0.008f, 0.26f, Hud.FontLabel);

                Hud.Text(words, cx, cy - Hud.Height(0.26f, Hud.FontLabel) * 0.5f, 0.26f,
                         ink, Hud.FontLabel, true);
            }

            if (string.IsNullOrEmpty(item.Tag)) return;

            const float chipH = 0.013f;

            var chipW = Hud.Width(item.Tag, 0.22f, Hud.FontLabel) + Hud.ToX(0.007f);

            Hud.Bar(tx + tw - chipW, ty + th - chipH, chipW, chipH,
                    Color.FromArgb((int)(215f * show), 12, 13, 15));

            Hud.TextRight(item.Tag, tx + tw - 0.0022f, ty + th - chipH + 0.0005f, 0.22f,
                          Palette.Alpha(Palette.Text, (int)(255f * show)), Hud.FontLabel);
        }

        /// <summary>The line under the grid: the chosen thing, said properly.</summary>
        private void Note(float x, float y, float wide, float arrive)
        {
            Theme.Plate(x, y, wide, NoteH - 0.006f, 0.55f * arrive);

            var tx = x + 0.010f;

            // WHAT JUST CAME OFF HIM, for a moment, over the top of everything else. It is
            // the one thing worth saying at the instant it happens.
            var flash = Kit.Flash(_tookAt, TookFlashMs);

            if (flash > 0f && !string.IsNullOrEmpty(_tookName))
            {
                Hud.Text("Took the " + _tookName.ToLowerInvariant() + ".", tx, y + 0.008f, 0.32f,
                         Palette.Alpha(Palette.Cash, (int)(255f * arrive)), Hud.FontBody);
                return;
            }

            if (_body.Items.Count == 0)
            {
                Hud.Text("That's everything " + _body.He + " had.", tx, y + 0.008f, 0.32f,
                         Palette.Alpha(Palette.TextDim, (int)(215f * arrive)), Hud.FontBody);
                return;
            }

            var at = Clamp(_selected, 0, _body.Items.Count - 1);
            var item = _body.Items[at];

            var grown = Theme.Grown(_pickedAt);

            Theme.Caption(item.Name, tx, y + 0.008f, grown, 0.32f);

            var says = About(item);

            if (string.IsNullOrEmpty(says)) return;

            Hud.Text(Kit.Fit(says, wide - 0.014f, 0.25f, Hud.FontBody), tx, y + 0.030f, 0.25f,
                     Palette.Alpha(Palette.TextDim, (int)((110f + 90f * grown) * arrive)),
                     Hud.FontBody);
        }

        private string About(LootItem item)
        {
            switch (item.Kind)
            {
                case LootKind.Gun:
                    return item.Count > 0
                        ? "Comes with the " + item.Count + " rounds still in it."
                        : "Empty. You'll need to feed it.";

                case LootKind.Money:
                    return "Straight in your pocket.";

                case LootKind.Food:
                    return _body.Female ? "She wasn't going to eat it."
                                        : "He wasn't going to eat it.";

                case LootKind.Drug:
                    return Math.Round(item.Purity * 100f) + "% pure, bagged up.";
            }

            return "";
        }

        private void Foot(float x, float right, float y, float arrive)
        {
            Theme.Rule(x, y, right - x, Mine, arrive);

            var ky = y + 0.011f;

            Kit.KeyRight(right, ky, Kit.Back, _body.Female ? "LEAVE HER" : "LEAVE HIM", arrive);

            var kx = x;

            if (_body.Items.Count > 0)
            {
                kx = Kit.Key(kx, ky, null, "arrow_leftright.png", "PICK", arrive);
                kx = Kit.Key(kx, ky, Kit.Confirm, null, "TAKE IT", arrive);
                kx = Kit.Key(kx, ky, Kit.Confirm, null, "HOLD FOR THE LOT", arrive);
            }

            // ---- AND THEN MOVE HIM ----
            //
            // DRAWN ONLY WHEN IT WOULD WORK. The carry switched off, or already holding
            // somebody, and the key is not offered -- a key on a footer that does nothing is
            // worse than no key. See CanCarry.
            if (CanCarry)
            {
                Kit.Key(kx, ky, Kit.Drop, null, _body.Female ? "CARRY HER" : "CARRY HIM", arrive);
            }
        }

        private static int Clamp(int v, int lo, int hi)
        {
            return v < lo ? lo : v > hi ? hi : v;
        }

        // ---- controls -----------------------------------------------------------

        private static bool Pressed(GTA.Control control)
        {
            try
            {
                return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)control);
            }
            catch
            {
                return false;
            }
        }

        private static bool Held(GTA.Control control)
        {
            try
            {
                return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)control);
            }
            catch
            {
                return false;
            }
        }

        private static void Sound(string name)
        {
            try
            {
                Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, name,
                              "HUD_FRONTEND_DEFAULT_SOUNDSET", true);
            }
            catch
            {
                // A menu without a click is still a menu.
            }
        }
    }
}
