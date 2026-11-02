using Microsoft.Xna.Framework;
using SERAAC.Core;
using SERAAC.Data;

namespace ExampleMod
{
    // A code mod: anything JSON can't express (here, an NPC, whose dialogue runs code).
    // Build with `dotnet build` in this folder; the DLL lands in Mods/ExampleMod/ where the game finds it.
    // OnLoad runs after every mod's Data/*.json is applied, before the save is loaded.
    public sealed class Mod : ISeraacMod
    {
        public void OnLoad(ModInfo mod)
        {
            NpcDb.All.Add(new NpcDef
            {
                Id = "npc_ex_archivist", Name = "The Archivist", Color = new Color(170, 150, 110), Weight = 2,
                CanAppear = _ => true,
                Entry = c => c.Run.Flags.Contains("ex_archivist_rest") ? "after" : "hello",
                Nodes = new()
                {
                    ["hello"] = new()
                    {
                        Text = "Shh. I'm cataloguing the infected. You look tired. Sit a moment?",
                        Choices =
                        {
                            new() { Label = "Rest.", Next = "rest" },
                            new() { Label = "No time.", Next = "end" },
                        },
                    },
                    ["rest"] = new()
                    {
                        OnEnter = c =>
                        {
                            c.Run.Flags.Add("ex_archivist_rest");
                            c.Run.HealParty(0.25f, reviveFallen: false);
                            c.Notify("The Archivist's tea: party healed 25%");
                        },
                        Text = "There. Now go on, and try not to end up in my notes.",
                    },
                    ["after"] = new() { Text = "Still alive? Good. Fewer pages for me." },
                },
            });
            mod.Notes.Add("added NPC 'The Archivist'");
        }
    }
}
