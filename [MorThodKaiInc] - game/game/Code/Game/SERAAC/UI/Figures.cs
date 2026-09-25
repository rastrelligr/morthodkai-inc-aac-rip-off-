using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SERAAC.Combat;
using SERAAC.Core;
using SERAAC.Data;

namespace SERAAC.UI
{
    // Shared drawing for characters, enemies and combat UI bits. Each method draws the real art
    // when a file for its art id exists in Art/, otherwise a readable placeholder.
    public static class Figures
    {
        // Finds the sprite set for a unit: its own sheet, else the shared fallback (recoloured by
        // `color` when recolor is set, which is how every monster type gets its own look).
        public static SpriteSet ResolveSprite(string artId, string fallbackSprite, Color color)
        {
            var set = Sprites.Get(artId);
            if (set != null || fallbackSprite == null) return set;
            return Sprites.Recolored(fallbackSprite, color);
        }

        // A standing figure whose feet sit on bounds.Bottom, bounds.Height tall (for the reference
        // pose; attack poses can be bigger). Enemies face left, the party faces right.
        public static void Unit(SpriteBatch sb, string artId, string glyph, Rectangle bounds, Color color, bool isEnemy,
                                double time, float alpha = 1f, float flash = 0f, bool bob = true,
                                string pose = null, string fallbackSprite = null)
        {
            float bobY = bob ? (float)Math.Sin(time * 2.2 + bounds.X * 0.05) * 2f : 0f;
            var r = new Rectangle(bounds.X, bounds.Y + (int)bobY, bounds.Width, bounds.Height);

            // Ground shadow.
            Draw.Rect(sb, new Rectangle(r.X + r.Width / 8, bounds.Bottom - 4, r.Width * 3 / 4, 8), Color.Black * 0.35f * alpha);

            var tint = Draw.Lerp(Color.White, Color.Red, flash * 0.7f) * alpha;
            var sprite = ResolveSprite(artId, fallbackSprite, color);
            if (sprite != null)
            {
                Sprites.Draw(sb, sprite, pose, new Vector2(r.Center.X, r.Bottom), r.Height, tint, flip: isEnemy);
                return;
            }
            if (Draw.Art(sb, artId, r, time, tint, flip: isEnemy)) return;

            var body = Draw.Lerp(color, Color.White, flash) * alpha;
            var dark = Draw.Lerp(color, Color.Black, 0.45f) * alpha;

            int headSize = (int)(r.Width * 0.46f);
            var head = new Rectangle(r.Center.X - headSize / 2, r.Y, headSize, headSize);
            var torso = new Rectangle(r.X + r.Width / 8, r.Y + headSize + 2, r.Width * 3 / 4, r.Height - headSize - 2);

            Draw.Rect(sb, torso, dark);
            Draw.Rect(sb, new Rectangle(torso.X + 3, torso.Y + 3, torso.Width - 6, torso.Height - 6), body);
            Draw.Rect(sb, head, dark);
            Draw.Rect(sb, new Rectangle(head.X + 3, head.Y + 3, head.Width - 6, head.Height - 6), body);

            // Eyes on the side they face.
            int eyeY = head.Y + head.Height / 2 - 2;
            int dir = isEnemy ? -1 : 1;
            var eye = isEnemy ? new Color(255, 70, 60) : new Color(30, 24, 30);
            int ex = head.Center.X + dir * head.Width / 6;
            Draw.Rect(sb, new Rectangle(ex - 2, eyeY, 4, 4), eye * alpha);
            Draw.Rect(sb, new Rectangle(ex + dir * 8 - 2, eyeY, 4, 4), eye * alpha);

            if (!string.IsNullOrEmpty(glyph))
                Draw.TextCentered(sb, Assets.Header, glyph, new Vector2(torso.Center.X, torso.Center.Y), Color.Black * 0.45f * alpha, shadow: false);
        }

        // Small portrait: portrait_<id> art, else the sheet's "portrait" pose, else a placeholder figure.
        public static void Portrait(SpriteBatch sb, CharacterDef c, Rectangle box, double time, float alpha = 1f)
        {
            var own = Sprites.Get(c.PortraitId);
            if (own != null) { Sprites.DrawFit(sb, own, null, box, Color.White * alpha); return; }
            var set = Sprites.Get(c.ArtId);
            if (set != null) { Sprites.DrawFit(sb, set, set.Has("portrait") ? "portrait" : null, box, Color.White * alpha); return; }
            Unit(sb, c.PortraitId, c.Role.Substring(0, 1), box, c.Color, false, time, alpha, bob: false);
        }

        // Rank pips laid out the way the battlefield reads: party front is on the right,
        // enemy front is on the left.
        public static void RankPips(SpriteBatch sb, Ranks ranks, Vector2 pos, bool enemySide, Color on)
        {
            for (int i = 0; i < 4; i++)
            {
                int slot = enemySide ? i : 3 - i;
                var c = ranks.Has(slot) ? on : new Color(60, 54, 66);
                Draw.Diamond(sb, new Vector2(pos.X + i * 13 + 5, pos.Y + 6), 8, c);
            }
        }

        public static Color StatusColor(StatusKind k) => k switch
        {
            StatusKind.Guard => Palette.Guard,
            StatusKind.Taunt => new Color(230, 180, 80),
            StatusKind.Blight => Palette.Blight,
            _ => Palette.Mark,
        };

        public static string StatusLabel(StatusKind k) => k switch
        {
            StatusKind.Guard => "GRD",
            StatusKind.Taunt => "TNT",
            StatusKind.Blight => "BLT",
            _ => "MRK",
        };

        public static void Statuses(SpriteBatch sb, IEnumerable<StatusInstance> statuses, Vector2 pos)
        {
            float x = pos.X;
            foreach (var s in statuses)
            {
                string label = StatusLabel(s.Kind) + (s.Kind == StatusKind.Blight || s.Kind == StatusKind.Mark ? s.Turns.ToString() : "");
                var size = Assets.Small.MeasureString(label);
                var r = new Rectangle((int)x, (int)pos.Y, (int)size.X + 6, 16);
                Draw.Rect(sb, r, StatusColor(s.Kind) * 0.85f);
                Draw.Text(sb, Assets.Small, label, new Vector2(r.X + 3, r.Y + 1), Color.Black, shadow: false);
                x += r.Width + 3;
            }
        }

        public static void HpBar(SpriteBatch sb, Rectangle r, int hp, int max, bool isEnemy, bool showNumbers = true)
        {
            Draw.Bar(sb, r, max > 0 ? (float)hp / max : 0, isEnemy ? new Color(150, 40, 44) : new Color(52, 130, 72));
            if (showNumbers)
                Draw.TextCentered(sb, Assets.Small, $"{hp}/{max}", new Vector2(r.Center.X, r.Center.Y - 1), Color.White);
        }
    }
}
