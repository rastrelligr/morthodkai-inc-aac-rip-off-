using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended.Input;

namespace SERAAC.Core
{
    // Keyboard + mouse snapshot for the current frame. Screens ask intent-level questions
    // (Confirm, Cancel, Up...) so rebinding keys only ever touches this file.
    // Mouse positions are in 1280x720 layout units (black bars and scaling already removed).
    // Edge detection (pressed / released this frame) comes from MonoGame.Extended's state pairs; the raw
    // states are still built here so a script can inject keys and clicks on top of the real devices.
    public static class Input
    {
        private static KeyboardState _kbPrev;
        private static MouseState _msPrev;
        private static KeyboardStateExtended _keys;
        private static MouseStateExtended _mouse;

        // Keys held down by a script (see ScriptRunner) on top of the real keyboard.
        public static readonly HashSet<Keys> Injected = new();
        public static bool ScriptOnly;   // ignore the real keyboard and mouse while a script drives the game

        // Set by ScriptRunner to click at a layout position this frame.
        public static Vector2? InjectedClick;
        private static bool _injectedClickPrev;

        public static int Frame { get; private set; }   // counts Updates

        public static void Update(bool windowActive = true)
        {
            Frame++;
            var real = ScriptOnly ? new KeyboardState() : Keyboard.GetState();
            var kb = Injected.Count == 0 ? real : new KeyboardState(real.GetPressedKeys().Concat(Injected).Distinct().ToArray());
            _keys = new KeyboardStateExtended(kb, _kbPrev);
            var prev = _kbPrev;
            AnyKeyPressed = kb.GetPressedKeys().Any(k => !prev.IsKeyDown(k) && k is not (Keys.F3 or Keys.F11 or Keys.F12 or Keys.LeftAlt or Keys.RightAlt));
            _kbPrev = kb;

            var ms = ScriptOnly || !windowActive ? default : Mouse.GetState();
            _mouse = new MouseStateExtended(ms, _msPrev);
            _msPrev = ms;
            PrevMouse = MousePos;
            if (!ScriptOnly && windowActive) MousePos = Draw.ToLayout(ms.Position);

            _injectedClickPrev = ScriptClick;
            ScriptClick = InjectedClick.HasValue;
            if (InjectedClick.HasValue) MousePos = InjectedClick.Value;
        }

        private static bool ScriptClick;

        // Any key went down this frame (not the F3 / F11 / F12 / Alt system keys), or any mouse button was clicked.
        public static bool AnyKeyPressed { get; private set; }
        public static bool AnyPress => AnyKeyPressed || Click || RightClick;

        public static bool Down(Keys k) => _keys.IsKeyDown(k);
        public static bool Pressed(Keys k) => _keys.WasKeyPressed(k);

        // ------------------------------------------------------------------ mouse

        public static Vector2 MousePos { get; private set; } = new(-1000, -1000);
        public static Vector2 PrevMouse { get; private set; }
        public static bool MouseMoved => (MousePos - PrevMouse).LengthSquared() > 0.25f;
        public static Vector2 MouseDelta => MousePos - PrevMouse;

        public static bool LeftDown => _mouse.IsButtonDown(MouseButton.Left) || ScriptClick;
        public static bool Click => _mouse.WasButtonPressed(MouseButton.Left) || (ScriptClick && !_injectedClickPrev);
        public static bool RightClick => _mouse.WasButtonPressed(MouseButton.Right);
        public static bool RightDown => _mouse.IsButtonDown(MouseButton.Right);
        // Positive = wheel up. (Extended's delta is previous minus current, hence the minus.)
        public static int Wheel => -_mouse.DeltaScrollWheelValue;

        public static bool MouseIn(Rectangle r) => r.Contains(MousePos);

        // ------------------------------------------------------------------ intents

        public static bool Up => Pressed(Keys.Up) || Pressed(Keys.W);
        public static bool DownKey => Pressed(Keys.Down) || Pressed(Keys.S);
        public static bool Left => Pressed(Keys.Left) || Pressed(Keys.A);
        public static bool Right => Pressed(Keys.Right) || Pressed(Keys.D);
        public static bool Confirm => Pressed(Keys.Enter) || Pressed(Keys.Space) || Pressed(Keys.Z);
        // Right click always means "back", like Esc.
        public static bool Cancel => Pressed(Keys.Escape) || Pressed(Keys.Back) || Pressed(Keys.X) || RightClick;
        public static bool Interact => Pressed(Keys.E) || Pressed(Keys.Enter) || Pressed(Keys.Space);
        // Held key or held left mouse button speeds up combat playback / text.
        public static bool FastForward => Down(Keys.Space) || Down(Keys.Enter) || Down(Keys.LeftShift) || LeftDown;

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
