using System;
using System.Collections.Generic;

namespace SERAAC.Dungeon
{
    public enum TileType { Wall, Floor }

    public class Room
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int CenterX => X + Width / 2;
        public int CenterY => Y + Height / 2;
    }

    public class DungeonMap
    {
        private TileType[,] _tiles;
        public int Width { get; }
        public int Height { get; }
        private Random _rand = new Random();
        public List<Room> Rooms { get; } = new List<Room>();

        public DungeonMap(int width, int height)
        {
            Width = width;
            Height = height;
            _tiles = new TileType[width, height];
            // initialize walls
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    _tiles[x, y] = TileType.Wall;
        }

        // Generate simple rooms + corridors
        public void GenerateRooms(int targetRooms, int minSize = 3, int maxSize = 6)
        {
            Rooms.Clear();
            int attempts = targetRooms * 5;
            while (Rooms.Count < targetRooms && attempts-- > 0)
            {
                int w = _rand.Next(minSize, maxSize + 1);
                int h = _rand.Next(minSize, maxSize + 1);
                int x = _rand.Next(1, Width - w - 1);
                int y = _rand.Next(1, Height - h - 1);
                var newRoom = new Room { X = x, Y = y, Width = w, Height = h };
                bool overlaps = false;
                foreach (var r in Rooms)
                {
                    if (x < r.X + r.Width + 1 && x + w + 1 > r.X && y < r.Y + r.Height + 1 && y + h + 1 > r.Y)
                    {
                        overlaps = true; break;
                    }
                }
                if (!overlaps)
                {
                    Rooms.Add(newRoom);
                    CarveRoom(newRoom);
                }
            }

            // connect rooms with corridors between centers
            for (int i = 1; i < Rooms.Count; i++)
            {
                var a = Rooms[i - 1];
                var b = Rooms[i];
                CarveCorridor(a.CenterX, a.CenterY, b.CenterX, b.CenterY);
            }
        }

        private void CarveRoom(Room r)
        {
            for (int x = r.X; x < r.X + r.Width; x++)
                for (int y = r.Y; y < r.Y + r.Height; y++)
                    _tiles[x, y] = TileType.Floor;
        }

        private void CarveCorridor(int x1, int y1, int x2, int y2)
        {
            int x = x1, y = y1;
            while (x != x2)
            {
                _tiles[Math.Max(0, Math.Min(Width - 1, x)), Math.Max(0, Math.Min(Height - 1, y))] = TileType.Floor;
                x += x < x2 ? 1 : -1;
            }
            while (y != y2)
            {
                _tiles[Math.Max(0, Math.Min(Width - 1, x)), Math.Max(0, Math.Min(Height - 1, y))] = TileType.Floor;
                y += y < y2 ? 1 : -1;
            }
        }

        public TileType GetTile(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return TileType.Wall;
            return _tiles[x, y];
        }

        public bool IsFloor(int x, int y) => GetTile(x, y) == TileType.Floor;
    }
}
