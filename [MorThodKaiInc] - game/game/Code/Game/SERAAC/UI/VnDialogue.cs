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
        public VnInterrupt Interrupt;        // a timed prompt while this line is up (see VnInterrupt)

        public string Resolve() => TextFn?.Invoke() ?? Text ?? "";

        public static VnLine Say(string speaker, string text) => new() { Speaker = speaker, Text = text };
        public static VnLine Narrate(string text) => new() { Text = text };
        public static VnLine TitleCard(string text) => new() { Text = text, Card = true };
    }

    // How a choice comes across: Kind (warm), Bold (blunt), Ask (find out more), Flirt, Leave. Hidden unless the
    // Dialogue Helper option is on (Core/Settings.cs): normally the player answers on the words alone and reads
    // the reply. The tone still colours an interrupt prompt.
    public enum VnTone { None, Kind, Bold, Ask, Flirt, Leave }

    public sealed class VnChoice
    {
        public string Label;
        public Action Pick;
        public VnTone Tone;
        public Func<IEnumerable<VnLine>> Then;   // lines played right after picking it (the branch)
        public VnChoice(string label, Action pick) { Label = label; Pick = pick; }
        public VnChoice(VnTone tone, string label, Func<IEnumerable<VnLine>> then, Action pick = null)
        {
            Tone = tone; Label = label; Then = then; Pick = pick;
        }

        public static Color ToneColor(VnTone t) => t switch
        {
            VnTone.Kind => Palette.KwSlow,
            VnTone.Bold => Palette.Coral,
            VnTone.Ask => Palette.Lilac300,
            VnTone.Flirt => Palette.Rose,
            _ => Palette.Grey500,
        };

        // The tag shown in front of a choice, or null for none (always none with the Dialogue Helper off).
        public static string ToneLabel(VnTone t) => !Settings.Current.DialogueHelper ? null : t switch
        {
            VnTone.Kind => "KIND",
            VnTone.Bold => "BOLD",
            VnTone.Ask => "ASK",
            VnTone.Flirt => "",
            VnTone.Leave => "END",
            _ => null,
        };
    }

    // A chance to act mid-line, open for a few seconds: a prompt over the box with a draining bar. Press E (or
    // click it) to take it; let it run out, or move on, and the moment passes. Skipping never takes one.
    public sealed class VnInterrupt
    {
        public string Label;
        public VnTone Tone = VnTone.Kind;
        public float Window = 3.5f;
        public Action Pick;
        public Func<IEnumerable<VnLine>> Then;
        public int Replaces;   // how many of the following lines play only when it is missed (dropped when taken)
    }

    // Short notices of how the squad took what Relle just said ("Mara approves"), stacked top right
    // with the character's face. Only shown with the Dialogue Helper on. Anything can post one; the VN draws them.
    public static class VnReactions
    {
        private sealed class Entry { public string CharId, Text; public Color Color; public string Icon; public float Age; }
        private static readonly List<Entry> _list = new();
        private const float Life = 3.2f;

        public static void Post(string charId, string text, Color color, string icon = null, string sound = "ui_select")
        {
            if (!Settings.Current.DialogueHelper) return;
            _list.Add(new Entry { CharId = charId, Text = text, Color = color, Icon = icon });
            if (_list.Count > 4) _list.RemoveAt(0);
            if (sound != null) Audio.Play(sound, 0.5f);
        }

        public static void Update(float dt)
        {
            foreach (var e in _list) e.Age += dt;
            _list.RemoveAll(e => e.Age > Life);
        }

        public static void Draw(SpriteBatch sb, float alpha = 1f)
        {
            for (int i = 0; i < _list.Count; i++)
            {
                var e = _list[i];
                float a = MathHelper.Clamp(Math.Min(e.Age / 0.2f, (Life - e.Age) / 0.4f), 0, 1) * alpha;
                float slide = (1 - MathHelper.Clamp(e.Age / 0.25f, 0, 1)) * 40;
                var r = new Rectangle((int)(Game1.Width - 330 + slide), 24 + i * 58, 306, 50);
                Core.Draw.Rect(sb, r, Palette.Ink * 0.78f * a);
                Core.Draw.GradientH(sb, r, e.Color * 0.35f * a, 0.1f);
                Core.Draw.Rect(sb, new Rectangle(r.X, r.Y, 3, r.Height), e.Color * a);
                var def = Data.CharacterDb.Get(e.CharId);
                if (def != null) Figures.Portrait(sb, def, new Rectangle(r.X + 8, r.Y + 5, 40, 40), 0, a);
                if (e.Icon != null) Icons.Draw(sb, e.Icon, new Rectangle(r.Right - 30, r.Y + 13, 24, 24), a);
                Core.Draw.Text(sb, Assets.Body, e.Text, new Vector2(r.X + 58, r.Center.Y - Assets.Body.MeasureString(e.Text).Y / 2), Palette.Paper * a, shadow: false);
            }
        }
    }

    // Who speaks in the story: portrait, name colour and which side they stand on.
    public static class VnCast
    {
        public sealed class Member
        {
            public string Portrait;
            public Color NameColor;
            public string Side;
            public float Height = 1f;   // portraits are framed differently (full figure vs bust): scales the drawn height
            public float ShiftX;        // nudges the portrait outward (+) from its usual spot
        }

        public static readonly Dictionary<string, Member> All = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Relle"] = new() { Portrait = "Dialogue/Portrait/Portrait_Relle", NameColor = Palette.Blush, Side = "left" },
            ["Naomi"] = new() { Portrait = "Dialogue/Portrait/Portrait_Naomi", NameColor = Palette.Lilac100, Side = "right" },
            ["Mara"] = new() { Portrait = "Dialogue/Portrait/Portrait_Mara", NameColor = Palette.Coral, Side = "right", Height = 0.8f, ShiftX = 50 },
            ["Elian"] = new() { Portrait = "Dialogue/Portrait/Portrait_Elian", NameColor = Palette.KwHeal, Side = "right", Height = 0.92f, ShiftX = 20 },
            ["Pillow"] = new() { Portrait = "Dialogue/Portrait/Portrait_Pillow", NameColor = Palette.KwPriority, Side = "right", Height = 0.78f, ShiftX = 30 },
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
        private bool _interruptTaken;

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
            _interruptTaken = false;

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
        private bool InterruptOpen => _line?.Interrupt != null && !_interruptTaken && _lineTime < _line.Interrupt.Window;
        private List<VnChoice> Choices => _line?.Choices ?? (List<VnChoice>)null;
        private bool WaitingOnChoice => TextDone && Choices is { Count: > 0 };

        // ------------------------------------------------------------------ update

        public void Update(float dt)
        {
            _time += dt;
            VnReactions.Update(dt);
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
                    var more = pick.Then?.Invoke();
                    if (more != null) Insert(more);
                    Next();
                }
                return;
            }

            if (InterruptOpen && (Input.Pressed(Keys.E) || Ui.Clicked("vnint")))
            {
                var it = _line.Interrupt;
                _interruptTaken = true;
                _log.Add((_line.Speaker, _text));
                _log.Add(("", "> " + it.Label));
                _text = "";
                Audio.Play("ui_select");
                it.Pick?.Invoke();
                for (int i = 0; i < it.Replaces && _queue.Count > 0; i++) _queue.RemoveFirst();
                var more = it.Then?.Invoke();
                if (more != null) Insert(more);
                Next();
                return;
            }

            bool advance = Input.Pressed(Keys.Enter) || Input.Pressed(Keys.Space) || Input.Pressed(Keys.Z)
                           || (Input.Click && Ui.Hover("vnbtn") < 0 && Ui.Hover("vnint") < 0) || Input.Wheel < 0;
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
            if (Hidden || _line == null) { VnReactions.Draw(sb, Alpha); return; }
            DrawBox(sb);
            if (InterruptOpen) DrawInterrupt(sb);
            VnReactions.Draw(sb, Alpha);
        }

        // The interrupt prompt: a tone-coloured plate over the box with the E key and a bar running out.
        private void DrawInterrupt(SpriteBatch sb)
        {
            var it = _line.Interrupt;
            float left = 1 - _lineTime / it.Window;
            float pop = MathHelper.Clamp(_lineTime / 0.15f, 0, 1);
            var c = VnChoice.ToneColor(it.Tone);
            var size = Assets.Header.MeasureString(it.Label);
            int w = (int)size.X + 96;
            var r = new Rectangle(Game1.Width / 2 - w / 2, BoxTop - 92 - (int)((1 - pop) * 12), w, 46);
            float pulse = 0.75f + 0.25f * MathF.Sin(_time * 10f);
            Core.Draw.SoftGlow(sb, r.Center.ToVector2(), new Vector2(w * 1.3f, 120), c * 0.25f * pulse * Alpha);
            Core.Draw.HighlightBar(sb, r, c, 10, Alpha * pop);
            Ui.KeyCap(sb, "E", new Vector2(r.X + 26, r.Center.Y));
            Core.Draw.Text(sb, Assets.Header, it.Label, new Vector2(r.X + 50, r.Center.Y - size.Y / 2), Palette.Paper * Alpha * pop);
            Core.Draw.Rect(sb, new Rectangle(r.X, r.Bottom + 3, w, 3), Palette.Ink * 0.6f * Alpha);
            Core.Draw.Rect(sb, new Rectangle(r.X, r.Bottom + 3, (int)(w * left), 3), c * Alpha);
            Ui.Hit("vnint", r);
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
            var cast = VnCast.Get(s.Speaker);
            float h = (640f + 16f * s.Talk) * (cast?.Height ?? 1f);
            float scale = h / tex.Height;
            float w = tex.Width * scale;
            float ease = 1 - (1 - s.In) * (1 - s.In);
            float cx = (left ? 250 : Game1.Width - 250) + (left ? -1 : 1) * ((1 - ease) * 80 + (cast?.ShiftX ?? 0));
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
                int rowH = ch.Count > 4 ? 25 : 30;   // long menus pack tighter to stay inside the box
                bool tags = ch.Exists(c => VnChoice.ToneLabel(c.Tone) != null);   // keep labels lined up when some have a tag
                for (int i = 0; i < ch.Count; i++)
                {
                    var row = new Rectangle(textX - 8, (int)y + i * rowH, 560, rowH - 2);
                    bool sel = i == _choice;
                    if (sel) Ui.SelectedRow(sb, row);
                    Ui.HoverRow(sb, "vnchoice", i, row, sel);
                    float tx = row.X + 20;
                    if (VnChoice.ToneLabel(ch[i].Tone) != null)
                    {
                        var tc = VnChoice.ToneColor(ch[i].Tone);
                        var tag = new Rectangle((int)tx, row.Center.Y - 9, 44, 18);
                        Core.Draw.Rect(sb, tag, tc * (sel ? 0.9f : 0.55f) * a);
                        if (ch[i].Tone == VnTone.Flirt) Icons.Draw(sb, "ui_hp", new Rectangle(tag.Center.X - 8, tag.Y + 1, 16, 16), a);
                        else Core.Draw.TextCentered(sb, Assets.Small, VnChoice.ToneLabel(ch[i].Tone), tag.Center.ToVector2(), Palette.Paper * a, shadow: false);
                    }
                    if (tags) tx += 54;
                    Core.Draw.Text(sb, Assets.Body, ch[i].Label, new Vector2(tx, row.Center.Y - Assets.Body.MeasureString(ch[i].Label).Y / 2), (sel ? Palette.Paper : Palette.Text) * a);
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
            if (!WaitingOnChoice)
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
