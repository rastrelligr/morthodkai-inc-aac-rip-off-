using SERAAC.Entities;

namespace SERAAC.World
{
    // An Enemy's runtime position while it is still patrolling the dungeon grid,
    // before it gets pulled into a CombatEncounter.
    public class DungeonEnemy
    {
        public Enemy Data;
        public int X;
        public int Y;
    }
}
