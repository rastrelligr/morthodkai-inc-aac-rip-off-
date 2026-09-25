using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SERAAC.World;

namespace SERAAC.Rendering3D
{
    // One drawable batch: a VertexBuffer/IndexBuffer pair plus how many triangles it holds.
    public class MeshPart
    {
        public VertexBuffer Vertices;
        public IndexBuffer Indices;
        public int PrimitiveCount;

        public void Draw(GraphicsDevice device)
        {
            if (PrimitiveCount <= 0) return;
            device.SetVertexBuffer(Vertices);
            device.Indices = Indices;
            device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, PrimitiveCount);
        }
    }

    public class DungeonMesh
    {
        public MeshPart Floor;
        public MeshPart Ceiling;
        public MeshPart Walls;
    }

    // Turns the grid-based DungeonMap into real 3D geometry: a floor slab, a ceiling slab
    // and a wall quad wherever a floor tile borders a wall tile. Built once per floor.
    public static class DungeonMeshBuilder
    {
        public const float TileSize = 2f;
        public const float WallHeight = 2.6f;

        public static DungeonMesh Build(GraphicsDevice device, DungeonMap map)
        {
            var floorVerts = new List<VertexPositionNormalTexture>();
            var floorIdx = new List<int>();
            var ceilVerts = new List<VertexPositionNormalTexture>();
            var ceilIdx = new List<int>();
            var wallVerts = new List<VertexPositionNormalTexture>();
            var wallIdx = new List<int>();

            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    if (!map.IsFloor(x, y)) continue;

                    float wx = x * TileSize;
                    float wz = y * TileSize;

                    AddQuad(floorVerts, floorIdx,
                        new Vector3(wx, 0, wz), new Vector3(wx + TileSize, 0, wz),
                        new Vector3(wx + TileSize, 0, wz + TileSize), new Vector3(wx, 0, wz + TileSize),
                        Vector3.Up);

                    AddQuad(ceilVerts, ceilIdx,
                        new Vector3(wx, WallHeight, wz + TileSize), new Vector3(wx + TileSize, WallHeight, wz + TileSize),
                        new Vector3(wx + TileSize, WallHeight, wz), new Vector3(wx, WallHeight, wz),
                        Vector3.Down);

                    // North (-Z)
                    if (!map.IsFloor(x, y - 1))
                        AddQuad(wallVerts, wallIdx,
                            new Vector3(wx, WallHeight, wz), new Vector3(wx + TileSize, WallHeight, wz),
                            new Vector3(wx + TileSize, 0, wz), new Vector3(wx, 0, wz),
                            new Vector3(0, 0, 1));

                    // South (+Z)
                    if (!map.IsFloor(x, y + 1))
                        AddQuad(wallVerts, wallIdx,
                            new Vector3(wx + TileSize, WallHeight, wz + TileSize), new Vector3(wx, WallHeight, wz + TileSize),
                            new Vector3(wx, 0, wz + TileSize), new Vector3(wx + TileSize, 0, wz + TileSize),
                            new Vector3(0, 0, -1));

                    // West (-X)
                    if (!map.IsFloor(x - 1, y))
                        AddQuad(wallVerts, wallIdx,
                            new Vector3(wx, WallHeight, wz + TileSize), new Vector3(wx, WallHeight, wz),
                            new Vector3(wx, 0, wz), new Vector3(wx, 0, wz + TileSize),
                            new Vector3(1, 0, 0));

                    // East (+X)
                    if (!map.IsFloor(x + 1, y))
                        AddQuad(wallVerts, wallIdx,
                            new Vector3(wx + TileSize, WallHeight, wz), new Vector3(wx + TileSize, WallHeight, wz + TileSize),
                            new Vector3(wx + TileSize, 0, wz + TileSize), new Vector3(wx + TileSize, 0, wz),
                            new Vector3(-1, 0, 0));
                }
            }

            return new DungeonMesh
            {
                Floor = BuildPart(device, floorVerts, floorIdx),
                Ceiling = BuildPart(device, ceilVerts, ceilIdx),
                Walls = BuildPart(device, wallVerts, wallIdx)
            };
        }

        // Quad given in CCW winding (a,b,c,d) as seen from the side the normal points to.
        private static void AddQuad(List<VertexPositionNormalTexture> verts, List<int> idx,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            int baseIndex = verts.Count;
            verts.Add(new VertexPositionNormalTexture(a, normal, new Vector2(0, 1)));
            verts.Add(new VertexPositionNormalTexture(b, normal, new Vector2(1, 1)));
            verts.Add(new VertexPositionNormalTexture(c, normal, new Vector2(1, 0)));
            verts.Add(new VertexPositionNormalTexture(d, normal, new Vector2(0, 0)));

            idx.Add(baseIndex + 0); idx.Add(baseIndex + 1); idx.Add(baseIndex + 2);
            idx.Add(baseIndex + 0); idx.Add(baseIndex + 2); idx.Add(baseIndex + 3);
        }

        private static MeshPart BuildPart(GraphicsDevice device, List<VertexPositionNormalTexture> verts, List<int> idx)
        {
            if (verts.Count == 0) return new MeshPart { PrimitiveCount = 0 };

            var vb = new VertexBuffer(device, typeof(VertexPositionNormalTexture), verts.Count, BufferUsage.WriteOnly);
            vb.SetData(verts.ToArray());

            var ib = new IndexBuffer(device, IndexElementSize.ThirtyTwoBits, idx.Count, BufferUsage.WriteOnly);
            ib.SetData(idx.ToArray());

            return new MeshPart { Vertices = vb, Indices = ib, PrimitiveCount = idx.Count / 3 };
        }
    }
}
