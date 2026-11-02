# Combat backgrounds and camera

Every fight is drawn in front of a pre-rendered background image from this folder.
Saved changes to the `.json` files here apply while the game runs (no rebuild). New or changed images need
the game restarted.

## Adding a background

1. Put the image here as a PNG: `Art/Stages/<name>.png`. **1280 x 720 or bigger** (bigger is sharper on 1080p and
   up, and gives the camera more room). The characters' feet stand at **y = 430** on a 1280 x 720 frame, so
   paint the floor there.
2. List it in `backgrounds.json`:

```json
{ "image": "city_street", "area": "city", "use": ["normal"] }
```

| Field | Meaning | Default |
|---|---|---|
| `image` | file name without `.png` | required |
| `area` | area id (`city`, `cathedral`); leave out for any area | any |
| `trial` | trial id, or a list of them (`"city_academy"`, `["city_outskirt", "city_academy"]`). Ids are in `Data/WorldDb.cs` | every trial |
| `use` | which fights: `normal`, `elite` (Elite rooms), `boss` (Objective rooms) | `["normal"]` |
| `enemies` | only for fights containing one of these enemy ids, e.g. `["hollow_warden"]`: **a specific elite or boss background** | none |
| `weight` | relative chance when several backgrounds match equally well | 1 |
| `overscan` | how much bigger than the screen the image is drawn, so the camera can pan and shake without showing an edge | 1.12 |
| `offsetY` | moves the image up (-) or down (+) in pixels, to line the floor up with the characters' feet | 0 |
| `layers` | parallax layers with your own files / depths (see below). Usually left out | found by name |

## Parallax layers

Split a background into layers and save them next to each other with these suffixes:

| File | Depth (`_camera.json`) | What goes on it |
|---|---|---|
| `<name>_Backdrop.png` | `backdropDepth` 0.1 | the farthest: sky, horizon. Opaque |
| `<name>_Background.png` | `backgroundDepth` 0.35 | far things: buildings (transparent where the sky shows) |
| `<name>_Middleground.png` | `middlegroundDepth` 0.8 | the ground the characters stand on (transparent above it) |
| `<name>_Foreground.png` (optional) | `foregroundDepth` 1.3 | things in front: grass, rubble, poles. Drawn **over** the characters |

List the entry by `<name>` as usual (`{ "image": "City_N_New2", ... }`): the layers are found automatically,
and a plain `<name>.png` isn't needed. Depth is how much of the camera's pan, zoom, shake and sway a layer
follows: 0 never moves, 1 moves exactly with the characters, above 1 moves more (closer to the camera).
The further apart the depths, the stronger the parallax.

- **Size:** 1920 x 720 works well. The extra width is room for the camera to pan sideways without
  showing an edge. All layers of one background should be the same size.
- **Ground line:** the characters' feet are at y = 430 on a 720-tall frame. Put the walkable ground of the
  middleground there.
- **Other files or depths:** list them yourself:
  `"layers": [ { "image": "City_N_New2_Background", "depth": 0.3 }, { "image": "City_N_New2_Middleground", "depth": 0.85 } ]`
- Dialogue uses the same layers (the room's background shows behind NPC conversations) with a gentle drift.
- In exploration, **F6** starts a fight on the spot (debug), handy for checking a new background.

### Examples

`backgrounds.json` has commented examples (remove the `//` to use one):

- **The same image in another area:** add another entry with that area, e.g.
  `{ "image": "City_N_2", "area": "cathedral", "use": ["normal"] }`.
- **One image for every area:** leave `area` out.
- **Only in one trial:** `{ "image": "Academy_N_1", "trial": "city_academy", "use": ["normal"] }`. The area's
  other trials keep using the area's backgrounds, so trials can get their own art one at a time.
- **A full set for an area:** one entry per image, e.g. `Cathedral_N_1` (normal), `Cathedral_E_1` (elite),
  `Cathedral_B_1` (boss), all with `"area": "cathedral"`.
- **Only for one enemy:** add `"enemies": ["hollow_warden"]`.
- **Show one more often:** `"weight": 3` against the default 1.

Dialogue reuses these images too: the room's background shows behind NPC dialogue, and a dialogue node can name
any image here (see `Art/Dialogue/README.md`).

## Which background a fight uses

- A room keeps its background: the first fight in a room picks one and stores it in `Room.StageId`, so the room
  looks the same if you fight there again. To force a background for a room, set `StageId` when the map is
  generated (`Trial/TrialMap.cs`).
- Otherwise, backgrounds set for another area, trial or enemy are skipped, and the best of the rest wins.
  Most important first:
  1. made for one of the enemies in the fight (a specific elite or boss),
  2. made for this kind of fight (an elite fight prefers an `elite` background),
  3. made for this trial,
  4. made for this area (rather than for every area).

  So an elite fight in Fallen Academy uses the city's elite background over an Academy-only normal one, while
  normal Academy fights use the Academy ones. An elite or boss fight with nothing of its kind uses a `normal` one.
  When several tie, one is picked at random (by `weight`).
- Dialogue does the same for the room it happens in, so NPC conversations follow trial backgrounds too.

`dotnet run -- --selftest` checks that every listed image exists and that every `area` and `trial` id is spelled
right, and lists, for every trial, which backgrounds each kind of fight can get.

## Camera (`_camera.json`)

The camera zooms in when a fight opens and pulls back, leans toward the target you're aiming at, pushes in and
pans toward whoever is acting (closer for melee), shakes on hits (more on crits, with a short freeze), and sways
slightly all the time. The characters get the full movement; the background follows `backdropFollow` of it
(0.6 = 60%), which makes it feel further away. The background is always kept covering the whole screen, so even
zooming out or a big shake never shows its edge.

Every number is in `_camera.json` and documented in `Stage/CombatCamera.cs`.
