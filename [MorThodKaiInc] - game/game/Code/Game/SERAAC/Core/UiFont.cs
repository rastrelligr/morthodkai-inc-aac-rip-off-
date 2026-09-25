using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace SERAAC.Core
{
    // One UI font built at several sizes (Content/Fonts/<name>, <name>_15, _20, _30 = 1x, 1.5x, 2x, 3x).
    // Screens lay text out in 1280x720 units; drawing picks the variant that matches the real
    // screen scale (Core.Draw.Scale), so text is rendered at native resolution instead of stretched.
    public sealed class UiFont
    {
        private static readonly (string suffix, float scale)[] Variants = { ("", 1f), ("_15", 1.5f), ("_20", 2f), ("_30", 3f) };

        private (SpriteFont font, float scale)[] _fonts;

        public static UiFont Load(ContentManager content, string name) => new()
        {
            _fonts = Variants.Select(v => (content.Load<SpriteFont>(name + v.suffix), v.scale)).ToArray(),
        };

        // Exact match for the common resolutions (720p/1080p/1440p/4K), otherwise the next size up (drawn slightly smaller).
        private (SpriteFont font, float scale) Current
        {
            get
            {
                float s = Core.Draw.Scale;
                foreach (var f in _fonts)
                    if (f.scale >= s - 0.01f) return f;
                return _fonts[^1];
            }
        }

        // Sizes are in 1280x720 layout units, whatever the screen resolution.
        public Vector2 MeasureString(string text)
        {
            var (font, scale) = Current;
            return font.MeasureString(text) / scale;
        }

        public float LineSpacing
        {
            get
            {
                var (font, scale) = Current;
                return font.LineSpacing / scale;
            }
        }

        public void Draw(SpriteBatch sb, string text, Vector2 pos, Color color)
        {
            var (font, scale) = Current;
            sb.DrawString(font, text, Core.Draw.Snap(pos), color, 0f, Vector2.Zero, 1f / scale, SpriteEffects.None, 0f);
        }
    }
}
