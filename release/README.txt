===============================================================================
  BARE MINIMUM 0.1.2  --  hunger and sleep for GTA V
  by spitmux
===============================================================================

  Franklin gets hungry and he gets tired. Two thin gauges sit low on the left
  and quietly empty until you do something about it.

  There are more than a hundred places to eat, and they are the real ones --
  the 24/7 you walk past every mission, the taco window, the hot dog cart on
  the corner, Burger Shot, Bean Machine, Rob's Liquor, the corner store with
  the grille on the window. Every one has its own menu, its own prices and its
  own opinions about you.

  THIS IS A WORK IN PROGRESS, and the part still growing is the map. Everything
  in here works and the shops that are in are finished -- but not every place in
  the city is in yet. The convenience stores and the fuel stations are the thin
  part. More go in with every update, and they are plain text in vendors.json,
  so you do not have to wait for me to add the one on your corner.

  No game file is modified and your save is never touched. Delete three things
  and it never happened.


-------------------------------------------------------------------------------
  INSTALL
-------------------------------------------------------------------------------

  Drag everything in this zip into your GTA V folder and say yes to merging.
  That is the folder with GTA5.exe in it, normally:

    C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V

  Load a save. A line in the top left says it is running.

  UNBLOCK THE ZIP BEFORE YOU EXTRACT IT. Windows marks anything downloaded from
  the internet, and ScriptHookVDotNet refuses to load a marked DLL. It does not
  say so -- the mod is simply not there, with nothing in any log to explain it.
  Right-click the zip, Properties, tick "Unblock", OK, and THEN extract. If you
  already extracted it, delete that, unblock the zip and extract it again.

  YOU ALSO NEED these, if you do not already have them:

    ScriptHookV          http://www.dev-c.com/gtav/scripthookv/
    ScriptHookVDotNet 3  https://github.com/scripthookvdotnet/scripthookvdotnet

  If you run any other .NET script mod you already have both. This was built
  against ScriptHookVDotNet 3.9.0. GTA V Enhanced needs 3.6 or newer; an older
  copy may load on Legacy only and Bare Minimum will simply not start.

  Works on both editions from the same files.


-------------------------------------------------------------------------------
  CONTROLS
-------------------------------------------------------------------------------

    E          buy, order, open a fridge, get into a bed
    F11        your pocket -- what you have bought and not eaten yet
    F7         settings, in game, with everything live as you change it

  In any panel:

    arrows     move             ENTER   choose
    Q and E    change tab       SPACE   move a thing in or out of the fridge
    BACKSPACE  close

  ON A CONTROLLER: the context button does everything E does -- buy, order, open
  a fridge, get into a bed. The two panels open on a chord, because a pad has no
  button spare:

    LB + D-pad UP      settings
    LB + D-pad LEFT    your pocket

  Inside a panel the d-pad moves, A chooses and B closes, and the key hints
  change to those glyphs the moment you pick a pad up. Fumes uses LB + D-pad
  DOWN for its own menu, so these two stay clear of it.


-------------------------------------------------------------------------------
  THE TWO GAUGES
-------------------------------------------------------------------------------

  Hunger on the left, sleep on the right, standing on the bottom edge of the
  screen out past the minimap. An apple and a moon underneath say which is
  which.

  They are animated, and the two move DIFFERENTLY on purpose. Hunger churns,
  and the emptier it gets the more agitated it becomes, which is the opposite
  of a fuel gauge. Sleep breathes as a whole, and the breath slows and deepens
  as you tire.

  THERE IS A SECOND HUD IN THE BOX. Set HUD style to ICONS and the bars come
  off, leaving the apple and the moon on their own -- each going through five
  drawings as it empties, the apple eaten down to a core, the eye closing -- so
  you read the state from the shape rather than the length. It tells you less
  on purpose. F7 -> HUD -> HUD style switches between them as you press.

  The needs run on the GAME clock, not the real one. A game hour is about two
  real minutes, so a full day is roughly 48 minutes of play.

  RUNNING COSTS MORE. Sprinting empties both faster than walking, and the HUD
  visibly speeds up while you do it, so the reason is on screen.

  LET HUNGER HIT NOTHING and it starts costing health, slowly, with a warning
  first. It will never kill you -- it stops well above the floor and leaves you
  there.

  LET SLEEP HIT NOTHING and the picture starts to swim. Stay up through that
  and you collapse where you stand and wake up four hours later.


-------------------------------------------------------------------------------
  EATING
-------------------------------------------------------------------------------

  Walk up to any shop counter, stall, window or shelf and press E.

  ONE-KEY STALLS hand you the thing and he eats it on the spot: the hot dog
  carts, the taco windows. SHOPS WITH A MENU let you browse -- tabs across the
  top, a picture and a price per row, and a line about each one.

  DRIVE-THROUGHS work from the driver's seat. Pull up to the window and press
  E. A meal at the wheel is eaten slowly between junctions instead of in four
  seconds at the window, and the horn is held off while the menu is up.

  WHAT YOU BUY GOES IN YOUR POCKET by default, three things at a time, for
  later. Turn that off in the settings and everything is eaten where you stand.

  Some places only do some things at some hours. A breakfast menu at midnight
  is shown greyed with the hours on it, rather than quietly missing, so you
  learn to come back.


-------------------------------------------------------------------------------
  THE FRIDGE
-------------------------------------------------------------------------------

  Every safehouse kitchen has one -- Franklin's aunt's, Michael's, Trevor's
  trailer. Stand at it and press E.

  Your pocket is on the left and the fridge on the right. LEFT and RIGHT cross
  between them, SPACE moves one across, ENTER eats it from either side. It
  holds forty things, which is what makes stocking up worth the trip home.

  Found by looking for a fridge, not by a coordinate, so any kitchen the game
  put one in works -- including ones added by other mods.


-------------------------------------------------------------------------------
  SLEEPING
-------------------------------------------------------------------------------

  Any bed. Stand at it and press E, pick how long, and the screen fades.

  Sleeping also sobers you up, because it advances the same clock everything
  else runs on. A night in a bed is a night in a bed.

  Sleeping rough is a different thing from sleeping in a safehouse and rests
  you less. Sleeping in a car is worse again but it is there when you need it.


-------------------------------------------------------------------------------
  SETTINGS
-------------------------------------------------------------------------------

  F7 opens the menu. A hundred and one settings across six tabs, every one with a
  line saying what it does, and everything takes effect as you turn it.

  Everything is also in scripts\BareMinimum.ini, which is commented at length
  and is NEVER overwritten by an update. That is deliberate -- it is the file
  you hand-edit. When a new version adds a setting, the console prints which
  ones are missing and the defaults apply until you add them.

  The F7 menu writes back to that file, but only the single lines whose values
  you changed. Your comments and your ordering are left exactly as they are.

  Worth knowing about:

    General    turn the whole mod off without uninstalling it
    Hunger     how fast it empties, and whether it costs health at zero
    Sleep      the same, plus the collapse
    HUD        where the marks sit, how big, bars instead of icons, motion off
    Map        markers for shops. All shops share one legend row so the pause
               map is not a directory -- left and right slide through them
    Money      prices, and whether buying fills your pocket or your stomach
    Counters   the shop counters, and hiding the game's own list
    Fridge     on, off, and how much it holds
    Keys       every key in the mod

  If you cannot see the marks, they are beside the minimap and they are small.
  HUD -> Size, or switch to the bar style.


-------------------------------------------------------------------------------
  THE FOOD ITSELF
-------------------------------------------------------------------------------

  scripts\BareMinimum\foods.json is every item: name, price, how much it fills
  you, what it looks like in his hand, and the line the shop says about it.

  scripts\BareMinimum\vendors.json is every shop: where it is, what it stocks,
  what hours it keeps and whether it gets a map marker.

  Both are plain text and both are yours to edit. Add a shop, change a price,
  give somewhere a menu it does not have. Anything the game cannot find is
  skipped with a line in the log rather than breaking the mod.


-------------------------------------------------------------------------------
  IF YOU ALSO RUN HOODRICH
-------------------------------------------------------------------------------

  The shops post on Hoodrich's social feed. Seventy-one accounts -- the hot dog
  man with opinions about the weather, a buffet that has never undersold
  itself, a corner store that has been closing down for three weeks.

  This is entirely optional and entirely one-way. Without Hoodrich installed
  nothing happens, nothing is created and nothing is logged beyond one line
  saying why. Bare Minimum has no feed of its own and is not growing one.


-------------------------------------------------------------------------------
  UNINSTALL
-------------------------------------------------------------------------------

  Delete these three and it is gone:

    scripts\BareMinimum.dll
    scripts\BareMinimum.ini
    scripts\BareMinimum\        (the folder)

  No game file is modified by this mod and nothing is written to your save.
  Your hunger and sleep levels live in scripts\BareMinimum\needs.json, which
  goes with the folder.


-------------------------------------------------------------------------------
  IF SOMETHING IS WRONG
-------------------------------------------------------------------------------

  1. THE MOD IS NOT THERE AT ALL.

     Almost always the blocked-file thing above. Delete what you extracted,
     unblock the ZIP, extract again.

     Otherwise: no ScriptHookVDotNet, or too old a one. Check that
     ScriptHookVDotNet.asi is next to GTA5.exe and that ScriptHookVDotNet.log
     in that same folder does not name Bare Minimum in an error.

  2. IT LOADS BUT NOTHING HAPPENS.

     scripts\BareMinimum\BareMinimum.log is written every session and says what
     it found: how many shops, how many items, which props this build of the
     game does not have. Set LogLevel = Debug in the ini for the loud version.

  3. A SHOP IS NOT WHERE I EXPECT IT.

     Every shop is a coordinate in vendors.json. Move it, or add your own.

  4. I WANT THE OTHER HUD.

     There are two and neither is a fault. BARS is what ships; ICONS replaces
     them with the apple and the moon on their own, changing shape as they
     empty.

     F7 -> HUD -> HUD style -> press right. It changes as you press and it is
     written to the ini for you, so there is nothing to restart. In the file it
     is Style = Bars or Style = Icons under [HUD] in scripts\BareMinimum.ini.

     BareMinimum.log names the style it drew on the line beginning "HUD:", if
     you want to be sure of what you are running.

  5. I WANT IT QUIETER.

     HUD -> Animate turns off every moving thing on screen, including the
     panels. General -> Enabled turns the whole mod off and leaves the F7 menu
     working so you can turn it back on.


===============================================================================
  Nothing in here modifies a game file. Nothing in here touches your save.
===============================================================================
