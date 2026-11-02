using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SERAAC.Core;

namespace SERAAC.Fx
{
    // One ambient layer in Art/Fx/ambience.json. Positions are fractions of the area it fills (0 = top, 1 = bottom),
    // sizes are layout pixels. Every field is optional.
    public sealed class AmbientLayerDef
    {
        public string Kind = "dust";          // dust, fog, smoke, clouds, embers, wind, rays
        public int Count = 40;                // dust / smoke / clouds / embers / wind: how many at once
        public float[] Y = { 0f, 1f };        // band it lives in (fractions of the area height)
        public float Alpha = 0.5f;
        public float Speed = 20f;             // drift, pixels per second (+ = right)
        public float Rise = 0f;               // pixels per second up (smoke, embers)
        public float[] Size = { 2f, 4f };     // [min, max]
        public float[] Color = { 255, 255, 255 };
        public bool Front;                    // drawn over the characters / the map
        public bool Additive;                 // glowy (dust in light, embers)
        public float Scale = 1f;              // fog / clouds: texture scale
    }

    public sealed class AmbienceFile
    {
        public Dictionary<string, List<AmbientLayerDef>> Presets = new();
        public Dictionary<string, string> Trials = new();   // trial id -> preset (combat and exploration)
    }

    // Atmosphere as lightweight particles (no art needed): drifting dust motes, fog banks of big soft puffs, rising smoke,
    // clouds, embers, wind streaks and light rays. Presets live in Art/Fx/ambience.json and reload while
    // the game runs. Used behind / in front of fights (per trial or per background), over exploration rooms,
    // and on the story maps (the Airlift platform's sky).
    public sealed class Ambience
    {
        public static readonly LiveFile<AmbienceFile> File = new("Fx/ambience.json");

        private sealed class Mote
        {
            public Vector2 P;     // fractions of the area
            public float Size, Phase, Life, Age, Speed, Alpha;
        }

        private sealed class Layer
        {
            public AmbientLayerDef Def;
            public readonly List<Mote> Motes = new();
            public float Scroll;
        }

        private readonly List<Layer> _layers = new();
        private readonly Random _rng = new();
        private float _time;
        private int _version = -1;
        private string _preset;

        public string Preset => _preset;

        public Ambience(string preset) { _preset = preset; }

        // Trial id -> preset (ambience.json "trials"), for fights and rooms without their own.
        public static string ForTrial(string trialId) =>
            trialId != null && File.Value.Trials.TryGetValue(trialId, out var p) ? p : null;

        public void SetPreset(string preset)
        {
            if (preset == _preset) return;
            _preset = preset;
            _version = -1;
        }

        private void Build()
        {
            _layers.Clear();
            _version = File.Version;
            if (_preset == null || !File.Value.Presets.TryGetValue(_preset, out var defs)) return;
            foreach (var d in defs)
            {
                var l = new Layer { Def = d, Scroll = (float)_rng.NextDouble() * 1000 };
                int count = d.Kind == "fog" ? Math.Min(d.Count, FogPuffs) : d.Count;
                if (d.Kind is "dust" or "smoke" or "clouds" or "embers" or "wind" or "fog")
                    for (int i = 0; i < Math.Max(0, count); i++) l.Motes.Add(Spawn(d, initial: true));
                _layers.Add(l);
            }
        }

        // Fog is made of this many soft puffs at most (each a fraction of the layer's alpha, so they build up).
        private const int FogPuffs = 24;
        private const float FogPuffAlpha = 0.5f;

        private float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
        private float Size(AmbientLayerDef d) => d.Size is { Length: >= 2 } ? R(d.Size[0], d.Size[1]) : d.Size is { Length: 1 } ? d.Size[0] : 3f;
        private float YMin(AmbientLayerDef d) => d.Y is { Length: >= 1 } ? d.Y[0] : 0f;
        private float YMax(AmbientLayerDef d) => d.Y is { Length: >= 2 } ? d.Y[1] : 1f;

        private Mote Spawn(AmbientLayerDef d, bool initial)
        {
            var m = new Mote
            {
                Size = Size(d),
                Phase = R(0, MathHelper.TwoPi),
                Speed = R(0.6f, 1.4f),
                Alpha = R(0.4f, 1f),
            };
            switch (d.Kind)
            {
                case "smoke":
                    m.Life = R(5f, 9f);
                    m.Age = initial ? R(0, m.Life) : 0;
                    m.P = new Vector2(R(-0.1f, 1.1f), YMax(d));
                    break;
                case "embers":
                    m.Life = R(3f, 7f);
                    m.Age = initial ? R(0, m.Life) : 0;
                    m.P = new Vector2(R(0f, 1f), initial ? R(YMin(d), YMax(d)) : YMax(d));
                    break;
                case "fog":
                    m.Life = R(10f, 18f);
                    m.Age = initial ? R(0, m.Life) : 0;
                    m.Size = R(0.8f, 1.6f);   // a multiple of the band's height
                    m.P = new Vector2(initial ? R(-0.2f, 1.2f) : (d.Speed >= 0 ? -0.3f : 1.3f), R(YMin(d), YMax(d)));
                    break;
                case "wind":
                    m.Life = R(0.5f, 1.4f);
                    m.Age = initial ? R(0, m.Life) : 0;
                    m.P = new Vector2(R(-0.2f, 1f), R(YMin(d), YMax(d)));
                    break;
                default:
                    m.Life = R(4f, 10f);
                    m.Age = initial ? R(0, m.Life) : 0;
                    m.P = new Vector2(initial ? R(-0.1f, 1.1f) : (d.Speed >= 0 ? -0.15f : 1.15f), R(YMin(d), YMax(d)));
                    break;
            }
            return m;
        }

        public void Update(float dt, Vector2 areaSize)
        {
            _time += dt;
            if (_version != File.Version) Build();
            float w = Math.Max(1, areaSize.X), h = Math.Max(1, areaSize.Y);
            foreach (var l in _layers)
            {
                var d = l.Def;
                l.Scroll += d.Speed * dt;
                for (int i = 0; i < l.Motes.Count; i++)
                {
                    var m = l.Motes[i];
                    m.Age += dt;
                    float vx = d.Speed * m.Speed / w;
                    float vy = -d.Rise * m.Speed / h;
                    switch (d.Kind)
                    {
                        case "dust":
                            m.P.X += vx * dt + MathF.Sin(_time * 0.7f + m.Phase) * 6f / w * dt;
                            m.P.Y += MathF.Cos(_time * 0.5f + m.Phase) * 5f / h * dt + vy * dt;
                            break;
                        case "smoke":
                        case "embers":
                            m.P.X += (vx + MathF.Sin(_time * 0.6f + m.Phase) * 10f / w) * dt;
                            m.P.Y += vy * dt;
                            break;
                        default:
                            m.P.X += vx * dt;
                            m.P.Y += vy * dt;
                            break;
                    }
                    bool gone = m.Age > m.Life * (d.Kind is "dust" or "clouds" ? 99 : 1)
                                || m.P.X > 1.45f || m.P.X < -0.45f || m.P.Y < YMin(d) - 0.3f || m.P.Y > 1.3f;
                    if (gone) l.Motes[i] = Spawn(d, initial: false);
                }
            }
        }

        public void DrawBack(SpriteBatch sb, Rectangle area, Matrix transform) => Draw(sb, area, transform, front: false);
        public void DrawFront(SpriteBatch sb, Rectangle area, Matrix transform) => Draw(sb, area, transform, front: true);

        // Draws with its own batches; reopens the caller's batch with `transform` (point sampling) afterwards.
        private void Draw(SpriteBatch sb, Rectangle area, Matrix transform, bool front)
        {
            if (_version != File.Version) Build();
            var layers = _layers.Where(l => l.Def.Front == front).ToList();
            if (layers.Count == 0) return;
            Textures.Ensure(sb.GraphicsDevice);
            sb.End();
            foreach (var l in layers)
            {
                var blend = l.Def.Additive ? BlendState.Additive : BlendState.AlphaBlend;
                sb.Begin(blendState: blend, samplerState: SamplerState.LinearClamp, transformMatrix: transform);
                DrawLayer(sb, l, area);
                sb.End();
            }
            sb.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
        }

        private static Color Tint(AmbientLayerDef d, float alpha)
        {
            var c = d.Color ?? new float[] { 255, 255, 255 };
            var col = new Color((int)c[0], (int)c[Math.Min(1, c.Length - 1)], (int)c[Math.Min(2, c.Length - 1)]);
            return col * MathHelper.Clamp(alpha, 0, 1);
        }

        private void DrawLayer(SpriteBatch sb, Layer l, Rectangle area)
        {
            var d = l.Def;
            float y0 = area.Y + YMin(d) * area.Height, y1 = area.Y + YMax(d) * area.Height;
            switch (d.Kind)
            {
                case "rays":
                {
                    // Soft diagonal shafts of light that fade in and out.
                    for (int i = 0; i < Math.Max(1, d.Count); i++)
                    {
                        float x = area.X + area.Width * ((i + 0.5f) / Math.Max(1, d.Count)) + MathF.Sin(_time * 0.05f + i) * 40f;
                        float a = d.Alpha * (0.5f + 0.5f * MathF.Sin(_time * 0.3f + i * 1.7f));
                        float width = Size(d) * 10;
                        sb.Draw(Textures.Ray, new Vector2(x, y0), null, Tint(d, a), 0.35f, new Vector2(Textures.Ray.Width / 2f, 0),
                            new Vector2(width / Textures.Ray.Width, (y1 - y0) / Textures.Ray.Height * 1.2f), SpriteEffects.None, 0);
                    }
                    break;
                }
                default:
                    foreach (var m in l.Motes) DrawMote(sb, d, m, area);
                    break;
            }
        }

        private void DrawMote(SpriteBatch sb, AmbientLayerDef d, Mote m, Rectangle area)
        {
            var pos = new Vector2(area.X + m.P.X * area.Width, area.Y + m.P.Y * area.Height);
            float lifeT = m.Life > 0 ? MathHelper.Clamp(m.Age / m.Life, 0, 1) : 0;
            switch (d.Kind)
            {
                case "dust":
                {
                    float twinkle = 0.55f + 0.45f * MathF.Sin(_time * 1.3f * m.Speed + m.Phase);
                    float s = m.Size / Textures.Dot.Width * 2f;
                    sb.Draw(Textures.Dot, pos, null, Tint(d, d.Alpha * m.Alpha * twinkle), 0, new Vector2(Textures.Dot.Width / 2f), s, SpriteEffects.None, 0);
                    break;
                }
                case "embers":
                {
                    float fade = MathF.Sin(MathHelper.Pi * lifeT);
                    float flicker = 0.7f + 0.3f * MathF.Sin(_time * 9f + m.Phase);
                    float s = m.Size / Textures.Dot.Width * 2f;
                    sb.Draw(Textures.Dot, pos, null, Tint(d, d.Alpha * fade * flicker), 0, new Vector2(Textures.Dot.Width / 2f), s, SpriteEffects.None, 0);
                    break;
                }
                case "smoke":
                {
                    // Puffs grow as they rise, thicken, then thin out.
                    float fade = MathF.Sin(MathHelper.Pi * lifeT);
                    float size = m.Size * (0.5f + lifeT * 1.6f);
                    float rot = m.Phase + _time * 0.08f * (m.Speed - 1);
                    sb.Draw(Textures.Puff, pos, null, Tint(d, d.Alpha * fade * m.Alpha), rot, new Vector2(Textures.Puff.Width / 2f),
                        size / Textures.Puff.Width, SpriteEffects.None, 0);
                    break;
                }
                case "clouds":
                {
                    // A cloud = a few overlapping puffs, stretched wide.
                    float s = m.Size * Math.Max(0.1f, d.Scale);
                    for (int k = 0; k < 4; k++)
                    {
                        float ox = (k - 1.5f) * s * 0.35f + MathF.Sin(m.Phase * 3 + k) * s * 0.1f;
                        float oy = MathF.Cos(m.Phase * 5 + k) * s * 0.08f;
                        float ks = s * (0.6f + 0.25f * ((k * 37 + (int)(m.Phase * 10)) % 3));
                        sb.Draw(Textures.Puff, pos + new Vector2(ox, oy), null, Tint(d, d.Alpha * m.Alpha), m.Phase + k,
                            new Vector2(Textures.Puff.Width / 2f), new Vector2(ks * 1.6f, ks) / Textures.Puff.Width, SpriteEffects.None, 0);
                    }
                    break;
                }
                case "fog":
                {
                    // A wide soft puff that fades in, drifts, and fades out, thinner near the band's edges.
                    float y0 = area.Y + YMin(d) * area.Height, y1 = area.Y + YMax(d) * area.Height;
                    float bandH = Math.Max(40f, y1 - y0);
                    float edge = MathF.Sin(MathHelper.Pi * MathHelper.Clamp((pos.Y - y0) / bandH, 0, 1));
                    float fade = MathF.Sin(MathHelper.Pi * lifeT);
                    float h = bandH * m.Size * Math.Max(0.1f, d.Scale) * 0.8f;
                    float breathe = 1f + 0.06f * MathF.Sin(_time * 0.3f + m.Phase);
                    sb.Draw(Textures.Puff, pos, null, Tint(d, d.Alpha * FogPuffAlpha * fade * (0.35f + 0.65f * edge)), m.Phase * 0.2f,
                        new Vector2(Textures.Puff.Width / 2f), new Vector2(h * 2.6f, h) * breathe / Textures.Puff.Width, SpriteEffects.None, 0);
                    break;
                }
                case "wind":
                {
                    // Thin streaks that flash past.
                    float fade = MathF.Sin(MathHelper.Pi * lifeT);
                    float len = m.Size * 40;
                    var p = pos + new Vector2(lifeT * area.Width * 0.25f * Math.Sign(d.Speed == 0 ? 1 : d.Speed), 0);
                    sb.Draw(Textures.Streak, p, null, Tint(d, d.Alpha * fade * m.Alpha), 0, new Vector2(Textures.Streak.Width / 2f, Textures.Streak.Height / 2f),
                        new Vector2(len / Textures.Streak.Width, 1.5f / Textures.Streak.Height), SpriteEffects.None, 0);
                    break;
                }
            }
        }

        // Generated once: a soft dot, a cloudy puff, a tileable fog noise, a light ray and a streak.
        private static class Textures
        {
            public static Texture2D Dot, Puff, Ray, Streak;

            public static void Ensure(GraphicsDevice gd)
            {
                if (Dot != null) return;
                Dot = Make(gd, 32, 32, (x, y) => { float d = 1 - MathF.Sqrt(x * x + y * y); return d > 0 ? d * d : 0; });
                var noise = new Noise(7);
                Puff = Make(gd, 128, 128, (x, y) =>
                {
                    float r = MathF.Sqrt(x * x + y * y);
                    float n = noise.Fbm((x + 1) * 2.5f, (y + 1) * 2.5f, 4, wrap: 0);
                    float falloff = MathHelper.Clamp(1 - r, 0, 1);
                    return MathHelper.Clamp(falloff * falloff * (0.55f + n * 0.9f), 0, 1);
                });
                Ray = Make(gd, 32, 64, (x, y) => MathHelper.Clamp(1 - Math.Abs(x), 0, 1) * MathHelper.Clamp(1 - (y + 1) / 2f, 0, 1));
                Streak = Make(gd, 64, 4, (x, y) => MathHelper.Clamp(1 - Math.Abs(x), 0, 1) * MathHelper.Clamp(1 - Math.Abs(y), 0, 1));
            }

            // alphaAt gets coordinates from -1 to 1; the result is premultiplied white.
            private static Texture2D Make(GraphicsDevice gd, int w, int h, Func<float, float, float> alphaAt)
            {
                var data = new Color[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float a = MathHelper.Clamp(alphaAt((x + 0.5f) / w * 2 - 1, (y + 0.5f) / h * 2 - 1), 0, 1);
                        byte b = (byte)(a * 255);
                        data[y * w + x] = new Color(b, b, b, b);
                    }
                var tex = new Texture2D(gd, w, h);
                tex.SetData(data);
                return tex;
            }
        }

        // Value noise with optional wrapping (period in lattice cells), so the fog tiles seamlessly.
        private sealed class Noise
        {
            private readonly float[] _v = new float[256];
            private readonly int[] _p = new int[512];

            public Noise(int seed)
            {
                var rng = new Random(seed);
                for (int i = 0; i < 256; i++) { _v[i] = (float)rng.NextDouble(); _p[i] = i; }
                for (int i = 255; i > 0; i--) { int j = rng.Next(i + 1); (_p[i], _p[j]) = (_p[j], _p[i]); }
                for (int i = 0; i < 256; i++) _p[256 + i] = _p[i];
            }

            private float Lattice(int x, int y, int wx, int wy)
            {
                if (wx > 0) x = ((x % wx) + wx) % wx;
                if (wy > 0) y = ((y % wy) + wy) % wy;
                return _v[_p[(_p[x & 255] + y) & 255]];
            }

            private float Value(float x, float y, int wx, int wy)
            {
                int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
                float tx = x - xi, ty = y - yi;
                tx = tx * tx * (3 - 2 * tx);
                ty = ty * ty * (3 - 2 * ty);
                float a = Lattice(xi, yi, wx, wy), b = Lattice(xi + 1, yi, wx, wy);
                float c = Lattice(xi, yi + 1, wx, wy), d = Lattice(xi + 1, yi + 1, wx, wy);
                return MathHelper.Lerp(MathHelper.Lerp(a, b, tx), MathHelper.Lerp(c, d, tx), ty);
            }

            public float Fbm(float x, float y, int octaves, int wrap = 0, int wrapY = -1)
            {
                if (wrapY < 0) wrapY = wrap;
                float sum = 0, amp = 0.5f, norm = 0;
                int wx = wrap, wy = wrapY;
                for (int o = 0; o < octaves; o++)
                {
                    sum += Value(x, y, wx, wy) * amp;
                    norm += amp;
                    x *= 2; y *= 2;
                    wx *= 2; wy *= 2;
                    amp *= 0.5f;
                }
                return sum / norm;
            }
        }
    }
}
