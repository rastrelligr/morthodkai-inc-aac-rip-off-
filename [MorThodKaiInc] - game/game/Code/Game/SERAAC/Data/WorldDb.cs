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
        // Objective Pool: one is picked at random each time the trial starts (Data/ObjectiveDb.cs).
        public List<ObjectiveDef> Objectives = new();
        public bool HasMainObjective => Objectives.Any(o => o.Main); // every pick completes the Area's Main Objective
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
            Description = "Where it began. Gnawers in the gutters, Wraiths in the windows, Husks that never finished changing.",
            ObjectiveId = "city_source", ObjectiveName = "Slay the Wyvern",
            Floor = new Color(52, 50, 56), FloorAlt = new Color(46, 44, 50), Wall = new Color(24, 22, 28),
            Accent = new Color(200, 110, 60),
            FrontPool = new() { EnemyDb.Gnawer, EnemyDb.Gnawer, EnemyDb.Husk },   // picked evenly: 2 in 3 are Gnawers
            BackPool = new() { EnemyDb.Wraith, EnemyDb.Gnawer },
            Elite = EnemyDb.Husk, Boss = EnemyDb.Wyvern,   // Elite Lairs: a Husk leads (stats +10% in elite rooms)
            Trials = new()
            {
                new() { Id = "city_outskirt", Name = "City Outskirts", RoomCount = 8, Tier = 1, LootMultiplier = 1.0f,
                    Objectives = new() { ObjectiveDb.CullGnawers, ObjectiveDb.CullBrutes, ObjectiveDb.HuntStalkers, ObjectiveDb.SupplyRun, ObjectiveDb.SignalTower },
                    Description = "A short dive. Good for learning the ropes and grabbing a few shards." },
                new() { Id = "city_academy", Name = "Fallen Academy", RoomCount = 12, Tier = 2, LootMultiplier = 1.35f,
                    Objectives = new() { ObjectiveDb.SlayWyvern },
                    Description = "Where we belongs." },
            },
        };

        // Drowned Cathedral: all its enemies and its boss were the old designs, so the area is out until new
        // ones are made for it. Uncomment, refill the pools / objectives, and add it to Areas again.
        /*
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
                    Objectives = new() { ObjectiveDb.RingBells, ObjectiveDb.CullHounds, ObjectiveDb.Reliquary },
                    Description = "Knee-deep water and pews full of the faithful." },
                new() { Id = "cathedral_tower", Name = "Bell Tower Depths", RoomCount = 14, Tier = 4, LootMultiplier = 2.1f,
                    ExtractionPoints = 2, Objectives = new() { ObjectiveDb.SilenceHymn, ObjectiveDb.BreakStones },
                    Description = "Down, not up. The bells ring from below." },
            },
        };
        */

        public static readonly List<AreaDef> Areas = new() { AbandonedCity };

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
