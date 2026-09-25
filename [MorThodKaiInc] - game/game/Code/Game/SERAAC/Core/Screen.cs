using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SERAAC.Core
{
    // One game state (menu, hub, exploration, combat, result).
    public abstract class Screen
    {
        public ScreenManager Screens { get; internal set; }

        // Screens beneath an overlay screen still draw (e.g. exploration under a modal).
        public virtual bool IsOverlay => false;

        public virtual void OnEnter() { }
        // Called when a screen pushed on top of this one is popped.
        public virtual void OnResume() { }
        public abstract void Update(GameTime gameTime);
        public abstract void Draw(SpriteBatch sb, GameTime gameTime);
    }

    // Stack of screens with a fade between changes. Combat is pushed over Exploration
    // and popped when it ends, so the trial state never has to be rebuilt.
    public sealed class ScreenManager
    {
        private readonly List<Screen> _stack = new();
        private Action _pending;
        private float _fade;       // 0 = clear, 1 = black
        private int _fadeDir;      // +1 fading out, -1 fading in
        private const float FadeSpeed = 4.5f;

        public Screen Top => _stack.Count > 0 ? _stack[^1] : null;
        public bool Transitioning => _fadeDir != 0;

        public void Replace(Screen s) => Queue(() =>
        {
            _stack.Clear();
            Add(s);
        });

        public void Push(Screen s) => Queue(() => Add(s));

        public void Pop() => Queue(() =>
        {
            if (_stack.Count > 0) _stack.RemoveAt(_stack.Count - 1);
            Top?.OnResume();
        });

        // Immediate push, no fade (for modal overlays).
        public void PushInstant(Screen s) => Add(s);

        public void PopInstant()
        {
            if (_stack.Count > 0) _stack.RemoveAt(_stack.Count - 1);
            Top?.OnResume();
        }

        private void Add(Screen s)
        {
            s.Screens = this;
            _stack.Add(s);
            s.OnEnter();
        }

        private void Queue(Action change)
        {
            if (_stack.Count == 0)
            {
                change();
                return;
            }
            _pending = change;
            _fadeDir = 1;
        }

        public void Update(GameTime gt)
        {
            float dt = (float)gt.ElapsedGameTime.TotalSeconds;
            if (_fadeDir != 0)
            {
                _fade += _fadeDir * FadeSpeed * dt;
                if (_fadeDir > 0 && _fade >= 1f)
                {
                    _fade = 1f;
                    _pending?.Invoke();
                    _pending = null;
                    _fadeDir = -1;
                }
                else if (_fadeDir < 0 && _fade <= 0f)
                {
                    _fade = 0f;
                    _fadeDir = 0;
                }
                return; // no input during a transition
            }
            Top?.Update(gt);
        }

        public void Draw(SpriteBatch sb, GameTime gt)
        {
            int first = _stack.Count - 1;
            while (first > 0 && _stack[first].IsOverlay) first--;
            for (int i = Math.Max(first, 0); i < _stack.Count; i++)
                _stack[i].Draw(sb, gt);

            if (_fade > 0f)
                Core.Draw.Rect(sb, new Rectangle(0, 0, Game1.Width, Game1.Height), Color.Black * _fade);
        }
    }
}
