using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace SERAAC.Data
{
    public enum EnemyRow { Front, Back }

    public sealed class EnemyDef
    {
        public string Id;
        public string Name;
        public int Hp, Damage, Speed;
        public Color Color;        // placeholder tint (fallback drawing, map, particles)
        public float Scale = 1f;   // drawn size (bosses are bigger)
        public EnemyRow Row = EnemyRow.Front;   // Position: where it starts in the formation (not its Type)
        public EnemyType Type;                   // its job: target preference and behaviour (Data/EnemyTypeDb.cs)
        public List<ActionDef> Skills;
        public string Blurb;                     // design note shown in the Art Viewer / balance sheet

        // Exploration can use different art from combat (Husk is curled up while roaming, spread out in battle).
        public string ExploreArtId;

        public string ArtId => "enemy_" + Id;
        public string ExploreArt => ExploreArtId ?? ArtId;
        public EnemyTypeDef TypeDef => EnemyTypeDb.Get(Type);
    }

    // From "New Enemies Type". Spec damage numbers = Damage x Power (before depth / tier scaling).
    public static class EnemyDb
    {
        // Enemies without their own sheet draw this, recoloured with their Color.
        public const string MockupSprite = "enemy_mockup";

        private static ActionDef Melee(string id, string name, float power, string from = "12", string to = "12") => new()
        {
            Id = id, Name = name, Kind = ActionKind.Attack, Power = power,
            UsableFrom = Ranks.Of(from), TargetRanks = Ranks.Of(to), Melee = true,
        };

        // Normal. Bite: 2 Physical, single, prefers the frontline, nothing that bypasses the formation.
        public static readonly EnemyDef Gnawer = new()
        {
            Id = "gnawer", Name = "Gnawer", Type = EnemyType.Normal, Hp = 14, Damage = 2, Speed = 6,
            Color = new Color(210, 200, 190), Scale = 0.85f,
            Blurb = "A common infected animal. Lowers its body, lunges, bites, and returns to its position.",
            Skills = new()
            {
                Melee("bite", "Bite", 1.0f),
            },
        };

        // Brute. Heavy Crash: 3 Physical, single, prefers the frontline. Curled larva while roaming,
        // the failed-metamorphosis "butterfly" form in battle.
        public static readonly EnemyDef Husk = new()
        {
            Id = "husk", Name = "Husk", Type = EnemyType.Brute, Hp = 26, Damage = 3, Speed = 2,
            Color = new Color(150, 60, 70), Scale = 1.3f, ExploreArtId = "enemy_husk_curled",
            Blurb = "An oversized larva whose metamorphosis failed. Slow and bulky; slams its head and upper body into the front line.",
            Skills = new()
            {
                Melee("heavy_crash", "Heavy Crash", 1.0f),
            },
        };

        // Stalker. Spectral Lunge: 2 Physical, single, Back 1 / Back 2 priority, from any position.
        public static readonly EnemyDef Wraith = new()
        {
            Id = "wraith", Name = "Wraith", Type = EnemyType.Stalker, Hp = 12, Damage = 2, Speed = 8,
            Color = new Color(150, 190, 240), Scale = 1.1f, Row = EnemyRow.Back,
            Blurb = "A tall, limbless apparition. Fast and precise; phases through the front line to bite the back line.",
            Skills = new()
            {
                new() { Id = "spectral_lunge", Name = "Spectral Lunge", Kind = ActionKind.Attack, Power = 1.0f, Melee = true,
                    UsableFrom = Ranks.All, TargetRanks = Ranks.All, HitFx = "hex" },
            },
        };

        // The Wyvern's big attack: telegraphed a turn ahead by Rear Up. Brace (Guard acts first), raise the
        // Aegis Wall, or drag the Wyvern to the front (Snare Line) and interrupt it (Shield Bash / Iron Rebuke).
        private static readonly ActionDef SkyfallCrush = new()
        {
            Id = "skyfall_crush", Name = "Skyfall Crush", Kind = ActionKind.Attack, Power = 1.25f, AreaEffect = true,
            TargetRanks = Ranks.All, HitFx = "impact",
            Description = "Crashes down on the whole party.",
        };

        // Boss (new type: Apex), the Fallen Academy's Main Objective. Original design: a serpent-like dragon
        // whose coils carry red rings. It sweeps the front, poisons one target, binds the whole party, and
        // every few turns rears up for Skyfall Crush. At half HP it enrages (Battle.Enrage).
        public static readonly EnemyDef Wyvern = new()
        {
            Id = "wyvern", Name = "Wyvern", Type = EnemyType.Apex, Hp = 60, Damage = 4, Speed = 5,
            Color = new Color(190, 40, 70), Scale = 1.9f, Row = EnemyRow.Back,
            Blurb = "A serpent-like dragon nesting in the Academy. Sweeps the front line, poisons the weak, slows the party with its ringed coils, and rears up to crash down on everyone.",
            Skills = new()
            {
                new() { Id = "rear_up", Name = "Rear Up", Kind = ActionKind.WindUp, Side = TargetSide.Self, Power = 0f, WindUp = SkyfallCrush,
                    Description = "Rises above the party. Next turn: Skyfall Crush on everyone, unless it is interrupted." },
                new() { Id = "tail_sweep", Name = "Tail Sweep", Kind = ActionKind.Attack, Power = 0.6f, AreaEffect = true,
                    TargetRanks = Ranks.Of("12"), HitFx = "slash" },
                new() { Id = "venom_fang", Name = "Venom Fang", Kind = ActionKind.Attack, Power = 1.2f, Melee = true,
                    TargetRanks = Ranks.All, Applies = StatusKind.Blight, StatusTurns = 3, StatusPower = 2, HitFx = "bile" },
                new() { Id = "binding_coils", Name = "Binding Coils", Kind = ActionKind.Attack, Power = 0.35f, AreaEffect = true,
                    TargetRanks = Ranks.All, Applies = StatusKind.Slow, StatusTurns = 2, StatusPower = 2, HitFx = "thread" },
            },
        };

        public static readonly List<EnemyDef> All = new() { Gnawer, Husk, Wraith, Wyvern };

        public static EnemyDef Get(string id) => All.FirstOrDefault(e => e.Id == id);
    }
}
