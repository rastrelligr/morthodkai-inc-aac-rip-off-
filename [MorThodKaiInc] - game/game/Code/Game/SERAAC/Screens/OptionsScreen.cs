using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SERAAC.Core;
using SERAAC.UI;

namespace SERAAC.Screens
{
    // Options, over whatever screen opened it (title menu, pause menu, the story maps): music and sound volume, and
    // the dialogue helper. Up / Down pick a row, Left / Right change it, or click / drag on a bar. Esc or Back saves and closes.
    public sealed class OptionsScreen : Screen
    {
        private int _row;
        private bool _dragging;
        private const int Rows = 4;   // music, sound effects, dialogue helper, back
        private const int HelperRow = 2, BackRow = 3;

        private static readonly Rectangle Box = new(Game1.Width / 2 - 330, Game1.Height / 2 - 185, 660, 370);
        private static Rectangle Bar(int row) => new(Box.X + 260, Box.Y + 100 + row * 64, 320, 14);

        public override bool IsOverlay => true;

        private static float Get(int row) => row == 0 ? Settings.Current.MusicVolume : Settings.Current.SfxVolume;

        private static void Set(int row, float v)
        {
            v = MathF.Round(Math.Clamp(v, 0f, 1f) * 20f) / 20f;   // 5% steps
            if (row == 0) Settings.Current.MusicVolume = v;
            else Settings.Current.SfxVolume = v;
            Settings.Apply();
        }

        private static void ToggleHelper()
        {
            Settings.Current.DialogueHelper = !Settings.Current.DialogueHelper;
            Audio.Play("ui_select");
        }

        private void Close()
        {
            Settings.Save();
            Screens.PopInstant();
        }

        public override void Update(GameTime gameTime)
        {
            if (Input.Cancel || Ui.Clicked("optback")) { Close(); return; }
            if (Input.Up) _row = (_row + Rows - 1) % Rows;
            if (Input.DownKey) _row = (_row + 1) % Rows;
            if (_row < 2 && (Input.Left || Input.Right))
            {
                Set(_row, Get(_row) + (Input.Right ? 0.1f : -0.1f));
                if (_row == 1) Audio.Play("ui_select");
            }
            if (_row == HelperRow && (Input.Left || Input.Right || Input.Confirm)) ToggleHelper();
            else if (Ui.Clicked("opthelper")) { _row = HelperRow; ToggleHelper(); }
            if (_row == BackRow && Input.Confirm) { Close(); return; }

            // Mouse: press on a bar (or its row) and drag.
            int hit = Ui.Hover("optbar");
            if (Input.Click && hit >= 0) { _row = hit; _dragging = true; }
            if (!Input.LeftDown)
            {
                if (_dragging && _row == 1) Audio.Play("ui_select");
                _dragging = false;
            }
            if (_dragging && _row < 2)
            {
                var b = Bar(_row);
                Set(_row, (Input.MousePos.X - b.X) / b.Width);
            }
        }

        public override void Draw(SpriteBatch sb, GameTime gameTime)
        {
            Core.Draw.Rect(sb, new Rectangle(0, 0, Game1.Width, Game1.Height), Palette.Ink * 0.7f);
            Ui.BlockAll();
            var r = Box;
            Core.Draw.Rect(sb, r, Color.Lerp(Palette.Ink, Palette.Grey900, 0.3f) * 0.97f);
            Core.Draw.GradientV(sb, new Rectangle(r.X, r.Y, r.Width, 80), Palette.Crimson * 0.45f);
            Core.Draw.Border(sb, r, Palette.LineBright, 1);
            Core.Draw.SpacedCentered(sb, Assets.Header, "OPTIONS", new Vector2(r.Center.X, r.Y + 30), Palette.Paper, 3);
            Core.Draw.Divider(sb, new Vector2(r.Center.X, r.Y + 52), r.Width - 160, Palette.Paper * 0.5f);
            Core.Draw.Ornament(sb, new Vector2(r.Center.X, r.Y + 52), 12, Palette.Gold);

            string[] names = { "Music", "Sound Effects" };
            for (int i = 0; i < 2; i++)
            {
                var bar = Bar(i);
                var row = new Rectangle(r.X + 20, bar.Center.Y - 20, r.Width - 40, 40);
                if (i == _row) Ui.SelectedRow(sb, row);
                Core.Draw.Text(sb, Assets.Body, names[i], new Vector2(r.X + 50, bar.Center.Y - 11), i == _row ? Palette.Paper : Palette.Text);
                float v = Get(i);
                Core.Draw.Rect(sb, bar, Palette.Ink * 0.8f);
                Core.Draw.Rect(sb, new Rectangle(bar.X, bar.Y, (int)(bar.Width * v), bar.Height), Palette.Rose);
                Core.Draw.Border(sb, bar, Palette.Line, 1);
                Core.Draw.Diamond(sb, new Vector2(bar.X + bar.Width * v, bar.Center.Y), 16, Palette.Paper);
                Core.Draw.TextRight(sb, Assets.Body, $"{(int)Math.Round(v * 100)}%", new Vector2(r.Right - 30, bar.Center.Y - 11), Palette.TextDim);
                Ui.Hit("optbar", i, new Rectangle(bar.X - 10, row.Y, bar.Width + 20, row.Height));
            }
            // Dialogue helper: an On / Off switch with a line on what it does.
            {
                var bar = Bar(HelperRow);
                var row = new Rectangle(r.X + 20, bar.Center.Y - 20, r.Width - 40, 40);
                if (_row == HelperRow) Ui.SelectedRow(sb, row);
                Core.Draw.Text(sb, Assets.Body, "Dialogue Helper", new Vector2(r.X + 50, bar.Center.Y - 11), _row == HelperRow ? Palette.Paper : Palette.Text);
                bool on = Settings.Current.DialogueHelper;
                var sw = new Rectangle(bar.X, bar.Center.Y - 14, 120, 28);
                Core.Draw.Rect(sb, sw, Palette.Ink * 0.8f);
                Core.Draw.Rect(sb, new Rectangle(on ? sw.Center.X : sw.X, sw.Y, sw.Width / 2, sw.Height), on ? Palette.Rose : Palette.Grey700);
                Core.Draw.Border(sb, sw, Palette.Line, 1);
                Core.Draw.TextCentered(sb, Assets.Small, "OFF", new Vector2(sw.X + sw.Width / 4f, sw.Center.Y), on ? Palette.TextDim : Palette.Paper, shadow: false);
                Core.Draw.TextCentered(sb, Assets.Small, "ON", new Vector2(sw.X + sw.Width * 3 / 4f, sw.Center.Y), on ? Palette.Paper : Palette.TextDim, shadow: false);
                Core.Draw.Text(sb, Assets.Small, "Tags each choice with its tone, and shows\nhow your squad takes what you say.",
                    new Vector2(sw.Right + 16, sw.Y - 3), Palette.TextDim, shadow: false);
                Ui.Hit("opthelper", row);
            }
            Ui.Button(sb, "optback", 0, new Rectangle(r.Center.X - 80, r.Bottom - 56, 160, 36), "Back", selected: _row == BackRow);
            Core.Draw.TextCentered(sb, Assets.Small, "Up / Down choose   Left / Right change   click or drag   Esc close",
                new Vector2(r.Center.X, r.Bottom + 20), Palette.TextDim);
        }
    }
}
