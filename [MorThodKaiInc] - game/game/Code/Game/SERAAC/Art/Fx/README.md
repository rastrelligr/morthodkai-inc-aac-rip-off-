# Combat particles

`particles.json` holds every combat effect (drawn by MonoGame.Extended's particle engine, see `Fx/Particles.cs`). Save it while the game runs and the next effect uses the new values. In combat, F4 fires every preset at once.
Each entry is a named preset. Every field is optional.

| Field | Meaning | Default |
|---|---|---|
| `count` | particles per burst | 10 |
| `life` | [min, max] seconds | [0.3, 0.6] |
| `speed` | [min, max] pixels per second | [80, 220] |
| `angle` | direction in degrees. 0 = toward the enemy (flips for enemy attackers), 90 = down, -90 = up | 0 |
| `spread` | degrees around `angle` (360 = all directions) | 360 |
| `offset` | [x, y] from the effect point (x flips with direction) | [0, 0] |
| `radius` | spawn anywhere inside this circle | 0 |
| `gravity` | pixels per second², positive = down | 0 |
| `drag` | fraction of speed lost per second | 2 |
| `size` | [start, end] pixels | [6, 1] |
| `stretch` | streaks only: [start, end] length multiplier | [1, 1] |
| `colors` | list of [r, g, b] or [r, g, b, alpha], blended from birth to death | white |
| `shape` | `square`, `diamond`, `circle` (soft dot), `streak` (stretched along its motion), `ring` (use `size` to grow it) | square |
| `texture` | an Art id (`Art/<id>.png`) to use instead of a shape | none |
| `additive` | glowy blending | false |
| `spin` | degrees per second (turns the way the particle moves) | 0 |
| `delay` | seconds before the burst | 0 |
| `also` | other presets fired at the same time, e.g. `["hit_flash"]` | none |

## Where presets are used

| Preset | When |
|---|---|
| a skill's `HitFx` (`Data/CharacterDb.cs`, `Data/EnemyDb.cs`) | each hit of that skill |
| `slash` / `impact` | hits of melee / ranged skills without their own `HitFx` |
| `crit` | on top of the hit, on a critical |
| a skill's `CastFx`, else `cast` / `cast_heal` | when an attack / heal starts (not for melee, which uses `dash`) |
| `dash` | a melee attacker starts its run |
| `step` | someone changes position |
| `heal`, `cleanse` | healing, cleansing |
| `status_guard`, `status_taunt`, `status_blight`, `status_mark` | a status is applied |
| `blight_tick` | Blight damage at the end of a turn |
| `feast` | Relle's Feast crit |
| `death` | a unit falls |
| `victory` | on each surviving party member after a win |

Current skill effects: `shot` (unused for now), `hex` (Mireille, the Hymn Below), `flame` (Black Flame), `bile` (Spitter, the Source).
Add a new preset here and set `HitFx = "your_preset"` on a skill to use it. The self-test fails if a skill names a preset that doesn't exist.
