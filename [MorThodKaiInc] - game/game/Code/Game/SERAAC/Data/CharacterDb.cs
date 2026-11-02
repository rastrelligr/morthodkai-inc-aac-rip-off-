using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace SERAAC.Data
{
    public enum PassiveKind
    {
        Feast,      // every skill: +1 Feast. At 3, the next skill crits and consumes the stacks. Attacks heal a little.
        Frontline,  // x1.25 damage in Front 1 / Front 2
        Backline,   // x1.25 damage and healing in Back 1 / Back 2
        GuardAfterSkill, // Guarded after using a skill
        Tough,      // takes 20% less damage
        Devotion,   // heals 30% more
        Deadeye,    // +20% crit chance
        Exploit,    // x1.3 damage to Blighted, Marked or Slowed targets
        Aegis,      // Mara: skills with GainAegis build Guard; at 2, the next attack on an adjacent ally is redirected to her
        Bulwark,    // Mara: takes 20% less damage in Front 1 / Front 2
        Sympathetic, // Elian: skills on an ally under 50% HP work 50% better
        Support,    // Elian: healing x1.2 in Back 1 / Back 2
        Weave,      // Elian: at max Thread, the next single-target heal / attack is Woven (hits every valid target)
        Riposte,    // Pillow: Guarded after a skill; while Guarded, strikes back at single-target attackers
    }

    public sealed class PassiveDef
    {
        public PassiveKind Kind;
        public string Name;
        public string Description;
    }

    public sealed class CharacterDef
    {
        public string Id;
        public string Name;
        public string Role;
        public string Blurb;
        public int BaseHp, BaseDamage, BaseSpeed;
        public int MaxSp;               // 0 = this character doesn't use SP
        public Color Color;             // placeholder body colour until art lands
        public List<ActionDef> Skills;  // character-specific; common actions are appended at runtime
        public List<PassiveDef> Passives = new();
        public bool StartsUnlocked = true;
        public string UnlockHint;       // shown in the Hub while locked

        // Face for small portraits (combat panel, cut-ins, barks, the trial sidebar, the hub): a crop of this art
        // (usually the dialogue portrait) around FaceCenter (fractions of the picture), FaceHeight of its height tall.
        // Null = the sheet's "portrait" pose as before.
        public string FaceArt;
        public Vector2 FaceCenter = new(0.5f, 0.25f);
        public float FaceHeight = 0.4f;

        public bool Has(PassiveKind k) => Passives.Any(p => p.Kind == k);

        public string ArtId => "char_" + Id;
        public string PortraitId => "portrait_" + Id;

        public IEnumerable<ActionDef> AllActions =>
            Skills.Concat(CommonActions.PartySpecials);
    }

    // The playable roster, from "New Playable Character Design" (Relle, Mara, Elian) plus the others rebalanced
    // to the same scale. Spec damage numbers = Damage stat x Power, before position / crit / target modifiers.
    public static class CharacterDb
    {
        public const int PartySize = 4;

        // Shared passives (Relle's Frontline is reused by other front-liners).
        private static readonly PassiveDef Frontline = new() { Kind = PassiveKind.Frontline, Name = "Frontline", Description = "Deals x1.25 damage in Front 1 or Front 2." };
        private static readonly PassiveDef Backline = new() { Kind = PassiveKind.Backline, Name = "Backline", Description = "Deals x1.25 damage and healing in Back 1 or Back 2." };

        private static readonly PassiveDef FeastPassive = new()
        {
            Kind = PassiveKind.Feast, Name = "Flesh-Eating Blade",
            Description = "Each skill grants 1 Feast. At 3 Feast the next skill always crits and consumes all Feast. Every hit restores a little HP.",
        };

        // Striker. Spec (Damage 5): Pierce 5, Blood Feast 2 x2, Cleaver 7.
        public static readonly CharacterDef Relle = new()
        {
            Id = "relle", Name = "Relle", Role = "Striker",
            FaceArt = "Dialogue/Portrait/Portrait_Relle", FaceCenter = new Vector2(0.57f, 0.22f), FaceHeight = 0.4f,
            Blurb = "The protagonist. She prayed for something to change, and something answered. Fights up close with Pulverizer, a flesh-eating bonded blade: it feeds, she heals.",
            BaseHp = 28, BaseDamage = 5, BaseSpeed = 6, MaxSp = 5, Color = new Color(200, 70, 60),
            Passives = new()
            {
                FeastPassive,
                Frontline,
            },
            Skills = new()
            {
                new() { Id = "pierce", Name = "Pierce", Kind = ActionKind.Attack, Power = 1.0f, SpCost = 0,
                    TargetRanks = Ranks.Of("12"), Anim = "stab1", Melee = true,
                    Description = "Stab into the gut of an enemy at the front." },
                // Added to the design document's kit: a cheap Feast builder, giving her 4 skills like Mara and Elian.
                new() { Id = "gnash", Name = "Gnash", Kind = ActionKind.Attack, Power = 0.7f, SpCost = 1, Lifesteal = 0.3f,
                    TargetRanks = Ranks.Of("12"), Applies = StatusKind.Blight, StatusTurns = 2, StatusPower = 2, Anim = "stab2", Melee = true, HitFx = "bile",
                    Description = "Pulverizer bites deep into an enemy at the front: it bleeds (Blight 2 dmg, 2 turns) and Relle heals 30% more of the damage." },
                new() { Id = "blood_feast", Name = "Blood Feast", Kind = ActionKind.Attack, Power = 0.4f, Hits = 2, SpCost = 2, HpCost = 3,
                    AreaEffect = true, TargetRanks = Ranks.All, ShuffleTargets = true, Anim = "swing1,swing2", Melee = true, HitFx = "slash",
                    Description = "Pay 3 HP: dash through every enemy twice, slashing from opposite directions. Their positions are scrambled." },
                new() { Id = "cleaver", Name = "Cleaver", Kind = ActionKind.Attack, Power = 1.4f, SpCost = 3,
                    UsableFrom = Ranks.Of("12"), TargetRanks = Ranks.Of("1"), Anim = "slam",
                    Description = "A powerful slash at the enemy directly in front (Front 1 only)." },
            },
        };

        // Warden: Mara replaces Bram (keeps the "warden" id so saves carry over). Spec (Damage 4): Shield Bash 4.
        // Additions to the design document: Iron Rebuke (cash Guard in for damage) and a counter-attack after intercepting, so Guard
        // stacks give her a choice and a payoff like Relle's Feast.
        // Art: draws with Bram's sheet (char_warden) until Mara's sheet arrives.
        public static readonly CharacterDef Warden = new()
        {
            Id = "warden", Name = "Mara", Role = "Warden",
            Blurb = "A defensive frontliner. Her infected, the Ironbound Aegis, turns into shield and armour to take the blows meant for her allies, and hits back.",
            BaseHp = 44, BaseDamage = 4, BaseSpeed = 3, MaxSp = 5, Color = new Color(80, 110, 150),
            Passives = new()
            {
                new() { Kind = PassiveKind.Aegis, Name = "Ironbound Aegis",
                    Description = "Shield Bash, Guard and Aegis Wall each give 1 Guard. At 2 Guard, the next attack on an ally next to her is redirected to her (she takes 50%) and uses up all Guard. Whenever she intercepts an attack, she strikes back. Her shields act before anyone else." },
                new() { Kind = PassiveKind.Bulwark, Name = "Bulwark", Description = "Takes 20% less damage in Front 1 or Front 2." },
            },
            Skills = new()
            {
                new() { Id = "shield_bash", Name = "Shield Bash", Kind = ActionKind.Attack, Power = 1.0f, KnockBack = 1, GainAegis = 1, Interrupts = true,
                    UsableFrom = Ranks.Of("12"), TargetRanks = Ranks.Of("12"), Melee = true,
                    Description = "Bash an enemy at the front with the Aegis and push it 1 position back. Interrupts a wind-up. +1 Guard." },
                new() { Id = "iron_rebuke", Name = "Iron Rebuke", Kind = ActionKind.Attack, Power = 0.6f, PowerPerAegis = 1.0f, SpCost = 1, Interrupts = true,
                    UsableFrom = Ranks.Of("12"), TargetRanks = Ranks.Of("12"), Melee = true, HitFx = "impact",
                    Description = "Spend all Guard on one crushing blow at the front: +100% power for every Guard spent. Interrupts a wind-up." },
                new() { Id = "guard_ally", Name = "Guard", Kind = ActionKind.Protect, Side = TargetSide.Ally, SpCost = 2, GainAegis = 1, Priority = true,
                    Description = "Acts first. Shield one ally: attacks aimed at them hit Mara instead (and she strikes back), until she acts again. +1 Guard." },
                new() { Id = "aegis_wall", Name = "Aegis Wall", Kind = ActionKind.Wall, Side = TargetSide.Self, SpCost = 3, GainAegis = 1, Priority = true,
                    Description = "Acts first. Plant the Aegis: Mara and the allies next to her take 40% less damage until she acts again. She can't move meanwhile. +1 Guard." },
            },
        };

        // Adept: Elian replaces Sister Noa (keeps the "priest" id so saves carry over). Spec (Damage 3): Bind 2.
        // Additions to the design document: Thread pays off (Threadweaver: a Woven skill reaches everyone) and Snare Line pulls enemies
        // into the reach of front-only strikers (Relle's Pierce / Cleaver, Pillow's Rend).
        // Art: draws with Noa's sheet (char_priest) until Elian's sheet arrives.
        public static readonly CharacterDef Priest = new()
        {
            Id = "priest", Name = "Elian", Role = "Adept",
            Blurb = "A support who mends wounds and moves allies, and enemies, with Threadweaver, an infected made of thread.",
            BaseHp = 22, BaseDamage = 3, BaseSpeed = 5, MaxSp = 5, Color = new Color(200, 190, 150),
            Passives = new()
            {
                new() { Kind = PassiveKind.Sympathetic, Name = "Sympathetic Thread", Description = "Healing on an ally under 50% HP is 50% stronger." },
                new() { Kind = PassiveKind.Support, Name = "Backline", Description = "Healing is 20% stronger in Back 1 or Back 2." },
                new() { Kind = PassiveKind.Weave, Name = "Threadweaver",
                    Description = "Each skill gives 1 Thread. At 3 Thread, his next Mend, Bind or Snare Line is Woven: it reaches every ally / every enemy, and uses up all Thread." },
            },
            Skills = new()
            {
                new() { Id = "mend", Name = "Mend", Kind = ActionKind.Heal, Side = TargetSide.Ally, Power = 2.0f, GainThread = 1,
                    Description = "Stitch one ally's wounds closed. Woven: heals every ally for 60%. +1 Thread." },
                new() { Id = "snare_line", Name = "Snare Line", Kind = ActionKind.Attack, Power = 0.34f, SpCost = 1, PullForward = 2, GainThread = 1, Priority = true,
                    TargetRanks = Ranks.All, HitFx = "thread",
                    Description = "Acts first. Hook any enemy and drag it 2 positions forward, into your strikers' reach. Woven: drags every enemy. +1 Thread." },
                new() { Id = "bind", Name = "Bind", Kind = ActionKind.Attack, Power = 0.67f, SpCost = 2, GainThread = 1, HitFx = "thread",
                    TargetRanks = Ranks.All, Applies = StatusKind.Slow, StatusTurns = 2, StatusPower = 3,
                    Description = "Web threads around any enemy and pull: a little damage and -3 Speed for 2 turns. Woven: binds every enemy. +1 Thread." },
                new() { Id = "reweave", Name = "Reweave", Kind = ActionKind.Reweave, Side = TargetSide.Ally, SpCost = 2, GainThread = 1, Priority = true,
                    Description = "Acts first. Pull an ally 1 position toward Elian (out of harm's way). If someone stands there, they swap. +1 Thread." },
            },
        };

        // ---- Strikers (only 3 classes for now: Striker, Warden, Adept). Rebalanced to the new enemy scale.

        // Pillow, redesigned: a counter-puncher. Skills leave him Guarded, and while Guarded he strikes back.
        public static readonly CharacterDef Knight = new()
        {
            Id = "knight", Name = "Pillow", Role = "Striker",
            Blurb = "A knight with a bonded blade who looks half asleep, and swings back the moment you touch him. (Placeholder bio.)",
            BaseHp = 34, BaseDamage = 6, BaseSpeed = 5, MaxSp = 5, Color = new Color(170, 60, 70),
            Passives = new()
            {
                Frontline,
                new() { Kind = PassiveKind.Riposte, Name = "Sleepwalker's Guard",
                    Description = "After using a skill Pillow is Guarded until he acts again (takes 50% less). While Guarded he strikes back at anyone who attacks him alone." },
            },
            Skills = new()
            {
                new() { Id = "rend", Name = "Rend", Kind = ActionKind.Attack, Power = 1.0f,
                    UsableFrom = Ranks.Of("12"), TargetRanks = Ranks.Of("12"),
                    Description = "A heavy slash at the enemy front. Leaves him Guarded." },
                new() { Id = "black_flame", Name = "Black Flame", Kind = ActionKind.Attack, Power = 0.7f, SpCost = 2,
                    UsableFrom = Ranks.Of("123"), TargetRanks = Ranks.All,
                    Applies = StatusKind.Blight, StatusTurns = 3, StatusPower = 2, HitFx = "flame",
                    Description = "Dark fire from the bonded blade. Burns any enemy and Blights it (2 dmg, 3 turns)." },
                new() { Id = "moonfall", Name = "Moonfall", Kind = ActionKind.Attack, Power = 0.9f, SpCost = 3, AreaEffect = true,
                    UsableFrom = Ranks.Of("12"), TargetRanks = Ranks.Of("12"), Applies = StatusKind.Mark, StatusTurns = 2,
                    Description = "A sweeping cut through the whole enemy front row that Marks them (+35% damage taken, 2 turns)." },
            },
        };

        // Naomi: a swift Striker who hits lighter than the others and controls the fight instead. She slows the
        // front, shoves it back out of reach, cuts a wind-up short before anyone else moves, and Marks the whole
        // line for the heavy hitters. Read the Opening pays her back on anything she has already crippled.
        public static readonly CharacterDef Naomi = new()
        {
            Id = "naomi", Name = "Naomi", Role = "Striker",
            FaceArt = "Dialogue/Portrait/Portrait_Naomi", FaceCenter = new Vector2(0.49f, 0.27f), FaceHeight = 0.38f,
            Blurb = "Senior Striker and Relle's reluctant mentor. Fast, precise and patient: she takes enemies apart before she takes them down.",
            BaseHp = 26, BaseDamage = 4, BaseSpeed = 8, MaxSp = 5, Color = new Color(150, 140, 160),
            Passives = new()
            {
                new() { Kind = PassiveKind.Exploit, Name = "Read the Opening", Description = "Deals x1.3 damage to Slowed, Marked or Blighted enemies." },
                Frontline,
            },
            Skills = new()
            {
                new() { Id = "flicker_cut", Name = "Flicker Cut", Kind = ActionKind.Attack, Power = 1.0f, SpCost = 0, Melee = true,
                    UsableFrom = Ranks.Of("12"), TargetRanks = Ranks.Of("12"), Applies = StatusKind.Slow, StatusTurns = 2, StatusPower = 2, HitFx = "slash",
                    Description = "A quick cut at an enemy in front. Slows it (-2 Speed, 2 turns)." },
                new() { Id = "sweeping_kick", Name = "Sweeping Kick", Kind = ActionKind.Attack, Power = 0.6f, SpCost = 1, AreaEffect = true, KnockBack = 1, Melee = true,
                    UsableFrom = Ranks.Of("12"), TargetRanks = Ranks.Of("12"), HitFx = "impact",
                    Description = "Kick through the enemy front row and push each of them 1 position back." },
                new() { Id = "pommel_strike", Name = "Pommel Strike", Kind = ActionKind.Attack, Power = 0.9f, SpCost = 2, Priority = true, Interrupts = true, Melee = true,
                    UsableFrom = Ranks.Of("12"), TargetRanks = Ranks.Of("12"), Applies = StatusKind.Slow, StatusTurns = 2, StatusPower = 3, HitFx = "impact",
                    Description = "Acts first. Strike an enemy in front with the hilt: interrupts a wind-up and Slows it (-3 Speed, 2 turns)." },
                new() { Id = "thousand_cuts", Name = "Thousand Cuts", Kind = ActionKind.Attack, Power = 0.55f, SpCost = 3, AreaEffect = true,
                    UsableFrom = Ranks.Of("12"), TargetRanks = Ranks.All, Applies = StatusKind.Mark, StatusTurns = 2, HitFx = "slash",
                    Description = "Dart through every enemy, leaving each one Marked (+35% damage taken, 2 turns)." },
            },
        };

        // Winter: now a back-row Striker (same kit).
        public static readonly CharacterDef Hunter = new()
        {
            Id = "hunter", Name = "Winter", Role = "Striker",
            Blurb = "Sniper. The rifle is a bonded infected that hums when prey is near. (Placeholder bio.)",
            BaseHp = 22, BaseDamage = 5, BaseSpeed = 7, MaxSp = 5, Color = new Color(110, 140, 80),
            Passives = new()
            {
                new() { Kind = PassiveKind.Deadeye, Name = "Deadeye", Description = "+20% critical hit chance." },
                Backline,
            },
            Skills = new()
            {
                new() { Id = "snipe", Name = "Snipe", Kind = ActionKind.Attack, Power = 1.3f, HitFx = "shot",
                    UsableFrom = Ranks.Of("34"), TargetRanks = Ranks.Of("234"),
                    Description = "Shoot an enemy behind their front line." },
                new() { Id = "mark", Name = "Mark Prey", Kind = ActionKind.Attack, Power = 0.5f, HitFx = "shot",
                    UsableFrom = Ranks.Of("234"), TargetRanks = Ranks.All,
                    Applies = StatusKind.Mark, StatusTurns = 2,
                    Description = "Graze and Mark any enemy: it takes +35% damage for 2 turns." },
                new() { Id = "point_blank", Name = "Point Blank", Kind = ActionKind.Attack, Power = 1.6f,
                    UsableFrom = Ranks.Of("1"), TargetRanks = Ranks.Of("1"), SelfMoveAfter = 1, Melee = false, HitFx = "shot",
                    Description = "Huge hit on Front 1, then the recoil knocks Winter back 1 position." },
                new() { Id = "piercing_round", Name = "Piercing Round", Kind = ActionKind.Attack, Power = 0.9f, SpCost = 3, AreaEffect = true, HitFx = "shot",
                    UsableFrom = Ranks.Of("34"), TargetRanks = Ranks.All,
                    Description = "One round through the whole enemy line." },
            },
        };

        // Relle's inverted self. Placeholder kit (design pending): a back-row Adept mirror of Relle.
        public static readonly CharacterDef RelleInverted = new()
        {
            Id = "relle_inv", Name = "Elle", Role = "Adept",
            Blurb = "Relle's reflection, left behind in the dark. Cold where Relle is hungry. (Placeholder kit.)",
            BaseHp = 22, BaseDamage = 4, BaseSpeed = 6, MaxSp = 5, Color = new Color(70, 190, 210),
            StartsUnlocked = false,
            UnlockHint = "Something that looks like Relle has been seen in the Trials. Find it and hear what it wants.",
            Passives = new()
            {
                FeastPassive,
                Backline,
            },
            Skills = new()
            {
                new() { Id = "needle", Name = "Needle", Kind = ActionKind.Attack, Power = 0.8f, SpCost = 0,
                    TargetRanks = Ranks.All, Applies = StatusKind.Mark, StatusTurns = 2, Anim = "stab2", Melee = true,
                    Description = "A cold thrust at any enemy. Marks it (+35% damage taken, 2 turns)." },
                new() { Id = "tidewash", Name = "Tidewash", Kind = ActionKind.Heal, Side = TargetSide.Ally, Power = 0.5f, SpCost = 2,
                    AreaEffect = true, Cleanse = true, UsableFrom = Ranks.Of("234"), Anim = "swing2",
                    Description = "Heal and cleanse every ally." },
                new() { Id = "undercut", Name = "Undercut", Kind = ActionKind.Attack, Power = 1.3f, SpCost = 3, KnockBack = 2,
                    UsableFrom = Ranks.Of("123"), TargetRanks = Ranks.Of("12"), Anim = "slam", Melee = true,
                    Description = "Slam a front-row enemy and knock it 2 positions back." },
            },
        };

        // Removed from the game (no sprite). To bring her back, uncomment and add her to All.
        /*
        public static readonly CharacterDef Occultist = new()
        {
            Id = "occultist", Name = "Mireille", Role = "Occultist",
            Blurb = "Studies the infection from the inside. Hears the Goat more clearly than she admits.",
            BaseHp = 22, BaseDamage = 5, BaseSpeed = 4, MaxSp = 5, Color = new Color(120, 80, 160),
            Passives = new()
            {
                new() { Kind = PassiveKind.Exploit, Name = "Exploit Weakness", Description = "Deals x1.3 damage to Blighted or Marked enemies." },
                Backline,
            },
            Skills = new()
            {
                new() { Id = "hex_wave", Name = "Hex Wave", Kind = ActionKind.Attack, Power = 0.5f, AreaEffect = true, HitFx = "hex",
                    UsableFrom = Ranks.Of("34"), TargetRanks = Ranks.All,
                    Description = "Damage every enemy." },
                new() { Id = "siphon", Name = "Siphon", Kind = ActionKind.Attack, Power = 0.9f, Lifesteal = 0.6f, HitFx = "hex",
                    UsableFrom = Ranks.Of("234"), TargetRanks = Ranks.Of("12"),
                    Description = "Drain an enemy at the front, healing for 60% of damage dealt." },
                new() { Id = "rot_bloom", Name = "Rot Bloom", Kind = ActionKind.Attack, Power = 0.4f, SpCost = 2, AreaEffect = true,
                    UsableFrom = Ranks.Of("234"), TargetRanks = Ranks.All,
                    Applies = StatusKind.Blight, StatusTurns = 3, StatusPower = 2,
                    Description = "Blight every enemy (2 dmg, 3 turns)." },
                new() { Id = "unmaking", Name = "Unmaking", Kind = ActionKind.Attack, Power = 1.6f, SpCost = 3, HitFx = "hex",
                    UsableFrom = Ranks.Of("234"), TargetRanks = Ranks.All,
                    Description = "Tear at any single enemy." },
            },
        };
        */

        // Relle (the protagonist) first; Elle is the only character that has to be unlocked.
        public static readonly List<CharacterDef> All = new() { Relle, Warden, Priest, Naomi, Knight, Hunter, RelleInverted };

        public static CharacterDef Get(string id) => All.FirstOrDefault(c => c.Id == id);
    }
}
