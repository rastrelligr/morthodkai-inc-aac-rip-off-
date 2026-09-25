# SERAAC prototype (new design)

Playable loop: **Prepare, then Enter Trial, Explore, Encounter, Loot, Decide, Extract, Upgrade.**
MonoGame 3.8 (DesktopGL) + MonoGame.Extended, .NET 9, 1280x720.

The previous designs are kept, uncompiled, in `_Archive/`. `OldDesign` is the old Game1 and minigames. `Unfinished3D` is the first-person attempt.

## Run

```
dotnet run                      # play
dotnet run -- --selftest        # headless checks + balance report (writes bin/.../selftest.txt)
dotnet run -- --script tour.txt # drive the game from a key script and save screenshots (see Core/ScriptRunner.cs)
```

The save file lives at `%AppData%/SERAAC/profile.json`. Use "New Game (erase save)" on the title screen to reset it.

## Controls

| Where | Keys |
|---|---|
| Menus / Hub | Arrows or WASD, Enter, Esc. Tab / Q / E / 1-4 switch hub tabs |
| Exploration | WASD move, E interact, I items, M map, Esc pause |
| Combat | Up/Down action, Enter select, Left/Right target, Backspace undo, hold Space to speed up, T toggle enemy intents |
| Display | Starts fullscreen (desktop resolution, black bars if needed). F11 or Alt+Enter toggles a resizable window |
| Debug | Hub: F5 +100 secured shards, F6 unlock all characters. Exploration: F8 warp to the NPC, F9 jump to the next room, F10 warp to Extraction. Anywhere: F12 screenshot |

## Design doc to code

| Design rule | Where |
|---|---|
| Turn = Choose Action for every party member, then Resolve by Speed (high first). Ties: party first, then front position | `Combat/Battle.cs` `ResolveOrder` |
| A user eliminated before its turn loses its action. Earlier actions affect later ones (a dead or moved target triggers a retarget, and being out of position makes the action fail) | `Battle.Execute`, `ResolveTargets` |
| 4 positions: Front 1, Front 2, Back 1, Back 2. Move 1 slot per Move action, swapping with any occupant. A dead unit leaves its slot empty | `Battle.DoMove`, `Battle.Kill` |
| Skills list the positions they can be used from and the positions they can hit | `Data/ActionDef.cs` (`UsableFrom`, `TargetRanks`) |
| Party of 4 chosen before the Trial | Hub, PARTY tab |
| Trial = connected rooms. Normal rooms start combat on entry. Special rooms: Treasure, Rest, Event, Elite, Extraction, Objective | `Trial/TrialMap.cs`, `Trial/Room.cs` |
| Room layouts are premade and reusable, and each room stores its type, layout, connections and state | `Trial/RoomLayout.cs` (5 templates, mirrored) |
| Loot appears after clearing, and you walk over it to collect. Collected loot never respawns | `TrialRun.SpawnLoot`, `ExplorationScreen.CollectPickups` |
| Trial Loot stays unsecured until Extract, and is all lost on defeat | `TrialRun.Extract` / `Fail` |
| Extraction points are placed away from the entrance. Extract or Continue | `TrialMap.AssignTypes`, the Extract modal |
| Main Objective unlocks the next Area, and is saved immediately (even if you die afterwards) | `TrialRun.OnCombatWon` |
| Upgrades (HP / Damage / Speed) cost Secured Loot, get more expensive each level, and are permanent | `Meta/Progression.cs` |
| Equipment (Weapon + Charm slots) is equipped before a Trial and kept on defeat | `Progression.Equip`, Hub EQUIPMENT tab |

## Tuning

All content is plain C# data. Change the numbers and rebuild:

- Characters, skills, passives, unlock hints: `Data/CharacterDb.cs` (SP, crit and passive numbers: constants at the top of `Combat/Battle.cs`)
- NPCs and dialogue: `Data/NpcDb.cs`. Quests: `Data/QuestDb.cs` (progress rules: `Meta/Quests.cs`)
- Item icons: `UI/Icons.cs` (12x12 pixel maps, overridden by `Art/icon_<id>.png`)
- Enemies: `Data/EnemyDb.cs`
- Areas, trials, enemy pools: `Data/WorldDb.cs`
- Equipment and consumables: `Data/ItemDb.cs`
- Events: `Data/EventDb.cs`
- Global difficulty (`EnemyHpMultiplier` 1.5, `EnemyDamageMultiplier` 1.3), scaling by depth and tier, encounter sizes and loot amounts: `Trial/TrialRun.cs`
- Upgrade costs and per-level gains: `Meta/Progression.cs`

Run `--selftest` after changing numbers. It auto-plays 400 runs per trial for a fresh party and an upgraded one, and prints survival and win rates plus a **roster parity** table (one party member swapped at a time). `--balance-only` skips the structural checks and is faster when tuning.


## Characters, NPCs and quests

- **Relle (Striker)**, built from the designer spec. SP (start 1, +1 per turn, max 5). Pierce (0 SP, any enemy), Blood Feast
  (2 SP, 2 hits on every enemy, then scrambles their positions), Cleaver (3 SP, front row, and only usable from the front row).
  Passives: Flesh-Eating Blade (Feast stacks: every 4th skill auto-crits, and each hit heals 20% of the damage) and Frontline (x1.25 damage in Front 1/2).
  Relle is the **protagonist** and leads the default party (Relle, Bram, Winter, Sister Noa). Also playable from the start: Pillow (Knight) and Mireille (Occultist).
- **Elle (Inverted Relle, Adept)** has a placeholder kit (Needle / Tidewash / Undercut, plus Feast and Backline).
  The only unlockable character. The Reflection appears in trials until Elle is unlocked. Take its quest, beat an Elite with Relle in the party, then talk to it again.
- **Maren** gives a kill quest ("Thin the Herd", 8 infected). The reward is the Butcher's Hatchet, which counts as trial loot until you extract.
- **The Angel** (the goddess from the World Overview) gives one blessing per trial (heals the party 30%) and hints about the Black Goat.
- **Topsala Tap / MathCG** (team quest) is defined but not added to `NpcDb.All` yet, and `QuestGoal.KillMathCGBoss` has no progress rule in `Meta/Quests.cs`.
- Each trial can have one NPC room (`@` on the map). The Hub's **JOURNAL** tab lists quests and how to unlock Elle.
- Placeholder sprites: Pillow, Bram, Noa and Winter in `Art/Sheets/*.json` (Winter lies prone: `"scale": 0.4` so she fits one slot).
- **Everyone is balanced to Relle.** Each original character now has SP skills and two passives (Pillow: Frontline + Knight's Stance; Bram: Thick Hide + Frontline; Noa: Devotion + Backline; Winter: Deadeye + Backline; Mireille: Exploit Weakness + Backline). Enemies were then scaled up to match.
- Target curve (simulated, full clear of every fight): fresh party survives the first trial about 98% of the time (it's the learning dive), wins the Trial 2 boss about 20%, and can't handle Area 2. A veteran party (upgrades + gear) wins the Trial 2 boss 100% and the final boss about 34%.
- Monsters: every enemy uses the mock-up monster recoloured by its `Color`, until it gets its own sheet.

Spec interpretations to confirm with the designer:
- Pierce "Target Type: All Enemy" is read as *any single enemy*, and Blood Feast "All Enemy" as *hits every enemy*.
- Cleaver "directly in front of them" is read as usable from Front 1/2 and hitting enemy Front 1/2.
- The spec doesn't define SP gain or crit. They're set to +1 SP per turn, a 5% base crit for everyone, and x1.5 crit damage.
- The Feast crit consumes the stacks and doesn't grant a new one, so exactly every 4th skill crits.
## Decisions the design docs left open (easy to change)

- Enemy intents are shown during Choose Action (press T to hide them).
- If a chosen target dies before the action resolves, the action retargets instead of being wasted.
- Defend / Taunt last until that character acts again, so a slow tank still protects the party.
- Alive members are packed toward the front at the start of each fight. Formation changes carry between fights.
- Consumables (Tonic, Ember Salt) only exist within one Trial. Equipment is the item loot that gets secured.
- Extraction points and the objective room are visible on the map from the start. The route to them is not.
- A shrine heals 40% and revives fallen members.
