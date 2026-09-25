using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SERAAC.Core;
using SERAAC.Input;

namespace SERAAC.Screens
{
    public class ResultScreen : Screen
    {
        private readonly VesselInput _vesselInput = new VesselInput();
        private readonly bool _victory;

        public ResultScreen(Game1 game, GameContext context, SharedAssets assets, bool victory) : base(game, context, assets)
        {
            _victory = victory;
        }

        public override void OnEnter() => Game.IsMouseVisible = true;

        public override void Update(GameTime gameTime)
        {
            _vesselInput.Update();

            if (_vesselInput.Confirm)
            {
                Context.StartNewRun();
                Game.ChangeScreen(new ExplorationScreen(Game, Context, Assets));
            }
            else if (_vesselInput.Cancel)
            {
                Game.ChangeScreen(new MainMenuScreen(Game, Context, Assets));
            }
        }

        public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            var device = Game.GraphicsDevice;
            int w = device.Viewport.Width;
            int h = device.Viewport.Height;

            spriteBatch.Begin();
            spriteBatch.Draw(Assets.Pixel, new Rectangle(0, 0, w, h), new Color(8, 8, 12));

            var title = Assets.HeaderFont;
            var body = Assets.BodyFont;

            string text = _victory ? "THE DUNGEON IS CLEARED" : "YOUR RUN HAS ENDED";
            if (title != null)
            {
                var size = title.MeasureString(text);
                spriteBatch.DrawString(title, text, new Vector2(w / 2f - size.X / 2f, h * 0.4f),
                    _victory ? Color.Gold : Color.OrangeRed);
            }

            if (body != null)
            {
                string prompt = "Space: Start a new run      Esc: Main Menu";
                var size = body.MeasureString(prompt);
                spriteBatch.DrawString(body, prompt, new Vector2(w / 2f - size.X / 2f, h * 0.55f), Color.White);
            }

            spriteBatch.End();
        }
    }
}
