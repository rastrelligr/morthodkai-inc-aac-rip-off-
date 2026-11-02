using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.Tweening;

namespace SERAAC.Core
{
    // One game state (menu, hub, exploration, combat, result).
    public abstract class Screen
    {
        public ScreenManager Screens { get; internal set; }

        // Screens beneath an overlay screen still draw (e.g. exploration under a modal).
        public virtual bool IsOverlay => false;

        // Shared UI sounds (UI/UiSounds.cs). MenuKeySounds: the arrow keys, Enter and Esc make the menu sounds
        // (off where they do something else, like walking). UiSoundsFor: hovering / clicking the clickable `key` does.
        public virtual bool MenuKeySounds => true;
        public virtual bool UiSoundsFor(string key) => true;

        public virtual void OnEnter() { }
        // Called when the screen is removed from the stack (free GPU resources here).
        public virtual void OnExit() { }
        // Called when a screen pushed on top of this one is popped.
        public virtual void OnResume() { }
        public abstract void Update(GameTime gameTime);
        public abstract void Draw(SpriteBatch sb, GameTime gameTime);
    }

    // Stack of screens with a fade between changes. Combat is pushed over Exploration
    // and popped when it ends, so the trial state never has to be rebuilt.
    // The fade is eased (out to black, swap, back in) with MonoGame.Extended's Tweener.
    public sealed class ScreenManager
    {
        private readonly List<Screen> _stack = new();
        private readonly Tweens _tweens = new();
        private readonly Anim _fade = new();   // 0 = clear, 1 = black
        private const float FadeOut = 0.2f, FadeIn = 0.26f;

        public Screen Top => _stack.Count > 0 ? _stack[^1] : null;
        public bool Transitioning { get; private set; }

        public void Replace(Screen s) => Queue(() =>
        {
            foreach (var old in _stack) old.OnExit();
            _stack.Clear();
            Add(s);
        });

        public void Push(Screen s) => Queue(() => Add(s));

        public void Pop() => Queue(PopInstant);

        // Immediate push, no fade (for modal overlays).
        public void PushInstant(Screen s) => Add(s);

        public void PopInstant()
        {
            if (_stack.Count > 0)
            {
                _stack[^1].OnExit();
                _stack.RemoveAt(_stack.Count - 1);
            }
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
            Transitioning = true;
            _tweens.To(_fade, 1f, FadeOut, EasingFunctions.QuadraticIn).OnEnd(_ =>
            {
                change();
                _tweens.To(_fade, 0f, FadeIn, EasingFunctions.QuadraticOut).OnEnd(_ => Transitioning = false);
            });
        }

        public void Update(GameTime gt)
        {
            _tweens.Update((float)gt.ElapsedGameTime.TotalSeconds);
            if (Transitioning) return; // no input during a transition
            Top?.Update(gt);
            UI.UiSounds.AfterUpdate(Top);
        }

        private int FirstVisible()
        {
            int first = _stack.Count - 1;
            while (first > 0 && _stack[first].IsOverlay) first--;
            return Math.Max(first, 0);
        }

        public void Draw(SpriteBatch sb, GameTime gt)
        {
            for (int i = FirstVisible(); i < _stack.Count; i++)
                _stack[i].Draw(sb, gt);

            if (_fade > 0f)
                Core.Draw.Rect(sb, new Rectangle(0, 0, Game1.Width, Game1.Height), Color.Black * _fade);
        }
    }
}
