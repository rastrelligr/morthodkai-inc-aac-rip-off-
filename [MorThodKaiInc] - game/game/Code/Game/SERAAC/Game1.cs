using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended;
using SERAAC.Core;
using SERAAC.Meta;
using SERAAC.Screens;

namespace SERAAC
{
    public class Game1 : Game
    {
        public const int Width = 1280;
        public const int Height = 720;

        public const string Version = "0.1.3";

        public static Game1 Instance { get; private set; }
        public static bool DevMode { get; private set; }   // --dev: Playtest, Mods and Art Viewer on the title menu

        // Permanent progression (secured loot, upgrades, equipment, unlocked areas).
        public Profile Profile { get; set; }

        private readonly GraphicsDeviceManager _graphics;
        private SpriteBatch _sb;
        private readonly ScreenManager _screens = new();
        public ScreenManager Screens => _screens;
        private readonly FramesPerSecondCounter _fps = new();   // MonoGame.Extended; F3 shows it
        private bool _showFps;
        private readonly ScriptRunner _script;
        private string _pendingShot;
        private bool _startWindowed;
        private string _startStory;

        public Game1(string[] args)
        {
            Instance = this;
            _graphics = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = Width,
                PreferredBackBufferHeight = Height,
            };
            Content.RootDirectory = "Content";
            IsMouseVisible = true;
            Window.Title = "Aetheris - Prototype v" + Version;
            DevMode = Array.IndexOf(args, "--dev") >= 0;
            Window.AllowUserResizing = true;
            _graphics.HardwareModeSwitch = false; // borderless fullscreen: no display mode change, fast Alt+Tab

            if (Array.IndexOf(args, "--intro") >= 0) SERAAC.Screens.MainMenuScreen.ForceIntro = true;   // scripts skip the intro unless asked
            int story = Array.IndexOf(args, "--story");   // jump to a prologue chapter: dorm, spar, airlift, hub, academy
            if (story >= 0 && story + 1 < args.Length) _startStory = args[story + 1];
            int i = Array.IndexOf(args, "--script");
            if (i >= 0 && i + 1 < args.Length)
            {
                _script = new ScriptRunner(args[i + 1]);
                Input.ScriptOnly = true;
                ProfileStore.OverrideDir = Path.Combine(Path.GetTempPath(), "SERAAC_script_" + Environment.ProcessId);
                ProfileStore.Delete();
                _startWindowed = true; // scripted captures stay in a window
            }
        }

        protected override void Initialize()
        {
            base.Initialize();
            SetFullscreen(!_startWindowed);
        }

        // Fullscreen uses the desktop resolution; the 1280x720 frame is scaled to fit (see Draw).
        public void SetFullscreen(bool on)
        {
            var mode = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
            _graphics.IsFullScreen = on;
            _graphics.PreferredBackBufferWidth = on ? mode.Width : Width;
            _graphics.PreferredBackBufferHeight = on ? mode.Height : Height;
            _graphics.ApplyChanges();
        }

        protected override void LoadContent()
        {
            _sb = new SpriteBatch(GraphicsDevice);
            Mods.LoadContent();   // mods' content changes, before anything reads the *Db lists or the save
            Assets.Load(GraphicsDevice, Content);
            Sprites.Init(GraphicsDevice);
            Profile = ProfileStore.Load();
            Settings.Load();
            if (_startStory != null)
            {
                Profile.Story = _startStory;
                Story.StoryFlow.Resume();
            }
            else _screens.Replace(new MainMenuScreen());
        }

        protected override void Update(GameTime gameTime)
        {
            if (_script != null)
            {
                _script.Tick();
                if (_script.PendingShot != null) _pendingShot = _script.PendingShot;
                if (_script.Done) Exit();
            }
            Input.Update(IsActive);
            Audio.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
            _fps.Update(gameTime);
            if (Input.Pressed(Keys.F3)) _showFps = !_showFps;
            if (Input.Pressed(Keys.F12)) _pendingShot = "shot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            bool altEnter = Input.Pressed(Keys.Enter) && (Input.Down(Keys.LeftAlt) || Input.Down(Keys.RightAlt));
            if (Input.Pressed(Keys.F11) || altEnter)
            {
                SetFullscreen(!_graphics.IsFullScreen);
                base.Update(gameTime);
                return; // so Alt+Enter doesn't also press Enter in the current menu
            }
            _screens.Update(gameTime);
            base.Update(gameTime);
        }

        // Screens lay out in 1280x720 units. The frame is drawn straight to the screen through a scale
        // transform (black bars if the screen isn't 16:9); fonts, sprites and icons pick sizes for the
        // real resolution (see Draw.Scale), so nothing is stretched or blurred.
        protected override void Draw(GameTime gameTime)
        {
            var screen = GraphicsDevice.Viewport.Bounds;
            float scale = Math.Min(screen.Width / (float)Width, screen.Height / (float)Height);
            int offsetX = (screen.Width - (int)Math.Round(Width * scale)) / 2;
            int offsetY = (screen.Height - (int)Math.Round(Height * scale)) / 2;
            Core.Draw.Scale = scale;
            Core.Draw.Offset = new Vector2(offsetX, offsetY);
            Core.Draw.FrameTransform = Matrix.CreateScale(scale, scale, 1f) * Matrix.CreateTranslation(offsetX, offsetY, 0f);

            UI.Ui.BeginFrame();
            GraphicsDevice.Clear(Color.Black);
            _sb.Begin(samplerState: SamplerState.PointClamp, transformMatrix: Core.Draw.FrameTransform);
            Core.Draw.Rect(_sb, new Rectangle(0, 0, Width, Height), Palette.Background);
            _screens.Draw(_sb, gameTime);
            _fps.Draw(gameTime);
            if (_showFps) Core.Draw.Text(_sb, Assets.Small, $"{_fps.FramesPerSecond} FPS", new Vector2(8, Height - 20), Palette.Accent);
            _sb.End();

            if (_pendingShot != null) SaveShot(_pendingShot);
            base.Draw(gameTime);
        }

        // Saves exactly what is on screen, at the screen's resolution.
        private void SaveShot(string name)
        {
            string dir = _script?.OutputDir ?? Path.Combine(AppContext.BaseDirectory, "Screenshots");
            Directory.CreateDirectory(dir);
            var pp = GraphicsDevice.PresentationParameters;
            var data = new Color[pp.BackBufferWidth * pp.BackBufferHeight];
            GraphicsDevice.GetBackBufferData(data);
            for (int i = 0; i < data.Length; i++) data[i].A = 255;
            using (var tex = new Texture2D(GraphicsDevice, pp.BackBufferWidth, pp.BackBufferHeight))
            using (var fs = File.Create(Path.Combine(dir, name + ".png")))
            {
                tex.SetData(data);
                tex.SaveAsPng(fs, tex.Width, tex.Height);
            }
            _pendingShot = null;
            if (_script != null) _script.PendingShot = null;
        }
    }
}
