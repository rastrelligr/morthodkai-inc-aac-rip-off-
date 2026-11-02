# Audio

Sounds and music are listed by id in `Core/Audio.cs` (`Files` for sounds, `Tracks` for music). Swap a file there to change
what plays. WAV (any bit depth or sample rate), OGG (Vorbis or Opus) and MP3 all work. A missing file is silent.
`dotnet run -- --selftest` checks that every listed file exists and decodes.

## Music

| id | plays | file |
|---|---|---|
| `menu` | title screen | `Music/MainMenu` |
| `dialogue` | Relle's room, the Staging Room scenes | `Music/Unicorn Heads` |
| `hub` | the Sanctum | `Music/Unicorn Heads` |
| `airlift` | the Airlift platform | `Music/Stormy` |
| `explore` | walking through a Trial | `Music/Dungeon_Explore` |
| `combat` | normal fights, one track at random each fight | `Music/Fight_1` to `Fight_4` |
| `boss` | fights with the Wyvern | `Music/WyvernFight` |

Tracks crossfade and are decoded in the background.

## Sounds

Menus use `UI/Fantasy`, `UI/Piano` and `UI/Skyward Hero`; fights use `Combat/`; walking uses `Move/`; the dialogue
typewriter uses `Dialogue/`. A name ending in `*` in `Files` plays a random take of a numbered set (`..._HY_PC-001` to `-006`).
The menu sounds play from one place for every screen (`UI/UiSounds.cs`).
