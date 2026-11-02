using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SERAAC.Core;

namespace SERAAC.UI
{
    // Mouse support without duplicating layout: while drawing, screens tag the rectangles they
    // already draw (Ui.Hit("actions", i, row)). On the next Update, Ui.Hover / Ui.Pick answer
    // "which item is under the mouse" from last frame's tags. Later tags sit on top, and
    // Ui.Block (a modal's backdrop) hides everything drawn before it.
    public static class Ui
    {
        private readonly struct Region
        {
            public readonly string Key;   // null = blocker
            public readonly int Index;
            public readonly Rectangle Rect;
            public Region(string key, int index, Rectangle rect) { Key = key; Index = index; Rect = rect; }
        }

        private static List<Region> _current = new(), _last = new();

        // Update frame in which a click hit something, or -1. After that frame the regions describe a
        // layout the click may have changed (new tab, closed modal), so they are ignored until the next
        // Draw. This matters when a slow frame makes MonoGame run several Updates without drawing.
        private static int _clickFrame = -1;

        // Game1 calls this before drawing screens.
        public static void BeginFrame()
        {
            (_last, _current) = (_current, _last);
            _current.Clear();
            _clickFrame = -1;
        }

        public static void Hit(string key, int index, Rectangle r) => _current.Add(new Region(key, index, r));
        public static void Hit(string key, Rectangle r) => Hit(key, 0, r);

        // Everything registered before this (in this rectangle) stops receiving the mouse.
        public static void Block(Rectangle r) => _current.Add(new Region(null, 0, r));
        public static void BlockAll() => Block(new Rectangle(0, 0, Game1.Width, Game1.Height));

        // Index of the item of `key` under the mouse, or -1.
        public static int Hover(string key)
        {
            if (_clickFrame >= 0 && Input.Frame > _clickFrame) return -1;
            var m = Input.MousePos;
            for (int i = _last.Count - 1; i >= 0; i--)
            {
                var r = _last[i];
                if (!r.Rect.Contains(m)) continue;
                if (r.Key == null) return -1;
                if (Input.Click && _clickFrame < 0) _clickFrame = Input.Frame;
                return r.Key == key ? r.Index : -1;
            }
            return -1;
        }

        public static bool IsHover(string key, int index = 0) => Hover(key) == index;

        // The clickable thing under the mouse (from last frame's tags), or null over nothing / a modal's backdrop.
        public static (string key, int index, Rectangle rect)? Under()
        {
            var m = Input.MousePos;
            for (int i = _last.Count - 1; i >= 0; i--)
            {
                var r = _last[i];
                if (!r.Rect.Contains(m)) continue;
                return r.Key == null ? null : (r.Key, r.Index, r.Rect);
            }
            return null;
        }

        // True when `key` item was clicked this frame.
        public static bool Clicked(string key, int index = 0) => Input.Click && Hover(key) == index;

        // Index clicked this frame, or -1.
        public static int ClickedIndex(string key) => Input.Click ? Hover(key) : -1;

        // Keeps a keyboard-driven selection in sync with the mouse: moving over an item selects it,
        // clicking it selects it and returns true (the screen then treats it like Enter).
        public static bool Pick(string key, ref int selected)
        {
            int h = Hover(key);
            if (h < 0) return false;
            if (Input.MouseMoved) selected = h;
            if (!Input.Click) return false;
            selected = h;
            return true;
        }

        // ------------------------------------------------------------------ drawing helpers

        // Seconds since start, for the small idle animations (cursor bob, embers).
        public static double Time => Environment.TickCount64 / 1000.0;

        // A labelled button that registers itself. Returns its rectangle.
        public static Rectangle Button(SpriteBatch sb, string key, int index, Rectangle r, string label, bool enabled = true,
                                       bool selected = false, UiFont font = null)
        {
            font ??= Assets.Body;
            bool hover = enabled && Hover(key) == index;
            // Dark and thinly outlined; the selected one is a solid accent plate, the hovered one glows from the left.
            if (selected && enabled)
            {
                Core.Draw.Rect(sb, r, Palette.Crimson);
                Core.Draw.GradientH(sb, r, Palette.Rose, 0.3f);
                Core.Draw.Border(sb, r, Palette.LineBright, 1);
            }
            else
            {
                Core.Draw.Rect(sb, r, Palette.Ink * (enabled ? 0.6f : 0.4f));
                if (hover) Core.Draw.GradientH(sb, r, Palette.Crimson * 0.9f);
                Core.Draw.Border(sb, r, hover ? Palette.LineBright : Palette.Line, 1);
            }
            Core.Draw.TextCentered(sb, font, label, r.Center.ToVector2(),
                !enabled ? Palette.TextDisabled : selected || hover ? Palette.Paper : Palette.Text * 0.85f, shadow: selected);
            if (enabled) Hit(key, index, r);
            return r;
        }

        // Soft highlight for list rows under the mouse (keyboard selection draws its own).
        public static void HoverRow(SpriteBatch sb, string key, int index, Rectangle r, bool selected)
        {
            if (!selected && Hover(key) == index)
            {
                Core.Draw.GradientH(sb, r, Palette.Crimson * 0.7f);
                Core.Draw.Border(sb, r, Palette.Line, 1);
            }
            Hit(key, index, r);
        }

        // A tall list card (areas, trials, characters): thinly outlined; the selected one glows from an accent
        // bar on its left edge and gets a bright outline.
        public static void Card(SpriteBatch sb, Rectangle r, bool selected, Color? fill = null)
        {
            Core.Draw.Rect(sb, r, (fill ?? Palette.Panel) * 0.85f);
            if (selected)
            {
                Core.Draw.GradientH(sb, r, Palette.Crimson, 0.1f);
                Core.Draw.Border(sb, r, Palette.LineBright, 1);
            }
            else Core.Draw.Border(sb, r, Palette.Line, 1);
            Core.Draw.Rect(sb, new Rectangle(r.X, r.Y, 3, r.Height), selected ? Palette.Rose : Palette.Grey700);
        }

        // A small "?" button that shows the controls for this screen while hovered, or while F1 is held.
        // Keeps key hints off the screen until someone wants them.
        public static void HelpButton(SpriteBatch sb, Vector2 center, string controls, string title = "Controls")
        {
            var r = new Rectangle((int)center.X - 13, (int)center.Y - 13, 26, 26);
            bool open = IsHover("help") || Input.Down(Microsoft.Xna.Framework.Input.Keys.F1);
            Core.Draw.Diamond(sb, center, 22, open ? Palette.Rose : Palette.Ink * 0.7f);
            Core.Draw.DiamondOutline(sb, center, 30, open ? Palette.LineBright : Palette.Line, 1);
            Core.Draw.TextCentered(sb, Assets.Body, "?", center + new Vector2(0, -1), Palette.Paper, shadow: false);
            Hit("help", r);
            if (open) Tooltip.Draw(sb, r, title, controls + "\n(F1 shows this anywhere)", Palette.Rose, null, 360);
        }

        // The selected row of a list: an accent glow inside a bright outline, with the pointer at its left edge.
        public static void SelectedRow(SpriteBatch sb, Rectangle r, bool dim = false, bool pointer = true)
        {
            Core.Draw.HighlightBar(sb, r, dim ? Palette.Grey700 : Palette.Rose);
            if (pointer && !dim) Core.Draw.Pointer(sb, new Vector2(r.X - 2, r.Center.Y), Time);
        }

        // A screen title: the crest, then the name in display type, with an optional caption after it.
        public static void ScreenTitle(SpriteBatch sb, string title, Vector2 pos, string subtitle = null)
        {
            var size = Assets.Title.MeasureString(title.ToUpperInvariant());
            Core.Draw.Ornament(sb, new Vector2(pos.X + 4, pos.Y + size.Y * 0.5f), 18, Palette.Paper * 0.85f);
            var textPos = new Vector2(pos.X + 26, pos.Y);
            Core.Draw.Text(sb, Assets.Title, title.ToUpperInvariant(), textPos, Palette.Paper);
            if (subtitle != null)
                Core.Draw.Text(sb, Assets.Body, subtitle, new Vector2(textPos.X + 2, pos.Y + size.Y + 2), Palette.TextDim, shadow: false);
        }

        // A section header: an accent bar that fades out to the right, with a chevron and the section name.
        public static void SectionBar(SpriteBatch sb, Rectangle r, string text, Color? accent = null)
        {
            var c = accent ?? Palette.Rose;
            Core.Draw.GradientH(sb, r, Color.Lerp(c, Palette.Ink, 0.15f), 0.45f);
            // A grainy band near the fade, like worn paint.
            for (int i = 0; i < 40; i++)
            {
                int h = (i * 7919) % 97;
                int x = r.X + (int)(r.Width * (0.55f + 0.4f * (h / 97f)));
                int y = r.Y + (i * 31) % Math.Max(1, r.Height - 1);
                Core.Draw.Rect(sb, new Rectangle(x, y, 2, 1), Palette.Ink * 0.35f);
            }
            float cy = r.Center.Y;
            var f = Assets.Small;
            var ts = f.MeasureString(text);
            Core.Draw.Line(sb, new Vector2(r.X + 11, cy - 4), new Vector2(r.X + 15, cy), Palette.Paper, 1.5f);
            Core.Draw.Line(sb, new Vector2(r.X + 15, cy), new Vector2(r.X + 11, cy + 4), Palette.Paper, 1.5f);
            Core.Draw.Text(sb, f, text, new Vector2(r.X + 24, cy - ts.Y / 2), Palette.Paper, shadow: false);
        }

        // The top menu bar (FF-style): names spread across, the current one bright with an accent line under it,
        // and key caps at both ends for cycling. Registers each name as `key` / index for clicks.
        public static void TabBar(SpriteBatch sb, string key, IReadOnlyList<string> names, int selected, Rectangle r,
                                  string prevKey = "Q", string nextKey = "E")
        {
            Core.Draw.Rect(sb, r, Palette.Ink * 0.55f);
            Core.Draw.Rect(sb, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), Palette.Line);
            int capW = 46;
            KeyCap(sb, prevKey, new Vector2(r.X + capW / 2 + 8, r.Center.Y));
            KeyCap(sb, nextKey, new Vector2(r.Right - capW / 2 - 8, r.Center.Y));
            float slotW = (r.Width - capW * 2f - 16) / names.Count;
            for (int i = 0; i < names.Count; i++)
            {
                var cell = new Rectangle((int)(r.X + capW + 8 + i * slotW), r.Y, (int)slotW, r.Height);
                bool sel = i == selected;
                bool hover = !sel && Hover(key) == i;
                if (sel)
                {
                    Core.Draw.GradientV(sb, new Rectangle(cell.X + 10, cell.Y, cell.Width - 20, cell.Height), Palette.Crimson * 0.6f, fromBottom: true);
                    Core.Draw.GlowLine(sb, new Vector2(cell.Center.X, cell.Bottom - 2), cell.Width - 20, Palette.Rose, 10);
                    Core.Draw.Rect(sb, new Rectangle(cell.X + 10, cell.Bottom - 3, cell.Width - 20, 3), Palette.Rose);
                }
                Core.Draw.TextCentered(sb, Assets.Body, names[i], cell.Center.ToVector2() + new Vector2(0, -1),
                    sel ? Palette.Paper : hover ? Palette.Text * 0.9f : Palette.Text * 0.55f, shadow: sel);
                Hit(key, i, cell);
            }
        }

        // A keyboard key drawn as a small rounded cap ("Q", "Esc", "Enter").
        public static float KeyCap(SpriteBatch sb, string label, Vector2 center)
        {
            var size = Assets.Small.MeasureString(label);
            int w = Math.Max(22, (int)size.X + 12), h = 20;
            var r = new Rectangle((int)(center.X - w / 2f), (int)(center.Y - h / 2f), w, h);
            Core.Draw.Rect(sb, new Rectangle(r.X + 1, r.Y, r.Width - 2, r.Height), Palette.Paper * 0.88f);
            Core.Draw.Rect(sb, new Rectangle(r.X, r.Y + 1, r.Width, r.Height - 2), Palette.Paper * 0.88f);
            Core.Draw.TextCentered(sb, Assets.Small, label, center, Palette.Ink, shadow: false);
            return w;
        }

        // The bottom bar with control hints, right aligned: ("Esc", "Close"), ("Enter", "Confirm")...
        // Each hint is clickable as `key` / index when a key is given.
        public static void Footer(SpriteBatch sb, string key, params (string cap, string label)[] hints)
        {
            var r = new Rectangle(0, Game1.Height - 40, Game1.Width, 40);
            Core.Draw.Rect(sb, r, Palette.Ink * 0.6f);
            Core.Draw.Rect(sb, new Rectangle(0, r.Y, r.Width, 1), Palette.Line);
            float x = r.Right - 28;
            for (int i = hints.Length - 1; i >= 0; i--)
            {
                var (cap, label) = hints[i];
                var ls = Assets.Body.MeasureString(label);
                x -= ls.X;
                var labelPos = new Vector2(x, r.Center.Y - ls.Y / 2);
                bool hover = key != null && Hover(key) == i;
                Core.Draw.Text(sb, Assets.Body, label, labelPos, hover ? Palette.Accent : Palette.Text, shadow: false);
                float capW = Math.Max(22, Assets.Small.MeasureString(cap).X + 12);
                x -= 10 + capW / 2;
                KeyCap(sb, cap, new Vector2(x, r.Center.Y));
                x -= capW / 2;
                if (key != null) Hit(key, i, new Rectangle((int)x - 4, r.Y + 6, (int)(labelPos.X + ls.X - x) + 8, r.Height - 12));
                x -= 36;
            }
        }

        // An announcement band ("ENCOUNTER", "Wyvern Slain"): a dark strip across `width`, hairlines on its edges with
        // lines of light at their middles, and the text spaced out in the centre. `open` (0..1) opens it from its middle.
        public static void Banner(SpriteBatch sb, string text, Vector2 center, float width, Color c, float open,
                                  UiFont font = null, float height = 88f, float tracking = 3f)
        {
            font ??= Assets.Title;
            open = MathHelper.Clamp(open, 0, 1);
            if (open <= 0.001f) return;
            float half = height / 2 * open;
            var band = new Rectangle((int)(center.X - width / 2), (int)(center.Y - half), (int)width, (int)(half * 2));
            Core.Draw.Rect(sb, band, Palette.Ink * 0.6f);
            Core.Draw.SoftGlow(sb, center, new Vector2(width * 0.6f, half * 3.2f), c * 0.22f);
            Core.Draw.Divider(sb, new Vector2(center.X, band.Y), width, c * 0.35f);
            Core.Draw.Divider(sb, new Vector2(center.X, band.Bottom - 1), width, c * 0.35f);
            float glowW = Math.Min(width * 0.5f, 620) * open;
            Core.Draw.GlowLine(sb, new Vector2(center.X, band.Y), glowW, c, 12);
            Core.Draw.GlowLine(sb, new Vector2(center.X, band.Bottom - 1), glowW, c, 12);
            Core.Draw.SpacedCentered(sb, font, text, center, Palette.Paper * open, tracking);
        }

        // A full-screen menu backdrop: near-black, a warm accent glow from one side and a cool one from the other,
        // a few long thin light lines, and embers drifting up.
        public static void Backdrop(SpriteBatch sb, double t, float warmth = 1f)
        {
            Core.Draw.Rect(sb, new Rectangle(0, 0, Game1.Width, Game1.Height), Color.Lerp(Palette.Ink, Palette.Grey900, 0.25f));
            Core.Draw.GradientH(sb, new Rectangle(0, 0, Game1.Width, Game1.Height), Palette.Crimson * 0.75f * warmth);
            Core.Draw.GradientH(sb, new Rectangle(Game1.Width / 3, 0, Game1.Width * 2 / 3, Game1.Height), Color.Lerp(Palette.Ink, Palette.Violet, 0.22f) * 0.8f, fromRight: true);
            Core.Draw.SoftGlow(sb, new Vector2(Game1.Width * 0.18f, Game1.Height * 0.2f), new Vector2(900, 700), Palette.Rose * 0.12f * warmth);
            Core.Draw.GradientV(sb, new Rectangle(0, Game1.Height / 2, Game1.Width, Game1.Height / 2), Palette.Ink * 0.7f, fromBottom: true);
            // Long hairlines sweeping across the corners.
            Core.Draw.Line(sb, new Vector2(-40, 520), new Vector2(380, Game1.Height + 20), Palette.Gold * 0.25f, 1);
            Core.Draw.Line(sb, new Vector2(Game1.Width - 330, -10), new Vector2(Game1.Width + 20, 300), Palette.Paper * 0.2f, 1);
            Core.Draw.Line(sb, new Vector2(Game1.Width - 260, -10), new Vector2(Game1.Width + 20, 220), Palette.Paper * 0.12f, 1);
            Embers(sb, t, warmth);
        }

        // Sparks rising and flickering. Deterministic from the time, so nothing to update.
        public static void Embers(SpriteBatch sb, double t, float amount = 1f, int count = 70)
        {
            for (int i = 0; i < count; i++)
            {
                uint h = (uint)(i * 2654435761u);
                float u = (h & 0xffff) / 65535f, v = ((h >> 16) & 0xffff) / 65535f;
                float speed = 14 + 30 * v;
                float life = (float)((t * speed / Game1.Height + u) % 1.0);
                // Most embers live on the warm (left) side.
                float x = (float)(Math.Pow(u, 1.6) * Game1.Width * 0.9 + Math.Sin(t * (0.6 + v) + i) * 18);
                float y = Game1.Height - life * (Game1.Height + 40);
                float flicker = 0.55f + 0.45f * (float)Math.Sin(t * (3 + 4 * v) + i * 1.7);
                float a = MathHelper.Clamp(Math.Min(life * 5, (1 - life) * 3), 0, 1) * flicker * amount * (1 - u * 0.6f);
                int s = v > 0.8f ? 3 : 2;
                Core.Draw.Rect(sb, new Rectangle((int)x, (int)y, s, s), Color.Lerp(Palette.Ember, Palette.Gold, v) * a);
            }
        }
    }
}
