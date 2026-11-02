using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SERAAC.Core;
using SERAAC.UI;

namespace SERAAC.Screens
{
    // Plays a visual-novel scene (UI/VnDialogue.cs) over whatever is underneath, then calls onDone.
    // Pushed instantly (Screens.PushInstant) over a map, a fight or the hub; with a background it covers them.
    public sealed class StoryScreen : Screen
    {
        public VnDialogue Dialogue { get; }
        private readonly Action _onDone;
        private readonly bool _black;
        private float _in, _out = -1;

        // black: draw on a black screen (nothing underneath shows through when there is no picture).
        public StoryScreen(IEnumerable<VnLine> lines, Action onDone = null, string background = null, bool black = false)
        {
            Dialogue = new VnDialogue(lines, background);
            _onDone = onDone;
            _black = black;
        }

        public override bool IsOverlay => true;

        public override void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _in = Math.Min(1f, _in + dt / 0.25f);
            if (_out >= 0)
            {
                _out += dt / 0.2f;
                Dialogue.Alpha = Math.Max(0, 1 - _out);
                if (_out >= 1)
                {
                    Screens.PopInstant();
                    _onDone?.Invoke();
                }
                return;
            }
            Dialogue.Alpha = _in;
            Dialogue.Update(dt);
            if (Dialogue.Finished) _out = 0;
        }

        public override void Draw(SpriteBatch sb, GameTime gameTime)
        {
            if (_black) Core.Draw.Rect(sb, new Rectangle(0, 0, Game1.Width, Game1.Height), Palette.Ink);
            Ui.BlockAll();
            Dialogue.Draw(sb);
        }
    }
}
