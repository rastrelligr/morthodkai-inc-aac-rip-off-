using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SERAAC.Core;

namespace SERAAC.UI
{
    // Item icons. Art/icon_<id>.png wins if it exists; otherwise a 12x12 pixel icon defined below.
    public static class Icons
    {
        private static readonly Dictionary<char, Color> Ink = new()
        {
            ['k'] = new Color(20, 16, 22), ['w'] = new Color(235, 230, 220), ['l'] = new Color(255, 252, 245),
            ['s'] = new Color(215, 200, 170), ['g'] = new Color(175, 180, 190), ['G'] = new Color(105, 110, 120),
            ['b'] = new Color(135, 88, 52), ['B'] = new Color(82, 52, 32), ['r'] = new Color(205, 52, 58),
            ['R'] = new Color(120, 26, 36), ['y'] = new Color(236, 196, 84), ['Y'] = new Color(160, 120, 40),
            ['c'] = new Color(96, 206, 226), ['C'] = new Color(40, 120, 150), ['p'] = new Color(150, 92, 192),
            ['e'] = new Color(122, 204, 92), ['E'] = new Color(60, 122, 52), ['o'] = new Color(242, 142, 60),
        };

        private static readonly Dictionary<string, string[]> Maps = new()
        {
            ["rusted_blade"] = new[] { "..........kk", ".........kgk", "........kgGk", ".......kgok.", "......kgGk..", "..k..kgGk...",
                                       "..kkkoGk....", "...kyyk.....", "..kbkkyk....", ".kbk..kk....", "kbk.........", "kk.........." },
            ["bonded_fang"] = new[] { "....kk......", "...kwwk.....", "...kwwlk....", "....kwwk....", "....kswwk...", ".....kswk...",
                                      ".....kswwk..", "......kswk..", "......kRrrk.", ".....kRrrrk.", "......kRRk..", ".......kk..." },
            ["hook_knife"] = new[] { "............", "......kkk...", ".....kgggk..", "....kgGkgk..", "...kgGk.kk..", "..kgGk......",
                                     ".kgGk.......", ".kkkk.......", "..kbk.......", "..kbk.......", "..kBk.......", "...k........" },
            ["tide_hook"] = new[] { "....kkkk....", "...kcccck...", "..kcCkkcck..", "..kck..kck..", "..kck...kk..", "..kcck......",
                                    "...kcck..k..", "....kccckck.", ".....kcccck.", "......kkkk..", "......kbk...", "......kBk..." },
            ["pale_locket"] = new[] { "...k....k...", "....k..k....", ".....kk.....", ".....yk.....", "....kyyk....", "...kwlwwk...",
                                      "..kwwlwwwk..", "..kwwwwwwk..", "..kwwwwwsk..", "...kwwwsk...", "....kkkk....", "............" },
            ["chapel_bell"] = new[] { ".....kk.....", "....kyyk....", "...kyyyyk...", "...kyyyYk...", "..kyyyyyYk..", "..kyyyyyYk..",
                                      ".kyyyyyyyYk.", ".kkkkkkkkkk.", "....kYYk....", ".....kk.....", "............", "............" },
            ["goat_horn"] = new[] { "............", "...kkkk.....", "..kBBBBk....", ".kBbkkBBk...", ".kBk..kBk...", ".kBk...kBk..",
                                    "..kBk..kBk..", "...kk.kBbk..", "......kBbk..", ".....kBbk...", "....kbbk....", "....kkk....." },
            ["choir_veil"] = new[] { "....kkkk....", "...kppppk...", "..kppwppk...", "..kpppppk...", ".kpppppppk..", ".kppwpppppk.",
                                     "kpppppwpppk.", "kpppppppppk.", "kpkpkpkpkpk.", "k.k.k.k.k.k.", "............", "............" },
            ["butcher_hatchet"] = new[] { "....kkkkk...", "...kgggggk..", "..kgggrgGk..", "..kgggGGk...", "...kkbkk....", ".....kbk....",
                                          ".....kbk....", ".....kbk....", ".....kbk....", ".....kBk....", ".....kRk....", "......k....." },
            ["tonic"] = new[] { "....kkkk....", "....kbbk....", ".....kk.....", "....kwwk....", "...kwllwk...", "..keeeeeek..",
                                ".keeeleeeek.", ".keeeeeeeek.", ".kEeeeeeeEk.", "..kEEEEEEk..", "...kkkkkk...", "............" },
            ["ember_salt"] = new[] { "............", ".....k.k....", "....kokok...", "....kbbbk...", "...kbbbbbk..", "..kbbobbbbk.",
                                     "..kbooobbbk.", "..kbbobbbbk.", "..kbbbbbbbk.", "...kBBBBBk..", "....kkkkk...", "............" },
            ["the_cube"] = new[] { "............", ".....kk.....", "...kkllkk...", ".kkllllllkk.", ".kgglllllGk.", ".kgggkkGGGk.",
                                   ".kggggGGGGk.", ".kggggGGGGk.", ".kggggGGGGk.", "..kkggGGkk..", "....kkkk....", "............" },
            ["shards"] = new[] { "......k.....", ".....kok....", ".....kook...", "....kyook...", "....kyyok...", "...kyyyook..",
                                 "...kyylyok..", "...kyyyook..", "....kyyok...", "....kyok....", ".....kk.....", "............" },
        };

        private static readonly Dictionary<string, Texture2D> _textures = new();

        public static IEnumerable<string> AllIds => Maps.Keys;

        // Rows that aren't 12 characters long (self-test uses this to catch typos).
        public static IEnumerable<string> MalformedIds()
        {
            foreach (var (id, rows) in Maps)
                if (rows.Length != 12 || Array.Exists(rows, r => r.Length != 12)) yield return id;
        }

        public static void Draw(SpriteBatch sb, string id, Rectangle box, float alpha = 1f)
        {
            var art = Sprites.Get("icon_" + id);
            if (art != null)
            {
                Sprites.DrawFit(sb, art, null, box, Color.White * alpha);
                return;
            }

            var tex = Texture(id);
            if (tex == null)
            {
                Core.Draw.Diamond(sb, box.Center.ToVector2(), Math.Min(box.Width, box.Height) * 0.5f, Palette.Guard * alpha);
                return;
            }
            // Whole-number scaling in real screen pixels keeps the pixel art sharp at any resolution.
            int scale = Math.Max(1, (int)(Math.Min(box.Width, box.Height) * Core.Draw.Scale / 12));
            float size = 12 * scale / Core.Draw.Scale;
            var pos = Core.Draw.Snap(new Vector2(box.Center.X - size / 2, box.Center.Y - size / 2));
            sb.Draw(tex, pos, null, Color.White * alpha, 0f, Vector2.Zero, scale / Core.Draw.Scale, SpriteEffects.None, 0f);
        }

        private static Texture2D Texture(string id)
        {
            if (id == null || !Maps.TryGetValue(id, out var rows)) return null;
            if (_textures.TryGetValue(id, out var tex)) return tex;
            var px = new Color[12 * 12];
            for (int y = 0; y < 12 && y < rows.Length; y++)
                for (int x = 0; x < 12 && x < rows[y].Length; x++)
                    px[y * 12 + x] = Ink.TryGetValue(rows[y][x], out var c) ? c : Color.Transparent;
            tex = new Texture2D(Assets.Pixel.GraphicsDevice, 12, 12);
            tex.SetData(px);
            _textures[id] = tex;
            return tex;
        }
    }
}
