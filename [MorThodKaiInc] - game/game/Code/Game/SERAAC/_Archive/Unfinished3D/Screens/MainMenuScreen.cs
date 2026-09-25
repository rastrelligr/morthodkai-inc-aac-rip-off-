using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SERAAC.Core;
using SERAAC.Input;

namespace SERAAC.Screens
{
    public class MainMenuScreen : Screen
    {
        private readonly VesselInput _vesselInput = new VesselInput();

        public MainMenuScreen(Game1 game, GameContext context, SharedAssets assets) : base(game, context, assets) { }

        public override void Update(GameTime gameTime)
        {
            _vesselInput.Update();

            if (_vesselInput.Confirm)
            {
                Context.StartNewRun();
                Game.ChangeScreen(new ExplorationScreen(Game, Context, Assets));
            }
        }

        public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            var device = Game.GraphicsDevice;
            int w = device.Viewport.Width;
            int h = device.Viewport.Height;

            spriteBatch.Begin();
            spriteBatch.Draw(Assets.Pixel, new Rectangle(0, 0, w, h), new Color(10, 10, 16));

            var title = Assets.HeaderFont;
            var body = Assets.BodyFont;

            if (title != null)
            {
                string text = "SERAAC";
                var size = title.MeasureString(text);
                spriteBatch.DrawString(title, text, new Vector2(w / 2f - size.X / 2f, h * 0.35f), Color.Goldenrod);
            }

            if (body != null)
            {
                string sub = "One Vessel. One Weapon. Two players.";
                var subSize = body.MeasureString(sub);
                spriteBatch.DrawString(body, sub, new Vector2(w / 2f - subSize.X / 2f, h * 0.35f + 60), Color.LightGray);

                string prompt = "P1: Press SPACE to begin the descent";
                var promptSize = body.MeasureString(prompt);
                spriteBatch.DrawString(body, prompt, new Vector2(w / 2f - promptSize.X / 2f, h * 0.6f), Color.White);
            }

            spriteBatch.End();
        }
    }
}
