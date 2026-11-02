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

            var tint = Draw.Lerp(Color.White, Palette.Coral, flash * 0.8f) * alpha;
            var sprite = (pose == null ? Sprites.Get(artId + "_idle") : null) ?? ResolveSprite(artId, fallbackSprite, color);
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

        // Small portrait: a crop of the character's face art (FaceArt), else portrait_<id> art, else the sheet's
        // "portrait" pose, else a placeholder figure.
        public static void Portrait(SpriteBatch sb, CharacterDef c, Rectangle box, double time, float alpha = 1f)
        {
            if (c.FaceArt != null && Face(c, box) is { } face)
            {
                sb.Draw(face, new Vector2(box.X, box.Y), null, Color.White * alpha, 0f, Vector2.Zero, 1f / Draw.Scale, SpriteEffects.None, 0f);
                return;
            }
            var own = Sprites.Get(c.PortraitId);
            if (own != null) { Sprites.DrawFit(sb, own, null, box, Color.White * alpha); return; }
            var set = Sprites.Get(c.ArtId);
            if (set != null) { Sprites.DrawFit(sb, set, set.Has("portrait") ? "portrait" : null, box, Color.White * alpha); return; }
            Unit(sb, c.PortraitId, c.Role.Substring(0, 1), box, c.Color, false, time, alpha, bob: false);
        }

        // The face crop at the box's real screen size, resampled once from the full picture (so a 2700-pixel portrait
        // shrinks cleanly instead of shimmering). Taller boxes show more of the body.
        private static readonly Dictionary<string, Color[]> _facePixels = new();
        private static readonly Dictionary<(string, int, int), Texture2D> _faces = new();

        private static Texture2D Face(CharacterDef c, Rectangle box)
        {
            int w = Math.Max(1, (int)Math.Round(box.Width * Draw.Scale)), h = Math.Max(1, (int)Math.Round(box.Height * Draw.Scale));
            var key = (c.FaceArt, w, h);
            if (_faces.TryGetValue(key, out var cached)) return cached;
            var art = Assets.Art(c.FaceArt);
            if (art == null) return null;
            var tex = art.Texture;
            if (!_facePixels.TryGetValue(c.FaceArt, out var px))
            {
                px = new Color[tex.Width * tex.Height];
                tex.GetData(px);
                _facePixels[c.FaceArt] = px;
            }
            float tall = MathHelper.Clamp(box.Height / (float)Math.Max(1, box.Width), 1f, 2f);
            int ch = Math.Min(tex.Height, (int)(tex.Height * c.FaceHeight * tall));
            int cw = Math.Min(tex.Width, (int)(ch * box.Width / (float)box.Height));
            ch = (int)(cw * box.Height / (float)box.Width);
            int cx = (int)MathHelper.Clamp(tex.Width * c.FaceCenter.X - cw / 2f, 0, tex.Width - cw);
            int cy = (int)MathHelper.Clamp(tex.Height * c.FaceCenter.Y - ch * 0.4f, 0, Math.Max(0, tex.Height - ch));
            var crop = new Color[cw * ch];
            for (int y = 0; y < ch; y++) Array.Copy(px, (cy + y) * tex.Width + cx, crop, y * cw, cw);
            var face = new Texture2D(tex.GraphicsDevice, w, h);
            face.SetData(Sprites.Resample(crop, cw, ch, w, h));
            _faces[key] = face;
            return face;
        }

        // Rank pips laid out the way the battlefield reads: party front is on the right,
        // enemy front is on the left.
        public static void RankPips(SpriteBatch sb, Ranks ranks, Vector2 pos, bool enemySide, Color on)
        {
            for (int i = 0; i < 4; i++)
            {
                int slot = enemySide ? i : 3 - i;
                var c = ranks.Has(slot) ? on : Palette.Grey900;
                Draw.Diamond(sb, new Vector2(pos.X + i * 13 + 5, pos.Y + 6), 8, c);
            }
        }

        // Status colours match the keyword colours used in descriptions (Palette.Kw*).
        public static Color StatusColor(StatusKind k) => k switch
        {
            StatusKind.Guard or StatusKind.Protected or StatusKind.Wall => Palette.KwGuard,
            StatusKind.Taunt => Palette.Taunt,
            StatusKind.Blight => Palette.KwBlight,
            StatusKind.Slow => Palette.KwSlow,
            StatusKind.Charging => Palette.KwPriority,
            _ => Palette.KwMark,
        };

        public static string StatusIcon(StatusKind k) => k switch
        {
            StatusKind.Guard or StatusKind.Protected => "ui_guard",
            StatusKind.Wall => "st_wall",
            StatusKind.Taunt => "ui_damage",
            StatusKind.Blight => "st_blight",
            StatusKind.Slow => "st_slow",
            StatusKind.Charging => "st_charging",
            _ => "st_mark",
        };

        public static string StatusName(StatusKind k) => k switch
        {
            StatusKind.Wall => "Aegis Wall",
            StatusKind.Charging => "Winding up",
            _ => k.ToString(),
        };

        // What a status does, in a sentence (status tooltips). The instance gives its numbers.
        public static string StatusText(StatusInstance s) => s.Kind switch
        {
            StatusKind.Guard => $"Takes {Math.Round((1 - Battle.GuardMultiplier) * 100)}% less damage until this unit acts again.",
            StatusKind.Taunt => "Draws single-target attacks onto itself.",
            StatusKind.Blight => $"Loses {Math.Max(1, s.Power)} HP at the end of each turn. {s.Turns} turn(s) left.",
            StatusKind.Mark => $"Takes +{Math.Round((Battle.MarkMultiplier - 1) * 100)}% damage from every hit. {s.Turns} turn(s) left.",
            StatusKind.Slow => $"-{s.Power} Speed: acts later in the turn. {s.Turns} turn(s) left.",
            StatusKind.Protected => $"Attacks aimed at this unit hit {s.Source?.Name ?? "its guardian"} instead, until {s.Source?.Name ?? "they"} acts again.",
            StatusKind.Wall => $"Takes {Math.Round((1 - Battle.WallMultiplier) * 100)}% less damage while {s.Source?.Name ?? "Mara"} holds the Aegis Wall.",
            StatusKind.Charging => "Winding up a huge attack: it lands next turn. Guard (acts first), Aegis Wall, or interrupt it with Shield Bash / Iron Rebuke.",
            _ => "",
        };

        // Status icons with their turns left. Returns each icon's rectangle (callers register hover / tooltips).
        public static List<(Rectangle rect, StatusInstance status)> Statuses(SpriteBatch sb, IEnumerable<StatusInstance> statuses, Vector2 pos, float alpha = 1f)
        {
            var list = new List<(Rectangle, StatusInstance)>();
            float x = pos.X;
            foreach (var s in statuses)
            {
                bool timed = s.Kind is StatusKind.Blight or StatusKind.Mark or StatusKind.Slow;
                string turns = timed ? s.Turns.ToString() : null;
                int w = 20 + (turns != null ? (int)Assets.Small.MeasureString(turns).X + 3 : 0);
                var r = new Rectangle((int)x, (int)pos.Y, w, 20);
                var col = StatusColor(s.Kind);
                Draw.Slant(sb, r, 5, Palette.Ink * 0.85f * alpha);
                Draw.Rect(sb, new Rectangle(r.X + 5, r.Bottom - 2, r.Width - 10, 2), col * alpha);
                Icons.Draw(sb, StatusIcon(s.Kind), new Rectangle(r.X + 3, r.Y + 2, 16, 16), alpha);
                if (turns != null) Draw.Text(sb, Assets.Small, turns, new Vector2(r.X + 19, r.Y + 1), col * alpha, shadow: false);
                list.Add((r, s));
                x += r.Width + 2;
            }
            return list;
        }

        // Enemy Type colours: Brutes hit the front (coral), Stalkers the back (violet), Normal is plain grey, Apex (bosses) rose.
        public static Color TypeColor(EnemyType t) => t switch
        {
            EnemyType.Brute => Palette.Coral,
            EnemyType.Stalker => Palette.Violet,
            EnemyType.Normal => Palette.Grey700,
            EnemyType.Apex => Palette.Rose,
            _ => Palette.Grey500,
        };

        // A small chip naming an enemy's Type ("BRUTE"), or just its letter ("B") when compact.
        // pos is the top-left, or the centre when centered. Returns where it was drawn.
        public static Rectangle TypeChip(SpriteBatch sb, EnemyTypeDef type, Vector2 pos, bool compact = false, bool centered = false, float alpha = 1f)
        {
            if (type == null) return Rectangle.Empty;
            string label = compact ? type.Name.Substring(0, 1) : type.Name.ToUpperInvariant();
            var size = Assets.Small.MeasureString(label);
            int w = (int)size.X + 8, h = 16;
            var r = centered ? new Rectangle((int)(pos.X - w / 2f), (int)(pos.Y - h / 2f), w, h) : new Rectangle((int)pos.X, (int)pos.Y, w, h);
            Draw.Rect(sb, r, TypeColor(type.Type) * 0.9f * alpha);
            Draw.Text(sb, Assets.Small, label, new Vector2(r.X + 4, r.Y + 1), Palette.Paper * alpha, shadow: false);
            return r;
        }

        public static void HpBar(SpriteBatch sb, Rectangle r, int hp, int max, bool isEnemy, bool showNumbers = true)
        {
            Draw.Bar(sb, r, max > 0 ? (float)hp / max : 0, isEnemy ? Palette.EnemyHp : Palette.PartyHp);
            if (showNumbers)
                Draw.TextCentered(sb, Assets.Small, $"{hp}/{max}", new Vector2(r.Center.X, r.Center.Y - 1), Palette.Paper);
        }
    }
}
