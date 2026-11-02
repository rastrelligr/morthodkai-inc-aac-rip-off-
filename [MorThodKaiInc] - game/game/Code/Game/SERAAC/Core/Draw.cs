using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;

namespace SERAAC.Core
{
    // UI colours. The swatches are sampled from Art/Palette.png; every UI colour below is one of
    // them (or a swatch darkened/faded). Recolour the UI by changing the swatches.
    public static class Palette
    {
        // ---- Swatches (Art/Palette.png, left to right, top to bottom)
        public static readonly Color Grey500 = new Color(123, 121, 133);
        public static readonly Color Grey700 = new Color(97, 95, 104);
        public static readonly Color Grey900 = new Color(59, 57, 65);
        public static readonly Color Ink = new Color(0, 0, 0);
        public static readonly Color Paper = new Color(255, 255, 255);
        public static readonly Color Blush = new Color(255, 190, 198);
        public static readonly Color Coral = new Color(234, 79, 89);
        public static readonly Color Rose = new Color(234, 43, 119);
        public static readonly Color Lilac100 = new Color(213, 203, 246);
        public static readonly Color Lilac300 = new Color(190, 175, 243);
        public static readonly Color Violet = new Color(140, 110, 243);

        // ---- Roles (the battle UI's look, used by every screen)
        public static readonly Color Background = Color.Lerp(Ink, Grey900, 0.35f);
        public static readonly Color Panel = Color.Lerp(Ink, Grey900, 0.25f);     // near-black panels
        public static readonly Color PanelDark = Grey900;                         // cards, wells and buttons inside panels
        public static readonly Color PanelLight = Grey700;                        // the selected row / card
        public static readonly Color Hover = Color.Lerp(Grey900, Grey700, 0.5f);  // mouse-over row
        public static readonly Color Border = Grey700;                            // thin panel outlines
        public static readonly Color Selected = Paper;                            // outline of the selected thing
        public static readonly Color Text = Paper;
        public static readonly Color TextDim = Lilac300;
        public static readonly Color TextDisabled = Grey500;
        public static readonly Color Accent = Blush;           // selection, secured loot
        public static readonly Color Danger = Coral;
        public static readonly Color Heal = Lilac100;          // healing, good news
        public static readonly Color Unsecured = Rose;         // trial loot at risk
        public static readonly Color Blight = Rose;
        public static readonly Color Mark = Blush;
        public static readonly Color Guard = Violet;           // guard, SP, equipment
        public static readonly Color Taunt = Coral;
        public static readonly Color PartyHp = Coral;
        public static readonly Color EnemyHp = Rose;
        public static readonly Color PartyTag = Color.Lerp(Ink, Violet, 0.35f);  // party chips (turn order, plans)
        public static readonly Color EnemyTag = Color.Lerp(Ink, Rose, 0.35f);    // enemy chips
        public static readonly Color EnemyText = Blush;

        // ---- Added for the menu look (dark, thin-lined, glowing), where no swatch fits
        public static readonly Color Line = Paper * 0.22f;                       // thin panel / row outlines
        public static readonly Color LineBright = Paper * 0.6f;                  // outline of the focused panel / selected row
        public static readonly Color Crimson = Color.Lerp(Ink, Rose, 0.45f);     // the dark end of accent gradients
        public static readonly Color Gold = new Color(236, 206, 140);            // victory banners, ornaments
        public static readonly Color Ember = new Color(255, 138, 56);            // drifting sparks in menu backdrops

        // ---- Keyword colours: game terms highlighted inside descriptions and tooltips (UI/RichText.cs).
        // Outside the swatches on purpose: each status needs its own hue to be told apart at a glance.
        public static readonly Color KwDamage = Coral;                          // "5 dmg", hits
        public static readonly Color KwBlight = new Color(168, 222, 74);        // poison green
        public static readonly Color KwMark = new Color(255, 166, 72);          // target orange
        public static readonly Color KwSlow = new Color(110, 196, 255);         // ice blue
        public static readonly Color KwGuard = new Color(166, 146, 255);        // Violet, lifted for text
        public static readonly Color KwHeal = new Color(96, 226, 196);          // mint
        public static readonly Color KwStack = Rose;                            // Feast, Thread, Woven: character resources
        public static readonly Color KwSp = Lilac300;                           // SP / Ethergy
        public static readonly Color KwPriority = new Color(255, 214, 96);      // "Acts first", interrupts, wind-ups
        public static readonly Color KwPosition = Lilac100;                     // Front 1, back row
        public static readonly Color KwMove = Blush;                            // push, pull, swap
        public static readonly Color KwNumber = Paper;                          // +35%, x1.5
    }

    // Small drawing vocabulary on top of a 1x1 white pixel. Everything is laid out in 1280x720 units;
    // Game1 draws with a scale transform so the frame renders at the screen's real resolution.
    public static class Draw
    {
        // Screen pixels per layout unit this frame (1 at 1280x720, 1.5 at 1080p, 2 at 1440p...).
        public static float Scale = 1f;
        // Where the 1280x720 frame sits on the real back buffer (black bars), set by Game1 each frame.
        public static Vector2 Offset;
        // The SpriteBatch transform Game1 uses for the frame (screens that restart the batch reuse it).
        public static Matrix FrameTransform = Matrix.Identity;

        // Real screen pixel -> layout unit.
        public static Vector2 ToLayout(Point screen) => (screen.ToVector2() - Offset) / Scale;

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

        // A plain panel: translucent black with a thin light outline. The focused one (border == Palette.Selected)
        // gets a brighter outline and a faint accent glow from its left edge; another border colour tints the outline.
        public static void Panel(SpriteBatch sb, Rectangle r, Color? fill = null, Color? border = null)
        {
            Rect(sb, r, (fill ?? Palette.Panel) * 0.82f);
            bool focused = border == Palette.Selected;
            if (focused) GradientH(sb, r, Palette.Crimson * 0.55f);
            var line = focused ? Palette.LineBright : border is { } b && b != Palette.Border ? b * 0.7f : Palette.Line;
            Border(sb, r, line, 1);
        }

        // Lines, circles and ellipses come from MonoGame.Extended's ShapeExtensions.
        public static void Line(SpriteBatch sb, Vector2 a, Vector2 b, Color c, float thickness = 2f)
        {
            if (Vector2.DistanceSquared(a, b) < 0.0001f) return;
            sb.DrawLine(a, b, c, thickness);
        }

        // Outline circle. More sides = smoother; 32 is plenty at UI sizes.
        public static void Circle(SpriteBatch sb, Vector2 center, float radius, Color c, float thickness = 2f, int sides = 32)
        {
            if (radius <= 0.5f) return;
            sb.DrawCircle(center, radius, sides, c, thickness);
        }

        // Outline ellipse (radius.X across, radius.Y tall), e.g. rings lying flat on the floor.
        public static void Ellipse(SpriteBatch sb, Vector2 center, Vector2 radius, Color c, float thickness = 2f, int sides = 32)
        {
            if (radius.X <= 0.5f || radius.Y <= 0.5f) return;
            sb.DrawEllipse(center, radius, sides, c, thickness);
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
            Rect(sb, r, back ?? Palette.PanelDark);
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

        // ------------------------------------------------------------------ slanted shapes (the UI's look)

        // Half of a square, filled below its "/" diagonal (u + v >= 1). Stretched to give clean slanted edges.
        private static Texture2D _wedge;
        private static Texture2D Wedge
        {
            get
            {
                if (_wedge != null) return _wedge;
                const int n = 128;
                var data = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        // Coverage of the pixel across the diagonal, for a soft (not stair-stepped) edge.
                        float d = (x + 0.5f) / n + (y + 0.5f) / n - 1f;
                        float a = MathHelper.Clamp(d * n / 1.4f + 0.5f, 0, 1);
                        data[y * n + x] = Color.White * a;
                    }
                _wedge = new Texture2D(Assets.Pixel.GraphicsDevice, n, n);
                _wedge.SetData(data);
                return _wedge;
            }
        }

        // How much of every slant is kept: 0 = square edges (the current look), 1 = the original slanted look.
        public static float SlantFactor = 0f;

        // A parallelogram leaning right ("/"): the top edge starts `slant` units further right than the bottom
        // edge. Negative slant leans left. r is the bounding box. Linear filtering keeps the edges smooth.
        public static void Slant(SpriteBatch sb, Rectangle r, int slant, Color c)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            slant = (int)(slant * SlantFactor);
            int s = Math.Clamp(Math.Abs(slant), 0, r.Width / 2);
            if (s == 0) { Rect(sb, r, c); return; }
            Rect(sb, new Rectangle(r.X + s, r.Y, r.Width - 2 * s, r.Height), c);
            if (slant > 0)
            {
                sb.Draw(Wedge, new Rectangle(r.X, r.Y, s, r.Height), null, c, 0, Vector2.Zero, SpriteEffects.None, 0);
                sb.Draw(Wedge, new Rectangle(r.Right - s, r.Y, s, r.Height), null, c, 0, Vector2.Zero,
                    SpriteEffects.FlipHorizontally | SpriteEffects.FlipVertically, 0);
            }
            else
            {
                sb.Draw(Wedge, new Rectangle(r.X, r.Y, s, r.Height), null, c, 0, Vector2.Zero, SpriteEffects.FlipVertically, 0);
                sb.Draw(Wedge, new Rectangle(r.Right - s, r.Y, s, r.Height), null, c, 0, Vector2.Zero, SpriteEffects.FlipHorizontally, 0);
            }
        }

        // Outline of the same parallelogram.
        public static void SlantBorder(SpriteBatch sb, Rectangle r, int slant, Color c, float t = 1.5f)
        {
            int s = Math.Clamp((int)(slant * SlantFactor), -r.Width / 2, r.Width / 2);
            var tl = new Vector2(r.X + Math.Max(0, s), r.Y);
            var tr = new Vector2(r.Right - Math.Max(0, -s), r.Y);
            var br = new Vector2(r.Right - Math.Max(0, s), r.Bottom);
            var bl = new Vector2(r.X + Math.Max(0, -s), r.Bottom);
            Line(sb, tl, tr, c, t); Line(sb, tr, br, c, t); Line(sb, br, bl, c, t); Line(sb, bl, tl, c, t);
        }

        // The UI's panel (descriptions, info boxes): a translucent dark box, thin outline, and an accent line
        // along its top that fades out to the right.
        public static void SlabPanel(SpriteBatch sb, Rectangle r, Color? accent = null, int slant = 14, float alpha = 0.9f)
        {
            Rect(sb, r, Palette.Panel * alpha * 0.9f);
            Border(sb, r, Palette.Line, 1);
            GradientH(sb, new Rectangle(r.X, r.Y, r.Width, 2), (accent ?? Palette.Rose) * 0.9f);
        }

        // Selection / highlight bar: an accent band that fades from its left edge, inside a bright thin outline.
        public static void HighlightBar(SpriteBatch sb, Rectangle r, Color c, int slant = 10, float alpha = 1f)
        {
            Rect(sb, r, Palette.Ink * 0.55f * alpha);
            GradientH(sb, r, Color.Lerp(c, Palette.Ink, 0.35f) * alpha, 0.15f);
            Border(sb, r, Palette.LineBright * alpha, 1);
        }

        // Text with a letter-spaced, all-caps look for labels ("TARGET", "TURN ORDER").
        public static void Label(SpriteBatch sb, string s, Vector2 pos, Color c, UiFont f = null)
            => Text(sb, f ?? Assets.Small, s.ToUpperInvariant(), pos, c, shadow: false);

        // ------------------------------------------------------------------ gradients, glows, ornaments

        private static Texture2D _ramp, _glow;

        // 512x1, opaque on the left fading to clear on the right (smoothstep).
        private static Texture2D Ramp
        {
            get
            {
                if (_ramp != null) return _ramp;
                const int n = 512;
                var data = new Color[n];
                for (int x = 0; x < n; x++)
                {
                    float u = 1 - x / (n - 1f);
                    data[x] = Color.White * (u * u * (3 - 2 * u));
                }
                _ramp = new Texture2D(Assets.Pixel.GraphicsDevice, n, 1);
                _ramp.SetData(data);
                return _ramp;
            }
        }

        // A soft elliptical blob (bright centre, clear edges) for glows and lines of light.
        private static Texture2D Glow
        {
            get
            {
                if (_glow != null) return _glow;
                const int w = 256, h = 64;
                var data = new Color[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float dx = (x + 0.5f) / w * 2 - 1, dy = (y + 0.5f) / h * 2 - 1;
                        float d = MathHelper.Clamp(1 - (dx * dx + dy * dy), 0, 1);
                        data[y * w + x] = Color.White * (d * d);
                    }
                _glow = new Texture2D(Assets.Pixel.GraphicsDevice, w, h);
                _glow.SetData(data);
                return _glow;
            }
        }

        // Colour `c` at the left edge fading to clear at the right (or the reverse). `hold` keeps it solid
        // for that share of the width before the fade starts.
        public static void GradientH(SpriteBatch sb, Rectangle r, Color c, float hold = 0f, bool fromRight = false)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            int solid = (int)(r.Width * MathHelper.Clamp(hold, 0, 1));
            if (solid > 0) Rect(sb, new Rectangle(fromRight ? r.Right - solid : r.X, r.Y, solid, r.Height), c);
            var fade = new Rectangle(fromRight ? r.X : r.X + solid, r.Y, r.Width - solid, r.Height);
            sb.Draw(Ramp, fade, null, c, 0, Vector2.Zero, fromRight ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0);
        }

        // Colour `c` at the top fading to clear at the bottom (or the reverse).
        public static void GradientV(SpriteBatch sb, Rectangle r, Color c, bool fromBottom = false)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            // The ramp turned a quarter turn clockwise: its left end lands on the top edge.
            sb.Draw(Ramp, new Rectangle(r.Right, r.Y, r.Height, r.Width), null, c, MathHelper.PiOver2, Vector2.Zero,
                fromBottom ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0);
        }

        // A soft glow centred on `center`, `size` across.
        public static void SoftGlow(SpriteBatch sb, Vector2 center, Vector2 size, Color c)
            => sb.Draw(Glow, new Rectangle((int)(center.X - size.X / 2), (int)(center.Y - size.Y / 2), (int)size.X, (int)Math.Max(1, size.Y)), c);

        // A horizontal line of light: a thin bright core that fades out at both ends, over a wide soft halo.
        public static void GlowLine(SpriteBatch sb, Vector2 center, float width, Color c, float halo = 22f)
        {
            if (width <= 1) return;
            SoftGlow(sb, center, new Vector2(width * 0.75f, halo), c * 0.5f);
            SoftGlow(sb, center, new Vector2(width, 4), c);
            SoftGlow(sb, center, new Vector2(width * 0.55f, 2), Color.Lerp(c, Palette.Paper, 0.6f));
        }

        // A thin divider that fades out at both ends.
        public static void Divider(SpriteBatch sb, Vector2 center, float width, Color c, int thickness = 1)
        {
            int half = (int)(width / 2);
            GradientH(sb, new Rectangle((int)center.X - half, (int)center.Y, half, thickness), c, fromRight: true);
            GradientH(sb, new Rectangle((int)center.X, (int)center.Y, half, thickness), c);
        }

        // Outline diamond.
        public static void DiamondOutline(SpriteBatch sb, Vector2 center, float size, Color c, float t = 1f)
        {
            float h = size / 2;
            var top = center - new Vector2(0, h); var right = center + new Vector2(h, 0);
            var bottom = center + new Vector2(0, h); var left = center - new Vector2(h, 0);
            Line(sb, top, right, c, t); Line(sb, right, bottom, c, t); Line(sb, bottom, left, c, t); Line(sb, left, top, c, t);
        }

        // The small crest in front of screen titles and on dividers: a filled diamond in an outlined one,
        // flanked by two small diamonds.
        public static void Ornament(SpriteBatch sb, Vector2 center, float size, Color c)
        {
            DiamondOutline(sb, center, size, c, 1.2f);
            Diamond(sb, center, size * 0.32f, c);
            Diamond(sb, center - new Vector2(size * 0.75f, 0), size * 0.22f, c * 0.8f);
            Diamond(sb, center + new Vector2(size * 0.75f, 0), size * 0.22f, c * 0.8f);
        }

        // The selection cursor, pointing right with its tip at `tip`. Art/ui_pointer.png replaces the drawn one
        // (drawn 32x24, its tip at the middle of the right edge).
        public static void Pointer(SpriteBatch sb, Vector2 tip, double time, float alpha = 1f)
        {
            tip.X += (float)Math.Sin(time * 6) * 2.5f;   // a small nudge forward and back
            if (Art(sb, "ui_pointer", new Rectangle((int)tip.X - 32, (int)tip.Y - 12, 32, 24), time, Color.White * alpha)) return;
            GradientH(sb, new Rectangle((int)tip.X - 32, (int)tip.Y - 1, 24, 2), Palette.Paper * 0.8f * alpha, fromRight: true);
            Diamond(sb, tip + new Vector2(-6, 1.5f), 10, Palette.Ink * 0.6f * alpha);
            Diamond(sb, tip + new Vector2(-7, 0), 10, Palette.Paper * alpha);
            Diamond(sb, tip + new Vector2(-7, 0), 4, Palette.Rose * alpha);
        }

        // Text with extra space between the letters ("G A M E   O V E R"). Returns the width drawn.
        public static float Spaced(SpriteBatch sb, UiFont f, string s, Vector2 pos, Color c, float tracking, float size = 1f, bool shadow = true)
        {
            float x = pos.X;
            foreach (char ch in s)
            {
                string g = ch.ToString();
                if (shadow) f.Draw(sb, g, Snap(new Vector2(x + 2, pos.Y + 2)), Color.Black * (c.A / 255f) * 0.6f, size);
                f.Draw(sb, g, Snap(new Vector2(x, pos.Y)), c, size);
                x += f.MeasureString(g, size).X + tracking;
            }
            return x - pos.X - tracking;
        }

        public static float SpacedWidth(UiFont f, string s, float tracking, float size = 1f)
        {
            float w = 0;
            foreach (char ch in s) w += f.MeasureString(ch.ToString(), size).X + tracking;
            return s.Length == 0 ? 0 : w - tracking;
        }

        public static void SpacedCentered(SpriteBatch sb, UiFont f, string s, Vector2 center, Color c, float tracking, float size = 1f, bool shadow = true)
        {
            float w = SpacedWidth(f, s, tracking, size);
            float h = f.MeasureString("A", size).Y;
            Spaced(sb, f, s, new Vector2(center.X - w / 2, center.Y - h / 2), c, tracking, size, shadow);
        }

        // Text drawn larger than its font size (UiFont picks the sharper variant for it).
        public static void TextSized(SpriteBatch sb, UiFont f, string s, Vector2 pos, Color c, float size, bool shadow = true)
        {
            pos = Snap(pos);
            if (shadow) f.Draw(sb, s, pos + new Vector2(2, 2), Color.Black * (c.A / 255f) * 0.7f, size);
            f.Draw(sb, s, pos, c, size);
        }

        public static void TextSizedCentered(SpriteBatch sb, UiFont f, string s, Vector2 center, Color c, float size, bool shadow = true)
            => TextSized(sb, f, s, center - f.MeasureString(s, size) / 2f, c, size, shadow);

        // Draws what follows scaled around `center` and nudged down by `drop` (a pop-in for boxes):
        // t = 0 starts small and low, t = 1 is normal. Restarts the frame's batch; call EndPop after.
        public static void BeginPop(SpriteBatch sb, Vector2 center, float t, float drop = 24f)
        {
            float s = MathHelper.Lerp(0.92f, 1f, t);
            var m = Matrix.CreateTranslation(-center.X, -center.Y, 0) * Matrix.CreateScale(s, s, 1)
                  * Matrix.CreateTranslation(center.X, center.Y + (1 - t) * drop, 0);
            sb.End();
            sb.Begin(samplerState: SamplerState.PointClamp, transformMatrix: m * FrameTransform);
        }

        public static void EndPop(SpriteBatch sb)
        {
            sb.End();
            sb.Begin(samplerState: SamplerState.PointClamp, transformMatrix: FrameTransform);
        }
    }
}
