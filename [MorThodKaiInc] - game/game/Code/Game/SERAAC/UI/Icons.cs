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
            // UI palette (Art/Palette.png) for the battle UI icons below.
            ['q'] = Palette.Coral, ['m'] = Palette.Rose, ['h'] = Palette.Blush, ['v'] = Palette.Violet,
            ['u'] = Palette.Lilac300, ['i'] = Palette.Lilac100, ['W'] = Palette.Paper,
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
            // ---- Battle UI (skill types, Special Abilities, stats). Override with Art/icon_ui_<name>.png.
            ["ui_attack"] = new[] { "..........kk", ".........kWk", "........kWWk", ".......kWWk.", "......kWWk..", "..k..kWWk...",
                                    "..kk.kWk....", "...kkqk.....", "..kqkkk.....", ".kqk..kk....", "kqk.........", "kk.........." },
            ["ui_heal"] = new[] { "............", "....kkkk....", "....kuuk....", "....kuuk....", ".kkkkuukkkk.", ".kuuuuuuuuk.",
                                  ".kuuuuuuuuk.", ".kkkkuukkkk.", "....kuuk....", "....kuuk....", "....kkkk....", "............" },
            ["ui_guard"] = new[] { "............", "..kkkkkkkk..", "..kvvvvvvk..", "..kvWvvvvk..", "..kvWvvvvk..", "..kvvvvvvk..",
                                   "..kvvvvvvk..", "...kvvvvk...", "...kvvvvk...", "....kvvk....", ".....kk.....", "............" },
            ["ui_item"] = new[] { "....kkkk....", "....kWWk....", ".....kk.....", "....kWWk....", "...kWhhWk...", "..khhhhhhk..",
                                  ".khhWhhhhhk.", ".khhhhhhhhk.", ".kmhhhhhhmk.", "..kmmmmmmk..", "...kkkkkk...", "............" },
            ["ui_charge"] = new[] { ".......kkk..", "......khhk..", ".....khhk...", "....khhk....", "...khhkkkk..", "..khhhhhhk..",
                                    "..kkkkhhk...", ".....khhk...", "....khhk....", "...khhk.....", "...khk......", "...kk......." },
            ["ui_position"] = new[] { "............", "...k........", "..kWk.......", ".kWWWkkkkkk.", "kWWWWWWWWWk.", ".kWWWkkkkkk.",
                                      "..kWk...k...", "...k...kuk..", ".kkkkkkuuuk.", ".kuuuuuuuuuk", ".kkkkkkuuuk.", "........k..." },
            ["ui_speed"] = new[] { "............", ".kk...kk....", ".kWk..kWk...", "..kWk..kWk..", "...kWk..kWk.", "....kWk..kWk",
                                   "....kWk..kWk", "...kWk..kWk.", "..kWk..kWk..", ".kWk..kWk...", ".kk...kk....", "............" },
            ["ui_damage"] = new[] { ".....kk.....", "....kqqk....", "....kqqk....", "...kqWqqk...", ".kkqWWqqqkk.", "kqqqWqqqqqqk",
                                    "kqqqqqqqqqqk", ".kkqqqqqqkk.", "...kqqqqk...", "....kqqk....", "....kqqk....", ".....kk....." },
            ["ui_hp"] = new[] { "............", ".kkk...kkk..", "kqqqk.kqqqk.", "kqWqqkqqqqk.", "kqWqqqqqqqk.", "kqqqqqqqqqk.",
                                ".kqqqqqqqk..", "..kqqqqqk...", "...kqqqk....", "....kqk.....", ".....k......", "............" },
            ["ui_sp"] = new[] { ".....kk.....", ".....kvk....", "....kvvk....", "kkkkkvvkkkk.", "kvvvvvvvvvk.", ".kvvvvvvvk..",
                                "..kvvvvvk...", "..kvvkvvk...", ".kvvk.kvvk..", ".kvk...kvk..", ".kk.....kk..", "............" },
            ["ui_objective"] = new[] { ".....kk.....", "....kqqk....", "...kqWWqk...", "..kqqWWqqk..", ".kqqqWWqqqk.", "kqqqqWWqqqqk",
                                       "kqqqqWWqqqqk", ".kqqqqqqqqk.", "..kqqWWqqk..", "...kqWWqk...", "....kqqk....", ".....kk....." },

            // ---- Status icons (Figures.StatusIcon).
            ["st_blight"] = new[] { ".....kk.....", "....keek....", "....keek....", "...keeeek...", "...keeeek...", "..keeleeek..",
                                    "..keleeeek..", "..keeeeeek..", "..kEeeeeEk..", "...kEEEEk...", "....kkkk....", "............" },
            ["st_mark"] = new[] { ".....kk.....", "....kook....", "...kokkok...", "..kok..kok..", ".kok.kk.kok.", "kok.kook.kok",
                                  "kok.kook.kok", ".kok.kk.kok.", "..kok..kok..", "...kokkok...", "....kook....", ".....kk....." },
            ["st_slow"] = new[] { ".kkkkkkkkkk.", ".kwwwwwwwwk.", "..kcccccck..", "...kcccck...", "....kcck....", ".....kk.....",
                                  ".....kk.....", "....kcck....", "...kcccck...", "..kcccccck..", ".kwwwwwwwwk.", ".kkkkkkkkkk." },
            ["st_wall"] = new[] { "............", "kkkkkkkkkkkk", "kvvvvkvvvvvk", "kvvvvkvvvvvk", "kkkkkkkkkkkk", "kvvkvvvvvkvk",
                                  "kvvkvvvvvkvk", "kkkkkkkkkkkk", "kvvvvkvvvvvk", "kvvvvkvvvvvk", "kkkkkkkkkkkk", "............" },
            ["st_charging"] = new[] { ".....kk.....", "....kyyk....", "....kyyk....", "....kyyk....", "....kyyk....", "....kyyk....",
                                      ".....kk.....", "............", ".....kk.....", "....kyyk....", ".....kk.....", "............" },

            ["shards"] = new[] { "......k.....", ".....kok....", ".....kook...", "....kyook...", "....kyyok...", "...kyyyook..",
                                 "...kyylyok..", "...kyyyook..", "....kyyok...", "....kyok....", ".....kk.....", "............" },
        };

        private static readonly Dictionary<string, Texture2D> _textures = new();

        public static IEnumerable<string> AllIds => Maps.Keys;

        // Adds or replaces a pixel icon (mods' "icons" section, Data/ModContent.cs).
        public static void Register(string id, string[] rows)
        {
            Maps[id] = rows;
            _textures.Remove(id);
        }

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
