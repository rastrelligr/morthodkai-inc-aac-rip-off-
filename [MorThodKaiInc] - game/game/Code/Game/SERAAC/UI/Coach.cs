using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SERAAC.Core;

namespace SERAAC.UI
{
    // One thing the tutorial points at. Done == null means the player only has to read it (click or Enter).
    public sealed class CoachStep
    {
        public string Text;
        public Func<Rectangle?> Target;   // where the spotlight goes; null = dim the whole screen
        public Func<bool> When;           // waits (and shows nothing) until this is true
        public Func<bool> Done;           // the player did the thing
        public Func<bool> Back;           // the player undid the previous step: go back to it
        public bool Soft;                 // only a marker and a bubble: nothing darkens, nothing is blocked
        public Action OnStart;
    }

    // Runs a list of steps in order. The screen calls Update first (true = the step took this frame's input)
    // and Draw last, over everything it drew.
    public sealed class Coach
    {
        private readonly List<CoachStep> _steps;
        private int _index;
        private int _started = -1;

        public Coach(IEnumerable<CoachStep> steps) { _steps = steps.ToList(); }

        public CoachStep Current => _index < _steps.Count ? _steps[_index] : null;
        public bool Finished => Current == null;
        public bool OnLastStep => _index >= _steps.Count - 1;
        public bool Showing => Current != null && (Current.When?.Invoke() ?? true);

        public bool Update()
        {
            while (Current?.Done?.Invoke() == true) _index++;
            if (Current?.Back?.Invoke() == true && _index > 0) _index--;
            if (!Showing) return false;
            if (_started != _index)
            {
                _started = _index;
                Current.OnStart?.Invoke();
            }
            if (Current.Done != null) return false;
            if (Input.Confirm || Input.Click) _index++;
            return true;   // reading: nothing behind the spotlight reacts
        }

        public void Draw(SpriteBatch sb)
        {
            if (Showing) Spotlight.Draw(sb, Current.Target?.Invoke(), Current.Text, Current.Done == null, Current.Soft);
        }
    }

    // Tips that aren't a sequence: each shows when its moment comes, in whatever order the player gets there.
    public sealed class CoachTips
    {
        private readonly List<CoachStep> _waiting;
        private Coach _now;

        public CoachTips(IEnumerable<CoachStep> tips) { _waiting = tips.ToList(); }

        public bool Update()
        {
            if (_now == null || _now.Finished)
            {
                var next = _waiting.FirstOrDefault(s => s.When?.Invoke() ?? true);
                if (next == null) { _now = null; return false; }
                _waiting.Remove(next);
                _now = new Coach(new[] { next });
            }
            return _now.Update();
        }

        public void Draw(SpriteBatch sb) => _now?.Draw(sb);
    }

    // Darkens the screen around one rectangle, with a speech bubble pointing at it. The mouse only reaches
    // what is inside the hole.
    public static class Spotlight
    {
        private const float Dim = 0.68f;
        private const int Pad = 6, Feather = 26;
        private static Texture2D _corner;

        public static Rectangle? LastHole { get; private set; }   // the script runner clicks it

        public static void Draw(SpriteBatch sb, Rectangle? target, string text, bool reading, bool soft = false)
        {
            LastHole = target;
            if (target == null && !reading) return;   // the thing isn't on screen yet (it was only just opened)
            if (soft && target is { } marked)
            {
                marked.Inflate(Pad, Pad);
                Brackets(sb, marked);
                Bubble(sb, marked, text, false);
                return;
            }
            var dim = Palette.Ink * Dim;
            var full = new Rectangle(0, 0, Game1.Width, Game1.Height);
            if (target == null)
            {
                Core.Draw.Rect(sb, full, dim);
                Ui.BlockAll();
                Bubble(sb, null, text, reading);
                return;
            }

            var hole = target.Value;
            hole.Inflate(Pad, Pad);
            Mask(sb, hole, dim);
            Brackets(sb, hole);
            if (reading) Ui.BlockAll();
            else
            {
                Ui.Block(new Rectangle(0, 0, full.Width, hole.Y));
                Ui.Block(new Rectangle(0, hole.Bottom, full.Width, full.Height - hole.Bottom));
                Ui.Block(new Rectangle(0, hole.Y, hole.X, hole.Height));
                Ui.Block(new Rectangle(hole.Right, hole.Y, full.Width - hole.Right, hole.Height));
            }
            Bubble(sb, hole, text, reading);
        }

        // The dark frame: solid outside, a soft rounded edge around the hole.
        private static void Mask(SpriteBatch sb, Rectangle hole, Color c)
        {
            var tex = Corner(sb.GraphicsDevice);
            int f = Feather;
            var o = new Rectangle(hole.X - f, hole.Y - f, hole.Width + 2 * f, hole.Height + 2 * f);
            Core.Draw.Rect(sb, new Rectangle(0, 0, Game1.Width, o.Y), c);
            Core.Draw.Rect(sb, new Rectangle(0, o.Bottom, Game1.Width, Game1.Height - o.Bottom), c);
            Core.Draw.Rect(sb, new Rectangle(0, o.Y, o.X, o.Height), c);
            Core.Draw.Rect(sb, new Rectangle(o.Right, o.Y, Game1.Width - o.Right, o.Height), c);

            void Piece(Rectangle dest, Rectangle src, SpriteEffects fx) => sb.Draw(tex, dest, src, c, 0f, Vector2.Zero, fx, 0f);
            var corner = new Rectangle(0, 0, f, f);
            var vertical = new Rectangle(f - 1, 0, 1, f);     // dark at the top, clear at the bottom
            var horizontal = new Rectangle(0, f - 1, f, 1);   // dark at the left, clear at the right
            const SpriteEffects H = SpriteEffects.FlipHorizontally, V = SpriteEffects.FlipVertically;
            Piece(new Rectangle(o.X, o.Y, f, f), corner, SpriteEffects.None);
            Piece(new Rectangle(hole.Right, o.Y, f, f), corner, H);
            Piece(new Rectangle(o.X, hole.Bottom, f, f), corner, V);
            Piece(new Rectangle(hole.Right, hole.Bottom, f, f), corner, H | V);
            Piece(new Rectangle(hole.X, o.Y, hole.Width, f), vertical, SpriteEffects.None);
            Piece(new Rectangle(hole.X, hole.Bottom, hole.Width, f), vertical, V);
            Piece(new Rectangle(o.X, hole.Y, f, hole.Height), horizontal, SpriteEffects.None);
            Piece(new Rectangle(hole.Right, hole.Y, f, hole.Height), horizontal, H);
        }

        // A quarter of the soft edge: clear around the inner corner, dark past Feather pixels from it.
        private static Texture2D Corner(GraphicsDevice gd)
        {
            if (_corner != null) return _corner;
            int f = Feather;
            var px = new Color[f * f];
            for (int y = 0; y < f; y++)
                for (int x = 0; x < f; x++)
                {
                    float d = new Vector2(f - 0.5f - x, f - 0.5f - y).Length();
                    float a = MathHelper.SmoothStep(0, 1, MathHelper.Clamp((d - 4f) / (f - 4f), 0, 1));
                    px[y * f + x] = Color.White * a;
                }
            _corner = new Texture2D(gd, f, f);
            _corner.SetData(px);
            return _corner;
        }

        // The same corner brackets the fight uses to mark a target, breathing in and out.
        private static void Brackets(SpriteBatch sb, Rectangle hole)
        {
            int push = 3 + (int)Math.Round(3 * (0.5 + 0.5 * Math.Sin(Ui.Time * 5)));
            var r = hole;
            r.Inflate(push, push);
            int len = Math.Min(22, Math.Min(r.Width, r.Height) / 3);
            const int w = 3;
            var c = Palette.Paper;
            Core.Draw.Rect(sb, new Rectangle(r.X, r.Y, len, w), c);
            Core.Draw.Rect(sb, new Rectangle(r.X, r.Y, w, len), c);
            Core.Draw.Rect(sb, new Rectangle(r.Right - len, r.Y, len, w), c);
            Core.Draw.Rect(sb, new Rectangle(r.Right - w, r.Y, w, len), c);
            Core.Draw.Rect(sb, new Rectangle(r.X, r.Bottom - w, len, w), c);
            Core.Draw.Rect(sb, new Rectangle(r.X, r.Bottom - len, w, len), c);
            Core.Draw.Rect(sb, new Rectangle(r.Right - len, r.Bottom - w, len, w), c);
            Core.Draw.Rect(sb, new Rectangle(r.Right - w, r.Bottom - len, w, len), c);
        }

        // Above the hole if it fits, else below it, else in the middle of the screen. Bobs gently.
        private static void Bubble(SpriteBatch sb, Rectangle? hole, string text, bool reading)
        {
            const string cue = "Click or press Enter";
            const int padX = 20, padY = 12, gap = 26;
            var lines = text.Split('\n');
            float lineH = Assets.Body.MeasureString("Ag").Y;
            float cueH = reading ? Assets.Small.MeasureString(cue).Y + 4 : 0;
            int w = (int)lines.Max(l => Assets.Body.MeasureString(l).X) + padX * 2;
            w = Math.Max(w, reading ? (int)Assets.Small.MeasureString(cue).X + padX * 2 : 0);
            int h = (int)(lines.Length * lineH + cueH) + padY * 2;
            int bob = (int)Math.Round(3 * Math.Sin(Ui.Time * 3));

            int x = Game1.Width / 2 - w / 2, y = Game1.Height / 2 - h / 2;
            int arrow = 0;   // +1 points down at the hole, -1 points up at it
            if (hole is { } t)
            {
                x = MathHelper.Clamp(t.Center.X - w / 2, 12, Game1.Width - w - 12);
                if (t.Y - gap - h >= 12) { y = t.Y - gap - h + bob; arrow = 1; }
                else if (t.Bottom + gap + h <= Game1.Height - 12) { y = t.Bottom + gap + bob; arrow = -1; }
            }

            var box = new Rectangle(x, y, w, h);
            if (arrow != 0)
            {
                var tip = new Vector2(MathHelper.Clamp(hole.Value.Center.X, box.X + 18, box.Right - 18), arrow > 0 ? box.Bottom : box.Y);
                Core.Draw.Diamond(sb, tip, 26, Palette.Ink);
                Core.Draw.Diamond(sb, tip, 20, Palette.Paper);
            }
            Core.Draw.Rect(sb, new Rectangle(box.X - 2, box.Y, box.Width + 4, box.Height), Palette.Ink);
            Core.Draw.Rect(sb, new Rectangle(box.X, box.Y - 2, box.Width, box.Height + 4), Palette.Ink);
            Core.Draw.Rect(sb, new Rectangle(box.X + 1, box.Y, box.Width - 2, box.Height), Palette.Paper);
            Core.Draw.Rect(sb, new Rectangle(box.X, box.Y + 1, box.Width, box.Height - 2), Palette.Paper);

            for (int i = 0; i < lines.Length; i++)
            {
                float lw = Assets.Body.MeasureString(lines[i]).X;
                Core.Draw.Text(sb, Assets.Body, lines[i], new Vector2(box.Center.X - lw / 2, box.Y + padY + i * lineH), Palette.Ink, shadow: false);
            }
            if (reading)
            {
                float cw = Assets.Small.MeasureString(cue).X;
                float pulse = 0.65f + 0.35f * (float)Math.Sin(Ui.Time * 4);
                Core.Draw.Text(sb, Assets.Small, cue, new Vector2(box.Center.X - cw / 2, box.Bottom - padY - cueH + 4), Palette.Crimson * pulse, shadow: false);
            }
        }
    }
}
