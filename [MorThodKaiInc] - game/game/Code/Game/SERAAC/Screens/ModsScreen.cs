using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SERAAC.Core;
using SERAAC.UI;

namespace SERAAC.Screens
{
    // The mod manager (Title screen > Mods): every mod in Mods/ with its icon and details. Switch mods on and
    // off and change their load order; choices are saved to Mods/load_order.json and apply after a restart,
    // which this screen handles. Mods themselves are loaded by Core/Mods.cs.
    public sealed class ModsScreen : Screen
    {
        private readonly List<ModInfo> _order = new();          // load order being edited, first loads first
        private readonly Dictionary<ModInfo, bool> _on = new();  // switches being edited
        private int _index;
        private string _toast;
        private double _toastUntil, _time;

        private static readonly Dictionary<string, SpriteFrame> _icons = new();

        private const int RowH = 64, RowStep = 70;
        private static readonly Rectangle ListPanel = new(40, 134, 560, 486);
        private static readonly Rectangle DetailPanel = new(620, 134, 620, 486);

        public override void OnEnter()
        {
            _order.AddRange(Mods.All);
            foreach (var m in _order) _on[m] = m.Enabled;
        }

        private ModInfo Selected => _order.Count > 0 ? _order[_index] : null;

        // Differs from what this session loaded: a restart is needed to apply it.
        private bool Pending => !_order.SequenceEqual(Mods.All) || _order.Any(m => _on[m] != m.Enabled);

        private static bool Locked(ModInfo m) => m.Errors.Any(e => e.StartsWith("mod.json") || e.StartsWith("another mod"));

        public override void Update(GameTime gameTime)
        {
            _time = gameTime.TotalGameTime.TotalSeconds;
            if (Input.Cancel || Ui.Clicked("top", 1)) { Screens.Replace(new MainMenuScreen()); return; }
            if (Ui.Clicked("top", 0)) Mods.OpenFolder(Mods.Root);
            if (_order.Count == 0) return;

            int was = _index;
            if (Input.Up) _index = Math.Max(0, _index - 1);
            if (Input.DownKey) _index = Math.Min(_order.Count - 1, _index + 1);
            if (Input.Wheel != 0 && Ui.Hover("mod") >= 0) _index = Math.Clamp(_index + Math.Sign(Input.Wheel), 0, _order.Count - 1);
            Ui.Pick("mod", ref _index);
            if (_index != was) Audio.Play("ui_move");

            int check = Ui.ClickedIndex("check");
            if (check >= 0) { _index = check; Toggle(_order[check]); }
            else if (Input.Confirm || Ui.Clicked("act", 0)) Toggle(Selected);
            if (Input.Pressed(Keys.PageUp) || Ui.Clicked("act", 1)) Move(-1);
            if (Input.Pressed(Keys.PageDown) || Ui.Clicked("act", 2)) Move(+1);
            if (Pending && (Input.Pressed(Keys.R) || Ui.Clicked("restart")) && !Mods.Restart())
                Toast("Couldn't restart. Close the game and start it again.");
        }

        // Turning a mod on also turns on what it needs; turning it off also turns off what needs it.
        private void Toggle(ModInfo mod)
        {
            if (Locked(mod)) { Toast("This mod can't load: " + mod.Errors[0]); return; }
            bool on = !_on[mod];
            var touched = new List<string>();
            void Set(ModInfo m)
            {
                if (_on[m] == on || Locked(m)) return;
                _on[m] = on;
                if (m != mod) touched.Add(m.Title);
                var next = on ? m.Dependencies.Select(id => _order.FirstOrDefault(x => x.Id == id)).Where(x => x != null)
                              : _order.Where(x => x.Dependencies.Contains(m.Id));
                foreach (var n in next.ToList()) Set(n);
            }
            Set(mod);
            if (touched.Count > 0) Toast((on ? "Also turned on: " : "Also turned off: ") + string.Join(", ", touched));
            Audio.Play("ui_select");
            Save();
        }

        private void Move(int by)
        {
            int to = _index + by;
            if (to < 0 || to >= _order.Count) return;
            // A mod always loads after the mods it needs.
            var (early, late) = by < 0 ? (_order[_index], _order[to]) : (_order[to], _order[_index]);
            if (early.Dependencies.Contains(late.Id)) { Toast($"{early.Title} needs {late.Title}, so it has to load after it."); return; }
            (_order[_index], _order[to]) = (_order[to], _order[_index]);
            _index = to;
            Audio.Play("ui_move");
            Save();
        }

        private void Save() => Mods.SaveLoadOrder(_order.Select(m => (m, _on[m])));

        private void Toast(string s)
        {
            _toast = s;
            _toastUntil = _time + 3.5;
        }

        // ------------------------------------------------------------------ drawing

        public override void Draw(SpriteBatch sb, GameTime gameTime)
        {
            Ui.ScreenTitle(sb, "Mods", new Vector2(60, 26),
                Mods.Disabled ? "Switched off: the game was started with --no-mods."
                : $"{Mods.Active.Count()} of {Mods.All.Count} loaded. Changes apply after a restart.");
            Ui.Button(sb, "top", 0, new Rectangle(Game1.Width - 410, 40, 220, 40), "Open Mods folder");
            Ui.Button(sb, "top", 1, new Rectangle(Game1.Width - 176, 40, 136, 40), "Back (Esc)");

            if (_order.Count == 0) { DrawEmpty(sb); return; }

            DrawList(sb);
            DrawDetails(sb, Selected);

            // Actions for the selected mod, under the list.
            var sel = Selected;
            int y = ListPanel.Bottom + 14;
            Ui.Button(sb, "act", 0, new Rectangle(ListPanel.X, y, 190, 40), _on[sel] ? "Turn off" : "Turn on", enabled: !Locked(sel));
            Ui.Button(sb, "act", 1, new Rectangle(ListPanel.X + 200, y, 170, 40), "Load earlier", enabled: _index > 0);
            Ui.Button(sb, "act", 2, new Rectangle(ListPanel.X + 380, y, 170, 40), "Load later", enabled: _index < _order.Count - 1);

            if (Pending)
            {
                var bar = new Rectangle(DetailPanel.X, y, DetailPanel.Width, 40);
                Core.Draw.Slant(sb, bar, 12, Palette.EnemyTag);
                Core.Draw.Text(sb, Assets.Body, "Restart to apply your changes.", new Vector2(bar.X + 22, bar.Y + 8), Palette.Text, shadow: false);
                Ui.Button(sb, "restart", 0, new Rectangle(bar.Right - 200, bar.Y, 200, 40), "Restart now (R)", selected: true);
            }

            if (_toast != null && _time < _toastUntil)
            {
                var size = Assets.Body.MeasureString(_toast);
                var r = new Rectangle((int)(Game1.Width / 2 - size.X / 2 - 16), 92, (int)size.X + 32, 34);
                Core.Draw.Panel(sb, r, Palette.EnemyTag, Palette.Rose);
                Core.Draw.TextCentered(sb, Assets.Body, _toast, r.Center.ToVector2(), Palette.Text);
            }

            Ui.HelpButton(sb, new Vector2(Game1.Width - 30, Game1.Height - 26),
                "Choose: Up/Down, wheel or mouse.\nOn / off: Enter, Space or the box.\nLoad order: Page Up / Page Down.\nRestart: R.  Back: Esc.");
        }

        private void DrawEmpty(SpriteBatch sb)
        {
            var r = new Rectangle(240, 200, 800, 300);
            Core.Draw.SlabPanel(sb, r, Palette.Rose);
            Core.Draw.Text(sb, Assets.Header, "NO MODS INSTALLED", new Vector2(r.X + 40, r.Y + 30), Palette.Paper);
            RichText.Wrapped(sb, Assets.Body,
                "A mod is a folder with a mod.json inside. Put mod folders in:\n" + Mods.Root +
                "\n\nthen come back here to switch them on. Mods/README.md explains how to make one, and Mods/ExampleMod is a working sample.",
                new Vector2(r.X + 40, r.Y + 80), r.Width - 80, Palette.Text, lineGap: 2);
        }

        private void DrawList(SpriteBatch sb)
        {
            Core.Draw.Panel(sb, ListPanel);
            Core.Draw.Text(sb, Assets.Body, "LOAD ORDER  (later ones win)", new Vector2(ListPanel.X + 16, ListPanel.Y + 12), Palette.TextDim);
            int shown = Math.Max(1, (ListPanel.Height - 52) / RowStep);
            int first = Math.Clamp(_index - shown + 1, 0, Math.Max(0, _order.Count - shown));
            for (int i = first; i < Math.Min(_order.Count, first + shown); i++)
            {
                var m = _order[i];
                var r = new Rectangle(ListPanel.X + 12, ListPanel.Y + 44 + (i - first) * RowStep, ListPanel.Width - 24, RowH);
                bool sel = i == _index;
                Ui.Card(sb, r, sel);
                Ui.HoverRow(sb, "mod", i, r, sel);

                // Switch, icon, name, version / author, status.
                var box = new Rectangle(r.X + 24, r.Y + (RowH - 22) / 2, 22, 22);
                Core.Draw.Rect(sb, box, Palette.Ink);
                Core.Draw.Border(sb, box, Locked(m) ? Palette.TextDisabled : Palette.Grey500, 2);
                if (_on[m]) Core.Draw.Rect(sb, new Rectangle(box.X + 5, box.Y + 5, 12, 12), Palette.Rose);
                Ui.Hit("check", i, box);

                DrawIcon(sb, m, new Rectangle(r.X + 58, r.Y + 8, 48, 48));
                var nameColor = _on[m] ? (sel ? Palette.Accent : Palette.Text) : Palette.TextDisabled;
                Core.Draw.Text(sb, Assets.Header, Fit(m.Title, Assets.Header, r.Width - 250), new Vector2(r.X + 118, r.Y + 6), nameColor);
                string by = $"v{m.Version}" + (string.IsNullOrEmpty(m.Author) ? "" : "  by " + m.Author);
                Core.Draw.Text(sb, Assets.Small, Fit(by, Assets.Small, r.Width - 250), new Vector2(r.X + 120, r.Y + 38), Palette.TextDim, shadow: false);

                var (status, color) = Status(m);
                Core.Draw.TextRight(sb, Assets.Small, status, new Vector2(r.Right - 14, r.Y + 8), color, shadow: false);
                Core.Draw.TextRight(sb, Assets.Small, $"#{i + 1}", new Vector2(r.Right - 14, r.Y + 38), Palette.Grey500, shadow: false);
            }
            if (first > 0) Core.Draw.TextRight(sb, Assets.Small, "more above", new Vector2(ListPanel.Right - 16, ListPanel.Y + 14), Palette.TextDim, shadow: false);
            if (first + shown < _order.Count) Core.Draw.TextRight(sb, Assets.Small, "more below", new Vector2(ListPanel.Right - 16, ListPanel.Bottom - 22), Palette.TextDim, shadow: false);
        }

        private (string, Color) Status(ModInfo m)
        {
            if (_on[m] != m.Enabled) return (_on[m] ? "ON AFTER RESTART" : "OFF AFTER RESTART", Palette.KwPriority);
            if (Locked(m)) return ("CAN'T LOAD", Palette.Danger);
            if (m.Loaded && m.Errors.Count > 0) return ($"ON, {m.Errors.Count} ERROR{(m.Errors.Count > 1 ? "S" : "")}", Palette.Danger);
            if (m.Loaded) return ("ON", Palette.KwHeal);
            if (m.Enabled) return ("NOT LOADED", Palette.Danger);
            return ("OFF", Palette.TextDisabled);
        }

        private void DrawDetails(SpriteBatch sb, ModInfo m)
        {
            var p = DetailPanel;
            Core.Draw.Panel(sb, p);
            DrawIcon(sb, m, new Rectangle(p.X + 20, p.Y + 20, 112, 112));
            float x = p.X + 152, w = p.Right - 20 - x;
            Core.Draw.Text(sb, Assets.Header, Fit(m.Title, Assets.Header, w), new Vector2(x, p.Y + 16), Palette.Paper);
            Core.Draw.Text(sb, Assets.Body, Fit($"Version {m.Version}" + (string.IsNullOrEmpty(m.Author) ? "" : "   by " + m.Author), Assets.Body, w),
                new Vector2(x, p.Y + 62), Palette.Text, shadow: false);
            if (!string.IsNullOrEmpty(m.Website))
                Core.Draw.Text(sb, Assets.Small, Fit(m.Website, Assets.Small, w), new Vector2(x, p.Y + 88), Palette.TextDim, shadow: false);

            // Tags, then what's inside the folder.
            float tx = x;
            var parts = m.Tags.Select(t => (t, Palette.PartyTag)).Concat(Contents(m).Select(c => (c, Palette.PanelDark)));
            foreach (var (tag, fill) in parts)
            {
                float tw = Assets.Small.MeasureString(tag).X + 20;
                if (tx + tw > p.Right - 20) break;
                var chip = new Rectangle((int)tx, p.Y + 110, (int)tw, 24);
                Core.Draw.Slant(sb, chip, 6, fill);
                Core.Draw.TextCentered(sb, Assets.Small, tag, chip.Center.ToVector2(), Palette.Text, shadow: false);
                tx += tw + 6;
            }

            float y = p.Y + 150;
            float bottom = p.Bottom - 34;
            string desc = string.IsNullOrWhiteSpace(m.Description) ? "No description." : m.Description;
            y += RichText.Wrapped(sb, Assets.Body, desc, new Vector2(p.X + 22, y), p.Width - 44, Palette.Text, lineGap: 1) + 10;

            if (m.Dependencies.Length > 0)
            {
                string needs = string.Join(", ", m.Dependencies.Select(id =>
                    _order.FirstOrDefault(x => x.Id == id) is { } d ? d.Title + (_on[d] ? "" : " (off)") : id + " (not installed)"));
                Core.Draw.Text(sb, Assets.Small, Fit("Needs: " + needs, Assets.Small, p.Width - 44), new Vector2(p.X + 22, y), Palette.KwPriority, shadow: false);
                y += 24;
            }

            // Problems first, then what it changed this session.
            var lines = new List<(string, Color)>();
            foreach (var e in m.Errors) lines.Add(("! " + e, Palette.Danger));
            if (m.Loaded)
            {
                if (m.Notes.Count > 0) lines.Add(("WHAT IT CHANGES", Palette.TextDim));
                foreach (var n in m.Notes) lines.Add(("  " + n, Palette.Text));
            }
            else if (!Locked(m)) lines.Add(("Turn it on and restart to see what it changes.", Palette.TextDim));
            if (m.HasCode) lines.Add(("Runs code (DLL): only use it if you trust where it came from.", Palette.KwMark));

            int room = Math.Max(0, (int)((bottom - y) / 20));
            for (int i = 0; i < Math.Min(lines.Count, room); i++)
            {
                var (text, color) = i == room - 1 && lines.Count > room ? ($"  ...and {lines.Count - room + 1} more", Palette.TextDim) : lines[i];
                Core.Draw.Text(sb, Assets.Small, Fit(text, Assets.Small, p.Width - 44), new Vector2(p.X + 22, y), color, shadow: false);
                y += 20;
            }

            Core.Draw.Text(sb, Assets.Small, Fit("Folder: " + m.Folder, Assets.Small, p.Width - 44), new Vector2(p.X + 22, p.Bottom - 28), Palette.Grey500, shadow: false);
        }

        private static IEnumerable<string> Contents(ModInfo m)
        {
            if (Directory.Exists(m.DataDir)) yield return "Data";
            if (Directory.Exists(m.ArtDir)) yield return "Art";
            if (Directory.Exists(m.AudioDir)) yield return "Audio";
            if (m.HasCode) yield return "Code";
        }

        // The mod's icon.png (resampled to the exact screen size so it stays crisp), else a tile with its initials.
        private static void DrawIcon(SpriteBatch sb, ModInfo m, Rectangle box)
        {
            var frame = Icon(m);
            if (frame != null)
            {
                float fit = Math.Min(box.Width / (float)frame.W, box.Height / (float)frame.H);
                int w = (int)(frame.W * fit), h = (int)(frame.H * fit);
                var tex = frame.Scaled(Assets.Pixel.GraphicsDevice, (int)Math.Round(h * Core.Draw.Scale));
                sb.Draw(tex, new Rectangle(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h), Color.White);
                return;
            }
            int hash = m.Id.Aggregate(17, (a, c) => a * 31 + c);
            var tints = new[] { Palette.Rose, Palette.Violet, Palette.Coral, Palette.Lilac300, Palette.Grey700 };
            Core.Draw.Rect(sb, box, Color.Lerp(Palette.Ink, tints[Math.Abs(hash) % tints.Length], 0.75f));
            string initials = new string(m.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(s => char.ToUpperInvariant(s[0])).ToArray());
            var font = box.Height >= 80 ? Assets.Title : Assets.Header;
            Core.Draw.TextCentered(sb, font, initials, box.Center.ToVector2(), Palette.Paper);
        }

        private static SpriteFrame Icon(ModInfo m)
        {
            string path = m.IconPath;
            if (path == null) return null;
            if (_icons.TryGetValue(path, out var cached)) return cached;
            SpriteFrame frame = null;
            try
            {
                if (File.Exists(path))
                {
                    using var tex = Texture2D.FromFile(Assets.Pixel.GraphicsDevice, path);
                    var px = new Color[tex.Width * tex.Height];
                    tex.GetData(px);
                    for (int i = 0; i < px.Length; i++)
                    {
                        var c = px[i];
                        px[i] = new Color(c.R * c.A / 255, c.G * c.A / 255, c.B * c.A / 255, c.A);
                    }
                    frame = new SpriteFrame { Name = m.Id, W = tex.Width, H = tex.Height, Pixels = px };
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Mods] {m.Id}: icon {path}: {e.Message}");
            }
            _icons[path] = frame;
            return frame;
        }

        private static string Fit(string s, UiFont f, float width)
        {
            if (f.MeasureString(s).X <= width) return s;
            while (s.Length > 1 && f.MeasureString(s + "...").X > width) s = s[..^1];
            return s + "...";
        }
    }
}
