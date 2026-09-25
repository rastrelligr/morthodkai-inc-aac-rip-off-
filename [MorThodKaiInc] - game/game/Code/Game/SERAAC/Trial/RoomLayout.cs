using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace SERAAC.Trial
{
    public enum Tile { Floor, Decor, Wall, Pillar }
    public enum Dir { North, East, South, West }

    // Pre-made, reusable room layouts (design: "Room Layout เป็นรูปแบบที่สร้างไว้ล่วงหน้า").
    // Interiors are 19x10; the builder adds the outer wall ring and door gaps.
    //   .  floor        ,  floor decoration (walkable)
    //   o  pillar/rubble (solid)
    //   L  loot spawn spot      F  feature spot (chest, portal, shrine, altar...)
    public sealed class RoomLayout
    {
        public const int Cols = 21, Rows = 12;          // including the outer wall
        public const int InnerCols = 19, InnerRows = 10;

        public string Name;
        public Tile[,] Tiles = new Tile[Cols, Rows];
        public List<Point> LootSpots = new();
        public Point Feature;

        // Door tiles in full-room coordinates. W/E doors are two tiles tall.
        public static readonly Dictionary<Dir, Point[]> Doors = new()
        {
            [Dir.North] = new[] { new Point(10, 0) },
            [Dir.South] = new[] { new Point(10, Rows - 1) },
            [Dir.West] = new[] { new Point(0, 5), new Point(0, 6) },
            [Dir.East] = new[] { new Point(Cols - 1, 5), new Point(Cols - 1, 6) },
        };

        // Where the party stands after walking in through a door.
        public static Point EntryTile(Dir from) => from switch
        {
            Dir.North => new Point(10, 1),
            Dir.South => new Point(10, Rows - 2),
            Dir.West => new Point(1, 5),
            _ => new Point(Cols - 2, 5),
        };

        public bool Solid(int x, int y) =>
            x < 0 || y < 0 || x >= Cols || y >= Rows || Tiles[x, y] == Tile.Wall || Tiles[x, y] == Tile.Pillar;

        public static readonly string[][] Templates =
        {
            new[] // Plaza
            {
                "...................",
                ".o.......L.......o.",
                "...,..........F,...",
                "..L...o.....o...L..",
                "...................",
                "...................",
                "..L...o.....o...L..",
                "...,...........,...",
                ".o.......L.......o.",
                "...................",
            },
            new[] // Pillared corridor
            {
                "..o.....o.o.....o..",
                "...................",
                ".L..oo.......oo..L.",
                "....oo.......oo....",
                "...................",
                "...................",
                "....oo.......oo....",
                ".L..oo...L...oo.F..",
                "...................",
                "..o.....o.o.....o..",
            },
            new[] // Rubble
            {
                "...,,.........,,...",
                ".oo.....L.......oo.",
                ".o...,.......,...o.",
                ".....L.......F.....",
                "...................",
                ".........,.........",
                "...L.....,.....L...",
                ".o...,.......,...o.",
                ".oo.......L.....oo.",
                "...,,.........,,...",
            },
            new[] // Ring
            {
                "...................",
                "..ooooooo.ooooooo..",
                "..o.............o..",
                "..o..L...F...L..o..",
                "...................",
                "...................",
                "..o..L.......L..o..",
                "..o.............o..",
                "..ooooooo.ooooooo..",
                "...................",
            },
            new[] // Chapel pews
            {
                "...................",
                ".L.............F.L.",
                "..oooo.......oooo..",
                "...................",
                "...................",
                "...................",
                "..oooo.......oooo..",
                "...................",
                ".L......L.L......L.",
                "...................",
            },
        };

        public static RoomLayout Build(int templateIndex, bool mirror, IEnumerable<Dir> openDoors)
        {
            var src = Templates[templateIndex];
            var layout = new RoomLayout { Name = $"T{templateIndex}{(mirror ? "m" : "")}" };

            for (int x = 0; x < Cols; x++)
                for (int y = 0; y < Rows; y++)
                    layout.Tiles[x, y] = Tile.Wall;

            for (int iy = 0; iy < InnerRows; iy++)
            {
                for (int ix = 0; ix < InnerCols; ix++)
                {
                    char ch = src[iy][mirror ? InnerCols - 1 - ix : ix];
                    int x = ix + 1, y = iy + 1;
                    layout.Tiles[x, y] = ch switch
                    {
                        'o' => Tile.Pillar,
                        ',' => Tile.Decor,
                        _ => Tile.Floor,
                    };
                    if (ch == 'L') layout.LootSpots.Add(new Point(x, y));
                    if (ch == 'F') layout.Feature = new Point(x, y);
                }
            }

            foreach (var d in openDoors)
                foreach (var p in Doors[d])
                    layout.Tiles[p.X, p.Y] = Tile.Floor;

            return layout;
        }

        // Flood fill over walkable tiles; used by the self-test to validate every template.
        public HashSet<Point> Reachable(Point from)
        {
            var seen = new HashSet<Point>();
            var q = new Queue<Point>();
            q.Enqueue(from);
            seen.Add(from);
            while (q.Count > 0)
            {
                var p = q.Dequeue();
                foreach (var n in new[] { new Point(p.X + 1, p.Y), new Point(p.X - 1, p.Y), new Point(p.X, p.Y + 1), new Point(p.X, p.Y - 1) })
                {
                    if (Solid(n.X, n.Y) || seen.Contains(n) || n == Feature) continue;
                    seen.Add(n);
                    q.Enqueue(n);
                }
            }
            return seen;
        }

        public static Point Step(Dir d) => d switch
        {
            Dir.North => new Point(0, -1),
            Dir.South => new Point(0, 1),
            Dir.West => new Point(-1, 0),
            _ => new Point(1, 0),
        };

        public static Dir Opposite(Dir d) => (Dir)(((int)d + 2) % 4);

        public static readonly Dir[] AllDirs = (Dir[])Enum.GetValues(typeof(Dir));
    }
}
