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
        Exploit,    // x1.3 damage to Blighted or Marked targets
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

        public bool Has(PassiveKind k) => Passives.Any(p => p.Kind == k);

        public string ArtId => "char_" + Id;
        public string PortraitId => "portrait_" + Id;

        public IEnumerable<ActionDef> AllActions =>
            Skills.Concat(new[] { CommonActions.Defend, CommonActions.Advance, CommonActions.Retreat });
    }

    // The playable roster. Names and numbers are placeholders for the design team to tune.
    public static class CharacterDb
    {
        public const int PartySize = 4;

        // Shared passives (Relle's Frontline is reused by other front-liners).
        private static readonly PassiveDef Frontline = new() { Kind = PassiveKind.Frontline, Name = "Frontline", Description = "Deals x1.25 damage in Front 1 or Front 2." };
        private static readonly PassiveDef Backline = new() { Kind = PassiveKind.Backline, Name = "Backline", Description = "Deals x1.25 damage and healing in Back 1 or Back 2." };

        // The original five, rebuilt to Relle's level: SP skills (0 / 2 / 3 SP) and two passives each.
        public static readonly CharacterDef Knight = new()
        {
            Id = "knight", Name = "Pillow", Role = "Knight",
            Blurb = "A knight with a bonded blade. Sleepy-eyed, never off guard. (Placeholder bio.)",
            BaseHp = 32, BaseDamage = 6, BaseSpeed = 5, MaxSp = 5, Color = new Color(170, 60, 70),
            Passives = new()
            {
                Frontline,
                new() { Kind = PassiveKind.GuardAfterSkill, Name = "Knight's Stance", Description = "After using a skill, Pillow is Guarded until acting again." },
            },
            Skills = new()
            {
                new() { Id = "rend", Name = "Rend", Kind = ActionKind.Attack, Power = 1.0f,
                    UsableFrom = Ranks.Of("12"), TargetRanks = Ranks.Of("12"),
                    Description = "A heavy slash at the enemy front." },
                new() { Id = "black_flame", Name = "Black Flame", Kind = ActionKind.Attack, Power = 0.8f, SpCost = 2,
                    UsableFrom = Ranks.Of("123"), TargetRanks = Ranks.All,
                    Applies = StatusKind.Blight, StatusTurns = 3, StatusPower = 3,
                    Description = "Dark fire from the bonded blade. Burns any enemy and Blights it (3 dmg, 3 turns)." },
                new() { Id = "moonfall", Name = "Moonfall", Kind = ActionKind.Attack, Power = 1.1f, SpCost = 3, AreaEffect = true,
                    UsableFrom = Ranks.Of("12"), TargetRanks = Ranks.Of("12"),
                    Description = "A sweeping cut through the whole enemy front row." },
            },
        };

        public static readonly CharacterDef Warden = new()
        {
            Id = "warden", Name = "Bram", Role = "Warden",
            Blurb = "An ex-riot officer whose bonded shield never leaves his arm.",
            BaseHp = 46, BaseDamage = 5, BaseSpeed = 3, MaxSp = 5, Color = new Color(80, 110, 150),
            Passives = new()
            {
                new() { Kind = PassiveKind.Tough, Name = "Thick Hide", Description = "Takes 20% less damage." },
                Frontline,
            },
            Skills = new()
            {
                new() { Id = "shield_bash", Name = "Shield Bash", Kind = ActionKind.Attack, Power = 1.0f,
                    UsableFrom = Ranks.Of("12"), TargetRanks = Ranks.Of("1"),
                    Description = "Batter the enemy standing in Front 1." },
                new() { Id = "taunt", Name = "Hold the Line", Kind = ActionKind.Taunt, Side = TargetSide.Self,
                    UsableFrom = Ranks.Of("12"),
                    Description = "Taunt + Defend until next action. Single-target enemy attacks that can reach Bram are redirected to him." },
                new() { Id = "shove", Name = "Riot Shove", Kind = ActionKind.Attack, Power = 1.0f, SpCost = 2, KnockBack = 2,
                    UsableFrom = Ranks.Of("12"), TargetRanks = Ranks.Of("12"),
                    Description = "Hit a front-row enemy and shove it 2 positions back, out of melee range." },
                new() { Id = "rally", Name = "Rally", Kind = ActionKind.Heal, Side = TargetSide.Ally, Power = 1.0f, SpCost = 3, AreaEffect = true,
                    Description = "Bark orders: every ally recovers a little HP." },
            },
        };

        public static readonly CharacterDef Priest = new()
        {
            Id = "priest", Name = "Sister Noa", Role = "Priest",
            Blurb = "Still prays every night. Nobody knows who answers.",
            BaseHp = 24, BaseDamage = 4, BaseSpeed = 5, MaxSp = 5, Color = new Color(200, 190, 150),
            Passives = new()
            {
                new() { Kind = PassiveKind.Devotion, Name = "Devotion", Description = "Heals for 30% more." },
                Backline,
            },
            Skills = new()
            {
                new() { Id = "mend", Name = "Mend", Kind = ActionKind.Heal, Side = TargetSide.Ally, Power = 2.0f,
                    UsableFrom = Ranks.Of("234"),
                    Description = "Heal one ally." },
                new() { Id = "smite", Name = "Smite", Kind = ActionKind.Attack, Power = 1.0f,
                    UsableFrom = Ranks.Of("234"), TargetRanks = Ranks.Of("12"),
                    Description = "A desperate strike at the enemy front." },
                new() { Id = "purify", Name = "Purify", Kind = ActionKind.Heal, Side = TargetSide.Ally, Power = 1.2f, SpCost = 2,
                    UsableFrom = Ranks.Of("234"), Cleanse = true,
                    Description = "Heal one ally and remove Blight and Mark." },
                new() { Id = "sanctuary", Name = "Sanctuary", Kind = ActionKind.Heal, Side = TargetSide.Ally, Power = 1.3f, SpCost = 3,
                    AreaEffect = true, Cleanse = true, UsableFrom = Ranks.Of("234"),
                    Description = "Heal and cleanse every ally." },
            },
        };

        public static readonly CharacterDef Hunter = new()
        {
            Id = "hunter", Name = "Winter", Role = "Hunter",
            Blurb = "Sniper. The rifle is a bonded infected that hums when prey is near. (Placeholder bio.)",
            BaseHp = 24, BaseDamage = 6, BaseSpeed = 7, MaxSp = 5, Color = new Color(110, 140, 80),
            Passives = new()
            {
                new() { Kind = PassiveKind.Deadeye, Name = "Deadeye", Description = "+20% critical hit chance." },
                Backline,
            },
            Skills = new()
            {
                new() { Id = "snipe", Name = "Snipe", Kind = ActionKind.Attack, Power = 1.1f,
                    UsableFrom = Ranks.Of("34"), TargetRanks = Ranks.Of("234"),
                    Description = "Shoot an enemy behind their front line." },
                new() { Id = "mark", Name = "Mark Prey", Kind = ActionKind.Attack, Power = 0.5f,
                    UsableFrom = Ranks.Of("234"), TargetRanks = Ranks.All,
                    Applies = StatusKind.Mark, StatusTurns = 2,
                    Description = "Graze and Mark any enemy: it takes +35% damage for 2 turns." },
                new() { Id = "point_blank", Name = "Point Blank", Kind = ActionKind.Attack, Power = 1.7f,
                    UsableFrom = Ranks.Of("1"), TargetRanks = Ranks.Of("1"), SelfMoveAfter = 1,
                    Description = "Huge hit on Front 1, then the recoil knocks Winter back 1 position." },
                new() { Id = "piercing_round", Name = "Piercing Round", Kind = ActionKind.Attack, Power = 0.9f, SpCost = 3, AreaEffect = true,
                    UsableFrom = Ranks.Of("34"), TargetRanks = Ranks.All,
                    Description = "One round through the whole enemy line." },
            },
        };

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
                new() { Id = "hex_wave", Name = "Hex Wave", Kind = ActionKind.Attack, Power = 0.5f, AreaEffect = true,
                    UsableFrom = Ranks.Of("34"), TargetRanks = Ranks.All,
                    Description = "Damage every enemy." },
                new() { Id = "siphon", Name = "Siphon", Kind = ActionKind.Attack, Power = 0.9f, Lifesteal = 0.6f,
                    UsableFrom = Ranks.Of("234"), TargetRanks = Ranks.Of("12"),
                    Description = "Drain an enemy at the front, healing for 60% of damage dealt." },
                new() { Id = "rot_bloom", Name = "Rot Bloom", Kind = ActionKind.Attack, Power = 0.4f, SpCost = 2, AreaEffect = true,
                    UsableFrom = Ranks.Of("234"), TargetRanks = Ranks.All,
                    Applies = StatusKind.Blight, StatusTurns = 3, StatusPower = 2,
                    Description = "Blight every enemy (2 dmg, 3 turns)." },
                new() { Id = "unmaking", Name = "Unmaking", Kind = ActionKind.Attack, Power = 1.6f, SpCost = 3,
                    UsableFrom = Ranks.Of("234"), TargetRanks = Ranks.All,
                    Description = "Tear at any single enemy." },
            },
        };

        private static readonly PassiveDef FeastPassive = new()
        {
            Kind = PassiveKind.Feast, Name = "Flesh-Eating Blade",
            Description = "Each skill grants 1 Feast. At 3 Feast the next skill always crits and consumes all Feast. Every hit restores a little HP.",
        };

        // From "SERAAC Character Relle" (designer spec). Damage numbers in the spec are with
        // Damage 5: Pierce 5, Blood Feast 3 x2, Cleaver 7.
        public static readonly CharacterDef Relle = new()
        {
            Id = "relle", Name = "Relle", Role = "Striker",
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
                    TargetRanks = Ranks.All, Anim = "stab1",
                    Description = "Stab into the gut of any enemy." },
                new() { Id = "blood_feast", Name = "Blood Feast", Kind = ActionKind.Attack, Power = 0.6f, Hits = 2, SpCost = 2,
                    AreaEffect = true, TargetRanks = Ranks.All, ShuffleTargets = true, Anim = "swing1,swing2",
                    Description = "Dash through every enemy twice. Their positions are scrambled." },
                new() { Id = "cleaver", Name = "Cleaver", Kind = ActionKind.Attack, Power = 1.4f, SpCost = 3,
                    UsableFrom = Ranks.Of("12"), TargetRanks = Ranks.Of("12"), Anim = "slam",
                    Description = "A huge slash at an enemy directly in front (front row only)." },
            },
        };

        // Relle's inverted self. Placeholder kit (design pending): a back-row Adept mirror of Relle.
        public static readonly CharacterDef RelleInverted = new()
        {
            Id = "relle_inv", Name = "Elle", Role = "Adept",
            Blurb = "Relle's reflection, left behind in the dark. Cold where Relle is hungry. (Placeholder kit.)",
            BaseHp = 24, BaseDamage = 4, BaseSpeed = 5, MaxSp = 5, Color = new Color(70, 190, 210),
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
                    TargetRanks = Ranks.All, Applies = StatusKind.Mark, StatusTurns = 2, Anim = "stab2",
                    Description = "A cold thrust at any enemy. Marks it (+35% damage taken, 2 turns)." },
                new() { Id = "tidewash", Name = "Tidewash", Kind = ActionKind.Heal, Side = TargetSide.Ally, Power = 1.0f, SpCost = 2,
                    AreaEffect = true, Cleanse = true, UsableFrom = Ranks.Of("234"), Anim = "swing2",
                    Description = "Heal and cleanse every ally." },
                new() { Id = "undercut", Name = "Undercut", Kind = ActionKind.Attack, Power = 1.3f, SpCost = 3, KnockBack = 2,
                    UsableFrom = Ranks.Of("123"), TargetRanks = Ranks.Of("12"), Anim = "slam",
                    Description = "Slam a front-row enemy and knock it 2 positions back." },
            },
        };

        // Relle (the protagonist) first; Elle is the only character that has to be unlocked.
        public static readonly List<CharacterDef> All = new() { Relle, Knight, Warden, Priest, Hunter, Occultist, RelleInverted };

        public static CharacterDef Get(string id) => All.FirstOrDefault(c => c.Id == id);
    }
}
