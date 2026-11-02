# Story maps (Tiled)

The walkable story maps are Tiled files read by the game at runtime (`World/TiledMap.cs`, `Screens/OverworldScreen.cs`).
Open them in Tiled straight from this folder. Their tilesets (`.tsx`) and images sit next to them.
Save a map while the game runs and it reloads on the spot. **F9** in game shows every shape.

| Map | Chapter |
|---|---|
| `RelleDorm.tmx` | Relle's room after Naomi wakes her |
| `RooftopAirlift.tmx` | The Airlift platform |

Save tile layers as **CSV** (Map > Map Properties > Tile Layer Format).

## Object layers

Add an object layer and draw shapes on it. What a shape does comes from its **Class**
(or from the layer's name when the class is left empty).

| Class (or layer name) | Shape | What it does |
|---|---|---|
| `collision` (layer `Collision`) | rectangle, ellipse, polygon | Blocks Relle's feet |
| `walkable` (layer `Walkable`) | rectangle, ellipse, polygon | When a map has any, Relle can only stand inside them (open areas like the rooftop) |
| `interact` (layer `Interact`) | any | Something to use with E or a click. The **Name** is what the story code looks up |
| `exit` | any | Same as interact; the dorm uses `door` to leave the room |
| `npc` | point | A character. Name = who it is (`Naomi`). Property `sprite` = art id (default `Overworld/<name>`) |
| `spawn` | point | Where Relle starts. The story uses `player_start` |

Only the feet collide (a small circle at the bottom of the sprite), so a wall's shape should cover where feet can't go,
not the whole wall picture.

### Properties

| Property | On | Meaning |
|---|---|---|
| `label` | interact, exit, npc | Text on the floating `[E]` prompt |
| `text` | interact, npc | Lines to show when nothing in code handles the object. Split lines with `\|`, put a speaker first with `Name:` (`Relle: Still warm.\|Relle: No. Focus.`) |
| `above` (bool) | tile layer | Drawn over the characters (the Osprey's wings and rotors) |

What each named object says in the prologue is in `Story/StoryScripts.cs` (`Inspect`, `AirliftInspect`).
A new object with only a `text` property works without touching code.

## Size and art

- Tiles draw at 2x. Characters use `Art/Overworld/<id>.png` (one front-facing frame; `name@4.png` = a 4-frame walk strip).
- Scale, backdrop (black or sky), atmosphere and music per map are set in `Story/StoryFlow.cs` (`MapScene`).
  Atmosphere presets (fog, clouds, wind, dust) are in `Art/Fx/ambience.json` and reload while the game runs.
