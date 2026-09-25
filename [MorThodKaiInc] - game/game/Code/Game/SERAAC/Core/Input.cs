using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace SERAAC.Core
{
    // Keyboard snapshot for the current frame. Screens ask intent-level questions
    // (Confirm, Cancel, Up...) so rebinding keys only ever touches this file.
    public static class Input
    {
        private static KeyboardState _now, _prev;

        // Keys held down by a script (see ScriptRunner) on top of the real keyboard.
        public static readonly HashSet<Keys> Injected = new();
        public static bool ScriptOnly;   // ignore the real keyboard while a script drives the game

        public static void Update()
        {
            _prev = _now;
            var real = ScriptOnly ? new KeyboardState() : Keyboard.GetState();
            _now = Injected.Count == 0 ? real : new KeyboardState(real.GetPressedKeys().Concat(Injected).Distinct().ToArray());
        }

        public static bool Down(Keys k) => _now.IsKeyDown(k);
        public static bool Pressed(Keys k) => _now.IsKeyDown(k) && !_prev.IsKeyDown(k);

        public static bool Up => Pressed(Keys.Up) || Pressed(Keys.W);
        public static bool DownKey => Pressed(Keys.Down) || Pressed(Keys.S);
        public static bool Left => Pressed(Keys.Left) || Pressed(Keys.A);
        public static bool Right => Pressed(Keys.Right) || Pressed(Keys.D);
        public static bool Confirm => Pressed(Keys.Enter) || Pressed(Keys.Space) || Pressed(Keys.Z);
        public static bool Cancel => Pressed(Keys.Escape) || Pressed(Keys.Back) || Pressed(Keys.X);
        public static bool Interact => Pressed(Keys.E) || Pressed(Keys.Enter) || Pressed(Keys.Space);
        public static bool FastForward => Down(Keys.Space) || Down(Keys.Enter) || Down(Keys.LeftShift);

        // Continuous movement vector for exploration (WASD / arrows), not normalized.
        public static Vector2 Move
        {
            get
            {
                var v = Vector2.Zero;
                if (Down(Keys.W) || Down(Keys.Up)) v.Y -= 1;
                if (Down(Keys.S) || Down(Keys.Down)) v.Y += 1;
                if (Down(Keys.A) || Down(Keys.Left)) v.X -= 1;
                if (Down(Keys.D) || Down(Keys.Right)) v.X += 1;
                return v;
            }
        }
    }
}
