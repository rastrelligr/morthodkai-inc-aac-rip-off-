using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace SERAAC.Data
{
    public sealed class TrialDef
    {
        public string Id;
        public string Name;
        public string Description;
        public int RoomCount;
        public int Tier;             // difficulty tier; scales enemy stats
        public float LootMultiplier;
        public int ExtractionPoints = 1;
        public bool HasMainObjective; // this trial holds the Area's Main Objective room
    }

    public sealed class AreaDef
    {
        public string Id;
        public string Name;
        public string Description;
        public string ObjectiveId;    // completing it unlocks the next area
        public string ObjectiveName;
        public string UnlockedBy;     // ObjectiveId of the previous area; null = unlocked from the start

        // Placeholder environment colours until tiles/backgrounds arrive.
        public Color Floor, FloorAlt, Wall, Accent;

        public List<EnemyDef> FrontPool, BackPool;
        public EnemyDef Elite, Boss;
        public List<TrialDef> Trials;
    }

    public static class WorldDb
    {
        public static readonly AreaDef AbandonedCity = new()
        {
            Id = "city", Name = "Abandoned City",
            Description = "Where it began. Streets full of Husks that still walk their old commutes.",
            ObjectiveId = "city_source", ObjectiveName = "Find the Source of the Infection",
            Floor = new Color(52, 50, 56), FloorAlt = new Color(46, 44, 50), Wall = new Color(24, 22, 28),
            Accent = new Color(200, 110, 60),
            FrontPool = new() { EnemyDb.Husk, EnemyDb.Crawler, EnemyDb.Bloated },
            BackPool = new() { EnemyDb.Spitter, EnemyDb.Husk },
            Elite = EnemyDb.HollowWarden, Boss = EnemyDb.BlightHeart,
            Trials = new()
            {
                new() { Id = "city_subway", Name = "Collapsed Subway", RoomCount = 8, Tier = 1, LootMultiplier = 1.0f,
                    Description = "A short dive. Good for learning the ropes and grabbing a few shards." },
                new() { Id = "city_quarantine", Name = "Quarantine Block", RoomCount = 12, Tier = 2, LootMultiplier = 1.35f,
                    HasMainObjective = true,
                    Description = "The sealed hospital district. Something pulses at its heart." },
            },
        };

        public static readonly AreaDef DrownedCathedral = new()
        {
            Id = "cathedral", Name = "Drowned Cathedral",
            Description = "The tide came in and never left. The choir still sings underwater.",
            ObjectiveId = "cathedral_hymn", ObjectiveName = "Silence the Hymn Below",
            UnlockedBy = "city_source",
            Floor = new Color(40, 54, 62), FloorAlt = new Color(34, 48, 56), Wall = new Color(16, 22, 30),
            Accent = new Color(90, 170, 200),
            FrontPool = new() { EnemyDb.Acolyte, EnemyDb.TideHound },
            BackPool = new() { EnemyDb.ChoirWretch, EnemyDb.Acolyte },
            Elite = EnemyDb.BellKeeper, Boss = EnemyDb.HymnBelow,
            Trials = new()
            {
                new() { Id = "cathedral_nave", Name = "Flooded Nave", RoomCount = 10, Tier = 3, LootMultiplier = 1.7f,
                    Description = "Knee-deep water and pews full of the faithful." },
                new() { Id = "cathedral_tower", Name = "Bell Tower Depths", RoomCount = 14, Tier = 4, LootMultiplier = 2.1f,
                    ExtractionPoints = 2, HasMainObjective = true,
                    Description = "Down, not up. The bells ring from below." },
            },
        };

        public static readonly List<AreaDef> Areas = new() { AbandonedCity, DrownedCathedral };

        public static AreaDef Area(string id) => Areas.FirstOrDefault(a => a.Id == id);

        public static (AreaDef area, TrialDef trial) Trial(string trialId)
        {
            foreach (var a in Areas)
                foreach (var t in a.Trials)
                    if (t.Id == trialId) return (a, t);
            return (null, null);
        }
    }
}
