using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using SERAAC.Data;

namespace SERAAC.Trial
{
    // A Trial's rooms and their connections, generated per run on a small grid.
    public sealed class TrialMap
    {
        public const int GridRadius = 4;
        public const int ShortRooms = 8;   // TrialMap.Short

        public readonly Dictionary<Point, Room> Rooms = new();
        public Room Entrance;
        public int MaxDepth;

        public Room At(Point p) => Rooms.TryGetValue(p, out var r) ? r : null;

        // objectiveRooms: how many Objective rooms the run's objective needs (ObjectiveDef.RoomCount).
        public static TrialMap Generate(TrialDef trial, Random rng, bool withNpcRoom = false, int objectiveRooms = 0)
        {
            var map = new TrialMap();
            var start = new Room { Id = 0, Grid = Point.Zero, Type = RoomType.Entrance };
            map.Rooms[start.Grid] = start;
            map.Entrance = start;
            var order = new List<Room> { start };

            // Grow a tree: attach new rooms to random existing ones, biased toward recent rooms
            // so the map gets long branches (routes) rather than one blob.
            int guard = 0;
            while (map.Rooms.Count < trial.RoomCount && guard++ < 5000)
            {
                var from = order[Math.Max(0, order.Count - 1 - (int)(Math.Pow(rng.NextDouble(), 2) * order.Count))];
                var dir = RoomLayout.AllDirs[rng.Next(4)];
                var step = RoomLayout.Step(dir);
                var p = new Point(from.Grid.X + step.X, from.Grid.Y + step.Y);
                if (Math.Abs(p.X) > GridRadius || Math.Abs(p.Y) > GridRadius || map.Rooms.ContainsKey(p)) continue;
                // Avoid tight 2x2 clumps so corridors read clearly.
                if (NeighbourCount(map, p) > 2) continue;

                var room = new Room { Id = map.Rooms.Count, Grid = p, Type = RoomType.Normal };
                map.Rooms[p] = room;
                Link(from, room, dir);
                order.Add(room);
            }

            // A few extra connections create loops (alternate routes).
            foreach (var r in map.Rooms.Values)
                foreach (var d in new[] { Dir.East, Dir.South })
                {
                    var s = RoomLayout.Step(d);
                    var n = map.At(new Point(r.Grid.X + s.X, r.Grid.Y + s.Y));
                    if (n != null && !r.Links.ContainsKey(d) && rng.NextDouble() < 0.18) Link(r, n, d);
                }

            map.ComputeDepths();
            map.AssignTypes(trial, rng, withNpcRoom, objectiveRooms);

            foreach (var r in map.Rooms.Values)
            {
                r.Template = rng.Next(RoomLayout.Templates.Length);
                r.Mirror = rng.Next(2) == 0;
                r.Layout = RoomLayout.Build(r.Template, r.Mirror, r.Links.Keys);
                if (r.Type == RoomType.Event) r.Event = EventDb.All[rng.Next(EventDb.All.Count)];
            }
            map.Entrance.Visited = true;
            map.Entrance.Cleared = true;
            return map;
        }

        // The playtest's fixed map. Two ways from the entrance to the shrine: the quick one through a single fight,
        // or the long one past an event and a harder fight to a cache. From the shrine, the boss one way and the
        // way out the other.
        public static TrialMap Short(Random rng)
        {
            var map = new TrialMap();
            Room Add(int x, int y, RoomType type, Room from = null, Dir dir = Dir.East)
            {
                var room = new Room { Id = map.Rooms.Count, Grid = new Point(x, y), Type = type };
                map.Rooms[room.Grid] = room;
                if (from != null) Link(from, room, dir);
                return room;
            }
            map.Entrance = Add(0, 0, RoomType.Entrance);
            var fight = Add(1, 0, RoomType.Normal, map.Entrance);
            var shrine = Add(2, 0, RoomType.Rest, fight);
            Add(3, 0, RoomType.Objective, shrine);
            Add(2, 1, RoomType.Extraction, shrine, Dir.South);
            var side = Add(0, -1, RoomType.Event, map.Entrance, Dir.North);
            var hard = Add(1, -1, RoomType.Normal, side);
            var cache = Add(2, -1, RoomType.Treasure, hard);
            Link(cache, shrine, Dir.South);
            map.ComputeDepths();
            foreach (var r in map.Rooms.Values)
            {
                r.Template = rng.Next(RoomLayout.Templates.Length);
                r.Mirror = rng.Next(2) == 0;
                r.Layout = RoomLayout.Build(r.Template, r.Mirror, r.Links.Keys);
                if (r.Type == RoomType.Event) r.Event = EventDb.All[rng.Next(EventDb.All.Count)];
            }
            map.Entrance.Visited = true;
            map.Entrance.Cleared = true;
            return map;
        }

        private static int NeighbourCount(TrialMap map, Point p) =>
            RoomLayout.AllDirs.Count(d =>
            {
                var s = RoomLayout.Step(d);
                return map.Rooms.ContainsKey(new Point(p.X + s.X, p.Y + s.Y));
            });

        private static void Link(Room a, Room b, Dir aToB)
        {
            a.Links[aToB] = b;
            b.Links[RoomLayout.Opposite(aToB)] = a;
        }

        private void ComputeDepths()
        {
            foreach (var r in Rooms.Values) r.Depth = -1;
            var q = new Queue<Room>();
            Entrance.Depth = 0;
            q.Enqueue(Entrance);
            while (q.Count > 0)
            {
                var r = q.Dequeue();
                foreach (var n in r.Links.Values)
                    if (n.Depth < 0) { n.Depth = r.Depth + 1; q.Enqueue(n); }
            }
            MaxDepth = Rooms.Values.Max(r => r.Depth);
        }

        private void AssignTypes(TrialDef trial, Random rng, bool withNpcRoom, int objectiveRooms)
        {
            var free = Rooms.Values.Where(r => r.Type == RoomType.Normal).ToList();

            Room Take(Func<Room, bool> filter, Func<Room, double> score)
            {
                var pool = free.Where(filter).ToList();
                if (pool.Count == 0) pool = free.ToList();
                if (pool.Count == 0) return null;
                var pick = pool.OrderByDescending(r => score(r) + rng.NextDouble() * 0.5).First();
                free.Remove(pick);
                return pick;
            }

            // The first Objective room sits at the deepest dead end: reaching it is the big risk.
            // Any others spread out away from it (and each other), still away from the entrance.
            var objectives = new List<Room>();
            for (int i = 0; i < objectiveRooms; i++)
            {
                var obj = objectives.Count == 0
                    ? Take(r => true, r => r.Depth * 2 + (r.Links.Count == 1 ? 3 : 0))
                    : Take(r => r.Depth >= 2, r => objectives.Min(o => Distance(o, r)) + r.Depth * 0.5);
                if (obj == null) break;
                obj.Type = RoomType.Objective;
                objectives.Add(obj);
            }

            // Extraction points are placed away from the entrance so leaving is a trip, not a free exit.
            int minExtractDepth = Math.Max(2, (int)Math.Ceiling(MaxDepth * 0.5));
            var extracts = new List<Room>();
            for (int i = 0; i < trial.ExtractionPoints; i++)
            {
                var ex = Take(r => r.Depth >= minExtractDepth,
                    r => extracts.Count == 0 ? r.Depth * 0.5 : extracts.Min(e => Distance(e, r)));
                if (ex == null) break;
                ex.Type = RoomType.Extraction;
                extracts.Add(ex);
            }

            var specials = new List<RoomType> { RoomType.Treasure, RoomType.Rest };
            if (withNpcRoom) specials.Add(RoomType.Npc);
            if (trial.RoomCount >= 9) specials.Add(RoomType.Event);
            if (trial.RoomCount >= 10) specials.Add(RoomType.Elite);
            if (trial.RoomCount >= 13) { specials.Add(RoomType.Treasure); specials.Add(RoomType.Elite); }

            foreach (var type in specials)
            {
                var room = type == RoomType.Elite
                    ? Take(r => r.Depth >= 2, r => r.Depth)
                    : type == RoomType.Rest
                        ? Take(r => r.Depth >= 2, r => -Math.Abs(r.Depth - MaxDepth / 2.0))
                        : Take(r => r.Depth >= 1, r => rng.NextDouble() * 3);
                if (room != null) room.Type = type;
            }
        }

        private static double Distance(Room a, Room b) => Math.Abs(a.Grid.X - b.Grid.X) + Math.Abs(a.Grid.Y - b.Grid.Y);
    }
}
