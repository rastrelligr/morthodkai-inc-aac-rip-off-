using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using SERAAC.Meta;
using SERAAC.Trial;

namespace SERAAC.Data
{
    // What dialogue scripts can see and change.
    public sealed class DialogueContext
    {
        public Profile Profile;
        public TrialRun Run;
        public Action<string> Notify;   // shows a toast in the exploration screen
    }

    public sealed class DialogueChoice
    {
        public string Label;
        public string Next;                          // node id; "end" closes the dialogue
        public Func<DialogueContext, bool> If;       // hide the choice when false
    }

    public sealed class DialogueNode
    {
        public string Text;
        public Func<DialogueContext, string> TextFn; // for text with live numbers (quest progress)
        public Action<DialogueContext> OnEnter;      // side effects: start quest, give item, unlock...
        public List<DialogueChoice> Choices = new(); // empty = a single "Continue" that closes

        public string TextFor(DialogueContext ctx) => TextFn?.Invoke(ctx) ?? Text;
    }

    public sealed class NpcDef
    {
        public string Id;
        public string Name;
        public string SpriteId;          // sprite sheet to draw; null = placeholder figure
        public string Pose;
        public Color Color;
        public int Weight = 1;           // chance to be picked when several NPCs can appear
        public Func<DialogueContext, bool> CanAppear;
        public Func<DialogueContext, string> Entry; // first node, based on current state
        public Dictionary<string, DialogueNode> Nodes;
    }

    // NPCs met in Trials (Special Room "NPC"). Dialogue is data: nodes + choices + effects.
    public static class NpcDb
    {
        private static DialogueChoice Go(string label, string next, Func<DialogueContext, bool> cond = null) =>
            new() { Label = label, Next = next, If = cond };

        // The goddess who sent Relle into the Trials (World Overview). Offers one blessing per trial,
        // and a little foreshadowing about where Relle's power really comes from.
        private static bool Unblessed(DialogueContext c) => !c.Run.Flags.Contains("angel_blessed");

        public static readonly NpcDef Angel = new()
        {
            Id = "npc_angel", Name = "The Angel", Color = new Color(235, 225, 190), Weight = 2,
            CanAppear = _ => true,
            Entry = _ => "hello",
            Nodes = new()
            {
                ["hello"] = new()
                {
                    Text = "Relle. My Vessel. You carry the light into the dark places, as I asked. Do you need my blessing?",
                    Choices = { Go("Bless us.", "bless", Unblessed), Go("What is the Black Goat?", "goat"), Go("Why me?", "why"), Go("Not now.", "end") },
                },
                ["bless"] = new()
                {
                    OnEnter = c =>
                    {
                        c.Run.Flags.Add("angel_blessed");
                        c.Run.HealParty(0.3f, reviveFallen: false);
                        c.Notify("The Angel's blessing: party healed 30%");
                    },
                    Text = "Rise, then. Walk on.",
                },
                ["goat"] = new()
                {
                    Text = "A fallen thing. It broke the world when it fell. The infected are its echo. Do not listen if it speaks to you.",
                    Choices = { Go("It hasn't spoken to me.", "goat2"), Go("Something else.", "hello") },
                },
                ["goat2"] = new()
                {
                    Text = "...Good. Your power is mine, Relle. Remember that. It is MINE.",
                    Choices = { Go("Bless us.", "bless", Unblessed), Go("Not now.", "end") },
                },
                ["why"] = new()
                {
                    Text = "You prayed for something to change. Something answered. I only came afterward... to guide you.",
                    Choices = { Go("Something else.", "hello"), Go("Not now.", "end") },
                },
            },
        };

        public static readonly NpcDef Maren = new()
        {
            Id = "npc_maren", Name = "Maren", Color = new Color(150, 120, 80), Weight = 2,
            CanAppear = c => !Quests.IsClaimed(c.Profile, QuestDb.MarenHunt),
            Entry = c => !Quests.IsActive(c.Profile, QuestDb.MarenHunt) ? "offer"
                       : Quests.IsDone(c.Profile, QuestDb.MarenHunt) ? "reward" : "progress",
            Nodes = new()
            {
                ["offer"] = new()
                {
                    Text = "Scavenger. Don't look at me like that, girl with the hungry sword, you're doing the same thing. Want work? Put down eight infected and come find me again.",
                    Choices = { Go("Deal.", "accepted"), Go("What's in it for me?", "info"), Go("Not interested.", "end") },
                },
                ["info"] = new()
                {
                    Text = "A Butcher's Hatchet. Heavy, mean, and yours if you do the job.",
                    Choices = { Go("Deal.", "accepted"), Go("Not interested.", "end") },
                },
                ["accepted"] = new()
                {
                    OnEnter = c => { Quests.Start(c.Profile, QuestDb.MarenHunt); c.Notify("Quest started: " + QuestDb.MarenHunt.Name); },
                    Text = "Good. Eight. I'll know if you're lying, the infected gossip.",
                },
                ["progress"] = new()
                {
                    TextFn = c => $"{Quests.Progress(c.Profile, QuestDb.MarenHunt)} of {QuestDb.MarenHunt.Target} down. Keep swinging.",
                },
                ["reward"] = new()
                {
                    OnEnter = c =>
                    {
                        Quests.Claim(c.Profile, QuestDb.MarenHunt);
                        c.Run.AddFoundEquipment("butcher_hatchet");
                        c.Notify("Received: Butcher's Hatchet (unsecured until you extract)");
                    },
                    Text = "Eight! Fine, the Hatchet's yours. It counts as loot until you're out of here, so don't die holding it.",
                },
            },
        };

        public static readonly NpcDef TopSalaTap = new()
        {
            Id = "npc_topsala_tap",
            Name = "Topsala Tap",
            Color = new Color(150, 120, 80),
            Weight = 2,
            CanAppear = c => !Quests.IsClaimed(c.Profile, QuestDb.MathCG),
            Entry = c => !Quests.IsActive(c.Profile, QuestDb.MathCG) ? "offer"
                       : Quests.IsDone(c.Profile, QuestDb.MathCG) ? "reward" : "progress",
            Nodes = new()
            {
                ["offer"] = new()
                {
                    Text = "Free us..... Free us all. Kill the MathCG.",
                    Choices = { Go("I'll do it.", "accepted"), Go("What is the MathCG?", "info"), Go("I can't help you.", "end") },
                },
                ["info"] = new()
                {
                    Text = "The Plague, The Blight, Humanity's Doom. It will kill us all",
                    Choices = { Go("Then this must be done.", "accepted"), Go("I can't help you defeat it.", "end") },
                },
                ["accepted"] = new()
                {
                    OnEnter = c => { Quests.Start(c.Profile, QuestDb.MathCG); c.Notify("Quest started: " + QuestDb.MathCG.Name); },
                    Text = "Thanks you........",
                },
                ["progress"] = new()
                {
                    TextFn = c => $"{Quests.Progress(c.Profile, QuestDb.MathCG)} of {QuestDb.MathCG.Target} down. Keep swinging.",
                },
                ["reward"] = new()
                {
                    OnEnter = c =>
                    {
                        Quests.Claim(c.Profile, QuestDb.MathCG);
                        c.Run.AddFoundEquipment("the_cube");
                        c.Notify("Received: Default Cube..... how is this even a thing? (unsecured until you extract)");
                    },
                    Text = "Thanks for freeing us, this might help you in the future and reminding you of what you've lost.",
                },
            },
        };

        public static readonly NpcDef Reflection = new()
        {
            Id = "npc_reflection", Name = "The Reflection", SpriteId = "char_relle_inv", Pose = "portrait", Color = new Color(70, 190, 210), Weight = 3,
            CanAppear = c => !c.Profile.IsUnlocked("relle_inv"),
            Entry = c => !Quests.IsActive(c.Profile, QuestDb.Reflection) ? "first"
                       : Quests.IsDone(c.Profile, QuestDb.Reflection) ? "merge" : "waiting",
            Nodes = new()
            {
                ["first"] = new()
                {
                    Text = "Hello, me. Don't make that face, Relle. You started feeding, and something had to be left behind in the dark. That was me.",
                    Choices = { Go("What are you?", "who"), Go("What do you want?", "want") },
                },
                ["who"] = new()
                {
                    Text = "Everything you aren't. Cold where you're hungry. Patient where you're loud. Pulverizer remembers me, even if you don't.",
                    Choices = { Go("What do you want?", "want") },
                },
                ["want"] = new()
                {
                    Text = "Show me you're worth following. Beat something strong, an Elite, and do it yourself. Then come find me.",
                    Choices = { Go("Fine.", "accepted"), Go("No.", "end") },
                },
                ["accepted"] = new()
                {
                    OnEnter = c => { Quests.Start(c.Profile, QuestDb.Reflection); c.Notify("Quest started: " + QuestDb.Reflection.Name); },
                    Text = "I'll be watching from the other side of the glass.",
                },
                ["waiting"] = new() { Text = "Not yet. An Elite. With your own blade. I'm watching." },
                ["merge"] = new()
                {
                    OnEnter = c =>
                    {
                        Quests.Claim(c.Profile, QuestDb.Reflection);
                        c.Profile.Unlock("relle_inv");
                        c.Notify("Elle (Inverted Relle) joined the Sanctum!");
                    },
                    Text = "...Fine. Two of us, then. Call me Elle, so nobody gets confused. Mostly you.",
                },
            },
        };

        public static readonly List<NpcDef> All = new() { Angel, Maren, Reflection };

        // Picks who (if anyone) is waiting in this trial's NPC room.
        public static NpcDef PickFor(DialogueContext ctx, Random rng)
        {
            var eligible = All.Where(n => n.CanAppear(ctx)).ToList();
            if (eligible.Count == 0) return null;
            int roll = rng.Next(eligible.Sum(n => n.Weight));
            foreach (var n in eligible)
            {
                roll -= n.Weight;
                if (roll < 0) return n;
            }
            return eligible[^1];
        }
    }
}
