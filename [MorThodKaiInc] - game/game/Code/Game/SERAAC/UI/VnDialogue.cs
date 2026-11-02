using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SERAAC.Core;

namespace SERAAC.UI
{
    // One line of a visual-novel scene. Speaker null = narration. Everything but Text is optional.
    public sealed class VnLine
    {
        public string Speaker;
        public string Text;
        public Func<string> TextFn;          // for text with live numbers
        public string Portrait;              // Art id; null = the speaker's default (VnCast)
        public string Side;                  // "left" / "right"; null = the speaker's default
        public string Background;            // switch the full-screen picture from this line on; "none" removes it
        public bool Card;                    // a centred title card on black ("Later, the training hall")
        public Action OnShow;                // side effects when the line appears
        public List<VnChoice> Choices;       // shown under the text once it is fully typed

        public string Resolve() => TextFn?.Invoke() ?? Text ?? "";

        public static VnLine Say(string speaker, string text) => new() { Speaker = speaker, Text = text };
        public static VnLine Narrate(string text) => new() { Text = text };
        public static VnLine TitleCard(string text) => new() { Text = text, Card = true };
    }

    public sealed class VnChoice
    {
        public string Label;
        public Action Pick;
        public VnChoice(string label, Action pick) { Label = label; Pick = pick; }
    }

    // Who speaks in the story: portrait, name colour and which side they stand on.
    public static class VnCast
    {
        public sealed class Member
        {
            public string Portrait;
            public Color NameColor;
            public string Side;
        }

        public static readonly Dictionary<string, Member> All = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Relle"] = new() { Portrait = "Dialogue/Portrait/Portrait_Relle", NameColor = Palette.Blush, Side = "left" },
            ["Naomi"] = new() { Portrait = "Dialogue/Portrait/Portrait_Naomi", NameColor = Palette.Lilac100, Side = "right" },
        };

        public static Member Get(string name) => name != null && All.TryGetValue(name, out var m) ? m : null;
    }

    // Visual-novel dialogue: portraits on both sides, a name plate, typewriter text, choices, and the usual
    // VN controls: backlog (L / wheel up), auto (F), skip (Tab, or hold Ctrl), hide the box (H / right click).
    // No WASD keys, so walking keys pressed during a scene never toggle anything.
    // A scene is a queue of lines; choices can queue more (Insert) so branching works too.
    public sealed class VnDialogue
    {
        private readonly LinkedList<VnLine> _queue = new();
        private VnLine _line;
        private string _text = "";
        private float _typed;
        private float _lineTime;
        private int _choice;

        private readonly List<(string speaker, string text)> _log = new();
        private bool _logOpen;
        private float _logScroll;   // pixels scrolled up from the newest entry

        public bool Auto, Skip, Hidden;
        private float _autoWait;

        // Portraits: whoever stands on each side, sliding / fading in.
        private sealed class Slot { public string Speaker, Art; public float In, Talk; }
        private readonly Slot _left = new(), _right = new();

        // Full-screen picture, crossfading when a line changes it.
        private string _bg, _bgPrev;
        private float _bgFade = 1f, _time;

        public bool Finished { get; private set; }
        public float Alpha = 1f;   // the whole box (screens fade it in)

        private const float TypeSpeed = 55f;       // characters per second
        private const float SkipDelay = 0.06f;      // seconds per line while skipping
        private const int BoxTop = 532;

        public VnDialogue(IEnumerable<VnLine> lines, string background = null)
        {
            _bg = Clean(background);
            Insert(lines);
            Next();
        }

        public bool HasBackground => _bg != null || (_bgPrev != null && _bgFade < 1f);

        // Queues lines to play right after the current one (choices use this to branch).
        public void Insert(IEnumerable<VnLine> lines)
        {
            var node = (LinkedListNode<VnLine>)null;
            foreach (var l in lines)
                node = node == null ? _queue.AddFirst(l) : _queue.AddAfter(node, l);
        }

        public void Append(IEnumerable<VnLine> lines)
        {
            foreach (var l in lines) _queue.AddLast(l);
        }

        // Drops everything still queued (a choice that ends the scene).
        public void ClearQueue() => _queue.Clear();

        private static string Clean(string name) => string.IsNullOrEmpty(name) || name == "none" ? null : name;

        private void Next()
        {
            if (_line != null && !_line.Card && _text.Length > 0) _log.Add((_line.Speaker, _text));
            if (_queue.Count == 0)
            {
                _line = null;
                Finished = true;
                return;
            }
            _line = _queue.First.Value;
            _queue.RemoveFirst();
            _line.OnShow?.Invoke();
            _text = _line.Resolve();
            _typed = 0;
            _lineTime = 0;
            _choice = 0;
            _autoWait = 0;

            if (_line.Background != null && Clean(_line.Background) != _bg)
            {
                _bgPrev = _bg;
                _bg = Clean(_line.Background);
                _bgFade = 0;
            }

            if (_line.Speaker != null && !_line.Card)
            {
                var cast = VnCast.Get(_line.Speaker);
                string art = _line.Portrait ?? cast?.Portrait;
                string side = _line.Side ?? cast?.Side ?? "right";
                var slot = side == "left" ? _left : _right;
                if (art != null && (slot.Speaker != _line.Speaker || slot.Art != art))
                {
                    bool same = slot.Speaker == _line.Speaker;
                    slot.Speaker = _line.Speaker;
                    slot.Art = art;
                    if (!same) slot.In = 0;
                }
            }
        }

        private bool TextDone => _typed >= _text.Length;
        private List<VnChoice> Choices => _line?.Choices ?? (List<VnChoice>)null;
        private bool WaitingOnChoice => TextDone && Choices is { Count: > 0 };

        // ------------------------------------------------------------------ update

        public void Update(float dt)
        {
            _time += dt;
            _bgFade = Math.Min(1f, _bgFade + dt / 0.45f);
            foreach (var s in new[] { _left, _right })
            {
                s.In = Math.Min(1f, s.In + dt * 4f);
                bool talking = _line != null && s.Speaker == _line.Speaker;
                s.Talk = MathHelper.Lerp(s.Talk, talking ? 1f : 0f, 1 - (float)Math.Pow(0.001, dt));
            }
            if (Finished || _line == null) return;

            if (_logOpen) { UpdateLog(); return; }

            // Toggles: keyboard or the buttons on the box.
            int button = Ui.ClickedIndex("vnbtn");
            if (Input.Pressed(Keys.L) || button == 0 || (Input.Wheel > 0 && !Hidden)) { OpenLog(); return; }
            if (Input.Pressed(Keys.F) || button == 1) { Auto = !Auto; Skip = false; return; }
            if (Input.Pressed(Keys.Tab) || button == 2) { Skip = !Skip; Auto = false; return; }
            if (Input.Pressed(Keys.H) || button == 3 || Input.RightClick) { Hidden = !Hidden; return; }
            if (Hidden)
            {
                // Any press brings the box back (and does nothing else).
                if (Input.Click || Input.Confirm || Input.Pressed(Keys.Escape)) Hidden = false;
                return;
            }

            bool holdSkip = Input.Down(Keys.LeftControl) || Input.Down(Keys.RightControl);
            bool skipping = (Skip || holdSkip) && !WaitingOnChoice;
            _lineTime += dt;
            int before = (int)_typed;
            _typed += dt * TypeSpeed * (Input.FastForward && !Input.LeftDown ? 2.5f : 1f);
            // Typewriter clicks: one every few letters while the line is typing.
            if (!skipping && before < _text.Length && (int)_typed / 3 != before / 3 && _text[Math.Min(before, _text.Length - 1)] != ' ')
                Audio.Play("type", 0.25f);

            if (skipping)
            {
                _typed = _text.Length;
                if (_lineTime >= SkipDelay) Next();
                return;
            }

            if (WaitingOnChoice)
            {
                var ch = Choices;
                if (Input.Up) _choice = (_choice + ch.Count - 1) % ch.Count;
                if (Input.DownKey) _choice = (_choice + 1) % ch.Count;
                if (Input.Pressed(Keys.Enter) || Input.Pressed(Keys.Space) || Input.Pressed(Keys.Z) || Ui.Pick("vnchoice", ref _choice))
                {
                    var pick = ch[_choice];
                    _log.Add((_line.Speaker, _text));
                    _log.Add(("", "> " + pick.Label));
                    _text = "";   // already logged
                    pick.Pick?.Invoke();
                    Next();
                }
                return;
            }

            bool advance = Input.Pressed(Keys.Enter) || Input.Pressed(Keys.Space) || Input.Pressed(Keys.Z)
                           || (Input.Click && Ui.Hover("vnbtn") < 0) || Input.Wheel < 0;
            if (!TextDone)
            {
                if (advance) _typed = _text.Length;
                return;
            }
            if (Auto)
            {
                _autoWait += dt;
                if (_autoWait >= 1.1f + _text.Length * 0.03f) { Next(); return; }
            }
            if (advance) Next();
        }

        // ------------------------------------------------------------------ backlog

        private void OpenLog()
        {
            if (_log.Count == 0 && _text.Length == 0) return;
            _logOpen = true;
            _logScroll = 0;
            Skip = Auto = false;
        }

        private void UpdateLog()
        {
            if (Input.Wheel != 0) _logScroll += Input.Wheel > 0 ? 60 : -60;
            if (Input.Up) _logScroll += 40;
            if (Input.DownKey) _logScroll -= 40;
            if (Input.Pressed(Keys.PageUp)) _logScroll += 300;
            if (Input.Pressed(Keys.PageDown)) _logScroll -= 300;
            bool close = Input.Pressed(Keys.L) || Input.Pressed(Keys.Escape) || Input.RightClick || Ui.Clicked("vnlogclose")
                         || (_logScroll < -40);
            _logScroll = MathHelper.Clamp(_logScroll, -60, Math.Max(0, _logHeight - LogView.Height + 20));
            if (close) _logOpen = false;
        }

        private float _logHeight;
        private static readonly Rectangle LogView = new(170, 90, 940, 560);

        // ------------------------------------------------------------------ draw

        // The full-screen picture (if any) and the portraits, then the box. Draw over whatever the screen shows.
        public void Draw(SpriteBatch sb)
        {
            if (Finished && _line == null && Alpha <= 0) return;
            DrawBackground(sb);
            if (_line != null && _line.Card) { DrawCard(sb); return; }
            if (!Hidden) DrawPortraits(sb);
            if (_logOpen) { DrawLog(sb); return; }
            if (Hidden || _line == null) return;
            DrawBox(sb);
        }

        // Art/Dialogue/Background/<name>, Art/Dialogue/<name>, or a combat background from Art/Stages (all its layers).
        public static List<(Texture2D tex, float depth)> BackgroundArt(string name)
        {
            var list = new List<(Texture2D, float)>();
            if (name == null) return list;
            var own = Assets.Art("Dialogue/Background/" + name) ?? Assets.Art("Dialogue/" + name);
            if (own != null) { list.Add((own.Texture, 1f)); return list; }
            var tuning = Stage.CombatCamera.Tuning.Value;
            foreach (var l in Stage.BackgroundDb.LayersFor(name, Stage.BackgroundDb.Get(name), tuning))
                if (Assets.Art(l.ArtId) is { } art) list.Add((art.Texture, l.Depth));
            return list;
        }

        private void DrawBackground(SpriteBatch sb)
        {
            var prev = BackgroundArt(_bgPrev);
            var cur = BackgroundArt(_bg);
            if (prev.Count == 0 && cur.Count == 0)
            {
                // No picture: dim what's underneath so the box reads.
                Core.Draw.Rect(sb, new Rectangle(0, 0, Game1.Width, Game1.Height), Palette.Ink * 0.35f * Alpha);
                return;
            }
            float fade = MathHelper.SmoothStep(0, 1, _bgFade);
            sb.End();
            sb.Begin(samplerState: SamplerState.LinearClamp, transformMatrix: Core.Draw.FrameTransform);
            foreach (var (tex, depth) in prev) Drift(sb, tex, (cur.Count == 0 ? 1 - fade : 1f) * Alpha, depth);
            foreach (var (tex, depth) in cur) Drift(sb, tex, fade * Alpha, depth);
            sb.End();
            sb.Begin(samplerState: SamplerState.PointClamp, transformMatrix: Core.Draw.FrameTransform);
        }

        // Covers the frame and drifts slowly (pan + zoom), never showing an edge; layers drift by depth.
        private void Drift(SpriteBatch sb, Texture2D tex, float alpha, float depth)
        {
            if (alpha <= 0) return;
            float cover = Math.Max(Game1.Width / (float)tex.Width, Game1.Height / (float)tex.Height);
            float scale = cover * (1.06f + 0.02f * depth * MathF.Sin(_time * 0.12f));
            var size = new Vector2(tex.Width, tex.Height) * scale;
            var pos = new Vector2(Game1.Width, Game1.Height) / 2f - size / 2f
                      + new Vector2(MathF.Sin(_time * 0.09f) * 24f, MathF.Cos(_time * 0.07f) * 6f) * depth;
            pos.X = MathHelper.Clamp(pos.X, Game1.Width - size.X, 0);
            pos.Y = MathHelper.Clamp(pos.Y, Game1.Height - size.Y, 0);
            sb.Draw(tex, pos, null, Color.White * alpha, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        private void DrawPortraits(SpriteBatch sb)
        {
            sb.End();
            sb.Begin(samplerState: SamplerState.LinearClamp, transformMatrix: Core.Draw.FrameTransform);
            DrawPortrait(sb, _left, left: true);
            DrawPortrait(sb, _right, left: false);
            sb.End();
            sb.Begin(samplerState: SamplerState.PointClamp, transformMatrix: Core.Draw.FrameTransform);
        }

        private void DrawPortrait(SpriteBatch sb, Slot s, bool left)
        {
            var art = s.Art == null ? null : Assets.Art(s.Art);
            if (art == null) return;
            var tex = art.Texture;
            float h = 640f + 16f * s.Talk;
            float scale = h / tex.Height;
            float w = tex.Width * scale;
            float ease = 1 - (1 - s.In) * (1 - s.In);
            float cx = (left ? 250 : Game1.Width - 250) + (left ? -1 : 1) * (1 - ease) * 80;
            var pos = new Vector2(cx - w / 2f, Game1.Height - h + 40);
            var tint = Color.Lerp(new Color(110, 105, 125), Color.White, s.Talk) * (ease * Alpha);
            sb.Draw(tex, pos, null, tint, 0f, Vector2.Zero, scale, left ? SpriteEffects.None : SpriteEffects.None, 0f);
        }

        private void DrawBox(SpriteBatch sb)
        {
            float a = Alpha;
            // Dark band that fades up from the bottom edge, with a bright rule on top.
            for (int i = 0; i < 12; i++)
                Core.Draw.Rect(sb, 0, BoxTop - 60 + i * 5, Game1.Width, 5, Palette.Ink * (0.06f * i * a));
            Core.Draw.Rect(sb, new Rectangle(0, BoxTop, Game1.Width, Game1.Height - BoxTop), Palette.Ink * 0.82f * a);
            Core.Draw.GlowLine(sb, new Vector2(Game1.Width / 2f, BoxTop), Game1.Width - 200, Palette.Paper * 0.5f * a);
            Ui.Hit("vnbox", new Rectangle(0, BoxTop, Game1.Width, Game1.Height - BoxTop));

            const int textX = 200, textW = 880;
            if (_line.Speaker != null)
            {
                var cast = VnCast.Get(_line.Speaker);
                var nameSize = Assets.Header.MeasureString(_line.Speaker);
                Core.Draw.HighlightBar(sb, new Rectangle(textX - 26, BoxTop - 22, (int)nameSize.X + 52, 40), Palette.Rose, 12, a);
                Core.Draw.Text(sb, Assets.Header, _line.Speaker, new Vector2(textX, BoxTop - 20), (cast?.NameColor ?? Palette.Paper) * a);
            }

            string shown = _text.Substring(0, Math.Min(_text.Length, (int)_typed));
            var color = _line.Speaker == null ? Palette.Lilac100 : Palette.Text;
            float used = Core.Draw.Wrapped(sb, Assets.Body, shown, new Vector2(textX, BoxTop + 34), textW, color * a);

            if (WaitingOnChoice)
            {
                var ch = Choices;
                float y = BoxTop + 34 + used + 8;
                for (int i = 0; i < ch.Count; i++)
                {
                    var row = new Rectangle(textX - 8, (int)y + i * 30, 560, 28);
                    bool sel = i == _choice;
                    if (sel) Ui.SelectedRow(sb, row);
                    Ui.HoverRow(sb, "vnchoice", i, row, sel);
                    Core.Draw.Text(sb, Assets.Body, ch[i].Label, new Vector2(row.X + 20, row.Y + 4), sel ? Palette.Paper : Palette.Text);
                }
            }
            else if (TextDone)
            {
                // Bobbing "next" marker at the end of the box.
                float bob = MathF.Sin(_time * 5f) * 3f;
                Core.Draw.Diamond(sb, new Vector2(textX + textW + 20, Game1.Height - 46 + bob), 9, Palette.Blush * a);
            }

            // VN controls, bottom right: LOG AUTO SKIP HIDE (lit when on).
            string[] labels = { "LOG", "AUTO", "SKIP", "HIDE" };
            bool[] on = { false, Auto, Skip, false };
            for (int i = 0; i < labels.Length; i++)
            {
                var r = new Rectangle(Game1.Width - 330 + i * 78, Game1.Height - 32, 70, 22);
                bool hover = Ui.IsHover("vnbtn", i);
                if (on[i]) Core.Draw.Rect(sb, r, Palette.Rose * 0.8f * a);
                else if (hover) Core.Draw.Rect(sb, r, Palette.Crimson * 0.8f * a);
                Core.Draw.Border(sb, r, (hover || on[i] ? Palette.LineBright : Palette.Line) * a, 1);
                Core.Draw.TextCentered(sb, Assets.Small, labels[i], r.Center.ToVector2(), (on[i] || hover ? Palette.Paper : Palette.TextDim) * a, shadow: false);
                Ui.Hit("vnbtn", i, r);
            }
            Core.Draw.Text(sb, Assets.Small, "L log   F auto   Tab / Ctrl skip   H hide", new Vector2(40, Game1.Height - 30), Palette.TextDisabled * a, shadow: false);
        }

        // Title card: black screen, letter-spaced text that fades in.
        private void DrawCard(SpriteBatch sb)
        {
            float fade = MathHelper.Clamp(_lineTime / 0.6f, 0, 1);
            Core.Draw.Rect(sb, new Rectangle(0, 0, Game1.Width, Game1.Height), Palette.Ink * Alpha);
            var c = Palette.Paper * fade * Alpha;
            Core.Draw.SpacedCentered(sb, Assets.Header, _text.ToUpperInvariant(), new Vector2(Game1.Width / 2f, Game1.Height / 2f - 10), c, 4);
            Core.Draw.Divider(sb, new Vector2(Game1.Width / 2f, Game1.Height / 2f + 24), 260 * fade, Palette.Paper * 0.4f * fade);
            Core.Draw.Ornament(sb, new Vector2(Game1.Width / 2f, Game1.Height / 2f + 24), 10, Palette.Gold * fade);
        }

        private void DrawLog(SpriteBatch sb)
        {
            Core.Draw.Rect(sb, new Rectangle(0, 0, Game1.Width, Game1.Height), Palette.Ink * 0.88f);
            Ui.BlockAll();
            Core.Draw.SpacedCentered(sb, Assets.Header, "LOG", new Vector2(Game1.Width / 2f, 50), Palette.Paper, 4);
            Core.Draw.Divider(sb, new Vector2(Game1.Width / 2f, 72), 400, Palette.Paper * 0.4f);

            // Newest at the bottom; scrolled up by _logScroll. Entries partly outside the view are skipped.
            var entries = _log.ToList();
            if (_line != null && _text.Length > 0) entries.Add((_line.Speaker, _text));
            const int nameW = 170;
            float textW = LogView.Width - nameW - 20;
            var heights = entries.Select(e => Measure(Assets.Body, e.text, textW) + 14).ToList();
            _logHeight = heights.Sum();
            float y = LogView.Bottom + _logScroll;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                y -= heights[i];
                if (y > LogView.Bottom || y < LogView.Top - 4) { if (y < LogView.Top - 4) break; continue; }
                var (speaker, text) = entries[i];
                bool picked = speaker == "";
                if (!string.IsNullOrEmpty(speaker))
                    Core.Draw.TextRight(sb, Assets.Header, speaker, new Vector2(LogView.X + nameW, y - 2), VnCast.Get(speaker)?.NameColor ?? Palette.Paper);
                Core.Draw.Wrapped(sb, Assets.Body, text, new Vector2(LogView.X + nameW + 20, y), textW,
                    picked ? Palette.KwPriority : speaker == null ? Palette.Lilac100 : Palette.Text);
            }
            Ui.Button(sb, "vnlogclose", 0, new Rectangle(Game1.Width - 170, 30, 130, 32), "Close (L)", font: Assets.Small);
            Core.Draw.TextCentered(sb, Assets.Small, "Wheel / Up / Down scroll    L, Esc or right click close", new Vector2(Game1.Width / 2f, Game1.Height - 30), Palette.TextDim);
        }

        public static float Measure(UiFont f, string s, float maxWidth)
        {
            int lines = 0;
            foreach (var paragraph in s.Split('\n'))
            {
                string line = "";
                lines++;
                foreach (var word in paragraph.Split(' '))
                {
                    string test = line.Length == 0 ? word : line + " " + word;
                    if (f.MeasureString(test).X > maxWidth && line.Length > 0) { lines++; line = word; }
                    else line = test;
                }
            }
            return lines * f.LineSpacing;
        }
    }
}
