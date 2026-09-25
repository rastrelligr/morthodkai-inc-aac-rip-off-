using System;
using System.Collections.Generic;
using SERAAC.Entities;
using SERAAC.World;

namespace SERAAC.Core
{
    // Run state shared across screens: who survives a screen transition (party, current
    // floor, where the Vessel is standing) lives here instead of inside any one screen.
    public class GameContext
    {
        public const int TotalFloors = 3;

        public Random Rng { get; } = new Random();
        public Party Party { get; private set; }

        public DungeonMap Dungeon { get; set; }
        public List<DungeonEnemy> DungeonEnemies { get; set; } = new List<DungeonEnemy>();

        public int VesselTileX { get; set; }
        public int VesselTileY { get; set; }
        public int VesselFacing { get; set; } // 0 = North, 1 = East, 2 = South, 3 = West

        public int FloorNumber { get; set; } = 1;

        // Set by ExplorationScreen right before switching to CombatScreen.
        public List<Enemy> PendingEncounter { get; set; }

        public void StartNewRun()
        {
            Party = Party.CreateNewRun();
            FloorNumber = 1;
        }
    }
}
