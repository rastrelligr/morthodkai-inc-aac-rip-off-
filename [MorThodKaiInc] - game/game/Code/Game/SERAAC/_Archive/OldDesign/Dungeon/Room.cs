using System.Collections.Generic;
using SERAAC.Data.Combat;

namespace SERAAC.Data.Dungeon
{
    public class Room
    {
        public string RoomId { get; set; }
        public RoomType RoomType { get; set; }
        public BattleScreen Encounter { get; set; }
        public List<string> LootTable { get; set; } = new List<string>();
        public bool Cleared { get; set; }
    }
}
