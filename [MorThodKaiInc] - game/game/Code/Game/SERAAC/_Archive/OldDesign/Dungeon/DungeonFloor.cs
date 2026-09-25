using System.Collections.Generic;

namespace SERAAC.Data.Dungeon
{
    public class DungeonFloor
    {
        public int FloorNumber { get; set; }
        public List<Room> Rooms { get; set; } = new List<Room>();
        public Dictionary<string, List<string>> RoomConnections { get; set; } = new Dictionary<string, List<string>>(); // roomId -> reachable roomIds
    }
}
