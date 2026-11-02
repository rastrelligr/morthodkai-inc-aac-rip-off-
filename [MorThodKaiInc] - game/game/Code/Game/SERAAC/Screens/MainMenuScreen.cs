using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.Tweening;
using SERAAC.Core;
using SERAAC.Meta;
using SERAAC.Stage;
using SERAAC.UI;

namespace SERAAC.Screens
{
    // The title. On launch: the intro video (Art/MainMenu/Video/Intro.mp4, any key skips it), then the title
    // picture held still with "Press any key", then the picture comes alive (layers follow the mouse,
    // see Stage/TitleScene.cs) while the menu slides in on the right. Coming back from the game skips
    // straight to the live picture and menu.
    public sealed class MainMenuScreen : Screen
    {
        private enum Phase { Intro, PressKey, Menu }

        // Scripts (--script) skip the intro and the press-any-key step unless --intro is passed too.
        public static bool ForceIntro;
        private static bool _introDone;
        private static TitleScene _scene;   // kept between visits: the layers load once

        private Phase _phase;
        private VideoPlayer _video;
        private readonly List<(string label, Action act)> _items = new();
        private int _index;
        private bool _confirmReset;
        private bool _showHelp;

        // Eased UI (MonoGame.Extended Tweener): the picture fades up, the menu slides in, the selection
        // line glides between rows, and the modals pop.
        private readonly Tweens _tweens = new();
        private readonly Anim _bar = new(), _sceneIn = new(), _menuIn = new(), _modalIn = new(), _parallax = new(), _framing = new();
        private int _barTarget;
        private float _pressAge;

        // The menu column's centre line.
        private const float MenuX = 985f;

        public override void OnEnter()
        {
            Playtest.Exit();   // back from a playtest: the real save is in use again
            Rebuild();
            _bar.Value = _barTarget = _index;
            _scene ??= new TitleScene();

            if (_introDone || Input.ScriptOnly && !ForceIntro)
            {
                _introDone = true;
                EnterMenu(fromStill: false);
                return;
            }
            _phase = Phase.Intro;
            string path = Mods.ArtRoots(Path.Combine(AppContext.BaseDirectory, "Art"))
                .Select(r => Path.Combine(r, "MainMenu", "Video", "Intro.mp4"))
                .FirstOrDefault(File.Exists);
            _video = path == null ? null : new VideoPlayer(Game1.Instance.GraphicsDevice, path) { Volume = Audio.MusicVolume * 1.6f };
            if (_video == null || _video.Failed) EndIntro();
        }

        public override void OnExit() => StopVideo();

        // The intro and "press any key" make their own sounds (or none).
        public override bool MenuKeySounds => _phase == Phase.Menu;
        public override bool UiSoundsFor(string key) => _phase == Phase.Menu;

        private void StopVideo()
        {
            _video?.Dispose();
            _video = null;
        }

        // Video over (or skipped): fade the still picture up from black.
        private void EndIntro()
        {
            StopVideo();
            _phase = Phase.PressKey;
            _pressAge = 0;
            _tweens.FromTo(_sceneIn, 0f, 1f, 1.2f, EasingFunctions.QuadraticOut);
        }

        // The picture starts following the mouse and slides left, and the menu comes in.
        private void EnterMenu(bool fromStill)
        {
            _phase = Phase.Menu;
            _introDone = true;
            if (fromStill)
            {
                _tweens.FromTo(_parallax, 0f, 1f, 1.6f, EasingFunctions.QuadraticInOut);
                _tweens.FromTo(_framing, 0f, 1f, 1.1f, EasingFunctions.CubicInOut);
                _tweens.FromTo(_menuIn, 0f, 1f, 1.0f, EasingFunctions.CubicOut, 0.25f);
            }
            else
            {
                _sceneIn.Value = _parallax.Value = _framing.Value = 1f;
                _tweens.FromTo(_menuIn, 0f, 1f, 0.6f, EasingFunctions.CubicOut);
            }
        }

        private void OpenModal(Action show)
        {
            show();
            _tweens.FromTo(_modalIn, 0f, 1f, 0.24f, EasingFunctions.BackOut);
        }

        private void Rebuild()
        {
            _items.Clear();
            bool hasSave = ProfileStore.Exists;
            _items.Add((hasSave ? "Continue" : "New Game", () =>
            {
                if (hasSave) Story.StoryFlow.Resume();
                else Story.StoryFlow.Begin();
            }));
            if (hasSave) _items.Add(("New Game (erase save)", () => OpenModal(() => _confirmReset = true)));
            _items.Add(("How to Play", () => OpenModal(() => _showHelp = true)));
            _items.Add(("Options", () => Screens.PushInstant(new OptionsScreen())));
            // Developer screens, shown only when the game is started with --dev.
            if (Game1.DevMode)
            {
                _items.Add(("Playtest", () => Screens.Replace(new PlaytestScreen())));
                _items.Add(("Mods", () => Screens.Replace(new ModsScreen())));
                _items.Add(("Art Viewer", () => Screens.Replace(new ArtViewerScreen())));
            }
            _items.Add(("Quit", () => Game1.Instance.Exit()));
            _index = Math.Clamp(_index, 0, _items.Count - 1);
        }

        public override void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            double t = gameTime.TotalGameTime.TotalSeconds;
            _tweens.Update(dt);
            _scene.Parallax = _parallax;
            _scene.Framing = _framing;
            _scene.Update(dt, Input.MousePos, t);

            if (_phase == Phase.Intro)
            {
                _video?.Update();
                if (Input.AnyPress || _video == null || _video.Finished) EndIntro();
                return;
            }
            Audio.Music("menu");   // no-op while it's already playing
            if (_phase == Phase.PressKey)
            {
                _pressAge += dt;
                // Wait for the picture (it may still be loading if the video was skipped early).
                bool ready = _scene.Ready || !_scene.Available;
                if (ready && _pressAge > 0.6f && Input.AnyPress)
                {
                    Audio.Play("ui_select");
                    EnterMenu(fromStill: true);
                }
                return;
            }

            if (_menuIn < 0.6f) return;   // let the menu arrive before it takes input
            if (_showHelp)
            {
                if (Input.Confirm || Input.Cancel || Input.Click) _showHelp = false;
                return;
            }
            if (_confirmReset)
            {
                if (Input.Pressed(Microsoft.Xna.Framework.Input.Keys.Y) || Ui.Clicked("modal", 0))
                {
                    ProfileStore.Delete();
                    Game1.Instance.Profile = Profile.CreateNew();
                    ProfileStore.Save(Game1.Instance.Profile);
                    _confirmReset = false;
                    Story.StoryFlow.Begin();
                }
                else if (Input.Cancel || Input.Pressed(Microsoft.Xna.Framework.Input.Keys.N) || Ui.Clicked("modal", 1)) _confirmReset = false;
                return;
            }

            int was = _index;
            if (Input.Up) _index = (_index + _items.Count - 1) % _items.Count;
            if (Input.DownKey) _index = (_index + 1) % _items.Count;
            bool go = Input.Confirm || Ui.Pick("menu", ref _index);
            if (_index != was) Audio.Play("ui_move");
            if (go) { Audio.Play("ui_select"); _items[_index].act(); }
            if (_index != _barTarget) { _barTarget = _index; _tweens.To(_bar, _index, 0.16f, EasingFunctions.CubicOut); }
        }

        public override void Draw(SpriteBatch sb, GameTime gameTime)
        {
            double t = gameTime.TotalGameTime.TotalSeconds;
            var screen = new Rectangle(0, 0, Game1.Width, Game1.Height);
            Core.Draw.Rect(sb, screen, Palette.Ink);

            if (_phase == Phase.Intro)
            {
                _video?.Draw(sb, screen, Color.White);
                if (_video != null && _video.Time > 1.0)
                    Core.Draw.TextRight(sb, Assets.Small, "Press any key to skip", new Vector2(Game1.Width - 28, Game1.Height - 30),
                        Palette.Paper * (0.35f + 0.15f * (float)Math.Sin(t * 3)), shadow: false);
                return;
            }

            // The picture (or the old placeholder backdrop when Art/MainMenu is missing).
            if (_scene.Available) _scene.Draw(sb, t, _sceneIn);
            else Ui.Backdrop(sb, t, _sceneIn);
            // A dark wash from the right for the title and menu, deeper once the menu is open.
            float wash = MathHelper.Lerp(0.55f, 0.92f, _menuIn);
            Core.Draw.GradientH(sb, new Rectangle(560, 0, Game1.Width - 560, Game1.Height), Palette.Ink * wash * _sceneIn, 0.35f, fromRight: true);
            Core.Draw.GradientV(sb, new Rectangle(0, Game1.Height - 180, Game1.Width, 180), Palette.Ink * 0.75f * _sceneIn, fromBottom: true);

            // The title: in the right half from the start, rising a little as the menu opens under it.
            float a = MathHelper.Clamp(_sceneIn * 1.3f - 0.3f, 0, 1);
            var titleCenter = new Vector2(MenuX, MathHelper.Lerp(250, 170, _menuIn));
            float size = 1.7f;
            Core.Draw.SoftGlow(sb, titleCenter, new Vector2(560, 200), Palette.Rose * 0.18f * a);
            Core.Draw.SpacedCentered(sb, Assets.Title, "AETHERIS", titleCenter, Palette.Paper * a, 8, size);
            Core.Draw.Ornament(sb, titleCenter + new Vector2(0, -64), 20, Palette.Gold * 0.9f * a);
            Core.Draw.Divider(sb, titleCenter + new Vector2(0, 46), 440, Palette.Paper * 0.5f * a);
            Core.Draw.SpacedCentered(sb, Assets.Small, "NO SINS ARE FORGIVEN.",
                titleCenter + new Vector2(0, 64), Palette.TextDim * a, 2, 1f, shadow: false);

            if (_phase == Phase.PressKey)
            {
                bool ready = _scene.Ready || !_scene.Available;
                float pa = ready ? MathHelper.Clamp((_pressAge - 0.6f) * 2, 0, 1) : 0;
                float pulse = 0.55f + 0.45f * (float)Math.Sin(_pressAge * 2.6);
                var at = new Vector2(Game1.Width / 2f, Game1.Height - 70);
                Core.Draw.GlowLine(sb, at + new Vector2(0, 16), 420, Palette.Rose * pa * pulse, 18);
                Core.Draw.SpacedCentered(sb, Assets.Body, "PRESS ANY KEY TO CONTINUE", at, Palette.Paper * pa * (0.65f + 0.35f * pulse), 3, 1.1f);
                if (!ready) Core.Draw.TextCentered(sb, Assets.Small, "Loading...", at, Palette.TextDim * 0.7f, shadow: false);
                return;
            }

            DrawMenu(sb, t);
            Core.Draw.TextRight(sb, Assets.Small, "Prototype build v" + Game1.Version, new Vector2(Game1.Width - 20, Game1.Height - 26),
                Palette.TextDim * 0.7f * _menuIn, shadow: false);
        }

        private void DrawMenu(SpriteBatch sb, double t)
        {
            float m = MathHelper.Clamp(_menuIn, 0, 1);
            // Rows slide in from the right one after another; a line of light under the chosen one glides between rows.
            const int rowY = 300, rowH = 36, rowW = 360;
            float slide = (1 - m) * 70;
            float barY = rowY + _bar * rowH + rowH - 6;
            Core.Draw.GlowLine(sb, new Vector2(MenuX + slide, barY), 400, Palette.Rose * m, 24);
            for (int i = 0; i < _items.Count; i++)
            {
                float ra = MathHelper.Clamp(m * 1.6f - i * 0.08f, 0, 1);
                float rs = (1 - ra) * 50;
                bool sel = i == _index;
                var row = new Rectangle((int)(MenuX - rowW / 2), rowY + i * rowH, rowW, rowH);
                if (m >= 0.6f) Ui.Hit("menu", i, row);
                bool hover = !sel && Ui.IsHover("menu", i);
                string label = _items[i].label;
                var size = Assets.Body.MeasureString(label, 1.3f);
                var center = new Vector2(row.Center.X + rs, row.Center.Y - 2);
                Core.Draw.TextSizedCentered(sb, Assets.Body, label, center,
                    (sel ? Palette.Paper : hover ? Palette.Text * 0.85f : Palette.Text * 0.5f) * ra, 1.3f, shadow: sel);
                if (sel) Core.Draw.Pointer(sb, new Vector2(center.X - size.X / 2 - 60, center.Y + 1), t, ra);
            }

            // What the highlighted option does, under the menu.
            string about = Describe(_items[_index].label);
            float aboutW = Math.Min(440, Assets.Body.MeasureString(about).X);
            RichText.Wrapped(sb, Assets.Body, about, new Vector2(MenuX - aboutW / 2 + slide, 572), 440, Palette.Text * 0.85f * m, lineGap: 2);

            // Controls live behind the "?" (hover it, or hold F1).
            Ui.HelpButton(sb, new Vector2(40, Game1.Height - 36), "Choose: Up/Down or the mouse.\nSelect: Enter or click.\nFullscreen: F11 or Alt+Enter.");
            Core.Draw.Text(sb, Assets.Small, "MorThodKai Inc.  -  prototype build", new Vector2(72, Game1.Height - 44), Palette.Text * 0.6f * m, shadow: false);
            Core.Draw.TextRight(sb, Assets.Small, "Version: prototype", new Vector2(Game1.Width - 40, Game1.Height - 44), Palette.Text * 0.6f * m, shadow: false);

            if (_confirmReset || _showHelp)
            {
                // Dim the menu, then pop the box in.
                Core.Draw.Rect(sb, new Rectangle(0, 0, Game1.Width, Game1.Height), Palette.Ink * 0.7f * MathHelper.Clamp(_modalIn, 0, 1));
                Ui.BlockAll();
                Core.Draw.BeginPop(sb, new Vector2(Game1.Width / 2f, Game1.Height / 2f), _modalIn);
                if (_confirmReset)
                {
                    var r = Modal(sb, "Erase save?", "All secured shards, upgrades, equipment and unlocked areas will be lost.");
                    Ui.Button(sb, "modal", 0, new Rectangle(r.Center.X - 230, r.Bottom - 70, 210, 44), "Erase (Y)");
                    Ui.Button(sb, "modal", 1, new Rectangle(r.Center.X + 20, r.Bottom - 70, 210, 44), "Cancel (N)");
                }
                else
                {
                    var r = Modal(sb, "How to Play", HelpText);
                    Core.Draw.TextCentered(sb, Assets.Small, "click or Enter to close", new Vector2(r.Center.X, r.Bottom - 20), Palette.TextDim);
                }
                Core.Draw.EndPop(sb);
            }
        }

        // The description card's text for each menu option.
        private static string Describe(string label) => label switch
        {
            "Continue" => "Back to the Sanctum: your party, upgrades and secured shards are waiting.",
            "New Game" => "Start fresh in the Sanctum. Pick a party of 4 and enter your first Trial.",
            "New Game (erase save)" => "Wipe the save and start over. Secured shards, upgrades and gear are lost.",
            "How to Play" => "The basics: the Sanctum, Trials, combat, and why loot is at risk until you Extract.",
            "Art Viewer (dev)" => "Every sprite sheet and pose, for checking new art.",
            "Mods" => Mods.All.Count == 0 ? "No mods installed yet. Open the mod manager to see where they go."
                    : $"{Mods.Active.Count()} of {Mods.All.Count} mods on" + (Mods.ErrorCount > 0 ? $", {Mods.ErrorCount} error(s)." : "."),
            "Quit" => "Close the game.",
            _ => "Jump straight into one part of the game to test it: one fight, a trial with a chosen objective, or the Sanctum with everything unlocked. Your real save is never touched.",
        };

        public const string HelpText =
            "HUB: pick a party of 4, upgrade stats with SECURED shards, equip gear, then choose a Trial. Everything can be clicked.\n\n" +
            "TRIAL: click a spot to walk there (or WASD/arrows). Enemies in a room hunt the party once they spot it; touching one starts combat (or run through a door). Walk over loot to pick it up. " +
            "Click chests, shrines, altars and the Extraction Point (or E). P = party (check members, change the formation), I = items, M = full map, Esc = menu.\n\n" +
            "COMBAT: click an action, then click its target, for every party member (keys: Up/Down, Left/Right, Enter; right click or Backspace goes back). " +
            "Then everyone acts in Speed order. Position matters: each skill lists where it can be used from and which enemy positions it reaches.\n\n" +
            "LOOT found in a Trial is UNSECURED until you Extract. If the party falls, it is all lost.";

        // A titled box in the middle of the screen (the caller dims the screen and blocks the mouse).
        public static Rectangle Modal(SpriteBatch sb, string title, string body)
        {
            var r = new Rectangle(Game1.Width / 2 - 380, Game1.Height / 2 - 210, 760, 420);
            // A dark box with a thin outline and an inner hairline, the title spaced out over a crest and a divider.
            Core.Draw.Rect(sb, r, Color.Lerp(Palette.Ink, Palette.Grey900, 0.3f) * 0.97f);
            Core.Draw.GradientV(sb, new Rectangle(r.X, r.Y, r.Width, 120), Palette.Crimson * 0.45f);
            Core.Draw.Border(sb, r, Palette.LineBright, 1);
            Core.Draw.Border(sb, new Rectangle(r.X + 5, r.Y + 5, r.Width - 10, r.Height - 10), Palette.Line, 1);
            Core.Draw.SpacedCentered(sb, Assets.Header, title.ToUpperInvariant(), new Vector2(r.Center.X, r.Y + 38), Palette.Paper, 4);
            Core.Draw.Divider(sb, new Vector2(r.Center.X, r.Y + 64), r.Width - 160, Palette.Paper * 0.5f);
            Core.Draw.Ornament(sb, new Vector2(r.Center.X, r.Y + 64), 14, Palette.Gold);
            RichText.Wrapped(sb, Assets.Body, body, new Vector2(r.X + 60, r.Y + 86), r.Width - 120, Palette.Text, lineGap: 1);
            return r;
        }
    }
}
