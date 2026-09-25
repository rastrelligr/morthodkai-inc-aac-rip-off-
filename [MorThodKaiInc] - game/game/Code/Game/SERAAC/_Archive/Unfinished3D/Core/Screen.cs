using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SERAAC.Core
{
    // Base for every game state (Main Menu, Exploration, Combat, Result). Game1 owns
    // exactly one active Screen and forwards Update/Draw to it.
    public abstract class Screen
    {
        protected readonly Game1 Game;
        protected readonly GameContext Context;
        protected readonly SharedAssets Assets;

        protected Screen(Game1 game, GameContext context, SharedAssets assets)
        {
            Game = game;
            Context = context;
            Assets = assets;
        }

        // Called every time this screen becomes active (may be more than once for menus).
        public virtual void OnEnter() { }
        public virtual void OnExit() { }

        public abstract void Update(GameTime gameTime);
        public abstract void Draw(GameTime gameTime, SpriteBatch spriteBatch);
    }
}
