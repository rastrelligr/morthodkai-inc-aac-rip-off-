using Microsoft.Xna.Framework.Input;

namespace SERAAC.Input
{
    // Player 1's device: keyboard. WASD drives the Vessel (movement / grid positioning),
    // Space is the Vessel's Defend / confirm action. All flags are edge-triggered (this-frame-only).
    public class VesselInput
    {
        private KeyboardState _previous;
        private KeyboardState _current;

        public bool Up { get; private set; }
        public bool Down { get; private set; }
        public bool Left { get; private set; }
        public bool Right { get; private set; }
        public bool Confirm { get; private set; }
        public bool Cancel { get; private set; }

        public void Update()
        {
            _current = Keyboard.GetState();

            Up = Pressed(Keys.W);
            Down = Pressed(Keys.S);
            Left = Pressed(Keys.A);
            Right = Pressed(Keys.D);
            Confirm = Pressed(Keys.Space);
            Cancel = Pressed(Keys.Escape);

            _previous = _current;
        }

        private bool Pressed(Keys key) => _current.IsKeyDown(key) && !_previous.IsKeyDown(key);

        public bool IsDown(Keys key) => _current.IsKeyDown(key);
    }
}
