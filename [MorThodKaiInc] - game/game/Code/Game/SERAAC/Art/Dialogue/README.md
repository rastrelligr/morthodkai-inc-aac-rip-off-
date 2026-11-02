# Dialogue backgrounds

Pictures shown full-screen behind NPC dialogue. Put them here as PNG (1280 x 720 or bigger).
The game also finds combat backgrounds in `Art/Stages`, so those can be reused by name.

## Which picture shows

1. When a conversation starts: the NPC's `Background`, else **the room's own background** (the same one a fight in
   that room would use), so dialogue matches the area without setting anything.
2. A dialogue node with a `Background` switches to that picture (with a crossfade) and it stays for the next lines
   until another node changes it. `"none"` removes the picture: the room shows through, dimmed.

Both are set in `Data/NpcDb.cs`:

```csharp
public static readonly NpcDef Maren = new()
{
    Id = "npc_maren", Name = "Maren",
    Background = "Maren_Camp",                 // whole conversation starts on Art/Dialogue/Maren_Camp.png
    Nodes = new()
    {
        ["offer"] = new() { Text = "Scavenger. Want work?" },              // Maren_Camp
        ["info"] = new()
        {
            Background = "City_B_1",          // switch to a combat background from Art/Stages
            Text = "A Butcher's Hatchet. Took it off something big, out by the tank.",
        },
        ["accepted"] = new() { Text = "Good." },                            // still City_B_1
        ["memory"] = new() { Background = "none", Text = "..." },           // back to the dimmed room
    },
};
```

Leave `Background` out entirely to keep the automatic, area-matching picture.

`dotnet run -- --selftest` fails if a name doesn't match a file in `Art/Dialogue` or `Art/Stages`.
New or changed images need the game restarted.
