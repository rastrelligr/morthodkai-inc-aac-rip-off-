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
        public Color Color;
        public float Scale = 1f;   // placeholder size (bosses are bigger)
        public EnemyRow Row = EnemyRow.Front;
        public List<ActionDef> Skills;

        public string ArtId => "enemy_" + Id;
    }

    public static class EnemyDb
    {
        // Every enemy without its own sheet uses this drawing, recoloured with its Color.
        public const string MockupSprite = "enemy_mockup";

        private static ActionDef Melee(string id, string name, float power, string from = "12", string to = "12") => new()
        {
            Id = id, Name = name, Kind = ActionKind.Attack, Power = power,
            UsableFrom = Ranks.Of(from), TargetRanks = Ranks.Of(to),
        };

        // ---------------- Area 1: Abandoned City ----------------
        public static readonly EnemyDef Husk = new()
        {
            Id = "husk", Name = "Husk", Hp = 16, Damage = 4, Speed = 3, Color = new Color(120, 110, 90),
            Skills = new() { Melee("claw", "Claw", 1.0f) },
        };

        public static readonly EnemyDef Crawler = new()
        {
            Id = "crawler", Name = "Crawler", Hp = 10, Damage = 3, Speed = 8, Color = new Color(150, 90, 70), Scale = 0.75f,
            Skills = new() { Melee("lunge", "Lunge", 1.0f, "123", "12") },
        };

        public static readonly EnemyDef Spitter = new()
        {
            Id = "spitter", Name = "Spitter", Hp = 12, Damage = 4, Speed = 5, Color = new Color(110, 150, 80), Row = EnemyRow.Back,
            Skills = new()
            {
                new() { Id = "bile", Name = "Bile", Kind = ActionKind.Attack, Power = 0.7f,
                    UsableFrom = Ranks.Of("234"), TargetRanks = Ranks.All,
                    Applies = StatusKind.Blight, StatusTurns = 2, StatusPower = 2 },
            },
        };

        public static readonly EnemyDef Bloated = new()
        {
            Id = "bloated", Name = "Bloated", Hp = 30, Damage = 5, Speed = 2, Color = new Color(140, 120, 130), Scale = 1.2f,
            Skills = new() { Melee("slam", "Slam", 1.3f, "12", "1"), CommonActions.Defend },
        };

        public static readonly EnemyDef HollowWarden = new()
        {
            Id = "hollow_warden", Name = "Hollowed Officer", Hp = 38, Damage = 7, Speed = 5,
            Color = new Color(90, 90, 120), Scale = 1.3f,
            Skills = new()
            {
                new() { Id = "cleave", Name = "Cleave", Kind = ActionKind.Attack, Power = 0.7f, AreaEffect = true,
                    UsableFrom = Ranks.Of("12"), TargetRanks = Ranks.Of("12") },
                Melee("crush", "Crush", 1.5f, "12", "1"),
            },
        };

        public static readonly EnemyDef BlightHeart = new()
        {
            Id = "blight_heart", Name = "Source of the Infection", Hp = 60, Damage = 6, Speed = 4,
            Color = new Color(150, 40, 70), Scale = 1.6f, Row = EnemyRow.Back,
            Skills = new()
            {
                new() { Id = "pulse", Name = "Pulse", Kind = ActionKind.Attack, Power = 0.5f, AreaEffect = true,
                    TargetRanks = Ranks.All, Applies = StatusKind.Blight, StatusTurns = 2, StatusPower = 2 },
                new() { Id = "tendril", Name = "Tendril", Kind = ActionKind.Attack, Power = 1.3f, TargetRanks = Ranks.All },
            },
        };

        // ---------------- Area 2: Drowned Cathedral ----------------
        public static readonly EnemyDef Acolyte = new()
        {
            Id = "acolyte", Name = "Drowned Acolyte", Hp = 22, Damage = 6, Speed = 4, Color = new Color(70, 110, 120),
            Skills = new() { Melee("censer", "Censer Swing", 1.0f) },
        };

        public static readonly EnemyDef TideHound = new()
        {
            Id = "tide_hound", Name = "Tide Hound", Hp = 16, Damage = 5, Speed = 9, Color = new Color(60, 90, 110), Scale = 0.8f,
            Skills = new() { Melee("maul", "Maul", 1.1f, "123", "123") },
        };

        public static readonly EnemyDef ChoirWretch = new()
        {
            Id = "choir_wretch", Name = "Choir Wretch", Hp = 18, Damage = 5, Speed = 6, Color = new Color(170, 170, 190), Row = EnemyRow.Back,
            Skills = new()
            {
                new() { Id = "dirge", Name = "Dirge", Kind = ActionKind.Attack, Power = 0.5f, AreaEffect = true,
                    UsableFrom = Ranks.Of("34"), TargetRanks = Ranks.All },
                new() { Id = "hymn", Name = "Drowned Hymn", Kind = ActionKind.Heal, Side = TargetSide.Ally, Power = 1.4f,
                    UsableFrom = Ranks.Of("234") },
            },
        };

        public static readonly EnemyDef BellKeeper = new()
        {
            Id = "bell_keeper", Name = "Bell Keeper", Hp = 70, Damage = 8, Speed = 5, Color = new Color(110, 90, 60), Scale = 1.35f,
            Skills = new()
            {
                new() { Id = "toll", Name = "Toll", Kind = ActionKind.Attack, Power = 0.6f, AreaEffect = true, TargetRanks = Ranks.All },
                Melee("hammer", "Bell Hammer", 1.5f, "12", "12"),
            },
        };

        public static readonly EnemyDef HymnBelow = new()
        {
            Id = "hymn_below", Name = "The Hymn Below", Hp = 95, Damage = 8, Speed = 5,
            Color = new Color(40, 70, 110), Scale = 1.7f, Row = EnemyRow.Back,
            Skills = new()
            {
                new() { Id = "undertow", Name = "Undertow", Kind = ActionKind.Attack, Power = 0.6f, AreaEffect = true,
                    TargetRanks = Ranks.All, Applies = StatusKind.Mark, StatusTurns = 2 },
                new() { Id = "drown", Name = "Drown", Kind = ActionKind.Attack, Power = 1.4f, TargetRanks = Ranks.All },
            },
        };

        public static readonly List<EnemyDef> All = new()
        {
            Husk, Crawler, Spitter, Bloated, HollowWarden, BlightHeart,
            Acolyte, TideHound, ChoirWretch, BellKeeper, HymnBelow,
        };

        public static EnemyDef Get(string id) => All.FirstOrDefault(e => e.Id == id);
    }
}
