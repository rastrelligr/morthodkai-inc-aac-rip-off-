using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace SERAAC.Input
{
    // Player 2's device: mouse. Delta drives the Weapon's free-look aim while exploring,
    // Position drives target picking in the combat panel, and the left button attacks.
    public class WeaponInput
    {
        private MouseState _previous;
        private MouseState _current;

        public Vector2 Delta { get; private set; }
        public Point Position { get; private set; }
        public bool Attack { get; private set; }
        public int ScrollDelta { get; private set; }

        public void Update()
        {
            _current = Mouse.GetState();

            Delta = (_current.Position - _previous.Position).ToVector2();
            Position = _current.Position;
            Attack = _current.LeftButton == ButtonState.Pressed && _previous.LeftButton == ButtonState.Released;
            ScrollDelta = _current.ScrollWheelValue - _previous.ScrollWheelValue;

            _previous = _current;
        }

        // Call after Update() while free-looking so the mouse never leaves the window.
        public void Recenter(Point center)
        {
            Mouse.SetPosition(center.X, center.Y);
            _previous = Mouse.GetState();
        }
    }
}
