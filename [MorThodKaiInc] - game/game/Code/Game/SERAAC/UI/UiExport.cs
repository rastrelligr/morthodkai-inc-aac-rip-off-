using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SERAAC.Core;

namespace SERAAC.UI
{
    // --export-ui [dir]: renders each UI element on a transparent background and saves it as a PNG, so the
    // drawn-in-code UI can be used in mockups, docs or as a base for painted replacements. Elements are drawn
    // at 2x (layout units x Scale), the same code path the game uses at 2560x1440.
    public static class UiExport
    {
        private const float Scale = 2f;
        private static GraphicsDevice _gd;
        private static SpriteBatch _sb;
        private static string _dir;
        private static int _count;

        public static int Run(GraphicsDevice gd, SpriteBatch sb, string dir)
        {
            _gd = gd;
            _sb = sb;
            _dir = dir;
            _count = 0;
            Directory.CreateDirectory(dir);
            Input.ScriptOnly = true;
            Core.Draw.Scale = Scale;
            Core.Draw.Offset = Vector2.Zero;

            // ---- Buttons and rows
            Shot("button_normal", 220, 40, (s, r) => Ui.Button(s, "b", 0, r, "Confirm"));
            Shot("button_hover", 220, 40, (s, r) => Ui.Button(s, "b", 0, r, "Confirm"), hover: true);
            Shot("button_selected", 220, 40, (s, r) => Ui.Button(s, "b", 0, r, "Confirm", selected: true));
            Shot("button_disabled", 220, 40, (s, r) => Ui.Button(s, "b", 0, r, "Confirm", enabled: false));
            Shot("row_hover", 360, 30, (s, r) => Ui.HoverRow(s, "h", 0, r, false), hover: true);
            Shot("row_selected", 400, 30, (s, r) => Ui.SelectedRow(s, new Rectangle(r.X + 36, r.Y, r.Width - 36, r.Height)));
            Shot("highlight_bar", 360, 30, (s, r) => Core.Draw.HighlightBar(s, r, Palette.Rose));
            Shot("highlight_bar_danger", 360, 30, (s, r) => Core.Draw.HighlightBar(s, r, Palette.Danger));

            // ---- Panels and cards
            Shot("card_normal", 300, 96, (s, r) => Ui.Card(s, r, false));
            Shot("card_selected", 300, 96, (s, r) => Ui.Card(s, r, true));
            Shot("panel_slab", 420, 200, (s, r) => Core.Draw.SlabPanel(s, r));
            Shot("panel", 420, 200, (s, r) => Core.Draw.Panel(s, r));
            Shot("tooltip", 340, 140, (s, r) => Tooltip.Draw(s, new Rectangle(r.Center.X, r.Bottom + 8, 1, 1), "Slash",
                "Deals 6 damage to the front enemy.\nApplies Mark for 2 turns.", Palette.Rose, "Skill", 320), pad: 10);

            // ---- Headers and bars
            Shot("screen_title", 520, 70, (s, r) => Ui.ScreenTitle(s, "Sanctum", new Vector2(r.X + 8, r.Y + 6), "Prepare before the next trial"));
            Shot("section_bar", 420, 28, (s, r) => Ui.SectionBar(s, r, "PARTY"));
            Shot("tab_bar", 900, 44, (s, r) => Ui.TabBar(s, "t", new[] { "Party", "Skills", "Items", "Trials" }, 1, r));
            Shot("footer", 0, Game1.Height - 40, Game1.Width, 40, s => Ui.Footer(s, null, ("Esc", "Back"), ("Enter", "Confirm")));
            Shot("banner", 900, 120, (s, r) => Ui.Banner(s, "ENCOUNTER", r.Center.ToVector2(), r.Width, Palette.Rose, 1f));
            Shot("banner_victory", 900, 120, (s, r) => Ui.Banner(s, "WYVERN SLAIN", r.Center.ToVector2(), r.Width, Palette.Gold, 1f));
            Shot("bar_party_hp", 200, 10, (s, r) => Core.Draw.Bar(s, r, 0.7f, Palette.PartyHp, Palette.Ink * 0.85f));
            Shot("bar_enemy_hp", 200, 10, (s, r) => Core.Draw.Bar(s, r, 0.45f, Palette.EnemyHp, Palette.Ink * 0.85f));
            Shot("bar_sp", 200, 10, (s, r) => Core.Draw.Bar(s, r, 0.6f, Palette.KwSp, Palette.Ink * 0.85f));
            Shot("divider", 300, 20, (s, r) => Core.Draw.Divider(s, r.Center.ToVector2(), r.Width, Palette.Paper * 0.4f));
            Shot("glow_line", 400, 30, (s, r) => Core.Draw.GlowLine(s, r.Center.ToVector2(), r.Width - 60, Palette.Rose, 12));

            // ---- Small pieces
            Shot("keycap_q", 30, 26, (s, r) => Ui.KeyCap(s, "Q", r.Center.ToVector2()));
            Shot("keycap_enter", 60, 26, (s, r) => Ui.KeyCap(s, "Enter", r.Center.ToVector2()));
            Shot("help_button", 40, 40, (s, r) => Ui.HelpButton(s, r.Center.ToVector2(), ""));
            Shot("pointer", 40, 28, (s, r) => Core.Draw.Pointer(s, new Vector2(r.Right - 2, r.Center.Y), 0));
            Shot("ornament", 60, 30, (s, r) => Core.Draw.Ornament(s, r.Center.ToVector2(), 18, Palette.Paper * 0.85f));
            Shot("ornament_gold", 60, 30, (s, r) => Core.Draw.Ornament(s, r.Center.ToVector2(), 18, Palette.Gold));
            Shot("diamond", 24, 24, (s, r) => Core.Draw.Diamond(s, r.Center.ToVector2(), 18, Palette.Rose));

            // ---- Icons (pixel art, 12x12 scaled 4x per layout unit)
            foreach (var id in Icons.AllIds)
            {
                string name = id;
                Shot("icons/" + name, 48, 48, (s, r) => Icons.Draw(s, name, r), pad: 0);
            }

            // ---- Visual novel box: the full 1280x720 frame (portraits and box, no background)
            var vn = new VnDialogue(new[] { VnLine.Say("Naomi", "Relle. Wake up, we're going to be late for the simulation.") });
            vn.Update(10f);
            Shot("vn_dialogue", 0, 0, Game1.Width, Game1.Height, vn.Draw);
            var vnNarr = new VnDialogue(new[] { VnLine.Narrate("The airlift hums beneath your feet.") });
            vnNarr.Update(10f);
            Shot("vn_narration", 0, 0, Game1.Width, Game1.Height, vnNarr.Draw);

            Input.ScriptOnly = false;
            return _count;
        }

        // A w x h element drawn into a rectangle with `pad` units of empty space around it.
        private static void Shot(string name, int w, int h, Action<SpriteBatch, Rectangle> draw, bool hover = false, int pad = 6)
        {
            var r = new Rectangle(pad, pad, w, h);
            Shot(name, 0, 0, w + pad * 2, h + pad * 2, s => draw(s, r), hover ? r.Center.ToVector2() : null);
        }

        // Captures the layout rectangle (x, y, w, h). For hover states the element is drawn once to register its
        // hit box, then again with the mouse over it.
        private static void Shot(string name, int x, int y, int w, int h, Action<SpriteBatch> draw, Vector2? mouse = null)
        {
            int pw = (int)(w * Scale), ph = (int)(h * Scale);
            using var rt = new RenderTarget2D(_gd, pw, ph, false, SurfaceFormat.Color, DepthFormat.None);
            Core.Draw.FrameTransform = Matrix.CreateTranslation(-x, -y, 0) * Matrix.CreateScale(Scale, Scale, 1f);
            Input.MousePos = new Vector2(-1000, -1000);
            int passes = mouse.HasValue ? 2 : 1;
            for (int p = 0; p < passes; p++)
            {
                if (p == 1) Input.MousePos = mouse.Value + new Vector2(x, y);
                Ui.BeginFrame();
                _gd.SetRenderTarget(rt);
                _gd.Clear(Color.Transparent);
                _sb.Begin(samplerState: SamplerState.PointClamp, transformMatrix: Core.Draw.FrameTransform);
                draw(_sb);
                _sb.End();
                _gd.SetRenderTarget(null);
            }
            Input.MousePos = new Vector2(-1000, -1000);

            // The game blends premultiplied colours; PNG wants straight alpha.
            var data = new Color[pw * ph];
            rt.GetData(data);
            for (int i = 0; i < data.Length; i++)
            {
                var c = data[i];
                if (c.A == 0) { data[i] = Color.Transparent; continue; }
                if (c.A == 255) continue;
                data[i] = new Color(Math.Min(255, c.R * 255 / c.A), Math.Min(255, c.G * 255 / c.A), Math.Min(255, c.B * 255 / c.A), c.A);
            }
            rt.SetData(data);

            string path = Path.Combine(_dir, name + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var fs = File.Create(path)) rt.SaveAsPng(fs, pw, ph);
            _count++;
        }
    }
}
