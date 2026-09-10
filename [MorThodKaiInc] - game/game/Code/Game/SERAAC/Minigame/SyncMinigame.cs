using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;

namespace SERAAC.Minigame
{
    public class SyncMinigame
    {
        public bool Active { get; private set; }
        public double Timer { get; private set; }
        public double Duration { get; private set; } = 3.0;
        public bool P1Pressed { get; private set; }
        public bool P2Pressed { get; private set; }
        private double _p1At = -1;
        private double _p2At = -1;
        public double PressWindowMs { get; set; } = 400.0;

        public void Start(double durationSeconds)
        {
            Active = true;
            Duration = durationSeconds;
            Timer = durationSeconds;
            P1Pressed = false;
            P2Pressed = false;
            _p1At = _p2At = -1;
        }

        public void Update(GameTime gt, KeyboardState ks, KeyboardState prev)
        {
            if (!Active) return;
            Timer -= gt.ElapsedGameTime.TotalSeconds;
            if (ks.IsKeyDown(Keys.Space) && !prev.IsKeyDown(Keys.Space) && !P1Pressed)
            {
                P1Pressed = true;
                _p1At = gt.TotalGameTime.TotalMilliseconds;
            }
            if (ks.IsKeyDown(Keys.RightShift) && !prev.IsKeyDown(Keys.RightShift) && !P2Pressed)
            {
                P2Pressed = true;
                _p2At = gt.TotalGameTime.TotalMilliseconds;
            }
        }

        public bool IsComplete()
        {
            return (!Active) ? true : ((P1Pressed && P2Pressed) || Timer <= 0);
        }

        public bool Evaluate()
        {
            if (!Active) return false;
            bool success = false;
            if (P1Pressed && P2Pressed)
            {
                var delta = Math.Abs(_p1At - _p2At);
                success = delta <= PressWindowMs;
            }
            Active = false;
            return success;
        }
    }
}
