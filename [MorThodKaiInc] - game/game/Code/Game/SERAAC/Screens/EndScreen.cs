using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SERAAC.Core;
using SERAAC.UI;

namespace SERAAC.Screens
{
    // Shown after the Wyvern falls: the end of the playtest build.
    public sealed class EndScreen : Screen
    {
        private float _t;

        public override void Update(GameTime gameTime)
        {
            _t += (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_t > 1.5f && (Input.Confirm || Input.Click || Input.Cancel)) Screens.Replace(new MainMenuScreen());
        }

        public override void Draw(SpriteBatch sb, GameTime gameTime)
        {
            double time = gameTime.TotalGameTime.TotalSeconds;
            Core.Draw.Rect(sb, new Rectangle(0, 0, Game1.Width, Game1.Height), Palette.Ink);
            Ui.Embers(sb, time, 0.6f);
            float a = MathHelper.Clamp(_t / 1.2f, 0, 1);
            var c = new Vector2(Game1.Width / 2f, Game1.Height / 2f);
            Core.Draw.SpacedCentered(sb, Assets.Title, "AETHERIS", c + new Vector2(0, -70), Palette.Paper * a, 8);
            Core.Draw.Divider(sb, c + new Vector2(0, -30), 360 * a, Palette.Paper * 0.5f * a);
            Core.Draw.Ornament(sb, c + new Vector2(0, -30), 12, Palette.Gold * a);
            Core.Draw.TextCentered(sb, Assets.Header, "Thank you for playing the playtest!", c + new Vector2(0, 6), Palette.Blush * a);
            Core.Draw.TextCentered(sb, Assets.Body, "Please tell us what you thought: what was fun, what was confusing, what broke.", c + new Vector2(0, 44), Palette.TextDim * a);
            if (_t > 1.5f)
                Core.Draw.TextCentered(sb, Assets.Small, "click or Enter to return to the title", c + new Vector2(0, 120), Palette.TextDim * (0.6f + 0.4f * (float)System.Math.Sin(time * 3)));
        }
    }
}
