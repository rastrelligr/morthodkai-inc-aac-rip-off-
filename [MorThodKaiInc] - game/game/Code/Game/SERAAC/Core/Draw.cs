using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SERAAC.Core
{
    public static class Palette
    {
        public static readonly Color Background = new Color(14, 12, 18);
        public static readonly Color Panel = new Color(60, 58, 66);
        public static readonly Color PanelLight = new Color(97, 95, 104);
        public static readonly Color Border = new Color(234, 79, 89);
        public static readonly Color Text = new Color(230, 222, 210);
        public static readonly Color TextDim = new Color(140, 132, 150);
        public static readonly Color TextDisabled = new Color(86, 80, 94);
        public static readonly Color Accent = new Color(255, 191, 199);   // gold: selection, secured loot
        public static readonly Color Danger = new Color(200, 60, 64);
        public static readonly Color Heal = new Color(131, 255, 3);
        public static readonly Color Unsecured = new Color(232, 128, 60); // trial loot at risk
        public static readonly Color Blight = new Color(140, 190, 70);
        public static readonly Color Mark = new Color(230, 90, 170);
        public static readonly Color Guard = new Color(110, 160, 230);
    }

    // Small drawing vocabulary on top of a 1x1 white pixel. Everything is laid out in 1280x720 units;
    // Game1 draws with a scale transform so the frame renders at the screen's real resolution.
    public static class Draw
    {
        // Screen pixels per layout unit this frame (1 at 1280x720, 1.5 at 1080p, 2 at 1440p...).
        public static float Scale = 1f;

        // Rounds a layout position to a whole screen pixel so textures drawn 1:1 stay sharp.
        public static Vector2 Snap(Vector2 p) => new((float)Math.Round(p.X * Scale) / Scale, (float)Math.Round(p.Y * Scale) / Scale);

        public static void Rect(SpriteBatch sb, Rectangle r, Color c) => sb.Draw(Assets.Pixel, r, c);

        public static void Rect(SpriteBatch sb, float x, float y, float w, float h, Color c)
            => sb.Draw(Assets.Pixel, new Rectangle((int)x, (int)y, (int)w, (int)h), c);

        public static void Border(SpriteBatch sb, Rectangle r, Color c, int t = 2)
        {
            Rect(sb, new Rectangle(r.X, r.Y, r.Width, t), c);
            Rect(sb, new Rectangle(r.X, r.Bottom - t, r.Width, t), c);
            Rect(sb, new Rectangle(r.X, r.Y, t, r.Height), c);
            Rect(sb, new Rectangle(r.Right - t, r.Y, t, r.Height), c);
        }

        public static void Panel(SpriteBatch sb, Rectangle r, Color? fill = null, Color? border = null)
        {
            Rect(sb, r, fill ?? Palette.Panel);
            Border(sb, r, border ?? Palette.Border);
        }

        public static void Line(SpriteBatch sb, Vector2 a, Vector2 b, Color c, float thickness = 2f)
        {
            var d = b - a;
            float len = d.Length();
            if (len < 0.01f) return;
            sb.Draw(Assets.Pixel, a, null, c, (float)Math.Atan2(d.Y, d.X), new Vector2(0, 0.5f),
                new Vector2(len, thickness), SpriteEffects.None, 0);
        }

        public static void Text(SpriteBatch sb, UiFont f, string s, Vector2 pos, Color c, bool shadow = true)
        {
            pos = Snap(pos);
            if (shadow) f.Draw(sb, s, pos + new Vector2(2, 2), Color.Black * (c.A / 255f) * 0.7f);
            f.Draw(sb, s, pos, c);
        }

        public static void TextCentered(SpriteBatch sb, UiFont f, string s, Vector2 center, Color c, bool shadow = true)
        {
            var size = f.MeasureString(s);
            Text(sb, f, s, center - size / 2f, c, shadow);
        }

        public static void TextRight(SpriteBatch sb, UiFont f, string s, Vector2 topRight, Color c, bool shadow = true)
        {
            var size = f.MeasureString(s);
            Text(sb, f, s, new Vector2(topRight.X - size.X, topRight.Y), c, shadow);
        }

        // Word-wraps into maxWidth. Returns the height used.
        public static float Wrapped(SpriteBatch sb, UiFont f, string s, Vector2 pos, float maxWidth, Color c)
        {
            float y = pos.Y;
            float lineH = f.LineSpacing;
            foreach (var paragraph in s.Split('\n'))
            {
                string line = "";
                foreach (var word in paragraph.Split(' '))
                {
                    string test = line.Length == 0 ? word : line + " " + word;
                    if (f.MeasureString(test).X > maxWidth && line.Length > 0)
                    {
                        Text(sb, f, line, new Vector2(pos.X, y), c);
                        y += lineH;
                        line = word;
                    }
                    else line = test;
                }
                Text(sb, f, line, new Vector2(pos.X, y), c);
                y += lineH;
            }
            return y - pos.Y;
        }

        public static void Bar(SpriteBatch sb, Rectangle r, float t, Color fill, Color? back = null)
        {
            t = MathHelper.Clamp(t, 0, 1);
            Rect(sb, r, back ?? new Color(20, 16, 22));
            Rect(sb, new Rectangle(r.X, r.Y, (int)(r.Width * t), r.Height), fill);
            Border(sb, r, Color.Black * 0.8f, 1);
        }

        // Filled diamond, used for rank pips and map markers.
        public static void Diamond(SpriteBatch sb, Vector2 center, float size, Color c)
        {
            sb.Draw(Assets.Pixel, center, null, c, MathHelper.PiOver4, new Vector2(0.5f, 0.5f),
                new Vector2(size, size), SpriteEffects.None, 0);
        }

        // Draws art if present and returns true; callers draw their placeholder when false.
        public static bool Art(SpriteBatch sb, string id, Rectangle dest, double time, Color? tint = null, bool flip = false)
        {
            var art = Assets.Art(id);
            if (art == null) return false;
            sb.Draw(art.Texture, dest, art.Source(time), tint ?? Color.White, 0, Vector2.Zero,
                flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0);
            return true;
        }

        public static Color Lerp(Color a, Color b, float t) => Color.Lerp(a, b, MathHelper.Clamp(t, 0, 1));
    }
}
