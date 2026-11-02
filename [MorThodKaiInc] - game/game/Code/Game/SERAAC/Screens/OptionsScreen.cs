using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SERAAC.Core;
using SERAAC.UI;

namespace SERAAC.Screens
{
    // Volume options, over whatever screen opened it (title menu, pause menu, the story maps).
    // Up / Down pick a row, Left / Right change it by 10%, or click / drag on a bar. Esc or Back saves and closes.
    public sealed class OptionsScreen : Screen
    {
        private int _row;
        private bool _dragging;
        private const int Rows = 3;   // music, sound effects, back

        private static readonly Rectangle Box = new(Game1.Width / 2 - 330, Game1.Height / 2 - 150, 660, 300);
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
            if (_row == 2 && Input.Confirm) { Close(); return; }

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
            Ui.Button(sb, "optback", 0, new Rectangle(r.Center.X - 80, r.Bottom - 60, 160, 36), "Back", selected: _row == 2);
            Core.Draw.TextCentered(sb, Assets.Small, "Up / Down choose   Left / Right change   click or drag the bars   Esc close",
                new Vector2(r.Center.X, r.Bottom + 20), Palette.TextDim);
        }
    }
}
