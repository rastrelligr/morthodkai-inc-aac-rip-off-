using System.Collections.Generic;
using Microsoft.Xna.Framework;
using SERAAC.Combat;
using SERAAC.Data;
using SERAAC.UI;

namespace SERAAC.Story
{
    // Every line of the prologue, as data. Story/StoryFlow.cs decides when each scene plays.
    // VnLine.Say(speaker, text) / Narrate(text) / TitleCard(text); Background names a picture in
    // Art/Dialogue/Background (or a combat background in Art/Stages); "none" removes it.
    public static class StoryScripts
    {
        private static VnLine S(string who, string text) => VnLine.Say(who, text);
        private static VnLine N(string text) => VnLine.Narrate(text);

        public const string DormBg = "RelleDormDialogueBG";
        public const string TrainingBg = "CombatTrainingDialogueBG";

        // ------------------------------------------------------------------ 1. Relle's dorm

        public static List<VnLine> WakeUp() => new()
        {
            VnLine.TitleCard("Aetheris Academy  -  Striker Dormitory"),
            new() { Text = "BANG. BANG. BANG.", Background = DormBg },
            S("Naomi", "Relle. RELLE. Open your eyes before I open this door with my boot."),
            S("Relle", "Mmh... five more minutes..."),
            S("Naomi", "It's past nine. Sparring started at seven."),
            S("Relle", "...Seven?"),
            S("Naomi", "Your partner waited an hour for you, then went to breakfast. Like a sensible person."),
            S("Relle", "The alarm didn't ring, I swear it didn't-"),
            S("Naomi", "It rang. I could hear it from the stairwell. Twice."),
            S("Relle", "..."),
            S("Naomi", "Here's how this goes. You missed your spar, so you spar with me instead."),
            S("Relle", "With you? Naomi, you're a senior Striker, you'll-"),
            S("Naomi", "And you're late. Get dressed, grab your blade, and meet me in the Staging Room."),
            S("Naomi", "Don't make me come back up here."),
            N("Her footsteps fade down the hall."),
            S("Relle", "...She's going to wipe the floor with me."),
            new() { Background = "none", Speaker = "Relle", Text = "Okay. Up. Blade. Go. I can do this." },
        };

        public const string DormHint = "Get ready: take your blade, then leave the room";

        // What Relle says about each thing in her room (object names in Art/Maps/RelleDorm.tmx).
        public static List<VnLine> Inspect(string obj, bool again, bool hasBlade) => obj switch
        {
            "bed" => again
                ? new() { S("Relle", "No. Absolutely not. Naomi would actually kill me.") }
                : new() { S("Relle", "Still warm. It's calling to me..."), S("Relle", "...No. Focus.") },
            "bookshelf" => new()
            {
                N("Field manuals. Combat theory. Ether Mechanics, Volume Two."),
                S("Relle", "And three volumes of a romance series I will deny owning until the day I die."),
            },
            "desk" when !hasBlade => new()
            {
                N("Pulverizer leans against the desk where she dropped it last night, wrapped in its sheath."),
                S("Relle", "Morning, partner. Hungry already? ...Yeah. Me too."),
                N("You take Pulverizer."),
            },
            "desk" => new() { N("Your Striker license and an unread operations briefing."), S("Relle", "I'll read it later. Probably.") },
            "tv" => new()
            {
                N("Last night's news is still paused on the screen."),
                N("\"...the Outskirts remain under quarantine after another wave of infected was sighted near the-\""),
                S("Relle", "I really need to stop falling asleep to this."),
            },
            "plant" => new() { S("Relle", "Still alive. Somehow. Good job, little guy.") },
            "glass_door" => new() { S("Relle", "The shower. ...I don't have time."), S("Relle", "Do I have time? I don't have time.") },
            _ => new() { N("Nothing interesting.") },
        };

        public static List<VnLine> NoBlade() => new()
        {
            S("Relle", "Wait. I can't show up to a spar without my blade."),
            S("Relle", "Pulverizer's by the desk. I think."),
        };

        public static List<VnLine> LeaveRoom(System.Action go) => new()
        {
            new()
            {
                Speaker = "Relle", Text = "Ready to head down to the Staging Room?",
                Choices = new() { new VnChoice("Let's go.", go), new VnChoice("Not yet.", null) },
            },
        };

        // ------------------------------------------------------------------ 2. Sparring with Naomi

        public static List<VnLine> SparIntro() => new()
        {
            VnLine.TitleCard("Staging Room  -  Combat Simulator"),
            new() { Speaker = "Naomi", Text = "Four minutes. Not bad, for someone who was asleep ten minutes ago.", Background = TrainingBg },
            S("Relle", "I ran."),
            S("Naomi", "Good. Get in the sim pod. It's already warmed up."),
            S("Relle", "The simulator? I thought we'd use practice blades."),
            S("Naomi", "Practice blades leave bruises. The sim only bruises your pride."),
            S("Naomi", "It's loading the City Outskirts from last month's survey. The pain feedback is turned down, but you'll still feel it."),
            S("Naomi", "I'll be in there with you, talking you through it. Show me what the Academy taught you."),
            N("The pod hisses shut. The room dissolves into grey streets and broken glass."),
        };

        public static List<VnLine> SparWon() => new()
        {
            N("SIMULATION ENDED. The streets flicker and fold away."),
            new() { Speaker = "Naomi", Text = "...Okay. Enough. That one's yours.", Background = TrainingBg },
            S("Relle", "I- wait, really? I won?"),
            S("Naomi", "You hit hard when you stop thinking about it. Keep that."),
            S("Naomi", "And you braced when I told you to. Most first-years just stand there and take it."),
            S("Relle", "So... does that make up for this morning?"),
            S("Naomi", "No."),
            S("Naomi", "But it's a start. Come up to the Airlift platform with me. There's something you should see."),
        };

        public static List<VnLine> SparLost() => new()
        {
            N("SIMULATION ENDED. The streets flicker and fold away."),
            new() { Speaker = "Naomi", Text = "Breathe. You're fine. It was only the sim.", Background = TrainingBg },
            S("Relle", "...It didn't feel like only the sim."),
            S("Naomi", "That's the point. Everybody eats the floor the first time."),
            S("Naomi", "Come up to the Airlift platform with me. There's something you should see."),
        };

        // Naomi in the simulated sparring match: a light cut, and a stance she winds up into a heavy swing.
        public static readonly ActionDef HeavySwing = new()
        {
            Id = "heavy_swing", Name = "Heavy Swing", Kind = ActionKind.Attack, Power = 2.4f, Melee = true,
            TargetRanks = Ranks.Of("12"), HitFx = "slash",
            Description = "A full-strength swing, wound up the turn before.",
        };

        public static readonly EnemyDef Naomi = new()
        {
            Id = "naomi", Name = "Naomi", Type = EnemyType.Normal, Hp = 34, Damage = 2, Speed = 5,
            Color = new Color(150, 140, 160), Scale = 1f,
            Blurb = "Senior Striker. Relle's sparring partner in the prologue.",
            Skills = new()
            {
                new() { Id = "practice_cut", Name = "Practice Cut", Kind = ActionKind.Attack, Power = 1.0f, Melee = true, TargetRanks = Ranks.Of("12") },
                new() { Id = "ready_stance", Name = "Ready Stance", Kind = ActionKind.WindUp, Side = TargetSide.Self, Power = 0f, WindUp = HeavySwing,
                    Description = "Sets her feet. Next turn: Heavy Swing." },
            },
        };

        public static CombatTutorial SparTutorial()
        {
            var t = new CombatTutorial { Banner = "SIMULATION", Objective = "Spar with Naomi in the simulator", Coach = "Naomi" };
            t.Steps.Add(new()
            {
                Allow = "pierce", EnemyAction = "practice_cut",
                Instruction = "Pick Pierce, then click me.",
                Lines = new()
                {
                    S("Naomi", "Every turn has two halves. First, you choose what to do."),
                    S("Naomi", "Then everyone acts, fastest first. The diamonds at the top are the turn order."),
                    S("Naomi", "Pierce. A straight stab at someone in front of you. Costs nothing."),
                    S("Naomi", "Pick it from your skills at the bottom, then click me. Go on."),
                },
            });
            t.Steps.Add(new()
            {
                Allow = "gnash", EnemyAction = "practice_cut",
                Instruction = "Use Gnash. It costs 1 Ethergy.",
                Lines = new()
                {
                    S("Naomi", "Not bad. Now look at the purple pips under your name. That's your Ethergy."),
                    S("Naomi", "Stronger skills spend it. You get one back every turn."),
                    S("Naomi", "Gnash costs one. It makes me bleed for a couple of turns, Blight, and patches you up while it bites."),
                    S("Relle", "Pulverizer likes this one."),
                    S("Naomi", "...I noticed. Use it."),
                },
            });
            t.Steps.Add(new()
            {
                Allow = "charge", EnemyAction = "ready_stance",
                Instruction = "Use Focus (Special Abilities).",
                Lines = new()
                {
                    S("Naomi", "You're running low on Ethergy. On the right are your Special Abilities. Everyone has them."),
                    S("Naomi", "Focus: you stand still and gather yourself. Two extra Ethergy."),
                    S("Naomi", "Use it now. And watch what I'm doing while you do."),
                },
            });
            t.Steps.Add(new()
            {
                Allow = "defend",
                Instruction = "Use Guard. It acts before anything else.",
                Lines = new()
                {
                    S("Naomi", "See that warning at the top? I've set my stance. Next turn I swing with everything."),
                    S("Naomi", "When something winds up, you get a turn to answer it."),
                    S("Naomi", "Guard acts before anyone else and halves the hit. Brace."),
                },
            });
            t.Steps.Add(new()
            {
                Allow = "cleaver", EnemyAction = "practice_cut",
                Instruction = "Use Cleaver on me. Three Ethergy.",
                Lines = new()
                {
                    S("Naomi", "Good. That would have floored you."),
                    S("Naomi", "Now you've got the Ethergy for Cleaver. Your heaviest cut, but only from up front, at whoever's right in front of you."),
                    S("Naomi", "Where you stand matters. Every skill shows the positions it works from and the ones it can reach."),
                    S("Naomi", "Show me."),
                },
            });
            t.Steps.Add(new()
            {
                EnemyAction = "practice_cut",
                Instruction = "Finish it, any way you like.",
                Lines = new()
                {
                    S("Relle", "Pulverizer's... humming."),
                    S("Naomi", "That's your Feast. Every fourth skill you use, that blade bites for real: a guaranteed critical."),
                    S("Naomi", "It's ready now. Finish this. Any way you like."),
                },
            });
            return t;
        }

        // ------------------------------------------------------------------ 3. The Airlift platform

        public static List<VnLine> AirliftArrive() => new()
        {
            S("Naomi", "Welcome to the Airlift. Every operation starts on this pad."),
            S("Relle", "It's freezing up here... and the clouds are under us."),
            S("Naomi", "Your squad's already here. Mara, by the railing. Elian, the one with the notebook."),
            S("Naomi", "Go introduce yourself. You'll be trusting them with your life. Come talk to me when you're done."),
        };

        public const string AirliftHint = "Meet your squad (Mara, Elian), then talk to Naomi";

        public static List<VnLine> AirliftInspect(string obj) => obj switch
        {
            "osprey" => new()
            {
                N("A tilt-rotor transport sits on the pad, engines cold, its hull scuffed from a hundred landings."),
                S("Relle", "That's what takes squads out to the Trials. I've only ever seen it from the courtyard."),
            },
            "edge" => new()
            {
                N("Past the railing there is nothing but cloud, rolling slowly like a grey sea."),
                S("Relle", "Somewhere under all that is the old city."),
                S("Relle", "...Don't look down. Don't look down."),
            },
            "stairs" => new() { S("Relle", "The stairs back down. Naomi's waiting, though.") },
            _ => new() { N("Wind howls across the platform.") },
        };

        // ------------------------------------------------------------------ 4. The Sanctum (hub tutorial)

        public static List<VnLine> HubWelcome() => new()
        {
            S("Naomi", "This is the Sanctum. Before every operation, you come here first."),
            S("Naomi", "The tabs at the top are everything you can do: pick your squad, spend funds on upgrades, change gear, and choose where to deploy."),
            S("Naomi", "Start with your squad. Open the PARTY tab."),
        };

        public static List<VnLine> HubParty() => new()
        {
            S("Naomi", "Four of you go out at a time, two in front and two behind."),
            S("Naomi", "Front-liners hit hardest and get hit hardest. The back row heals, shoots and supports."),
            S("Naomi", "Every skill shows where it can be used from. Put people where their skills work."),
            S("Naomi", "Click someone to add or remove them; the arrows move them through the formation."),
            S("Naomi", "When you're happy with it, open UPGRADES."),
        };

        public static List<VnLine> HubUpgrades() => new()
        {
            S("Naomi", "Upgrades make someone permanently stronger: more Health, more Damage, more Speed."),
            S("Naomi", "They cost shards. You bring shards back from operations, but only if you make it out."),
            S("Naomi", "Here. The Academy is fronting you some funds for your first operation."),
            N("Received 200 shards."),
            S("Naomi", "Spend some. Buy at least one upgrade."),
        };

        public static List<VnLine> HubEquipmentGo() => new()
        {
            S("Naomi", "Good. Stronger already. Now open EQUIPMENT."),
        };

        public static List<VnLine> HubEquipment() => new()
        {
            S("Naomi", "Everyone has a weapon slot and a charm slot. Gear you find on operations ends up here."),
            S("Naomi", "Unlike shards, gear is never lost, even if the operation goes badly."),
            S("Naomi", "Click a character, then a slot to change it. Then open TRIALS."),
        };

        public static List<VnLine> HubTrials() => new()
        {
            S("Naomi", "Last one. TRIALS is where you deploy."),
            S("Naomi", "Out there you explore room by room. Infected roam the halls; if they touch you, you fight."),
            S("Naomi", "Shards you pick up aren't yours until you reach an Extraction Point and get out. Fall, and you lose them."),
            S("Naomi", "Today it's the Fallen Academy. Find the Wyvern. End it."),
            S("Relle", "...Right. No pressure."),
            S("Naomi", "Select the Fallen Academy and Embark when you're ready."),
        };

        public static List<VnLine> HubWrongTrial() => new()
        {
            S("Naomi", "Not today. The Academy, Relle."),
        };

        // ------------------------------------------------------------------ 5. The end of the playtest

        public static List<VnLine> WyvernDown() => new()
        {
            N("The Wyvern crashes into the rubble of the old lecture hall, and does not rise again."),
            S("Relle", "...It's down. It's actually down."),
            N("Pulverizer hums in her hand, warm and satisfied."),
            S("Naomi", "Don't celebrate yet. Nothing counts until we're out."),
            S("Naomi", "Find an Extraction Point. Then we go home."),
        };
    }
}
