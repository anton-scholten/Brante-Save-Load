# Working on this repository

Notes for anyone, human or AI agent, changing this mod. The [README](README.md) is
for people who just want to use it.

## What it is

A mod for [The Life and Suffering of Sir Brante](https://store.steampowered.com/app/1272160/)
(Unity 2018.3, Windows build under Proton): load the game at **any choice you have
already reached**, plus a save/load system of its own whose saves live outside the
game's own. Two tabs the mod adds to the top of the book open its window: the
tree of past choice points, and the table of saves it can return to. Clicking an entry returns the
game to that exact state. [docs/FINDINGS.md](docs/FINDINGS.md) explains how the
game works and where every fact used by the mod came from.

## Layout

```
Brante-Save-Load/            the folder that gets installed into the game dir
  Brante-Save-Load.dll       built by tools/build.sh; the Doorstop entry assembly
  0Harmony.dll               vendored Harmony (copied by the build from vendor/)
  scene-texts.tsv            every scene's title and answers (from the game; see data/)
  scene-flow.tsv             every chapter's scene gates: flow row(s) per scene + conditions (from the game)
  scene-effects.tsv          every answer's stat/relation/status changes, and each scene's opening ones
  scene-params.tsv           key chapter parameter changes by scenes/answers (from the game)
  scene-objectives.tsv       the story objectives scenes/answers unlock (from the game)
  Scripts/*.cs               the source
    Entrypoint.cs            Doorstop entry: Harmony patches, host creation
    ModLog.cs                file logger (usable before Unity is up)
    ModHost.cs               host component; probes, dev markers, startup repairs
    Recorder.cs              choice snapshots, checkpoints, autosave mirror, baseline,
                             run tracking, id repair, traces
    ChoiceWindow.cs          book tabs, save table (type filter, pages), tree view,
                             path picks + Load scene
    AutoPlay.cs              plays scenes by itself (Load scene replay, debug runs)
    History.cs               the save's scene path + offered answers
    SceneTexts.cs            loader for scene-texts.tsv (titles + answers)
    SceneFlow.cs             scene-flow.tsv: upcoming scenes, condition evaluator
    SceneEffects.cs          scene-effects.tsv: what each answer changes (tree pop-up)
    SceneParams.cs           scene-params.tsv: key chapter parameter simulation
    SceneObjectives.cs       scene-objectives.tsv: objectives scenes/answers unlock
    StatPanel.cs             tree's stat columns: clones of the game's stat row
    PathId.cs                path IDs: the game state as a short text line (copy/paste)
    Loc.cs                   the UI's own translations (13 languages)
    Palette.cs               every colour the UI uses (fades, not near-copies)
    BoltDump.cs              runtime dump of the choice machine (dev only)
doorstop/doorstop_config.ini the Doorstop config installed into the game folder
vendor/                      vendored third-party binaries + their licenses
  doorstop/winhttp.dll       Unity Doorstop 4.6.0 (x64), LGPL-2.1
  0Harmony.dll               Lib.Harmony 2.3.3 (net472), MIT
tools/build.sh               compile against the game's own Managed/ DLLs (UI, TMP, Audio, IMGUI modules)
tools/install.sh             build, then symlink/copy into the game folder
tools/steam.sh               Steam library lookup (distro/Flatpak/Snap), sourced by both
tools/gamedir.py             the same lookup for the Python tools (+ the prefix's Runs/)
tools/nrbf.py                read a .dat save (BinaryFormatter) outside the game
tools/localization.py        extract titles/answers -> data/, shipped TSV
tools/bolt_extract.py        dump every Bolt graph as JSON (no UnityPy needed)
tools/bolt_graph.py          Walk Bolt graphs (macro resolution, LoadScene)
tools/bolt_story.py          every chapter's scene flow + conditions -> shipped TSV
tools/scene_effects.py       answer effects from the scene files -> shipped TSV
tools/scene_params.py        key chapter parameter changes from scenes/answers -> shipped TSV
tools/scene_objectives.py    the objectives scenes/answers unlock -> shipped TSV
tools/build_graph.py         merge traces + snapshots + event sheet
data/                        extracted game data (see docs/BRANCHES.md)
docs/                        FINDINGS (game internals), BRANCHES (choice data), TODO
```

## Build, install

```sh
./tools/build.sh             # after editing any .cs  (dotnet SDK >= 6 required)
./tools/install.sh           # build + install: symlink Brante-Save-Load/, write
                             # doorstop_config.ini + winhttp.dll into the game dir
./tools/install.sh --copy    # copy instead of symlink
./tools/install.sh --uninstall                    # keep the player's data
./tools/install.sh --uninstall --restore-backup   # put the pre-mod save back
./tools/install.sh --uninstall --purge            # delete the mod's data
```

The build needs the game installed (it compiles against the game's own
`_Data/Managed` assemblies — no .NET Framework reference packs needed).
Set `BRANTE_DIR` if the game is not found, `DOTNET_DIR` for a non-default SDK.

## How the mod gets into the game

[Unity Doorstop](https://github.com/NeighTools/UnityDoorstop) (the same mechanism
BepInEx uses, minus BepInEx): `winhttp.dll` + `doorstop_config.ini` in the game
folder cause the game's Mono runtime to run `Doorstop.Entrypoint.Start()` before
anything else. There is no mod loader, no plugin framework — the repo is
self-contained. Doorstop files are the only things placed beside the game files,
and `--uninstall` removes exactly what was installed.

## Game data files (regenerate, don't hand-edit)

`Brante-Save-Load/scene-texts.tsv`, `scene-flow.tsv`, `scene-effects.tsv`,
`scene-params.tsv` and `scene-objectives.tsv` are extracted from the
installed game and shipped with the mod. Regenerate them after a game update with

```sh
python3 tools/bolt_story.py       # scene-flow.tsv (needs UnityPy); run first
python3 tools/localization.py     # scene-texts.tsv (+ data/localization.csv)
python3 tools/scene_effects.py    # scene-effects.tsv (needs UnityPy)
python3 tools/scene_params.py     # scene-params.tsv (needs UnityPy)
python3 tools/scene_objectives.py # scene-objectives.tsv (needs UnityPy)
```

`tools/bolt_*.py`, `tools/scene_effects.py`, `tools/scene_params.py`,
`tools/scene_objectives.py` and `tools/localization.py` need the
game installed; all but `localization.py` and `bolt_extract.py` need UnityPy (`pip install unitypy`, or PYTHONPATH pointing at it).
`tools/build_graph.py` is optional tooling that merges the mod's traces with the
community event sheet (`data/events.json`).

## Rules that are not obvious

- **The game's save system is BinaryFormatter** over a `[Serializable]` class
  (`SaveLoadHelper`) that holds the entire game state. The mod never creates or
  renames the folder of a save that has an id: a new game's folder comes from the
  game's own `CreateSaveLoadSlot`. The one exception is a save with **no**
  main-save id loaded through the mod (an old game, or one that lost its id):
  `Recorder.AdoptAsCurrentSave` gives it one with `NewSlotId`, built exactly as
  `CreateSaveLoadSlot` builds it (hero name + `_` + `DateTime.Now` without spaces,
  colons and dots), so the game's autosave and chapter files get a folder of their
  own and its menu lists the game. Otherwise the mod writes only inside its own
  folder (`Brante-Save-Load/`) plus the one-time id repair below.
- **One live game save per playthrough.** The game has exactly one rolling
  `Autosave.dat` in one `Saves/<id>/` folder, plus a `Chapter_N.dat` at each
  chapter start. Everything else is a copy the mod makes; the game's own save is
  never duplicated, so its Continue, autosave and chapter restart keep following
  the live timeline.
- **A save's folder is its id.** `SlotInfo.Id` contains slashes
  (`Biggie_10/23/2026103816AM`), so the folder is a nested path. Saves written by
  older builds can have an empty `SlotInfo.Id`, even sitting in the right folder;
  the id is filled from that folder wherever the game reads saves — Harmony
  postfixes on `SaveLoadManager.LoadSaves()` (matched back by hero + save date,
  only when unambiguous) and `SaveLoadManager.LoadData(string)` — and
  `Recorder.RepairIds` writes the recovered id back into `Saves/**/Autosave.dat`
  once at startup.
- **A loose `Saves/Autosave.dat` is merged into its run.** A game that lost its
  id saves in `Saves/` itself. `Recorder.RepairLooseRoot` merges it only into
  a run it continues: same hero, the run's whole scene list is the start of the
  loose one (>= 5 scenes - every run shares the opening), the loose file is
  newer than the run's `Autosave.dat`, and exactly one run shares the longest
  start. Then it moves it plus `Chapter_N.dat` and `testSave.txt` in,
  quarantining the files it replaces to
  `Brante-Save-Load/Backups/quarantine/<id>/`. Anything else stays in place
  (logged); an older loose state never replaces a newer run save.
- **The current run is tracked, never guessed.** It is set only where the game
  changes saves: postfix on `LoadGame(int)` (the game's own Load/Continue),
  `CreateSaveLoadSlot` (new game), `LoadGame(string,int)` (death -> restart
  chapter), and `Recorder.BeginModLoad` from the mod's own load path.
  `CurrentRunId()` prefers the live slot; an empty id is a valid value and is
  never replaced by a previous run's.
- **Slot repair only runs on a mod timeline.** `Recorder.EnsureSlotId` (prefix on
  `SaveGame`/`SaveChapterState`) fills a missing id only while the game plays a
  save the *mod* loaded, and never changes a non-empty slot.
  `Recorder.FixSaveFolder` (postfix on `GetSaveFolderPath`) only redirects the
  bare `Saves/` root. A load from the game's own menu is left alone and saves
  back into its own folder.
- **One baseline, taken once.** At the first start of this version (marker
  `Brante-Save-Load/.baseline` missing) every existing game save folder is copied
  to `Brante-Save-Load/Backups/<id>/` (loose root files to `Backups/_no_save/`):
  staged in `*.tmp-<pid>`, with `MANIFEST.txt`, `README.txt` and `.complete`,
  then moved into place. It is never re-copied or pruned; a backup from an older
  version (a folder without `.complete`) is kept untouched and only marked.
  `Recorder.EnsureBaseline(id)` is the fallback before a load.
- **Checkpoint before every rewind.** `Runs/<run>/Checkpoints/` gets the live
  state before every mod load (a single save or a Load scene replay), before a
  load from the game's own menu (a copy of the run's `Autosave.dat`, since the
  main menu has no live state), and before a death -> Restart chapter. Sidecar
  has `kind=checkpoint` and `reason=`. A load that switches runs also files the
  checkpoint with the target run, so that run's table shows it; the save's own
  `SlotInfo.Id` (the run it belongs to) wins over the folder it is filed in when
  it is loaded.
- **The four stores under `Runs/<run-id>/`:**
  - `Choice/` — choice-point snapshots, one per decision, content-hashed, never
    pruned. Only the tree view reads them; they are **not** in the save table.
  - `Checkpoints/` — pre-rewind checkpoints, newest 100 per run.
  - `Autosaves/` — a mirror of the game's own `Autosave.dat`, copied after
    `SaveGame`/`SaveChapterState` (hash-deduped, newest 100, skipped while
    `AutoPlay.Running` so a replay gets one checkpoint rather than one per
    scene). These are the game's resume points (`SceneState_GAME_SETUP`), not
    choice points.
  - `Manual/` — "Save now", never pruned.
  `Auto` was renamed to `Choice` by `Recorder.MigrateFolderNames`; readers fall
  back to `Auto` if the rename was skipped. `trace.log` stays at
  `Runs/<run>/trace.log`.
- **Save table categories.** `Game autosave` (mirror), `Before load`
  (checkpoints), `Manual`, and `Original save` (the baseline
  `Backups/<id>/Autosave.dat`, read-only, no delete). The live
  `Saves/<id>/Autosave.dat` is not listed, and identical states are not merged
  across categories (they are different artifacts). The first header, "Save
  type", is a filter button: it cycles `All -> Game autosave -> Before load ->
  Manual -> Original -> All`, resets to All when the window opens, and does not
  sort. Rows are built in pages of 120 with a "Show more" row.
- **Table rows are de-duplicated by state.** `Recorder.StateSignature` hashes
  the save with `SlotInfo.Id`/`Date` cleared, so two writes of the same state
  (the same scene saved twice, "Save now" pressed twice, a checkpoint filed in
  two runs) are one row; the newest is shown, older files are its copies, and
  deleting the row deletes every copy. Automatic stores are also pruned of
  duplicates at startup (`Recorder.DedupeStores`); manual files are never
  deleted automatically.
- **Load scene validates the picked path.** `PlanLoad` builds the choice-scene
  order from the start to the target, requires every answer to be one of the
  scene's choices and open, and checks that the start save is this path's
  (`StartLooksOnPath`: same `LastOpenScene` and a scene-list prefix). The replay
  walks that order: a scene not on it, or a scene that skips an earlier one,
  stops the run and opens the tree with the scene named. A path through scenes
  still ahead is logged as speculative. `dev-plan.txt` logs a plan without
  replaying it. A stop or a failed pre-check is shown in the tree's hint line in
  `Palette.Red` (the delete-mark red).
- **Same-scene loads unload the old copy first.** The game adds a scene and
  unloads the previous active scene only after the new copy's `Start`; loading
  a save of the scene on screen therefore left two copies (`14 buttons` for a
  7-answer scene, `TextController.Start` out of range). `LoadFlow` unloads every
  loaded copy of the scene on screen after `LoadGame`, makes `GameLoadingScreen`
  active, then broadcasts the transition. Answer sidecars are also de-duplicated
  by `ButtonIndex` (and repaired at startup by `Recorder.DedupeSidecars`), so a
  duplicate copy can never write each answer twice.
- **The chapter flow restarts at the top after every scene.** `scene-flow.tsv`
  has one row per flow occurrence (a scene can sit at different positions under
  different switch labels, e.g. `TriggerBlock`), and `SceneFlow.After` returns
  every unshown scene of the chapter in flow order that passes the live labels
  and conditions - not only scenes after the current one. A triggered scene
  whose flow position is earlier than the scene on screen (e.g. `02.N.03` after
  `02.07.02`) is therefore offered as the next scene, as the game does; the
  per-branch order keeps the true/false branches apart. The TSV is regenerated
  by `tools/bolt_story.py`.
- **Time gates can be ranges.** The flow TSV's turn column is `N` (the gate's
  `Turn == N`) or an inclusive range `N-M` / `N-` / `-M` for the trigger gates
  that compare with `<`, `<=`, `>=`, `>` (`054.100.090_Milanid` is
  `Turn >= 4 && Turn < 8`, i.e. `4-7`, Season any). Recording only the first
  constant (as `8`) put such scenes turns too late. `SceneFlow.InSlot` is the
  test; a row whose last turn has passed is dropped. Immediate scenes come first
  in flow order, then later slots by (turn, season) - the game moves to the
  earliest slot, not the next in flow order.
- **The scenes ahead are a forward simulation.** `AddUpcoming` steps a `Sim`
  (hero stats, characters, objectives, key chapter parameters, scenes shown,
  chapter, turn, season) from the scene on screen - or from the first played
  scene whose answer was changed, starting from that scene's snapshot - to the
  end of the story: `SceneFlow.Step` returns the first scene the flow would
  open (the simulated state is swapped into the game's managers only for the
  call, synchronously, and always put back - the game's per-frame achievement
  poll can never see it), its opening parameters apply, the picked (or played,
  or first open) answer applies its effects, objectives and parameters, then the
  scene's own objectives. Columns are added for scenes that offer answers. A
  pick anywhere re-simulates what follows it. Played entries after the scene on
  screen (an abandoned attempt in the save's list) get no columns.
  `SceneFlow.FirstCore` sorts candidates by the final order before evaluating
  any condition and stops at the first that passes: the whole story simulates
  in ~60 ms of flow time, and the tree builds in about 0.5 s.
- **A pick or Reset path is a re-prediction, not a rebuild (~60 ms).** The
  window keeps the played columns of the last full build (`_playedCols`) and
  `ResimulateAhead` predicts from them again, for a played or an upcoming pick
  alike; the history and snapshots are read only when the tree opens.
  `SceneCol` caches each scene's title, snapshot and answers per build
  (`History.SnapshotFor` touches the disk: ~2 ms a scene under Wine). The
  drawing is per column (`DrawColumn`, one container each): a column whose
  `ColDrawKey` (scene, flags, visible answers, chapter rule, "Upcoming" foot)
  is unchanged is kept - slid to its new index when a scene before it came or
  went - and its `NodeRef`s are re-pointed at the new column and choice
  objects before `StyleNode`. Restyling boxes that still pointed at the old
  prediction was why a pick in a predicted scene showed nothing.
- **Picks ahead are told apart from assumptions.** A predicted scene that the
  old path played keeps that answer by default (`PlayedChoice`); it is drawn
  pale yellow, a real pick full yellow. The "Pick an answer here to go on"
  note (`Loc.K.PickNext`, drawn in `DrawPath`) sits under the first scene
  before the dashed answer that has no answer - the one Load scene's
  `MissingPick` names, the scene on screen included - or else under the
  scene after the dashed answer. A dashed answer in the scene on screen has
  nothing to load (the game is there): the button's hint says so
  (`Loc.K.OnThisScene`) instead of "nothing to load".
- **Path lines and stat marks.** A picked answer lower than the next
  scene's last answer gets that scene's grey spine extended down to it
  (`DrawPath`, the spine's colour and width), so the red path never ends in
  the air. Hovering an answer marks the stat rows it changes
  (`StatPanel.HighlightChanges`: the hero at that scene before and after the
  answer's effects; `Palette.Good`/`Bad` at 28%, the game's consequence-row
  green and red); leaving the answer clears them The mark reaches `MarkPadX`
  past the row's ends and halfway to the rows above and below in its
  column (`MarkPadY`), so marked neighbours meet - at most `MarkPadYMax`
  (8): rows set far apart (Ingenuity under Spirituality, Nathan under
  Gregor) were stretched to half that gap. The mark and a row's
  hover area cover what the row draws (`StatPanel.OverDrawn`: icon to
  slider end), not its 375-unit box, which reaches into the next column.
- **Load scene shows its progress and blocks the player.** `ReplayTo` puts
  up a box on its own canvas (sorting order 9000, `DontDestroyOnLoad`) over a
  dim that catches every click; `AutoPlay` clicks through `ExecuteEvents`, not
  the pointer, so it is not blocked. "Scene N of M" counts the plan's choice
  scenes; the bar creeps within a scene and the time left is the pace so far
  (5 s a scene before the first is reached) times the scenes to go, on the
  real clock, in the language's units (`Loc.K.UnitSec`/`UnitMin`). **Stop**
  sets `AutoPlay.StopRequested`; the run ends at its next step
  (`Stop.User`) and the game stays on the scene reached, which may be a
  chapter's picture or cutscene - no message, no tree. The box is removed in
  a `finally`, before the tree opens on any other stop.
- **The load/delete dialog.** The question centred on top, the save's time
  and choice in a `Palette.Card` box, a note (delete, save now) under it. The
  row it is about keeps its colours and gets the red dashed outline: the dim
  is four shade rectangles around it (`ShadeAround`), the panel's own image
  stays the clear click target. A sorting canvas on the row did not lift it
  out of the table's masked scroll view.
- **The stat panel shows live stats only, at a readable size.** The game's
  Destiny *stat* tab (Window_Soul) and the stats the game never changes or
  reads (`StatPanel.Dead`: Volition,
  Love, Rule, Tolerance, GWrath, Control, Cruelty, Weight, Сontention - Cyrillic
  С) are left out; FINDINGS ("Dead stats") has the evidence. Column widths are
  measured on what a row draws (icon to slider end), not its 375-unit box.
  Heir is left out too (`StatPanel.LeftOut`: only Stephan or the hero).
  Work is ordered Career, Justice, Inquisition power, Tolerance of faiths,
  Unrest, Spy network (the game's `WorkWindowController` panels: Judge,
  Inquisitor, Rebel, two stats each); the Revolt as its window (The Revolt,
  Troops, Nobles, Clergy, Common Folk); House as Reputation, Wealth, Unity.
  `dev-pick.txt` `windows` logs both game windows' rows in layout order.
- **Personality is drawn by chapter (`StatPanel.FlowPlace`).** Column 1:
  Willpower, Deaths, Perception, Determination (chapter 1); column 2: Nobility,
  Spirituality, Ingenuity (chapter 2, the game's own order), each level with the middle of its two
  skills; then the six chapter-3 skills. `Forks` draws each occupation's fork
  into its two skills (`Fork` 0.32 of a column), the arms marked "+"
  Perception's / Determination's icon, whose hover opens that stat's pop-up -
  `CalculateChapterParams`: Diplomacy = Perception + Nobility, Valor =
  Determination + Nobility, Manipulation/Scheming from Ingenuity,
  Theology/Eloquence from Spirituality. It needs six rows in a column
  (`FitsWidth` refuses less), else `Place`'s groups are the fallback. A stat
  not open at the chapter shown is faded with "Unlocks in Chapter N"
  (`Loc.K.UnlocksIn`, also under a locked tab's title; `StatChapter`: occupations 2, skills 3, else the tab's chapter).
- **Characters are met by the scene, not the answer.** The game sets
  `CharacterModel.IsUnlocked` only through `UnlockCharacter` components;
  all 46 sit on a scene's `Window_Answer_Selection` and fire when the scene
  opens, whatever is answered (`02.11.01` unlocks Otton for every answer).
  `tools/scene_effects.py` writes them as button-0 `U` rows and `Hero.Apply`
  sets `IsUnlocked` (`UnlockDate` "~": after any date the game wrote).
  Listing whoever had a changed relation instead made Otton come and go
  with the Fugitive Girl answer and showed Felipe before he is met.
- **Destiny tab (story objectives).** A fourth panel tab lists one
  chapter's objectives as the game's Destiny window does
  (`KeyChapterParametersController.AllGameObjectives`, by `Category`, chapter
  4 filtered by `Objective.Post`; the true death with the outcomes from
  chapter 3). Chapter buttons I..V (default: the chapter of the scene shown).
  Colours: happened by the scene shown (green), happens later on the picked
  or predicted path (`DestinyEnd`, the simulation's final hero; pale green),
  still possible (grey), out of reach (red, struck: its chapter is over, or
  every scene/answer that earns it in scene-objectives.tsv is in
  `DestinyDone` - the scenes passed by the scene shown - or on another post's
  track, `SceneFlow.SceneFits`), or the game's own condition list for it
  (`Objective.Condition`'s `RealCondition`, what the Destiny window shows:
  every part required except those marked `OrCondition`, of which one is
  enough - Blood Tide is the ritual and the Nobleman's Sacrament or
  Gregor >= 2; the pop-up lists them under "One of") has a part that can never be met again (`RuledOut`: a status
  for a character who is dead - the explosion kills Remy El Verman, and
  Master of Anizotte needs him at `Deal` - or an objective it must not have).
  An objective of another post is still listed when a scene of this post
  earns it (`EarnableOn`: Tommas's New Life, inquisitor-only in the game,
  comes from a wedding on every track). Hover: the stat pop-up with everything in
  its description (state, description, the window's conditions, the earning
  scenes) - its rows are sized for a stat's scale and mangled longer lines -
  placed beside the entry's words, level with it. `dev-pick.txt` also takes
  `destinyhover <objective>` and `forkhover`. Hovering an answer or event
  (`HighlightDestiny`) backs the entries it earns green and the ones it puts
  out of reach red (every way to earn them is another answer of that scene
  or already behind the scene shown). The destinies a hovered answer or
  destiny needs (`StatPanel.Needed`/`RequiredBy`: the answer's `O`
  requirements without "not"; a destiny's condition list and its earning
  answers' requirements) get a `Palette.Required` (amber) outline
  (`MarkRequired`) over their fate colour, so a needed destiny that
  happened or is out of reach still shows both. Condition parts on a
  stat the panel leaves out (`LeftOut`: Heir) are not listed.
  Exclusive destinies (`StatPanel.Exclusive`: the chapter-3 paths,
  shields and Lotless, the family's fortune pairs, the family in the
  Revolt - Fled / Perished / Saved, not In the City - and the sides; the
  evidence is in the comment) are listed together under a bracket like a
  scene's answers. Struck text puts its colour before `<s>`: TextMesh Pro
  draws the line in the colour current at the tag (it came out black).
  Fills and outlines reach `RowPadX`/`RowPadY` past the row (a `Fill`
  child; the row's own image stays the clear hover target). In each
  category the entries go in the order the story can first bring them
  (`FirstAppearance`: the earliest earning scene in flow order; then the
  game's `Order`; true death last). The pop-up is wide, over the tree
  (`ShowWideTip`, `TreeArea`): icon, name, state, description on top,
  Requires and Comes from side by side below, on the game tooltip's
  parchment and font. Requires is one row per part, never wrapped
  (`FillRequires`): a bullet, the icon of what the part tests
  (`ConditionIcon`, from `RealCondition` - the operand is a translation
  key such as `GregorBronte`, not an asset name; key-parameter parts have
  none), the text; "One of:" with its options under a bracket
  (`DrawSpines`, after layout). Requires is at least as wide as its
  longest line (up to half the box, then its lines wrap): unsized, it was
  squeezed and its lines ran into Comes from. A key-parameter part that
  only the earning answers set (`ShownByComesFrom`: every
  `SceneParams.Setters` entry is an earner - "Kiss the Sword") is left
  out: Comes from names that answer. One set elsewhere too ("GirlsMeet")
  stays. True Death's "Accept True Death" is set by no scene, and every
  answer that earns it sets Deaths to 4: it reads "Deaths = 4". Comes
  from is one row per scene, "Scene: answer (needs)", a scene's several
  earning answers bracketed under it (`OptionMark`), wrapped; the tall game tooltip beside the list covered the
  next columns. `ChoiceWindow.ShowStats` fills the context.
  `dev-pick.txt` takes `mode destiny` and `destiny N`.
- **The book tabs' wood stops at the page.** `MakeBookTab` copies the
  wood under the settings tab out of the book images, minus a share of
  its foot per tab (`crop`; the patch is that much shorter, its top in
  place). The left page's top edge slopes down to the right: whole, the
  save/load tab's wood (3% off) reached over it and looked laid on the
  page, while the tree view tab's (0%) just meets it - cropped, a gap
  showed. Two-cut copies (settings top + chapter column under the page)
  looked worse and were dropped.
- **A true death ends the predicted path.** The fourth death sends the game
  into `GameFinals` (true-death pages, the chapter's finale, credits), not the
  chapter flow. `AddUpcoming` stops when the hero's Death reaches 4 and ends
  the path with a "True Death" event box (`TrueDeathScene` = `FourthDeath_1`,
  not a load target). Before, the simulation went on predicting the chapter.
  Any answer or scene that sets the key parameter `GoToFinals=Yes` ends the
  story the same way ("Glory to the Empire!", either answer of "The Judgment
  of the Twins", the chapter-5 endings): the path ends in a "The End" box
  (`EndScene`, `StoryEnded`). The Deaths row reads "True Death" at 4 (the
  stat's scale stops at "Died thrice").
- **"Comes from" is one line per scene.** `StatPanel.EarnedByLines` groups
  a destiny's earners by scene title: a scene that earns it by itself or
  with every answer (Father's Sword, The Ant Farm, Forbidden Love) is its
  title alone - listed per answer it read as the same line twice - else
  each earning answer with what it needs (`Needs`: and-requirements, then
  the or-group; Last Respects: the Nobleman's Sacrament, or Nobility >= 6
  or Gregor >= 2). Until the destiny is earned, a scene behind the scene
  shown or on another post's track is struck in the out-of-reach red, and
  so is each condition part that can no longer be met (`PartRuledOut`;
  needing a destiny that is out of reach counts, one level deep: Blood
  Tide's Nobleman's Sacrament once chapter 1 is over without it, the
  Tournament without The Fencing Lesson), so the ways left stand out.
  `dev-pick.txt` `destinyall` logs every chapter's
  pop-up text ("DEV DESTINY"), each destiny's metadata ("meta") and its
  condition parts with their `RealCondition` ("cond"). The displayed
  operands can differ from the real test: Nobleman's Sacrament shows
  Willpower >= 0, its `RealCondition` is Will >= 5.
- **Every text in all of the game's languages.** The game has 14 (I2
  terms carry 14 columns): `Loc` has a table for 13 - both Spanish ones
  share `_es` - and `Loc.Table` matches the language code or name
  (Portuguese has no code in the game: "Portuguese - Brazil";
  "Chinese (Traditional)" / zh-TW before Simplified). Pick the game's
  own word where the mod names a game thing (True Death: "Morte
  Genuína", "真正的死亡"). Fonts come from the game's `ChangeFontAsset`,
  which follows `GameSettings.Language`, not I2's language:
  `dev-pick.txt` `lang <game language name>` sets both (not remembered),
  else Traditional Chinese renders blank. Path ID refusal reasons are
  `Loc.K.Pid*`. Adding a key: give it all 13 tables.
- **Path IDs never reach BinaryFormatter.** The tree header's paste and
  copy buttons (left of reset) turn the live state into
  `BSL1:<n>:<base64url>:<CRC-32>` and back (`PathId`). The record is the
  mod's own: the game's fixed lists in their own order (69 key
  parameters, 42 characters, 78 objectives, 50 events, the 64
  `ModelParameters` ints by name), scenes as build-scene indices,
  description keys as indices into the sorted I2 term list, zigzag
  varints, dates as seconds from the previous one, then raw DEFLATE
  (`MonoPosixHelper.dll` ships with the game). A schema hash over all
  those names refuses an ID from another game version. Decoding checks
  the shape, the length, the CRC, a 64 KB unpack cap, every count, range,
  enum and string (printable, no `<>`), that the scene is a build scene
  and that nothing is left over, before anything changes; it is applied
  to `Recorder.LiveCopy()` (the live state through our own BinaryFormatter
  round trip), saved by `Recorder.SaveBuilt` as `Manual/pathid_*.dat`
  with the player's hero name and run, and loaded as the table loads
  (checkpoint first). Locked objectives and events keep the dates the
  game left in them (empty, year 1132, 2021 leftovers in its assets), so
  every entry is encoded. A chapter-4 save is ~1,100 characters; a copy
  re-encodes identically after a paste (`dev-pick.txt` `pathid`: round
  trip and five tampered strings, all refused; `pathidpaste` pastes the
  clipboard). Compare states by content (re-encode, `nrbf.py`), not
  bytes: BinaryFormatter shares the live state's repeated strings.
- **Objectives earned are listed in the pop-ups.** An answer's or an event's
  pop-up adds what it earns (`E` effects: `SceneEffects.Shown`,
  `SceneChanges`; display only, never in `For`/`ForScene`).
- **Panel colours compare with the same point, not with today.** A value
  is green/red against `StatPanel.Baseline` (`ChoiceWindow.StatsBaseline`):
  at a played scene the hero after the answer really played there, at the
  scene on screen the hero there, ahead today's hero. Against today, a
  chapter-2 pick in `02.12.01` showed Sophia's -2 green and Octavia's 0 red
  only because they differ today. A status change marked on answer hover is
  red for a loss or a turn against the hero (`BadStatus`: dead, exile,
  Hatred, Betrayed, ...), green for a gain (`GoodStatus`), a muted mark
  otherwise - it was green for anything short of death.
- **Stats / Family / Relations tabs.** Three tabs at the top of the panel
  (`StatPanel.Mode`, `ModeTabs`) switch it between the stats and the
  characters as the same cloned stat row: portrait (the chapter's sprite),
  `GetCharacterTrueName`, relation -5..+5 on the two-sided slider, the game's
  relation word or, when the status is not `Good`/`Not`, the status
  (`<Character>.<Status>`). Family = `Character.IsFamily` without the hero,
  Relations = everyone else the hero shown has met (`IsUnlocked`, as
  the game's windows) whose relation is not 0 or can still change
  (`StatPanel.RelationCounts`: an `R` row in a scene not yet passed on the
  hero's track - the game meets Egmont on every track, but only the
  rebel's and the judge's scenes change him), ordered by `UnlockDate`;
  the rows are rebuilt when that set changes. Dead/left/exiled rows are
  faded. A hovered answer marks relation and status changes like stats.
  Family is drawn as the game's family tree (`FamilyTree`: Gregor; Robert,
  Lydia; Stephan, Nathan, Gloria; `Gloria2` is a second asset of the same
  `CharacterList`, the shorter name wins). Relations are three columns by
  `Character.Estates` (nobles + Arkn, `Cleric`, commoners + La-Tari),
  titled with the Insurrection stats' names (Nobles / Clergy / Common
  Folk), each as wide as its sub-columns; the family generations share the
  spare height equally above, between and below them. Both are scaled up to
  `PeopleMaxScale` (1.25), larger than the stat rows. A tab under the
  pointer turns accent-coloured on a `Palette.Card` back. `dev-pick.txt`
  takes `mode stats|family|relations`, `tabhover <mode>` and `allpeople`
  (list all 24 characters any scene unlocks - 12 nobles, 7 clergy, 5
  common folk; each estate's column takes as many sub-columns as it
  needs: the layout's worst case; one
  playthrough meets at most 19 - 3014 saves agree - and all 24 fit at
  0.83 at 1080p, more than the stat rows' 0.7).
- **Hovers show what is ahead.** A column title's hover (`TreeHover` on the
  "Head" label, the column looked up in `_drawCols` by scene, as a kept
  column outlives its `TreeCol`) shows `SceneText`: the scene's last
  `<scene>_Text_N` page - the left page beside the answers ("Who will you
  spend your last few hours at home with?" in `02.12.01`) - cut near 600
  characters, an answer-less scene's `EventText`, and its opening changes.
  Hovering a title or an answer also lists, on the Family/Relations tab,
  the characters met by then that the panel does not list as pale ghost
  rows (`StatPanel.Ghosts`, alpha 0.3, values from that hero); leaving
  clears them (`ClearHighlight`). `dev-pick.txt` takes `hoverhead <scene>`.
- **The tree never scrolls vertically.** `LayoutTreeArea` makes it as tall as
  the scene with the most answers in the whole story (`SceneTexts.MostAnswers`:
  7, three chapter-5 scenes), so the stats below never move with the path (answer boxes `TreeNodeH` 34 in `TreeRowH` 40 rows, the
  17-unit text unchanged) and gives the stats the rest (~0.7 of the game's row
  size at 1080p). `ScrollRect.vertical` is off and `TreeWheel` turns every
  vertical wheel notch into a sideways one (down = newer columns). It sits on
  the viewport and on the horizontal scroll bar: over the bar the notch
  otherwise reached `ScrollRect.OnScroll`, which flips it, and scrolled the
  other way. `dev-pick.txt` `wheel` logs one notch over each.
- **Flow order is topological.** `tools/bolt_story.py` numbers the scenes in
  the Bolt graph's topological order, so every scene comes after everything
  that leads to it. Discovery order put the scenes after a rejoining branch
  inside the first branch: the chapter-5 finals came before the rebel's
  "The Final Hour" (`060.010.160_TheFinalHour2`), and the simulation reached
  a final first and never showed it. Siblings keep their port order (the
  DFS walks successors last-first): reversed, a Sequence's second output
  came first and the chapter-4 family scenes (`051.*`) fell after the
  post's (`054.*`), where the game opens them first in each slot
  (Arrive_Prefect, FirstDayWork, Scene_1, Scene_2, Case1). Check a flow
  change by scene names, not column counts: the default prediction from
  `02.12.01` is identical to discovery order (48 columns, same scenes).
  `dev-pick.txt` `conds` logs the trigger scenes' `SceneConditionsCollector`
  lists and every objective's condition list.
- **A switch's default output is a label too.** `scene-flow.tsv` writes a
  `SwitchOnString`'s default branch as `Name=!A,B` (none of the listed
  values; `SceneFlow.LabelOk`), not as no label. Dropped, the default
  branch's scenes matched every value: "Clean Hands"
  (`053.100.120_Disclosure`) answered `Disclosure=Yes` still led to A
  Sleepless Night (`053.070.010_Bridges`, default branch, earlier in flow
  order) instead of Glory to the Empire! (`053.090.060_Final6`). Unset
  values are `None`/`No` in a save, never listed, so they take the default.
  `Covers` ignores default labels of other rows (the listed values decide).
- **Load scene stops at a story end it did not plan.** `AutoPlay.StopAtStoryEnd`
  (set by `ReplayTo` only) stops the run 0.5 s after a scene that opens
  with `GoToFinals=Yes` (`SceneParams.EndsStory`) and is not the event
  aimed at; it used to click on through the finale. Debug runs play on.
- **Changers off the answer buttons apply with any answer.** In
  `scene-params.tsv` button `-1` is a `KeyChapterParametersChanger` on a
  button every answer passes - the text pages' `NextButton/Image`
  (`02.10.01` sets `TriggerBlock=true`, `02.10.02_Funeral_Ded` sets it back)
  or every answer's continue button (`Post` in the chapter-3 finals). It fires
  on a pointer press. `SceneParams.Apply` applies them with any answer;
  ignoring them let the simulation open the trigger scenes (`02.N.0x`, only
  under `TriggerBlock=false`) before Last Respects, and Load scene stopped
  there. `AutoPlay.Click`/`TurnPages` also fire changers on a pressed
  button's children, as a mouse press bubbling up from the child would.
- **A scene's opening changes are simulated (button 0).** A scene changes
  the hero by itself when it opens, before its answers: the
  `ConsequenceSceneController` of 52 answer-less scenes
  (`Insurrection_Incident_N`, `051.090.010_FamilyQuarrel`, triggers such as
  `052.N5.010_Trigger5`, the finals) and the interludes its
  `SceneStateMachine` shows in PREGAME (62 references, 59 scenes, e.g.
  `TeenagerInterlude4` in `02.12.01`: Church +1, Order -1). The chapter-3
  start's interlude converts the earlier stats into the six chapter-3 skills
  (`CalculateChapterParams`, a `C Convert` row). `tools/scene_effects.py`
  writes all of it as button `0` (`SceneEffects.ForScene`). `Sim.Advance`
  applies it when a scene opens; `WorkOutOpenAnswers` applies it to a
  column's hero (`SceneEffects.AtOpen`) and, for an upcoming column, also
  that of the answer-less scenes stepped over before it (`TreeCol.Between`).
  The "case of the year" window (`Case` assets) is opened by nothing but the
  game's developer window, so there is nothing of it to simulate.
- **Answer boxes show the answer's name.** The tree's box has the button's
  short text ("Charge the attackers", `SceneTexts.Name`: the live
  translation of `ParameterButtonChanger.ButtonTextKey`), the hover pop-up
  the full description (`ButtonDescriptionN`). The key is
  `<scene>_Button_<N>` except for 13 buttons, listed as `N` rows in
  scene-effects.tsv; with no translation the box falls back to the
  description.
- **Events are drawn as columns, outside the logic.** An answer-less scene
  whose opening changes stats, relations or fates (`IsEventScene`: a
  trigger event, an incident, `FamilyQuarrel`, a final; ~60 scenes; the
  no-change epilogue pages are left out) gets a column of its own with one
  box named after the event (its title), not clickable; hovering
  it shows the event's first text page (`EventText`: `<scene>_Text_1..3`
  or the title key's prefix, cut near 420 characters, then "Happens on its
  own") and lists `SceneEffects.SceneChanges`, and marks the stats. Only the drawn
  list has them: `WithEvents` inserts them before each column from
  `TreeCol.PlayedBetween` (the save's scene list, `BuildColumns`) or
  `TreeCol.Between` (the simulation; `_tailBetween` after the last column),
  with the hero carried through the answer-less scenes in order. `_tree`
  (picks, Load scene, plans, replay) never contains them, so anything that
  positions by column index on screen uses `_drawCols` / `_focusDraw`. A
  title key that is not `<scene>_Title` is a `K` row in scene-effects.tsv;
  a chapter start uses the chapter's name. Choice scenes have `K` rows too
  where `SceneController.TitleKey` differs (4: `054.030.010_Call_Titile`, a
  typo in the game, and three that reuse another scene's title), and
  `Loc.SceneTitle` looks every title up by that key.
- **An event box is a load target.** Clicking it (`SelectEvent`) makes the
  event the selection (`_selScene` = the event, `_selChoice` = `"Event"`,
  until the next pick or a second click). `PlanLoad` then plans to the
  `_tree` column after it (`EventColumn`: whose `Between`/`PlayedBetween`
  holds it), gives the answers up to the one before it (`Order` ends
  there; the start is at or before that column, never past the event),
  and `plan.ToEvent` makes the replay set `AutoPlay.StopAtScene`: the run
  stops 0.5 s after `Story()` is the event, pressing nothing in it, so
  the game shows its opening (a year-news pop-up first, where the game
  has one). `SelIndex` is the last column such a path goes through;
  `StatsHero` is the event's `AfterHero`. With the game on an event (or any
  answer-less scene) no column is current: `AddUpcoming` steps on from the
  last played answer's snapshot, as from a changed answer; with no start
  it listed every scene still possible and skipped chapter 4 from the
  chapter-3 farewell. Verified: 12 answers from
  `02.12.01` to `030.080.010_Departure_Noble` in 38 s. `dev-pick.txt`
  takes `event <scene>`.
- **The chapter-3 skills are projected in chapters 1-2.** They are 0 until
  the chapter-3 start converts the earlier stats. While the stats shown are
  from chapter 1 or 2, `StatPanel.Show` draws the six skill rows faded with
  the game's "Will unlock in Chapter III" (`HUD.WillOpen3`) and the values
  `SceneEffects.WithSkillsProjected` gives (the game's own
  `CalculateChapterParams` on a copy), and `HighlightChanges` marks the
  skills an answer feeds. So a pick there shows what it does in chapter 3.
- **Snapshots know whether the interludes ran.** A choice snapshot is taken
  at `SceneController.Awake`, before the scene's PREGAME interludes; a save
  of the live answer screen is after them. `Recorder.LiveSceneState` writes
  `GAME_SETUP` for the latter (the scene's state machine, or the state a
  loaded save brought in) and `SHOW_ANIMATION` otherwise, so loading it does
  not run the interludes - and their stat changes - a second time.
  `Hero.Opened` (from the save's `SceneState`) tells the tree which one it
  has.
- **Objectives unlocked by an answer or a scene are simulated too.**
  `scene-objectives.tsv` (from `tools/scene_objectives.py`) lists every
  `KeyChapterParametersController.EarnObjective` target by scene and button
  (0 = the scene itself, N = `ButtonDescriptionN`; 201 rows, 94 scenes).
  `Hero.ApplyObjectives` adds them after the answer's `SceneEffects`, so a
  changed pick keeps the branches its objectives open (e.g. the chapter-3
  `Bourgeois`/`Noble`/`Cleric` tracks after `030.020.010_Exam`). A scene-level
  row is applied when the scene ends, never at its open, so a scene cannot
  unblock its own answers.
- **Key chapter parameters are extracted per scene/answer.** `scene-params.tsv`
  (from `tools/scene_params.py`) records the `KeyChapterParametersOnEnableChanger`
  (button 0, or N when its object is under the answer window's
  `Consequence<N>`, the result page answer N switches on) and
  `KeyChapterParametersChanger` (answer button index) values. 187 changers
  sit on those result pages; read as button 0, all of a scene's values were
  applied when it opened and the last won, so `053.070.010_Bridges`
  ("Go into hiding" = `Insomnia=Hide` -> `053.080.010_SecretMeet`) always
  led to `Final1`, and `052.070.010_Case11` (`LastStep`) and
  `054.080.090_Court` (`TheHourHasCome`) to one fixed chapter end.
  `SceneParams` loads them. `PlanLoad` runs `SimulatePlan`: the same `Sim` from
  the start save through the plan's answers, checking that each choice scene the
  game would open next is the plan's next one, and logs the first difference
  (`plan.Speculative`); the label-only `SceneParams.Simulate` is the fallback
  when the start save cannot be simulated. Unpicked scenes count as their
  first open answer, so this is a prediction: the ordered replay is still
  the final check.
- **A double click on a menu save loads once.** The game's save slot
  (`SaveSlotBehaviour.OnPointerClick`) calls `LoadGame(int)` on every click
  with no guard; two clicks 0.14 s apart loaded the save twice, opened the
  scene four times and left the game on the timeline screen. `GameLoadPrefix`
  drops a second `LoadGame(int)` within 3 s of the first (logged "a double
  click").
- **The true-death pages are fixed (a game bug).** `FourthDeath_3`..`_6`
  wire CONTINUE to `FourthDeathContinueButtonController.Button_Click`, whose
  `DeathObjective` is empty in the shipped scenes: `SetAchievementInfo` throws,
  the button stays disabled, the story stops. A prefix (`Entrypoint.TrueDeathPrefix`)
  calls `SceneButton_Click` instead when the objective is missing, as
  `FourthDeath_1`/`_2`/`_7`/`_8` do. Filling the objective in is not enough:
  the pop-up's close reruns `GameFinals`, which reopens the page it never
  marked shown, forever. Verified: `FourthDeath_1`..`_8`, `LiveTimeline`,
  `Credits_GameOver`, main menu.
- **Achievements stay on.** The game unlocks one only while
  `GameManager.BlockAchievements` is false; `AchievementsBlocker` objects set it
  on `Start` (the main menu's to true, the loading screen's to false). The
  mod's load passes the loading screen too, and `ChoiceWindow.AllowAchievements`
  clears the flag after every mod load unless a scene that blocks on purpose is
  loaded. `ModHost.Update` logs the flag, the loaded blockers and Steam's stats
  whenever it changes ("Achievements: ...").
- **Fast replay.** `AutoPlay` runs the game's clock at 40x (every animation
  and timed wait is on the scaled clock; `Time.maximumDeltaTime` is raised to
  0.5 s, as a 0.1 s cap held a 60 fps frame to 6x whatever the scale), its own
  waits shrink with it (`Guard`), a scene's text pages are turned in one step
  through `TextController.NextPage` (`TurnPages`; it stops after a page that
  triggers a death or opens a stat/character pop-up), and a button counts only
  when a click could reach it on a settled screen (`Reachable`: no parent
  `CanvasGroup` blocks raycasts, is invisible, or has an alpha that is still
  moving). After an answer screen's continue the next press waits 0.25 s of
  real time: closer, it and an event pop-up's started the next scene's load
  twice and crashed the game in native code. A 28-answer chapter-4 replay:
  389 s -> 78 s; the whole story (debug run, chapters 1-5) ~4.5 min. What is
  left is mostly scene loading (~0.8 s a scene).
- **Pop-ups are closed before a mod load.** The game's pop-ups (interlude,
  event, trigger, objective) live on `UIManager.DialogCanvas`, which survives
  scene changes, and an interlude applies its stat changes when it is closed.
  `ChoiceWindow.ClosePopups` destroys them, empties the pop-up queue and
  clears `IsPopupOpened` before `LoadGame`, or a pop-up of the scene left
  behind lands its changes on the loaded state. Interludes applied are logged
  ("Interlude X applied in S: ...").
- **No quick load.** An estimated save built from recorded playthroughs
  (Quick load, 5 Nov 2026) matched the exact replay but was only about twice
  as fast on long paths with many recordings, and barely faster otherwise;
  it was removed. FINDINGS ("Quick load everywhere") has what was measured.
- **Chapter flows' `Once` units are reset before a mod load.** Every chapter's
  flow macro (`ChildhoodChapter` ... `Insurrection_Default`, on the persistent
  `TalismanFlowMachine`) has a Bolt `Once`: the first chapter end loads the
  finish screen, any later one fires `ClickTriggerEvent`, which reruns the
  flow - endless recursion, a native crash. Its flag lives in the machine's
  graph data, not in the save, so playing a chapter end a second time in one
  session after a mod load crashed (any replay across it).
  `BoltDump.ResetOnceUnits` clears every `Once` flag in `LoadFlow`, as in a
  fresh session.
- **Texts come from the live I2 terms, not the embedded CSV.**
  `resources.assets` holds an old CSV import *and* the serialized
  `I2Languages` terms the game reads; `tools/localization.py` parses the
  terms. The CSV lacked answers the game shows (`052.N12.010_Trigger12`
  "Gather your strength…", `051.060.030_Mortally` "Overcome death", all of
  `051.070.035_Mortally`, `052.N6.010_Trigger6`), spelled
  `054.100.090_MIlanid` (so "The Road to the Top" had no answers and was
  simulated as answer-less), and had ~150 stale titles. Answers are listed
  in the scene's sibling order, which is `ButtonDescriptionN` order in every
  scene.
- **Answer names are the button objects' names.** A few localisation keys are
  `ButtonDescription_1`; the scene's button is always `ButtonDescription1`, and
  `tools/localization.py` writes that name, or a predicted answer can never be
  clicked.
- **Save type help.** Each table type cell has an invisible hover target; its
  `TreeHover` shows the same paper/outline pop-up as the tree, with the short
  explanation from `Loc.K.HelpAutosave/HelpCheckpoint/HelpManual/HelpOriginal`.
  The year and turn columns are wide enough for their headers and values.
- **`SaveLoadManager.Instance` can be a destroyed object the game still saves
  with.** Never test it with Unity's `== null` and fall back to
  `FindObjectOfType`: use `Recorder.SaveManagers()` / `GameSaveManager()`.
- **`_no_save` is a folder name only** (`Recorder.NoRun`: the mod's saves and
  baseline copy for a game that had no main-save id). It must never reach the
  game as a save slot id: the game's autosave would throw and the scene would
  stay blank.
- **`SaveLoadManager.LoadGame(SaveLoadHelper)` is public** — that is the mod's
  load path. It restores all singletons and resumes via `LastOpenScene`.
- **Choice scenes are additive scenes containing `ParameterButtonChanger`
  buttons**; the story graph lives in Bolt visual-scripting machines, not in C#.
  Do not try to statically model the story; hook at runtime.
- **The game's `log.txt` answer log is dead code** (never written) — see
  FINDINGS.md. Anything the mod needs, it must record itself.
- **Unity 2018.3 / Mono / C# 7.3**: the compiler is modern csc with
  `-langversion:latest` targeting the game's mscorlib, but the *runtime* is old.
  Stick to constructs safe on .NET 4.x-era Mono. No `Span`, no nullable reference
  types at runtime boundaries, etc.
- **`0Harmony.dll` is vendored on purpose** (Lib.Harmony 2.3.3, net472): the
  game is Unity 2018-era; net35/net472 Harmony builds are the battle-tested
  combination for such games. Bump it deliberately, not casually.
- **The game runs under Proton** — paths are Windows-style inside the prefix;
  `Application.persistentDataPath` lands in
  `.../compatdata/1272160/pfx/drive_c/users/steamuser/AppData/LocalLow/SEVER/...`.
  All file access goes through Unity's own APIs, so nothing prefix-specific is
  needed in code.
- **Do not run the game while building** — Proton maps the DLL and the overwrite
  will fail (or the game will run the old one).

## Workflow the owner expects

Finish each change with `./tools/build.sh` succeeding, then commit (including
the rebuilt `Brante-Save-Load/Brante-Save-Load.dll` — the repo's mod folder is meant
to be runnable without a toolchain, same as Besiege-Sand ships its DLL), then
run `./tools/install.sh`. Read `Brante-Save-Load/Brante-Save-Load.log` after starting
the game — that log is the mod's only diagnostic channel.

To test the mod in-game without playing to a choice point, drop marker files
into the mod's folder inside the Proton prefix
(`<persistentDataPath>/Brante-Save-Load/Snapshots/`). All are one-shot (deleted
on use) and must be paired with `dev-autoload.txt`:

| marker | what it does |
|---|---|
| `dev-autoload.txt` | replay the vanilla load sequence onto the newest autosave |
| `dev-load.txt` | with `dev-autoload.txt`: after the autoloaded scene is open, load one mod save through the table path (checkpoint, baseline, adopt). Holds a file name under `Runs/`. The other dev markers still run afterwards, so `dev-load` + `dev-book`/`dev-loadpath` works on the loaded scene |
| `dev-plan.txt` | with `dev-autoload.txt`: build a path from the scene on screen to the named scene (first open answer everywhere), log the plan and its pre-checks; with `dev-load.txt` too, plan from the loaded scene. Does not replay |
| `dev-keyparam.txt` | with `dev-load.txt`: `Name=Value` set on `KeyChapterParametersController` after the load, before `dev-plan`. Used to test a branch such as `TriggerBlock=false` |
| `dev-stat.txt` | with `dev-load.txt`: `Name=Value` set on `ParametersManager.Parameters` (e.g. `Cunning=4`) after the load, before `dev-plan` |
| `dev-capture.txt` | with `dev-load.txt`: capture the current scene's choice point after `dev-stat`, so `dev-plan` starts from the modified state |
| `dev-replay.txt` | with `dev-plan.txt`: replay the planned path (fast-forward) instead of only logging it; `N`: load the `dev-load` save again and replay, N times in one session (logs Bolt variables that changed) |
| `dev-slow.txt` | kept while present: replays run at 1x instead of 40x |
| `dev-uitest.txt` | drive the save table: open, confirm, delete, load |
| `dev-table.txt` | open the save table, cycle the type filter through every category, log row counts, show/hide a type pop-up, page with "Show more" (a 10-row first page when the run has fewer rows) and check the Original row has no delete cell; with `dev-table-load.txt` also click the Original row and confirm its load; with `dev-table-open.txt` leave the table open afterwards (screenshots) |
| `dev-restart.txt` | with `dev-autoload.txt` (and `dev-load.txt`): make the game's chapter restart call (`LoadChapterState`, as its Restart button does) and log the checkpoint and run state |
| `dev-pick.txt` | with `dev-autoload.txt`: open the tree, apply `scene=ButtonDescriptionN` per line (`reset` = Reset path), log every column after each step (drawn colours, Load scene target, where the pick note is) and leave it open; `walk N`: pick the first open answer in each of the next N scenes, logging after each whether Load scene runs and why not; `hover scene=answer`: scroll to that answer and hold its hover (pop-up and stat marks); `load`: press Load scene and confirm (replays the path); `stopafter N`: press the progress box's Stop N s later; `confirm`: then open the save table and click its first row, leaving the load dialog open; `destinyall`: log every chapter's destiny pop-up text; `pathid`: path ID round trip + tamper tests; `pathidpaste`: paste the clipboard's path ID; `lang <game language name>` (first, before the tree opens): switch the game's text and font language for the run |
| `dev-book.txt` | the book tabs (tree view, then save table), the save table, then the tree view: wheel, pop-ups, stat pop-up, a conflicting pick, chapter rule, a pick + its load plan, undo |
| `dev-loadpath.txt` | with `dev-book.txt`: Load scene end to end; `back N` replays from another answer N scenes back, `chapter` from the chapter's start save, `start` from chapter 1 with an early change, `ahead` into the next chapter, `death` through a death, `offpath` a stop off the path, `first` the story's first scenes from a stand-in start (another run's chapter 1 save), `new` the same from a start built from the game's defaults, `file <name>` load that mod save as the table does (`file <name> norun`: with no run active) |
| `dev-history.txt` | log the save's scene path (`ShowedScenesList`) |
| `dev-autoplay.txt` | empty: log the buttons the game puts up after an answer; `chapters 2 3`: start a debug run (own main save `Debug_<date>`, from the newest `Chapter_1.dat`), play answers that keep the hero alive (no Death, most Will, build the stat the track's final gate needs - `AutoPlay.SafePick`), on through the hero's last death, and make a mod save when each listed chapter starts; `on`: play on from the scene on screen (after `dev-autoload`/`dev-load`) with the same picker, through the hero's last death, to the credits |
| `dev-stattrail.txt` | `<run folder> <stat> ...`: log those stats at every choice point of a run, oldest first (no `dev-autoload.txt` needed) |
| `dev-scene-tree.txt` | dump the loaded scene's UI components |
| `dev-bolt.txt` | dump the live Bolt graph of the choice machine |
| `dev-ironman.txt` | load the save as an Iron Man game |
| `dev-lang.txt` | switch the game language, to exercise the window rebuild |

Kill the game's own `…Sir Brante.exe` processes (not just the Proton wrapper)
plus `wineserver -k` after each run.

To screenshot the running game (Wayland/KDE session):
`DISPLAY=:1 xwininfo -root -tree | grep -i brante` finds the window's display,
then `spectacle -b -n -o shot.png` captures the full screen (it includes
XWayland windows). ImageMagick `import`, `xwd`, and `ffmpeg x11grab` do **not**
work here (black/`BadMatch` on the X1 root).

## Open work

- [docs/TODO.md](docs/TODO.md): what remains of the plan.
- Phases 0–13 are implemented and verified in-game: the tree over the whole
  playthrough with a forward simulation of the scenes ahead, path picks with
  Load scene (replayed by `AutoPlay`), the save/load rework (save ownership and
  repair, the one-time baseline, pre-rewind checkpoints including the chapter
  restart, the game-autosave mirror, the unified table with paging and the
  Original row) and id minting for id-less loads.
- Remaining: the full playthrough pass on a clean run and the README
  screenshot refresh.
