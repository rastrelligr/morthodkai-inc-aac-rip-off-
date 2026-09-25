# Art drop-in folder

Everything in `Art/` is copied next to the exe on build and loaded at runtime. There is **no content pipeline step**:
don't add these files to `Content.mgcb`. If a file is missing, the game draws a placeholder.
Check your work in the game with **Title screen > Art Viewer (dev)**.

## Characters and monsters: sprite sheets (any layout, any frame size)

Poses don't need to be the same size or sit on a grid. Put the sheet PNG in `Art/Sheets/` and add a small
`.json` next to it that says roughly where each pose is:

```json
{
  "id": "char_relle",            // what the game asks for (char_<characterId>, enemy_<enemyId>, ...)
  "image": "relle_sheet.png",
  "reference": "combat_idle",    // the pose that sets the character's size in game
  "frames": [
    { "name": "combat_idle", "box": [300, 100, 580, 890], "footX": 510 },
    { "name": "slam",        "box": [3100, 2060, 890, 935], "foot": [3330, 2832] }
  ]
}
```

- `box` = x, y, width, height on the sheet. It can be sloppy: the game trims it to the visible pixels.
  Just keep text labels and other poses out of it.
- The character stands on the **bottom of the pose** by default. `footX` sets where the feet are horizontally.
  `foot: [x, y]` sets both, for poses where something (a blade, an effect) reaches below the feet.
- Every pose is drawn at the **same scale** (set by `reference`), so a big attack pose really draws bigger.
- Big sheets are fine (the Relle sheets are 4000x3000). They are cut and shrunk when loaded.
- Draw characters **facing right**. Enemies are flipped automatically to face left.

Pose names the game uses:

| Pose | Used for |
|---|---|
| the `reference` pose | standing in combat |
| `portrait` | Hub, sidebar and dialogue portraits (falls back to the reference pose) |
| `walk` | the party walking in rooms (falls back to the reference pose) |
| any name in a skill's `Anim` | played while that skill resolves. `Anim = "swing1,swing2"` plays one pose per hit (see `Data/CharacterDb.cs`) |

Current sheets: `relle.json`, `relle_inverted.json` (Elle), `pillow.json`, `bram.json`, `noa.json`, `winter.json`, and `monster.json` (the mock-up monster).
A sheet can set `"scale"` to draw a character bigger or smaller than normal (Bram uses `1.1`).

### Monster recolours

Any enemy without its own sheet uses `enemy_mockup` (monster.png), recoloured with that enemy's `Color` in
`Data/EnemyDb.cs`. Grey maps to the colour, while black lines and white highlights are kept. Give an enemy real art by adding
`enemy_<id>` (for example `enemy_husk.json` + png).

## Single images

`Art/<id>.png` works for anything without a sheet:

| Where | Id | Size (px) |
|---|---|---|
| Combat background | `bg_combat_city`, `bg_combat_cathedral` | 1280 x 500 |
| Title / hub background | `bg_title`, `bg_hub` | 1280 x 720 |
| Room tiles | `tile_floor_<area>`, `tile_wall_<area>`, `prop_pillar_<area>` (area = `city` / `cathedral`) | 48 x 48 |
| Room features | `feature_chest`, `feature_chest_open`, `feature_shrine`, `feature_event`, `feature_portal`, `feature_objective`, `feature_entrance` | 64 x 64 |
| Shard pickups | `loot_shards` | 32 x 32 |
| **Item icons** | `icon_<itemId>` (for example `icon_rusted_blade`, `icon_tonic`). Missing ones use the built-in pixel icons in `UI/Icons.cs` | any, square |
| Portrait override | `portrait_<characterId>` | any |
