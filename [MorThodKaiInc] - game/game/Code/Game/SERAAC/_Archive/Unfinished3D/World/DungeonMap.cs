using System;
using System.Collections.Generic;

namespace SERAAC.World
{
    public enum TileType { Wall, Floor }

    public class Room
    {
        public int X, Y, Width, Height;
        public int CenterX => X + Width / 2;
        public int CenterY => Y + Height / 2;
    }

    // A random-path dungeon floor: rooms carved into a wall grid and joined by corridors.
    // Used both as the 3D exploration grid and as the source of truth for wall/floor collision.
    public class DungeonMap
    {
        private readonly TileType[,] _tiles;
        public int Width { get; }
        public int Height { get; }
        public List<Room> Rooms { get; } = new List<Room>();

        public DungeonMap(int width, int height, Random rng, int targetRooms = 6, int minSize = 3, int maxSize = 6)
        {
            Width = width;
            Height = height;
            _tiles = new TileType[width, height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    _tiles[x, y] = TileType.Wall;

            GenerateRooms(rng, targetRooms, minSize, maxSize);
        }

        private void GenerateRooms(Random rng, int targetRooms, int minSize, int maxSize)
        {
            int attempts = targetRooms * 8;
            while (Rooms.Count < targetRooms && attempts-- > 0)
            {
                int w = rng.Next(minSize, maxSize + 1);
                int h = rng.Next(minSize, maxSize + 1);
                int x = rng.Next(1, Math.Max(2, Width - w - 1));
                int y = rng.Next(1, Math.Max(2, Height - h - 1));
                var room = new Room { X = x, Y = y, Width = w, Height = h };

                bool overlaps = false;
                foreach (var r in Rooms)
                {
                    if (x < r.X + r.Width + 1 && x + w + 1 > r.X && y < r.Y + r.Height + 1 && y + h + 1 > r.Y)
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (overlaps) continue;

                Rooms.Add(room);
                CarveRoom(room);
            }

            for (int i = 1; i < Rooms.Count; i++)
                CarveCorridor(Rooms[i - 1].CenterX, Rooms[i - 1].CenterY, Rooms[i].CenterX, Rooms[i].CenterY);
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
            while (x != x2) { Set(x, y); x += x < x2 ? 1 : -1; }
            while (y != y2) { Set(x, y); y += y < y2 ? 1 : -1; }
            Set(x, y);

            void Set(int cx, int cy) =>
                _tiles[Math.Clamp(cx, 0, Width - 1), Math.Clamp(cy, 0, Height - 1)] = TileType.Floor;
        }

        public TileType GetTile(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return TileType.Wall;
            return _tiles[x, y];
        }

        public bool IsFloor(int x, int y) => GetTile(x, y) == TileType.Floor;

        public (int x, int y) FindStartTile()
        {
            if (Rooms.Count > 0)
            {
                var r0 = Rooms[0];
                if (IsFloor(r0.CenterX, r0.CenterY)) return (r0.CenterX, r0.CenterY);
            }

            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    if (IsFloor(x, y)) return (x, y);

            return (0, 0);
        }
    }
}
