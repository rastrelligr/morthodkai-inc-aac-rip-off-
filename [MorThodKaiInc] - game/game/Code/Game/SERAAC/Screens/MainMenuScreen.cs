using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SERAAC.Core;
using SERAAC.Meta;

namespace SERAAC.Screens
{
    public sealed class MainMenuScreen : Screen
    {
        private readonly List<(string label, Action act)> _items = new();
        private int _index;
        private bool _confirmReset;
        private bool _showHelp;

        public override void OnEnter() => Rebuild();

        private void Rebuild()
        {
            _items.Clear();
            bool hasSave = ProfileStore.Exists;
            _items.Add((hasSave ? "Continue" : "New Game", () => Screens.Replace(new HubScreen())));
            if (hasSave) _items.Add(("New Game (erase save)", () => _confirmReset = true));
            _items.Add(("How to Play", () => _showHelp = true));
            _items.Add(("Art Viewer (dev)", () => Screens.Replace(new ArtViewerScreen())));
            _items.Add(("Quit", () => Game1.Instance.Exit()));
            _index = Math.Clamp(_index, 0, _items.Count - 1);
        }

        public override void Update(GameTime gameTime)
        {
            if (_showHelp)
            {
                if (Input.Confirm || Input.Cancel) _showHelp = false;
                return;
            }
            if (_confirmReset)
            {
                if (Input.Pressed(Microsoft.Xna.Framework.Input.Keys.Y))
                {
                    ProfileStore.Delete();
                    Game1.Instance.Profile = Profile.CreateNew();
                    ProfileStore.Save(Game1.Instance.Profile);
                    _confirmReset = false;
                    Screens.Replace(new HubScreen());
                }
                else if (Input.Cancel || Input.Pressed(Microsoft.Xna.Framework.Input.Keys.N)) _confirmReset = false;
                return;
            }

            if (Input.Up) _index = (_index + _items.Count - 1) % _items.Count;
            if (Input.DownKey) _index = (_index + 1) % _items.Count;
            if (Input.Confirm) _items[_index].act();
        }

        public override void Draw(SpriteBatch sb, GameTime gameTime)
        {
            double t = gameTime.TotalGameTime.TotalSeconds;
            if (!Core.Draw.Art(sb, "bg_title", new Rectangle(0, 0, Game1.Width, Game1.Height), t))
            {
                // Placeholder backdrop: a slow pulsing "eye" of light.
                for (int i = 0; i < 18; i++)
                {
                    float r = 40 + i * 26 + (float)Math.Sin(t * 0.8 + i * 0.4) * 6;
                    Core.Draw.Rect(sb, Game1.Width / 2 - r, 250 - r * 0.35f, r * 2, r * 0.7f, new Color(90, 20, 40) * (0.06f));
                }
            }

            Core.Draw.TextCentered(sb, Assets.Title, "SERAAC", new Vector2(Game1.Width / 2, 200), Palette.Accent);
            Core.Draw.TextCentered(sb, Assets.Body, "Enter the Trials. Take what you can. Get out alive.", new Vector2(Game1.Width / 2, 260), Palette.TextDim);

            for (int i = 0; i < _items.Count; i++)
            {
                bool sel = i == _index;
                var pos = new Vector2(Game1.Width / 2, 360 + i * 44);
                if (sel) Core.Draw.Rect(sb, pos.X - 180, pos.Y - 18, 360, 36, Palette.PanelLight);
                Core.Draw.TextCentered(sb, Assets.Header, _items[i].label, pos, sel ? Palette.Accent : Palette.Text);
            }

            Core.Draw.TextCentered(sb, Assets.Small, "Prototype build  -  Up/Down + Enter  -  F11 / Alt+Enter: fullscreen", new Vector2(Game1.Width / 2, Game1.Height - 30), Palette.TextDim);

            if (_confirmReset)
                Modal(sb, "Erase save?", "All secured shards, upgrades, equipment and unlocked areas will be lost.\n\nY = erase    N = cancel");
            if (_showHelp)
                Modal(sb, "How to Play", HelpText);
        }

        public const string HelpText =
            "HUB: pick a party of 4, upgrade stats with SECURED shards, equip gear, then choose a Trial.\n\n" +
            "TRIAL: walk with WASD/arrows. Entering an infested room starts combat. Walk over loot to pick it up. " +
            "E / Enter interacts with chests, shrines, altars and the Extraction Point. I = items, M = full map, Esc = menu.\n\n" +
            "COMBAT: choose an action + target for every party member (Up/Down, Left/Right, Enter; Backspace undoes). " +
            "Then everyone acts in Speed order. Position matters: each skill lists where it can be used from and which enemy positions it reaches.\n\n" +
            "LOOT found in a Trial is UNSECURED until you Extract. If the party falls, it is all lost.";

        public static void Modal(SpriteBatch sb, string title, string body)
        {
            Core.Draw.Rect(sb, new Rectangle(0, 0, Game1.Width, Game1.Height), Color.Black * 0.7f);
            var r = new Rectangle(Game1.Width / 2 - 360, Game1.Height / 2 - 200, 720, 400);
            Core.Draw.Panel(sb, r);
            Core.Draw.TextCentered(sb, Assets.Header, title, new Vector2(r.Center.X, r.Y + 34), Palette.Accent);
            Core.Draw.Wrapped(sb, Assets.Body, body, new Vector2(r.X + 30, r.Y + 70), r.Width - 60, Palette.Text);
        }
    }
}
