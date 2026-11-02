using System.Collections.Generic;
using Microsoft.Xna.Framework;
using SERAAC.Data;

namespace SERAAC.Trial
{
    public enum RoomType { Entrance, Normal, Elite, Treasure, Rest, Event, Extraction, Objective, Npc }
    public enum LootKind { Shards, Consumable, Equipment }

    public sealed class LootPickup
    {
        public Vector2 Tile;      // position in tile units (centre of tile = x + 0.5)
        public LootKind Kind;
        public int Amount;        // shards
        public string ItemId;     // consumable / equipment id
        public bool Collected;
        public float Age;         // seconds since it appeared (pop-in animation)
    }

    public sealed class Room
    {
        public int Id;
        public Point Grid;
        public RoomType Type;
        public int Depth;               // BFS distance from the entrance; drives danger and loot
        public int Template;
        public bool Mirror;
        public RoomLayout Layout;
        public readonly Dictionary<Dir, Room> Links = new();

        // Current State (design: every room stores Type, Layout, Connections and Current State)
        public bool Visited;
        public bool Cleared;            // combat won / special condition done
        public bool FeatureUsed;        // chest opened, shrine used, event resolved
        public readonly List<LootPickup> Pickups = new();
        public EventDef Event;
        public NpcDef Npc;              // who stands in an NPC room

        // The enemies in this room, rolled when the trial starts (empty = no fight here). While exploring they
        // roam the room and chase the party (Trial/Pack.cs); touching one starts the fight.
        public List<EnemyDef> Encounter = new();
        public Pack Pack;               // their live positions while the party is in the room
        public bool Alerted;            // its pack has spotted the party before: on guard when the party comes back

        // Combat background (an image listed in Art/Stages/backgrounds.json). Picked by BackgroundDb the
        // first time a fight starts here, then kept so the room looks the same in a later fight.
        // Set it earlier (e.g. in TrialMap) to force a background for a room.
        public string StageId;

        public bool IsCombatRoom => Encounter.Count > 0;
        public bool HasEnemies => IsCombatRoom && !Cleared;
        public bool IsSpecial => Type != RoomType.Normal && Type != RoomType.Entrance;
        public bool HasFeature => Type != RoomType.Normal;

        // Special rooms are revealed on the map once the party is next to them.
        public string MapLabel => Type switch
        {
            RoomType.Entrance => "S",
            RoomType.Elite => "!",
            RoomType.Treasure => "$",
            RoomType.Rest => "+",
            RoomType.Event => "?",
            RoomType.Extraction => "X",
            RoomType.Objective => "*",
            RoomType.Npc => "@",
            _ => "",
        };

        public string DisplayName => Type switch
        {
            RoomType.Entrance => "Entrance",
            RoomType.Normal => "Infested Room",
            RoomType.Elite => "Elite Lair",
            RoomType.Treasure => "Cache",
            RoomType.Rest => "Shrine",
            RoomType.Event => Event?.Title ?? "Strange Room",
            RoomType.Extraction => "Extraction Point",
            RoomType.Objective => "Objective Site",
            RoomType.Npc => Npc?.Name ?? "Quiet Room",
            _ => "Room",
        };
    }
}
