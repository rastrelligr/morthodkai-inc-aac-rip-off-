using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SERAAC.Core;
using StbImageSharp;

namespace SERAAC.Stage
{
    // The title screen's layered picture: Art/MainMenu/0.png, 1.png, 2.png... drawn in that order (the highest
    // number is the closest). Every layer is the same size; the layers shift against each other as the mouse
    // moves (closer ones more), and embers drift up between some of them.
    // Loading runs in the background (it starts while the intro video plays); Ready says when it's done.
    public sealed class TitleScene
    {
        private sealed class Layer
        {
            public Texture2D Texture;
            public Rectangle Crop;   // where the texture sits in the full-size image (transparent margins are cut off)
            public float Depth;      // 0 = the backdrop, 1 = the closest layer
        }

        // Framing, in 1280x720 units. The picture is scaled to the screen's height times Zoom (the extra
        // gives the parallax room to move without showing an edge). FocusU is the spot across the picture
        // (0..1, here the character) that is kept at FocusX: one value for the still title, one with the menu open.
        public const float Zoom = 1.08f;
        private const float FocusU = 0.385f;
        private const float FocusXStill = 470f, FocusXMenu = 420f;
        // How far the backdrop and the closest layer move at full mouse deflection.
        private static readonly Vector2 ShiftBack = new(8, 5), ShiftFront = new(64, 30);

        private readonly List<Layer> _layers = new();
        private Task<List<(Rectangle crop, Color[] px)>> _load;
        private List<(Rectangle crop, Color[] px)> _pending;
        private int _imageW, _imageH;
        private Vector2 _look;   // eased mouse deflection, -1..1

        public bool Available { get; private set; }   // false when Art/MainMenu has no layers
        public bool Ready => Available && _pending == null && _load == null;

        // Eased 0..1 values the title screen sets: how much the mouse moves the layers, and the framing
        // (0 = still title, 1 = menu open: the picture slides left to make room).
        public float Parallax;
        public float Framing;

        public TitleScene()
        {
            string dir = Mods.ArtRoots(Path.Combine(AppContext.BaseDirectory, "Art"))
                .Select(r => Path.Combine(r, "MainMenu"))
                .FirstOrDefault(d => File.Exists(Path.Combine(d, "0.png")));
            if (dir == null) return;
            var files = new List<string>();
            for (int i = 0; File.Exists(Path.Combine(dir, i + ".png")); i++) files.Add(Path.Combine(dir, i + ".png"));
            Available = true;
            _load = Task.Run(() => files.Select(Decode).ToList());
        }

        // Straight-alpha PNG -> premultiplied pixels, cropped to what isn't transparent.
        private (Rectangle crop, Color[] px) Decode(string path)
        {
            ImageResult img;
            using (var s = File.OpenRead(path)) img = ImageResult.FromStream(s, ColorComponents.RedGreenBlueAlpha);
            _imageW = img.Width;
            _imageH = img.Height;
            var d = img.Data;
            int minX = img.Width, minY = img.Height, maxX = -1, maxY = -1;
            for (int y = 0; y < img.Height; y++)
                for (int x = 0; x < img.Width; x++)
                    if (d[(y * img.Width + x) * 4 + 3] != 0)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
            if (maxX < 0) return (Rectangle.Empty, null);
            var crop = new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
            var px = new Color[crop.Width * crop.Height];
            for (int y = 0; y < crop.Height; y++)
            {
                int src = ((crop.Y + y) * img.Width + crop.X) * 4;
                for (int x = 0; x < crop.Width; x++, src += 4)
                {
                    int a = d[src + 3];
                    px[y * crop.Width + x] = new Color(d[src] * a / 255, d[src + 1] * a / 255, d[src + 2] * a / 255, a);
                }
            }
            return (crop, px);
        }

        public void Update(float dt, Vector2 mouse, double time)
        {
            if (_load != null && _load.IsCompleted)
            {
                if (_load.IsFaulted)
                {
                    Console.WriteLine($"[Title] failed to load Art/MainMenu: {_load.Exception?.GetBaseException().Message}");
                    Available = false;
                }
                else _pending = _load.Result;
                _load = null;
            }
            // Upload one layer per frame, so a video playing meanwhile doesn't stall.
            if (_pending != null)
            {
                int i = _layers.Count;
                if (i < _pending.Count)
                {
                    var (crop, px) = _pending[i];
                    Texture2D tex = null;
                    if (px != null)
                    {
                        tex = new Texture2D(Assets.Pixel.GraphicsDevice, crop.Width, crop.Height);
                        tex.SetData(px);
                    }
                    _layers.Add(new Layer { Texture = tex, Crop = crop, Depth = _pending.Count == 1 ? 0 : i / (float)(_pending.Count - 1) });
                }
                if (_layers.Count >= _pending.Count) _pending = null;
            }

            // The mouse's place on screen, -1..1 from the centre, plus a slow idle drift; eased so the layers glide.
            var target = new Vector2(mouse.X / Game1.Width * 2 - 1, mouse.Y / Game1.Height * 2 - 1);
            if (mouse.X < -100) target = Vector2.Zero;   // no mouse yet
            target = Vector2.Clamp(target, -Vector2.One, Vector2.One);
            target += new Vector2((float)Math.Sin(time * 0.37) * 0.08f, (float)Math.Cos(time * 0.29) * 0.06f);
            target *= Parallax;
            _look = Vector2.Lerp(_look, target, 1 - (float)Math.Exp(-dt * 4));
        }

        // Ember groups: (drawn after layer, depth for the parallax, count, size, rise speed, brightness).
        private static readonly (int after, float depth, int count, float size, float speed, float bright)[] EmberGroups =
        {
            (0, 0.12f, 90, 0.7f, 0.6f, 0.55f),   // far: small and dim, behind everything but the backdrop
            (4, 0.55f, 40, 1.1f, 1.0f, 0.85f),   // around the character
            (7, 0.95f, 14, 2.4f, 1.6f, 0.7f),    // close: big, fast, soft
        };

        public void Draw(SpriteBatch sb, double t, float alpha = 1f)
        {
            if (!Ready || _layers.Count == 0) return;
            float scale = Game1.Height * Zoom / _imageH;
            float focusX = MathHelper.Lerp(FocusXStill, FocusXMenu, Framing);
            var origin = new Vector2(focusX - FocusU * _imageW * scale, Game1.Height / 2f - _imageH * scale / 2f);

            // Smooth (linear) sampling for the big downscaled art, then back to the frame's usual batch.
            sb.End();
            sb.Begin(samplerState: SamplerState.LinearClamp, transformMatrix: Core.Draw.FrameTransform);
            var tint = Color.White * alpha;
            for (int i = 0; i < _layers.Count; i++)
            {
                var l = _layers[i];
                if (l.Texture != null)
                    sb.Draw(l.Texture, origin + Shift(l.Depth) + new Vector2(l.Crop.X, l.Crop.Y) * scale, null, tint, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
                foreach (var g in EmberGroups)
                    if (g.after == i) Embers(sb, t, g.depth, g.count, g.size, g.speed, g.bright * alpha, i * 97);
            }
            sb.End();
            sb.Begin(samplerState: SamplerState.PointClamp, transformMatrix: Core.Draw.FrameTransform);
        }

        private Vector2 Shift(float depth) => -_look * Vector2.Lerp(ShiftBack, ShiftFront, depth);

        // Sparks rising from below, swaying, flickering and fading out. Each one is a pure function of the time
        // (no state), wrapping around when it leaves the top. Drawn additively (alpha 0 in premultiplied blending).
        private void Embers(SpriteBatch sb, double t, float depth, int count, float size, float speed, float bright, int seed)
        {
            var shift = Shift(depth);
            for (int i = 0; i < count; i++)
            {
                uint h = (uint)((i + seed) * 2654435761u);
                float u = (h & 0xffff) / 65535f, v = ((h >> 16) & 0xffff) / 65535f, w = ((h >> 8) & 0xff) / 255f;
                float rise = (40 + 70 * v) * speed;
                float life = (float)((t * rise / (Game1.Height + 120) + u * 7.3) % 1.0);
                float y = Game1.Height + 40 - life * (Game1.Height + 120);
                // Drift right as they rise (heat carried by the wind), with a slow wobble.
                float x = u * (Game1.Width + 200) - 100 + life * (60 + 120 * w) * speed + (float)Math.Sin(t * (0.8 + v) + i) * 14 * size;
                float flicker = 0.6f + 0.4f * (float)Math.Sin(t * (5 + 6 * w) + i * 2.1);
                float fade = MathHelper.Clamp(Math.Min(life * 6, (1 - life) * 2.5f), 0, 1);
                float a = fade * flicker * bright;
                if (a <= 0.01f) continue;
                var pos = new Vector2(x, y) + shift;
                var hot = Color.Lerp(Palette.Ember, Palette.Gold, w * 0.6f);
                float s = size * (0.8f + 0.9f * v);
                Core.Draw.SoftGlow(sb, pos, new Vector2(14, 14) * s, Additive(hot) * (a * 0.55f));
                Core.Draw.SoftGlow(sb, pos, new Vector2(3.2f, 3.2f) * s, Additive(Color.Lerp(hot, Palette.Paper, 0.5f)) * a);
            }
        }

        private static Color Additive(Color c) => new(c.R, c.G, c.B, (byte)0);
    }
}
