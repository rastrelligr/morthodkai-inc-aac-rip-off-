using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;
using MonoGame.Extended.Graphics;
using MonoGame.Extended.Particles;
using MonoGame.Extended.Particles.Data;
using MonoGame.Extended.Particles.Modifiers;
using MonoGame.Extended.Particles.Profiles;
using SERAAC.Core;
using ExtendedParticles = MonoGame.Extended.Particles.SpriteBatchExtensions;

namespace SERAAC.Fx
{
    // One effect in Art/Fx/particles.json. Every field is optional. Ranges are [min, max].
    public sealed class ParticlePreset
    {
        public int Count = 10;
        public float[] Life = { 0.3f, 0.6f };       // seconds
        public float[] Speed = { 80, 220 };         // layout pixels per second
        public float Angle;                          // degrees, 0 = toward the target (right for the party), 90 = down
        public float Spread = 360;                   // degrees around Angle
        public float[] Offset = { 0, 0 };            // spawn offset from the effect point (x flips with direction)
        public float Radius;                         // spawn anywhere inside this circle
        public float Gravity;                        // pixels per second², + is down
        public float Drag = 2;                       // velocity lost per second (fraction)
        public float[] Size = { 6, 1 };              // start, end (pixels)
        public float[] Stretch = { 1, 1 };           // streak: length multiplier start, end
        public List<float[]> Colors = new() { new float[] { 255, 255, 255 } }; // over lifetime, 0-255 (optional 4th = alpha)
        public string Shape = "square";              // square, diamond, circle, streak, ring
        public string Texture;                        // Art id (Art/<id>.png) to use instead of a shape
        public bool Additive;                         // glowy blending
        public float Spin;                            // degrees per second (direction follows the particle's motion)
        public float Delay;                           // seconds before the burst appears
        public List<string> Also = new();             // other presets fired at the same time
    }

    public sealed class ParticleFile : Dictionary<string, ParticlePreset> { }

    // Combat effects on MonoGame.Extended's particle engine. Each preset in Art/Fx/particles.json becomes one
    // Extended ParticleEmitter (built the first time it fires); Emit releases a burst from it. Saving the file
    // while the game runs rebuilds the emitters, and bursts already in the air finish on the old ones.
    public sealed class ParticleSystem : IDisposable
    {
        public static readonly LiveFile<ParticleFile> Presets = new("Fx/particles.json");

        private const int Capacity = 600;   // per preset; a burst that doesn't fit is cut short

        private readonly Dictionary<string, PresetEmitter> _emitters = new();
        private readonly List<PresetEmitter> _retired = new();   // from an older particles.json, still finishing
        private int _builtVersion = -1;
        private readonly List<(float delay, string preset, Vector2 pos, int dir, float scale)> _pending = new();
        private readonly Random _rng = new();

        public int Count => _emitters.Values.Concat(_retired).Sum(e => e.Emitter.ActiveParticles);

        // Fires a preset at a layout position. dir = +1 aims right (party attacking), -1 aims left.
        public void Emit(string presetId, Vector2 pos, int dir = 1, float scale = 1f)
        {
            if (string.IsNullOrEmpty(presetId)) return;
            if (!Presets.Value.TryGetValue(presetId, out var p)) return;
            if (p.Delay > 0) { _pending.Add((p.Delay, presetId, pos, dir, scale)); return; }
            Burst(presetId, p, pos, dir, scale);
            foreach (var also in p.Also) if (also != presetId) Emit(also, pos, dir, scale);
        }

        private void Burst(string id, ParticlePreset p, Vector2 pos, int dir, float scale)
        {
            if (Presets.Version != _builtVersion)
            {
                _retired.AddRange(_emitters.Values);
                _emitters.Clear();
                _builtVersion = Presets.Version;
            }
            if (!_emitters.TryGetValue(id, out var e)) _emitters[id] = e = new PresetEmitter(p, _rng);
            e.Release(pos, dir, scale);
        }

        public void Update(float dt)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var e = _pending[i];
                e.delay -= dt;
                if (e.delay > 0) { _pending[i] = e; continue; }
                _pending.RemoveAt(i);
                if (Presets.Value.TryGetValue(e.preset, out var p))
                {
                    Burst(e.preset, p, e.pos, e.dir, e.scale);
                    foreach (var also in p.Also) if (also != e.preset) Emit(also, e.pos, e.dir, e.scale);
                }
            }

            PresetModifier.FrameDt = dt;
            foreach (var e in _emitters.Values) e.Emitter.Update(dt, Vector2.Zero);
            for (int i = _retired.Count - 1; i >= 0; i--)
            {
                _retired[i].Emitter.Update(dt, Vector2.Zero);
                if (_retired[i].Emitter.ActiveParticles == 0) { _retired[i].Emitter.Dispose(); _retired.RemoveAt(i); }
            }
        }

        public void Clear()
        {
            _pending.Clear();
            foreach (var e in _emitters.Values.Concat(_retired)) e.Emitter.Dispose();
            _emitters.Clear();
            _retired.Clear();
        }

        public void Dispose() => Clear();

        // Draws with whatever batch is open (normal blending), then additive ones in their own batch.
        // `transform` is the batch matrix to reopen with.
        public void Draw(SpriteBatch sb, Matrix transform)
        {
            // Extended's renderer picks how it applies opacity from the device's blend state at draw time
            // (AlphaBlend: premultiply; anything else: alpha only), so set it to match each pass.
            var gd = sb.GraphicsDevice;
            var all = _emitters.Values.Concat(_retired).Where(e => e.Emitter.ActiveParticles > 0).ToList();
            gd.BlendState = BlendState.AlphaBlend;
            foreach (var e in all) if (!e.Additive) ExtendedParticles.Draw(sb, e.Emitter);
            if (!all.Any(e => e.Additive)) return;
            sb.End();
            sb.Begin(blendState: BlendState.Additive, samplerState: SamplerState.LinearClamp, transformMatrix: transform);
            gd.BlendState = BlendState.Additive;
            foreach (var e in all) if (e.Additive) ExtendedParticles.Draw(sb, e.Emitter);
            sb.End();
            sb.Begin(samplerState: SamplerState.LinearClamp, transformMatrix: transform);
        }

        // ------------------------------------------------------------------ one preset = one Extended emitter

        private sealed class PresetEmitter
        {
            public readonly ParticleEmitter Emitter;
            public readonly bool Additive;
            private readonly ParticlePreset _p;
            private readonly PresetProfile _profile;

            public PresetEmitter(ParticlePreset p, Random rng)
            {
                _p = p;
                Additive = p.Additive;
                float lifeMax = Math.Max(0.01f, Max(p.Life));
                var (region, texSize) = Shapes.For(p);
                _profile = new PresetProfile(p, rng);
                Emitter = new ParticleEmitter(Capacity)
                {
                    LifeSpan = lifeMax,
                    TextureRegion = region,
                    Profile = _profile,
                    // Extended runs modifiers at most once per update; this makes it every update.
                    Modifiers = { new PresetModifier(p, texSize) { Frequency = 1e6f } },
                };
                var first = PresetModifier.ColorAt(p, 0);
                var par = Emitter.Parameters;
                var hsl0 = ToHsl(first.rgb);
                par.Color = new ParticleColorParameter(new Vector3(hsl0.H, hsl0.S, hsl0.L));   // Extended particles use HSL
                par.Opacity = new ParticleFloatParameter(first.alpha);
                float s0 = Lerp(p.Size, 0) / texSize;
                par.Scale = new ParticleVector2Parameter(new Vector2(s0, s0));
                par.Rotation = UsesRotation(p) ? new ParticleFloatParameter(-MathHelper.Pi, MathHelper.Pi) : new ParticleFloatParameter(0f);
                // Each particle's share of the longest life (Extended has one life span per emitter):
                // Mass carries it, and PresetModifier ends the particle early when it runs out.
                par.Mass = new ParticleFloatParameter(Math.Max(0.01f, Min(p.Life)) / lifeMax, 1f);
            }

            public void Release(Vector2 pos, int dir, float scale)
            {
                int n = Math.Max(0, (int)Math.Round(_p.Count * Math.Max(0.2f, scale)));
                if (n == 0) return;
                float speedScale = 0.6f + 0.4f * scale;
                var par = Emitter.Parameters;
                par.Quantity = new ParticleInt32Parameter(n);
                par.Speed = new ParticleFloatParameter(Min(_p.Speed) * speedScale, Max(_p.Speed) * speedScale);
                _profile.Dir = dir;
                var off = _p.Offset is { Length: >= 2 } ? new Vector2(_p.Offset[0] * dir, _p.Offset[1]) : Vector2.Zero;
                Emitter.Trigger(pos + off, 0f);
            }

            private static bool UsesRotation(ParticlePreset p) => p.Texture != null || p.Shape is "square" or "diamond";
        }

        // Where a burst starts and which way each particle flies: anywhere in Radius, heading Angle ± Spread/2,
        // mirrored for enemy attackers (Dir = -1).
        private sealed unsafe class PresetProfile : Profile
        {
            private readonly ParticlePreset _p;
            private readonly Random _rng;
            public int Dir = 1;

            public PresetProfile(ParticlePreset p, Random rng) { _p = p; _rng = rng; }

            public override void GetOffsetAndHeading(Vector2* offset, Vector2* heading)
            {
                float ang = MathHelper.ToRadians(_p.Angle + ((float)_rng.NextDouble() - 0.5f) * _p.Spread);
                *heading = new Vector2(MathF.Cos(ang) * Dir, MathF.Sin(ang));
                float ra = (float)_rng.NextDouble() * MathHelper.TwoPi, rr = MathF.Sqrt((float)_rng.NextDouble()) * _p.Radius;
                *offset = new Vector2(MathF.Cos(ra), MathF.Sin(ra)) * rr;
            }
        }

        // The preset's look over each particle's life: colour ramp + fade, size, gravity, drag, spin, streaks.
        // Extended hands modifiers a fixed step, so the real frame time comes in through FrameDt.
        private sealed unsafe class PresetModifier : Modifier
        {
            public static float FrameDt;
            private readonly ParticlePreset _p;
            private readonly float _texSize;
            private readonly float _spin;
            private readonly bool _streak, _rotates;

            public PresetModifier(ParticlePreset p, float texSize)
            {
                _p = p;
                _texSize = texSize;
                _spin = MathHelper.ToRadians(p.Spin);
                _streak = p.Texture == null && p.Shape == "streak";
                _rotates = p.Texture != null || p.Shape is "square" or "diamond";
            }

            protected override void Update(float elapsed, ParticleIterator iterator, int count)
            {
                float dt = FrameDt;
                while (iterator.HasNext)
                {
                    var q = iterator.Next();
                    float t = q->Age / Math.Max(0.01f, q->Mass);   // 0..1 over this particle's own life
                    if (t >= 1f) { q->Opacity = 0f; continue; }

                    var vel = new Vector2(q->Velocity[0], q->Velocity[1]);
                    vel.Y += _p.Gravity * dt;
                    vel *= MathF.Max(0, 1 - _p.Drag * dt);
                    q->Velocity[0] = vel.X;
                    q->Velocity[1] = vel.Y;

                    var (rgb, alpha) = ColorAt(_p, t);
                    float fade = t > 0.7f ? 1 - (t - 0.7f) / 0.3f : 1f;   // fade out over the last 30%
                    var hsl = ToHsl(rgb);
                    q->Color[0] = hsl.H;
                    q->Color[1] = hsl.S;
                    q->Color[2] = hsl.L;
                    q->Opacity = alpha * fade;

                    float size = Lerp(_p.Size, t);
                    if (_streak)
                    {
                        float len = Math.Max(size, vel.Length() * 0.04f * Lerp(_p.Stretch, t));
                        q->Rotation = MathF.Atan2(vel.Y, vel.X);
                        q->Scale[0] = len / _texSize;
                        q->Scale[1] = size / _texSize;
                    }
                    else
                    {
                        if (_rotates && _spin != 0) q->Rotation += _spin * dt * (vel.X < 0 ? -1 : 1);
                        q->Scale[0] = q->Scale[1] = size / _texSize;
                    }
                }
            }

            // Colour (and its alpha stop) at t along the preset's colour list.
            public static (Color rgb, float alpha) ColorAt(ParticlePreset p, float t)
            {
                var cols = p.Colors;
                if (cols == null || cols.Count == 0) return (Color.White, 1f - t);
                float f = t * (cols.Count - 1);
                int i = Math.Min(cols.Count - 1, (int)f);
                var (a, aa) = Stop(cols[i]);
                var (b, ba) = Stop(cols[Math.Min(cols.Count - 1, i + 1)]);
                return (Color.Lerp(a, b, f - i), MathHelper.Lerp(aa, ba, f - i));
            }

            private static (Color, float) Stop(float[] v)
            {
                if (v == null || v.Length < 3) return (Color.White, 1f);
                return (new Color((int)v[0], (int)v[1], (int)v[2]), v.Length >= 4 ? v[3] / 255f : 1f);
            }
        }

        // ------------------------------------------------------------------ shapes (premultiplied, made once)

        private static class Shapes
        {
            private static Texture2DRegion _square, _diamond, _circle, _ring;

            // The region a preset draws with, and the size (pixels) that equals scale 1.
            public static (Texture2DRegion region, float size) For(ParticlePreset p)
            {
                Ensure(Assets.Pixel.GraphicsDevice);
                if (p.Texture != null && Assets.Art(p.Texture) is { } art)
                {
                    var src = art.Source(0);
                    return (new Texture2DRegion(art.Texture, src), Math.Max(src.Width, src.Height));
                }
                return p.Shape switch
                {
                    "diamond" => (_diamond, 32f),
                    "circle" => (_circle, 64f),
                    "ring" => (_ring, 128f),
                    _ => (_square, 1f),   // square and streak
                };
            }

            private static void Ensure(GraphicsDevice gd)
            {
                if (_square != null) return;
                _square = new Texture2DRegion(Assets.Pixel);
                _diamond = new Texture2DRegion(Make(gd, 32, (dx, dy) => Math.Abs(dx) + Math.Abs(dy) <= 1f ? 1f : 0f));
                _circle = new Texture2DRegion(Make(gd, 64, (dx, dy) => { float d = 1 - MathF.Sqrt(dx * dx + dy * dy); return d > 0 ? d * d : 0; }));
                _ring = new Texture2DRegion(Make(gd, 128, (dx, dy) => MathHelper.Clamp(1 - Math.Abs(MathF.Sqrt(dx * dx + dy * dy) - 0.9f) / 0.08f, 0, 1)));
            }

            private static Texture2D Make(GraphicsDevice gd, int size, Func<float, float, float> alphaAt)
            {
                var data = new Color[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        data[y * size + x] = Color.White * alphaAt((x + 0.5f) / size * 2 - 1, (y + 0.5f) / size * 2 - 1);
                var tex = new Texture2D(gd, size, size);
                tex.SetData(data);
                return tex;
            }
        }

        // ------------------------------------------------------------------ helpers

        private static HslColor ToHsl(Color c) => HslColor.FromRgb(c);
        private static float Min(float[] r) => r == null || r.Length == 0 ? 0 : r.Min();
        private static float Max(float[] r) => r == null || r.Length == 0 ? 0 : r.Max();
        private static float Lerp(float[] range, float t) =>
            range == null || range.Length == 0 ? 0 : range.Length == 1 ? range[0] : MathHelper.Lerp(range[0], range[1], t);
    }
}
