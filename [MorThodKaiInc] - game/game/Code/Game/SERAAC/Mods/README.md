# Modding SERAAC

A mod is a folder in `Mods/`. It can add or replace **art**, **audio**, and **game content** (characters, enemies,
items, events, trials...) with plain files, and run **code** for anything files can't express.
`ExampleMod/` uses every feature; it is switched off, so set `"enabled": true` in its `mod.json` to try it.

Where `Mods/` is:
- playing a build: `Mods/` next to `SERAAC.exe`
- developing: this folder (the project's `Mods/`), so edits never get overwritten by a build

## The mod manager

**Title screen > Mods** lists every installed mod with its icon, version, author, tags and description, what it
changed this session, and any errors (they also print to the console). There you can:

- switch a mod on or off (Enter / Space, or click its box). Turning a mod on also turns on the mods it needs;
  turning one off also turns off the mods that need it.
- change the load order (Page Up / Page Down). Later mods win when two change the same thing. A mod always
  loads after the mods it needs.
- open the Mods folder, and **Restart now** to apply your changes. Mods are only read when the game starts.

Your choices are saved in `Mods/load_order.json` (each player has their own; it isn't in git).
Start the game with `--no-mods` to ignore every mod.

## Folder layout

```
Mods/
  MyMod/
    mod.json          required
    icon.png          shown in the mod manager (optional)
    Art/...           same layout as the game's Art/ (see Art/README.md)
    Audio/...         same layout as the game's Audio/ (see Audio/README.md)
    Data/*.json       content: new things, and changes to the game's own
    *.dll             optional code mods
```

Folders whose name starts with `_` or `.` are skipped (handy for switching a mod off by renaming it).

### mod.json

```json
{
  "id": "my_mod",            // unique; defaults to the folder name. Other mods name it in "dependencies"
  "name": "My Mod",
  "version": "1.0",
  "author": "you",
  "description": "What it does. Shown in the mod manager.",
  "website": "where to get it or report bugs",
  "icon": "icon.png",        // in the mod's folder; square, any size. Without one, a tile with the initials
  "tags": ["content", "balance"],
  "dependencies": ["other_mod_id"],   // must be installed and on; they load first
  "enabled": true,           // the default; the mod manager's choice wins
  "priority": 0              // default load order: higher loads later and wins when two mods change the same thing
}
```

Only `id` is really needed; everything else has a default. A mod whose dependency is missing or off doesn't load,
and the mod manager says why.

Comments (`//`) and trailing commas are fine in every JSON file.

## Art and audio

Anything in a mod's `Art/` or `Audio/` is used **instead of** the game's file with the same path, or **added** if
the game has none. Nothing needs registering:

| To... | Put this in your mod |
|---|---|
| Re-skin Relle | `Art/Sheets/relle.json` + its PNG (same `"id": "char_relle"`; any file name works, the id decides) |
| Give a new enemy `ash_rat` a sprite | `Art/Sheets/ash_rat.json` with `"id": "enemy_ash_rat"` + PNG |
| Replace the title background | `Art/bg_title.png` |
| Give an item an icon | `Art/icon_<item id>.png` (or a pixel icon in Data, see `icons` below) |
| Add a combat background | `Art/Stages/<name>.png` (list it in your own copy of `Art/Stages/backgrounds.json`) |
| Change particle effects | your copy of `Art/Fx/particles.json` |
| Replace the combat music | `Audio/music/combat.ogg` |
| Replace a sound | `Audio/sfx/hit.wav` |

Tuning files (`backgrounds.json`, `particles.json`, `_camera.json`) are replaced **whole**: copy the game's file
into your mod and edit the copy. They still reload live while the game runs.

## Content (Data/*.json)

Every `.json` in a mod's `Data/` folder (subfolders too) is read. A file is one object holding any of these
sections. Each section is a list of entries, matched by `"id"`:

- **an id that exists:** only the fields you write change. `{ "id": "gnawer", "hp": 16 }` just makes Gnawers tougher.
- **a new id:** adds a new entry. Fill in every field it needs (look at the game's own entries).
- **`"$remove": true`:** takes the entry out (`{ "id": "knight", "$remove": true }`). Anything that still names it
  (an area's enemy pool, for example) keeps it until you change that too.

| Section | Game file it changes | Notes |
|---|---|---|
| `characters` | `Data/CharacterDb.cs` | playable characters and their skills |
| `enemies` | `Data/EnemyDb.cs` | without a sheet, a new enemy draws the mock-up monster in its `color` |
| `enemyTypes` | `Data/EnemyTypeDb.cs` | change only, matched by `"type"` (`Normal`, `Brute`, `Stalker`, `Apex`) |
| `commonActions` | `Data/ActionDef.cs` | change only: `defend`, `item`, `charge`, `position`, `step_forward` |
| `equipment`, `consumables` | `Data/ItemDb.cs` | each needs an icon |
| `events` | `Data/EventDb.cs` | Special Room events |
| `quests` | `Data/QuestDb.cs` | |
| `objectives` | `Data/ObjectiveDb.cs` | |
| `areas` | `Data/WorldDb.cs` | trials live inside their area (`"trials+"`) |
| `barks` | `Data/BarkDb.cs` | see below |
| `icons` | `UI/Icons.cs` | see below |

Sections run in that order across **all** mods, so one mod's area can use another mod's enemy.

### Writing values

Field names are the C# field names from those files, in any case (`baseHp`, `BaseHp`). The values:

| Kind | Write it as | Example |
|---|---|---|
| numbers, text, true/false | as is | `"hp": 20`, `"name": "Ash Rat"`, `"melee": true` |
| a choice (enum) | its name | `"kind": "Attack"`, `"applies": "Blight"`, `"slot": "Charm"` |
| positions (Ranks) | the slots, 1 = Front 1 ... 4 = Back 2 | `"usableFrom": "12"`, `"targetRanks": "1234"` |
| a colour | hex or RGB | `"color": "#b08060"` or `[176, 128, 96]` |
| other content | its id | `"frontPool": ["gnawer", "husk"]`, `"target": "wyvern"` |
| a new nested thing | a `{ }` object | a skill in `"skills"`, a trial in `"trials"`, `"windUp": { ... }` |

A skill can be named by id to share one that already exists: `"skills": ["pierce", { ...a new skill... }]`.
A passive can be named by its kind to reuse the game's: `"passives": ["Frontline", "Deadeye"]`.

### Changing lists instead of replacing them

Writing a list field replaces the whole list. Add a sign to the field name to edit it instead:

| Field name | Does | Example |
|---|---|---|
| `"skills+"` | adds to the end | `"frontPool+": ["ash_rat"]` |
| `"skills-"` | removes by id (one copy) | `"frontPool-": ["husk"]` |
| `"skills~"` | changes entries by id | `"skills~": [{ "id": "cleaver", "power": 1.5 }]` |

### barks

```json
"barks": {
  "relle":   { "Kill": ["Replaces her kill lines."], "Crit+": ["Adds a crit line."] },
  "default": { "Victory+": ["Said by anyone without their own Victory lines."] }
}
```
Triggers: `FightStart`, `AllyLow`, `AllyDown`, `Crit`, `Kill`, `Hurt`, `Victory`.

### icons

12 rows of 12 characters, using the ink letters at the top of `UI/Icons.cs` (`.` is transparent, `k` is the outline).
The id is the item's id. A PNG at `Art/icon_<id>.png` in your mod works too and wins over this.

## Code mods (DLL)

For things data can't describe (NPC dialogue runs code, so NPCs are added this way), put a .NET 9 DLL in the
mod's folder with a public class implementing `SERAAC.Core.ISeraacMod`:

```csharp
public sealed class Mod : ISeraacMod
{
    public void OnLoad(ModInfo mod)
    {
        NpcDb.All.Add(new NpcDef { ... });
        mod.Notes.Add("added an NPC");   // shows in the self-test report
    }
}
```

`OnLoad` runs once at startup, after every mod's `Data/*.json`, before the save loads. Everything in the game's
`*Db` classes is yours to read and change. `ExampleMod/Code/` is a ready project: run `dotnet build` there and
the DLL lands in `ExampleMod/`. It references the game's build folder; point it elsewhere with
`dotnet build -p:GameDir=<folder with SERAAC.dll>`.

**A DLL can do anything a program can do on your computer.** Only install code mods from people you trust.

## Checking a mod

```bash
dotnet run -- --selftest --no-balance
```

The self-test loads every enabled mod, reports each change it made and every error (a misspelled field, an
unknown id...), then runs the game's normal content checks over the result. For example, it catches a trial too
small for its objective. Drop `--no-balance` to also see how a modded party fares in simulated runs.

## Limits for now

- New *behaviour* (a new passive, status, action kind or enemy type) needs code changes in the game itself;
  data can only combine the ones that exist.
- NPCs need a code mod.
- Room layouts, maps and screens can't be modded yet.
- Saves remember ids. If a mod that added a character or item is removed, saves that used them may drop them.
