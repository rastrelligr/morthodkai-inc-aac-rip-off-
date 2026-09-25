using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SERAAC.Rendering3D
{
    // No art pipeline yet, so dungeon surfaces get a cheap checker pattern generated
    // at runtime instead of solid colors. Good enough to read depth/scale in 3D.
    public static class ProceduralTextures
    {
        public static Texture2D CreateChecker(GraphicsDevice device, Color a, Color b, int size = 64, int cell = 8)
        {
            var tex = new Texture2D(device, size, size);
            var data = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool even = ((x / cell) + (y / cell)) % 2 == 0;
                    data[y * size + x] = even ? a : b;
                }
            }
            tex.SetData(data);
            return tex;
        }

        public static Texture2D CreateSolid(GraphicsDevice device, Color color)
        {
            var tex = new Texture2D(device, 1, 1);
            tex.SetData(new[] { color });
            return tex;
        }
    }
}
