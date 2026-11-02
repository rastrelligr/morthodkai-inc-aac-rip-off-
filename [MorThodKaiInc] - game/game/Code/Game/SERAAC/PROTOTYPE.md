# SERAAC prototype (new design)

Playable loop: **Prepare, then Enter Trial, Explore, Encounter, Loot, Decide, Extract, Upgrade.**
MonoGame 3.8 (DesktopGL) + MonoGame.Extended, .NET 9, 1280x720.

The previous designs are kept, uncompiled, in `_Archive/`. `OldDesign` is the old Game1 and minigames. `Unfinished3D` is the first-person attempt.

## Run

```
dotnet run                      # play
dotnet run -- --selftest        # headless checks + balance report (writes bin/.../selftest.txt)
dotnet run -- --script tour.txt # drive the game from a key script and save screenshots (see Core/ScriptRunner.cs)
dotnet run -- --intro --script x.txt  # scripts skip the intro video and "press any key" unless --intro is given
dotnet run -- --story airlift   # jump to a prologue chapter: dorm, spar, airlift, hub, academy
```

## Prologue (playtest flow)

New Game plays the prologue; Continue resumes the chapter saved in `Profile.Story` (`Story/StoryFlow.cs`).

| Chapter | What happens | Where |
|---|---|---|
| dorm | Naomi wakes Relle (visual novel), then Relle walks her room, inspects things, takes her blade and leaves | `Art/Maps/RelleDorm.tmx` |
| spar | Time skip to the training floor; the sparring match teaches actions, Ethergy, Focus, Guard vs a wind-up, Cleaver, Feast (one allowed action per turn, coach line on screen) | `Combat/CombatTutorial.cs`, `StoryScripts.SparTutorial` |
| airlift | The Airlift platform above the clouds (sky, clouds, fog, wind); talking to Naomi starts the briefing | `Art/Maps/RooftopAirlift.tmx` |
| hub | Naomi walks through Party, Upgrades (200 shards given once), Equipment and Trials; only the Fallen Academy can be deployed to | `HubScreen` (`Tutorial`) |
| academy | The Fallen Academy as a full trial. Killing the Wyvern plays a short scene; extracting afterwards plays the flight home and the end-of-playtest screen | `StoryFlow.OnMainObjective` / `OnExtracted`, `Screens/EndScreen.cs` |

- All prologue lines: `Story/StoryScripts.cs`. Portraits and name colours: `VnCast` in `UI/VnDialogue.cs`.
- Dialogue (story and NPCs) is a visual novel box: click / Enter / wheel down advances, **L** or wheel up opens the log, **F** auto, **Tab** (or hold Ctrl) skip, **H** or right click hides the box.
- Maps: Tiled files with collision / walkable / interact / npc / spawn object layers, reloaded on save, F9 shows the shapes (`Art/Maps/README.md`).
- Atmosphere (dust, fog, smoke, clouds, embers, wind, light rays) behind and in front of fights, over exploration rooms and on the story maps: `Art/Fx/ambience.json` (hot-reloaded), `Fx/Ambience.cs`. A background can pick its own preset with `"ambience"` in `Art/Stages/backgrounds.json`.

The save file lives at `%AppData%/SERAAC/profile.json`. Use "New Game (erase save)" on the title screen to reset it.

## Controls

| Where | Keys |
|---|---|
| Mouse (everywhere) | Left click selects / confirms, right click goes back (like Esc). Every menu row, tab and button is clickable |
| Menus / Hub | Arrows or WASD, Enter, Esc. Tab / Q / E / 1-4 switch hub tabs |
| Exploration | Click a spot to walk there, click a chest / shrine / NPC / portal to walk over and use it. Or WASD move, E interact, P party (check members, change the formation), I items, M map, Esc pause |
| Combat | Click an action, then click its target (or click an enemy straight away to use the selected action on it). Keys: Up/Down action, Enter select, Left/Right target, Backspace undo. Hold the mouse button or Space to speed up. Hover a turn-order diamond for its speed and action |
| Display | Starts fullscreen (desktop resolution, black bars if needed). F11 or Alt+Enter toggles a resizable window |
| Playtest | Title > **Playtest**: jump straight to one fight (pick enemies, trial, depth), a trial (pick objective and starting room) or the hub sandbox, with any party, Fresh / Veteran / Maxed, full SP and item kits. Runs on a throwaway save (`Meta/Playtest.cs`, `Screens/PlaytestScreen.cs`). In any playtest fight, **F2** or the button under the objective box leaves it (one-fight tests go back to the menu; fights in a playtest trial are skipped, room cleared without loot). Testers log results in the balance sheet's Playtest tab |
| Debug | Anywhere: F3 FPS counter. Combat: F4 fires every particle preset. Hub: F5 +100 secured shards, F6 unlock all characters. Exploration: F6 fight right here (enemies of this or the nearest room), F7 warp to an objective site, F8 warp to the NPC, F9 jump to the next room, F10 warp to Extraction. Anywhere: F12 screenshot |

Scripts (`--script`) can also `click <x> <y>` at a 1280x720 position.

## Combat presentation

| What | Where |
|---|---|
| Pre-rendered background behind every fight, chosen per room / area / elite / boss, split into parallax layers (_Backdrop / _Background / _Middleground / _Foreground) | `Art/Stages/backgrounds.json`, `Stage/Backgrounds.cs` |
| Dialogue backgrounds: the room's background by default, or set per NPC / per dialogue node (crossfades) | `NpcDef.Background`, `DialogueNode.Background` in `Data/NpcDb.cs`, `Art/Dialogue/README.md` |
| Battle UI (from the artist's design): reactions top-left, turn priority diamonds top-centre, objective top-right; status / skills + Special Abilities / description at the bottom | `Screens/CombatScreen.cs`, icons `ui_*` in `UI/Icons.cs` (override with `Art/icon_ui_<name>.png`) |
| Special Abilities: Guard (acts first), Item (a trial consumable, as that character's action), Focus (the design doc's "Advance": gather energy, +2 SP), Position (swap with any slot). Priority actions (Guard, Mara's Guard and Aegis Wall, Snare Line, Reweave) resolve before everything else | `CommonActions` in `Data/ActionDef.cs`, `Battle.ChargeSp` |
| Character reactions in combat (placeholder lines) | `Data/BarkDb.cs` |
| Cinematic camera: zoom, pan, sway, shake, hit-stop, through a MonoGame.Extended OrthographicCamera. The background follows less than the characters (parallax) and never shows its edges | `Art/Stages/_camera.json`, `Stage/CombatCamera.cs` |
| Melee attackers run to their target, hit, and run back (area melee hops between victims) | `ActionDef.Melee` (default: front row to front row), `CombatScreen.PlayEvent` |
| Particles (hot-reloaded) on MonoGame.Extended's particle engine: one emitter per preset | `Art/Fx/particles.json`, `Fx/Particles.cs`, a skill's `HitFx` / `CastFx` |
| UI colours, all from `Art/Palette.png` | `Palette` in `Core/Draw.cs` |
| Mouse: last frame's clickable rectangles, so screens tag what they already draw | `UI/Ui.cs`, `Core/Input.cs` |
| MonoGame.Extended is used for: input edge detection (`Core/Input.cs`), the combat camera, particles, shapes (`Draw.Line` / `Circle` / `Ellipse`), eased UI with its Tweener (`Core/Tweens.cs`: screen fades, menu bar, modal pop-ins, banners, objective glow, damage numbers, result reveal) and the FPS counter. Screens (a stack: combat sits over exploration) and character sprites (foot pivots, trimming, recolours) stay custom | `Core/`, `Fx/Particles.cs`, `Stage/CombatCamera.cs` |

## UI and audio

- Title flow: on launch the intro video plays (`Art/MainMenu/Video/Intro.mp4`, any key or click skips it), then the title picture fades up and holds still with "Press any key to continue". The next key or click brings it to life: the layers follow the mouse (parallax) and slide left while the menu comes in on the right. Coming back to the title from the game goes straight to the menu (`Screens/MainMenuScreen.cs`).
- Title picture: `Art/MainMenu/0.png, 1.png, ...` are its layers, drawn in number order (the highest is the closest; all the same size, transparent around what they show). Embers drift up behind everything but the backdrop, around the character, and in front of the near layers (`Stage/TitleScene.cs`: framing, parallax strength and the ember groups are constants at its top).
- Video playback (`Core/VideoPlayer.cs`) uses Windows Media Foundation, since MonoGame DesktopGL has no video player: any MP4 (H.264 + AAC) Windows can play. Elsewhere, or if the file is missing, the intro is skipped. A video that ends in a black, silent stretch is cut once that stretch reaches 2 seconds (`VideoPlayer.TailSeconds`), so a long empty tail doesn't hold up the title.

- Fonts (`Content/Fonts/*.spritefont`): **Cinzel** (serif, SIL Open Font License, `Content/Cinzel-OFL.txt`) for titles, headers and character names, **Roboto** for body text. Character names and call-outs (CRIT, RIPOSTE!, INTERRUPTED!) use Cinzel too.
- Look (modelled on Final Fantasy XVI's menus, in the game's rose accent): translucent black panels with thin light outlines, selections that glow from the left inside a bright outline with a pointer beside them, accent section bars that fade out (`Ui.SectionBar`), a top tab bar with Q / E key caps (`Ui.TabBar`), a key-hint footer (`Ui.Footer`), lines of light, crests and letter-spaced titles (`Draw.GlowLine`, `Draw.Ornament`, `Draw.Spaced`), announcement bands (`Ui.Banner`) and an ember backdrop (`Ui.Backdrop`). Shared by every screen through `Draw.Panel` / `HighlightBar` / `SlabPanel` and `Ui.Button` / `SelectedRow` / `Card`. `Draw.SlantFactor = 1` brings back the old slanted edges; `Art/ui_pointer.png` replaces the drawn cursor.
- Game terms in descriptions are coloured automatically (`UI/RichText.cs`; colours `Palette.Kw*`): Blight green, Mark orange, Slow blue, Guard violet, healing mint, damage coral, "Acts first" / wind-ups gold...
- Combat: skills show real numbers (5-7 DMG, +6 HP), status icons with hover tooltips, SP / stack pips with tooltips, a turn-order caption, identical enemies lettered (Gnawer A / B), a wind-up alert, and a pulsing red edge when someone is at 30% HP or less.
- Audio: drop `Audio/sfx/<id>.wav` (or `.ogg`) and `Audio/music/<id>.ogg` files in and they play; files named otherwise are mapped to ids in `Audio.Files` (the UI sounds in `Audio/UI/`). Menu sounds come from one shared hook (`UI/UiSounds.cs`) (`Core/Audio.cs`; ids in `Audio/README.md`). Missing files are silent.
- Mods: folders in `Mods/` can replace or add art and audio, add or change content with JSON (characters, enemies, items, events, trials...), and run code DLLs (`Core/Mods.cs`, `Data/ModContent.cs`; how-to in `Mods/README.md`, working sample in `Mods/ExampleMod/`). Title screen > Mods is the mod manager (`Screens/ModsScreen.cs`): icons and details, switch mods on/off, load order, restart to apply; `--no-mods` turns them all off.

## Design doc to code

| Design rule | Where |
|---|---|
| Turn = Choose Action for every party member, then Resolve by Speed (high first). Ties: party first, then front position | `Combat/Battle.cs` `ResolveOrder` |
| A user eliminated before its turn loses its action. Earlier actions affect later ones (a dead or moved target triggers a retarget, and being out of position makes the action fail) | `Battle.Execute`, `ResolveTargets` |
| 4 positions: Front 1, Front 2, Back 1, Back 2. The party moves with Position (to any slot, swapping with whoever is there); enemies step forward 1 slot. A dead unit leaves its slot empty | `Battle.SwapTo`, `Battle.DoMove`, `Battle.Kill` |
| Skills list the positions they can be used from and the positions they can hit | `Data/ActionDef.cs` (`UsableFrom`, `TargetRanks`) |
| Party of 4 chosen before the Trial | Hub, PARTY tab |
| Trial = connected rooms. Special rooms: Treasure, Rest, Event, Elite, Extraction, Objective site | `Trial/TrialMap.cs`, `Trial/Room.cs` |
| Combat does not start on entry: a room's enemies (rolled when the trial starts) roam it, notice the party by sight (or come looking after a few seconds), and chase it. Touching one starts the fight. A chasing pack follows the party through up to 2 doors (`Pack.Follow`), and a room whose pack spotted the party is on guard next visit. Objective guardians hold their ground and give up if you run | `Trial/Pack.cs`, `ExplorationScreen.UpdatePack` |
| Every enemy has one Enemy Type (Normal, Brute, Stalker, and Apex for bosses). The Type sets Target Preference and behaviour (and chase speed / sight); it does not set Position (`EnemyDef.Row`) | `Data/EnemyTypeDb.cs`, `Combat/CombatAI.cs` |
| Objective: each trial has an Objective Pool, one is picked per run and shown from the start (top bar). Types: Defeat Target (a specific enemy, or N of a Type), Collect Item, Interact, Reach Location. Progress is tracked, completion is announced, and it does not end the run | `Data/ObjectiveDb.cs`, `TrialRun.RecordKills` / `UseObjectivePoint` |
| Extraction is sealed until the objective is complete | `TrialRun.CanExtract`, `ExplorationScreen.Interact` |
| Room layouts are premade and reusable, and each room stores its type, layout, connections and state | `Trial/RoomLayout.cs` (5 templates, mirrored) |
| Loot appears after clearing, and you walk over it to collect. Collected loot never respawns | `TrialRun.SpawnLoot`, `ExplorationScreen.CollectPickups` |
| Trial Loot stays unsecured until Extract, and is all lost on defeat | `TrialRun.Extract` / `Fail` |
| Extraction points are placed away from the entrance. Extract or Continue | `TrialMap.AssignTypes`, the Extract modal |
| An objective marked Main also completes the Area's Main Objective: unlocks the next Area, saved immediately (even if you die afterwards) | `ObjectiveDef.Main`, `TrialRun.Advance` |
| Upgrades (HP / Damage / Speed) cost Secured Loot, get more expensive each level, and are permanent | `Meta/Progression.cs` |
| Equipment (Weapon + Charm slots) is equipped before a Trial and kept on defeat | `Progression.Equip`, Hub EQUIPMENT tab |

## Tuning

All content is plain C# data. Change the numbers and rebuild:

- Characters, skills, passives, unlock hints: `Data/CharacterDb.cs` (SP, crit and passive numbers: constants at the top of `Combat/Battle.cs`)
- Enemy Types (target preference, chase speed, sight): `Data/EnemyTypeDb.cs`. Objective pools: `Data/ObjectiveDb.cs`, assigned per trial in `Data/WorldDb.cs`. Chase timings: constants at the top of `Trial/Pack.cs`
- NPCs and dialogue: `Data/NpcDb.cs`. Quests: `Data/QuestDb.cs` (progress rules: `Meta/Quests.cs`)
- Item icons: `UI/Icons.cs` (12x12 pixel maps, overridden by `Art/icon_<id>.png`)
- Enemies: `Data/EnemyDb.cs`
- Areas, trials, enemy pools: `Data/WorldDb.cs`
- Equipment and consumables: `Data/ItemDb.cs`
- Events: `Data/EventDb.cs`
- Global difficulty (`EnemyHpMultiplier` 1.5, `EnemyDamageMultiplier` 1.6), scaling by depth and tier, encounter sizes and loot amounts: `Trial/TrialRun.cs`
- Combat stages, camera and particles are JSON under `Art/Stages` and `Art/Fx`, and reload while the game runs (no rebuild)
- Upgrade costs and per-level gains: `Meta/Progression.cs`

Run `--selftest` after changing numbers. It auto-plays 400 runs per trial for a fresh party and an upgraded one, and prints survival and win rates plus a **roster parity** table (one party member swapped at a time). `--balance-only` skips the structural checks and is faster when tuning.


## Characters, NPCs and quests

- **Roster** (from "New Playable Character Design"; `Data/CharacterDb.cs`). Five characters: Relle, Mara, Elian, Naomi and Pillow. Default party, front to back: Relle, Mara, Naomi, Elian.
  - **Relle (Striker)**: Pierce (0 SP, front row, 5 dmg), Gnash (1 SP, front row, 3.5 dmg + Blight 2 x 2 turns, +30% lifesteal), Blood Feast (2 SP + 3 HP, 2 x 2 dmg to every enemy, then scrambles them), Cleaver (3 SP, Front 1 only, 7 dmg). Feast heals 10% of each hit.
    Passives: Flesh-Eating Blade (every 4th skill auto-crits, hits heal 20% of damage) and Frontline (x1.25 damage in Front 1/2).
  - **Classes:** only 3 for now. **Striker** (Relle, Naomi, Pillow), **Warden** (Mara), **Adept** (Elian).
  - **Mara (Warden)** replaces Bram (keeps the `warden` id so saves carry over): Shield Bash (0 SP, 4 dmg, push back 1, interrupts), Iron Rebuke (1 SP, spends all Guard: 0.6 + 1.0 Power per Guard, interrupts), Guard (2 SP, acts first, attacks on one ally hit Mara instead), Aegis Wall (3 SP, acts first, Mara + adjacent allies take 40% less, she can't move).
    Ironbound Aegis: Shield Bash / Guard / Aegis Wall each give 1 **Guard**; at 2, the next attack on an ally next to her is redirected to her at half damage. She strikes back whenever she intercepts. Bulwark: 20% less damage in Front 1/2.
  - **Elian (Adept)** replaces Sister Noa (keeps the `priest` id): Mend (0 SP, heal; Woven heals everyone for 60%), Snare Line (1 SP, acts first, pull an enemy 2 forward), Bind (2 SP, 2 dmg, -3 Speed for 2 turns), Reweave (2 SP, acts first, pull an ally 1 toward him; swap if taken).
    Passives: Sympathetic Thread (healing x1.5 on allies under 50% HP), Backline (healing x1.2 in Back 1/2), and **Threadweaver**: every skill gives 1 Thread; at 3, the next Mend / Bind / Snare Line is **Woven** and reaches every ally / enemy.
  - **Naomi (Striker)**, swift crowd control (HP 30, Damage 5, Speed 8). Fast enough to dart in from Back 1: every skill but Sweeping Kick works from Front 1, Front 2 or Back 1 (Frontline only pays at the front): Flicker Cut (0 SP, Slow), Sweeping Kick (1 SP, front row, push back 1), Pommel Strike (2 SP, acts first, interrupts, Slow), Thousand Cuts (3 SP, every enemy, Mark). Passives: Read the Opening (x1.3 vs Slowed / Marked / Blighted) and Frontline. Idle-only sheet for now.
  - **Pillow (Striker)**, the developer cameo: Power Nap (Guarded after every skill; while Guarded he swats back at single-target attackers) and Twelve Out of Ten (x1.25 damage in Front 1/2). Pixel Sword (0 SP, front), Fetch, Project! (1 SP, his dog drags an enemy from behind the front 1 forward and Marks it), I Hate Math (2 SP, every enemy, scrambles their positions), MorThodKai Is Open (3 SP, acts first, big hit at the front, heals 35% of the damage).
  - Winter, Elle and Mireille are removed from the game.
  - Every character has a dialogue portrait (`Art/Dialogue/Portrait`), a battle sheet (`Art/Sheets`) and an overworld sprite (`Art/Overworld`, `CharacterDef.OverworldArt`). Pillow is a developer cameo and a hidden character: he joins only if Relle finds him on the Airlift and talks to him before the squad leaves. Until then the Sanctum does not list him (a locked character with no `UnlockHint` stays hidden).
  - Gnash, Iron Rebuke, Mara's counter, Threadweaver / Woven, Snare Line and Pillow's riposte are our additions (not in the design doc), to make the kits as deep as Relle's Feast.
- No NPC is in rotation right now (the Reflection left with Elle; the Angel, Maren and Topsala Tap are commented out until they get sprites). Each trial can have one NPC room (`@` on the map) when `NpcDb.All` has someone. The Hub's **JOURNAL** tab lists quests.

## Squad conversations and reactions

Talking to squadmates works like crew talks between missions (`Story/Squad.cs`, `Story/SquadTalk.cs`):

- **One talk per squadmate per story stop.** For now the only stop is the Airlift: Mara, Elian and Naomi each get one conversation; after that they have a short line. Later stops come after each main mission.
- **Topic menu.** A greeting, then topics, each asked once; leaving ends the talk.
- **Romance** is not a menu item. A flirt is one answer among others inside a topic (Mara: "Ready for the Wyvern?", Elian: the notebook, Naomi: this morning), so it is easy to miss. It records a romance interest (`Profile.RomanceInterest`).
- **Reactions.** Choices carry a hidden tone (kind, bold, ask, flirt). Each squadmate answers in character, and the choice moves their rapport (`Profile.Rapport`); the player reads how it landed from the reply. In the briefing, one choice moves several squadmates at once.
- **Dialogue Helper** (Options, off by default) shows the tones as tags (KIND, BOLD, ASK, heart, END) and pops up the reactions ("Mara approves", "Naomi will remember that", "Elian liked that").
- **Interrupts.** Some lines open a timed prompt over the box (press **E** or click it). Take it and the scene changes; let the bar run out and the moment passes.
- **Payoff.** The squadmate with the highest rapport sits next to Relle on the flight home (a warmer line with a romance interest).
- **Pillow** is not part of this. Knock on the Osprey three times on the Airlift and he rolls out of the cargo bay; every talk is a new bit about nothing to do with the game (hot dogs, noodles, rating dogs, hating math, the MorThodKai fried chicken shop that is never open...). Talking to him unlocks him.
- **Enemies** ("New Enemies Type"; `Data/EnemyDb.cs`, own sheets in `Art/Sheets/`): Gnawer (Normal, Bite 2), Husk (Brute, Heavy Crash 3; curled while roaming, spread like a butterfly in battle), Wraith (Stalker, Spectral Lunge 2, hunts the back line), and the **Wyvern** (new **Apex** boss type, HP 60: Tail Sweep, Venom Fang with Blight, Binding Coils with Slow, and **Rear Up**: a telegraphed wind-up into **Skyfall Crush** on the whole party next turn, answered by Guard, the Aegis Wall or an interrupt; enrages at half HP: cleanses, +2 Speed, winds up more often). Brutes and Apex are Steadfast (can't be knocked back). The Wyvern is the Fallen Academy's Main Objective. The Drowned Cathedral is commented out until it gets new enemies.
- Target curve after playtest 1 (testers found everything too easy; simulated with `--balance-only`): City Outskirts ~100% (learning dive); the AI clears every room of the Fallen Academy ~10% of the time (people skip fights and use items); the Wyvern alone, at full HP, ~70% at depth 3 and ~50% at depth 6; a veteran party (upgrades + gear) clears both ~100%.
- Full numbers, formulas and what is tunable: `Docs/SERAAC_Balance_Sheet.xlsx`.

Spec interpretations to confirm with the designer:
- Pierce "Front Only" = enemy Front 1/2. Blood Feast "All Enemy" = hits every enemy. Cleaver "Front 1 Only" = enemy Front 1, used from Front 1/2.
- Relle's Frontline says "Front or Mid 1": there is no Mid position, so it is Front 1/2.
- Mara's Guard skill and Aegis Wall last **until Mara acts again** (like the Guard special ability), not "until the turn ends": she is slow (Speed 3), so end-of-turn would protect almost nothing.
- Mara's skill is called **Guard**, like the party's Guard special ability; her stacks are shown as "Guard x/2". Rename one if it confuses players.
- Aegis Wall's damage reduction (40%) and the redirected-attack reduction (50%) aren't in the spec; both are constants in `Combat/Battle.cs`.
- Elian's **Thread** has no effect in the spec; we made it pay off (Threadweaver: 3 Thread = the next Mend / Bind / Snare Line is Woven). Every skill gives Thread, like Relle's Feast.
- Reweave moves the ally 1 position **toward Elian** (no separate forward / backward choice), swapping if the spot is taken.
- Sympathetic Thread boosts healing (Mend's primary effect). It doesn't change Bind or Reweave.
- The Wyvern's kit and the Apex type are our own design (the spec only says "a boss, a serpent-like dragon").
- The spec doesn't define SP gain or crit. They're set to +1 SP per turn, a 5% base crit for everyone, and x1.5 crit damage.
- The Feast crit consumes the stacks and doesn't grant a new one, so exactly every 4th skill crits.
## Decisions the design docs left open (easy to change)

- Enemy intents are shown during Choose Action (press T to hide them).
- If a chosen target dies before the action resolves, the action retargets instead of being wasted.
- Defend / Taunt last until that character acts again, so a slow tank still protects the party.
- Alive members are packed toward the front at the start of each fight. Formation changes carry between fights.
- Consumables (Tonic, Ember Salt) only exist within one Trial. Equipment is the item loot that gets secured.
- Extraction points and objective sites are visible on the map from the start. The route to them is not.
- Enemy Types: Normal (Gnawer), Brute (Husk), Stalker (Wraith) from the design; Apex is our addition for the Wyvern boss (no favourite position, hunts the weakest).
- "Defeat N of a Type" objectives always have at least N+1 such enemies in the trial (swapped in if the roll was short), so one room can be skipped. Kills in any fight count.
- Collect Item and Reach Location complete just by walking up to the objective point; Interact needs E.
- Debug warps (F7 / F8 / F10) put you next to the feature, so a pack may already be close.
- A shrine heals 40% and revives fallen members.
