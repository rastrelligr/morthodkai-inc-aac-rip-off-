using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.Tweening;

namespace SERAAC.Core
{
    // A number that UI animates (alpha, slide, glow...). Read it with .Value or use it as a float.
    public sealed class Anim
    {
        public float Value;
        public Anim(float value = 0f) { Value = value; }
        public static implicit operator float(Anim a) => a.Value;
    }

    // UI animation on MonoGame.Extended's Tweener: each screen owns one and calls Update every frame.
    // Starting a new tween on an Anim replaces the one already running on it.
    public sealed class Tweens
    {
        private readonly Tweener _tweener = new();

        public void Update(float dt) => _tweener.Update(dt);

        // Animates a from where it is now to `to`. ease: an EasingFunctions method (CubicOut, BackOut...).
        public Tween To(Anim a, float to, float seconds, Func<float, float> ease = null, float delay = 0f)
        {
            Cancel(a);
            return _tweener.TweenTo(a, x => x.Value, to, Math.Max(0.0001f, seconds), delay).Easing(ease ?? EasingFunctions.CubicOut);
        }

        // Jumps to `from`, then animates to `to`.
        public Tween FromTo(Anim a, float from, float to, float seconds, Func<float, float> ease = null, float delay = 0f)
        {
            Cancel(a);
            a.Value = from;
            return To(a, to, seconds, ease, delay);
        }

        public void Cancel(Anim a) => _tweener.FindTween(a, nameof(Anim.Value))?.Cancel();

        public bool IsRunning(Anim a) => _tweener.FindTween(a, nameof(Anim.Value)) is { IsAlive: true };
    }

    public static class Ease
    {
        // 0..1 progress of something that started `elapsed` seconds ago and lasts `duration`, eased
        // (for things that already keep their own age, like floating numbers).
        public static float Progress(float elapsed, float duration, Func<float, float> ease) =>
            ease(MathHelper.Clamp(duration <= 0 ? 1 : elapsed / duration, 0, 1));
    }
}
