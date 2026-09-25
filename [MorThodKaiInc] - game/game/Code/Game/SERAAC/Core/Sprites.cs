using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StbImageSharp;

namespace SERAAC.Core
{
    // One pose cut out of a sheet: trimmed pixels (premultiplied) plus the foot point it stands on.
    public sealed class SpriteFrame
    {
        public string Name;
        public int W, H;
        public Color[] Pixels;
        public Vector2 Pivot;           // feet, in this frame's pixel space
        private readonly Dictionary<int, Texture2D> _scaled = new();

        // A copy resampled to exactly this height (cached), so point sampling stays crisp at any size.
        public Texture2D Scaled(GraphicsDevice gd, int height)
        {
            height = Math.Max(1, height);
            if (_scaled.TryGetValue(height, out var tex)) return tex;
            int w = Math.Max(1, (int)Math.Round(W * (height / (float)H)));
            var data = Sprites.Resample(Pixels, W, H, w, height);
            tex = new Texture2D(gd, w, height);
            tex.SetData(data);
            _scaled[height] = tex;
            return tex;
        }
    }

    // All poses of one character/monster. Every pose shares one scale, taken from the reference
    // pose, so bigger attack poses really do draw bigger instead of being squashed into a box.
    public sealed class SpriteSet
    {
        public string Id;
        public string Reference;
        public float DisplayScale = 1f;
        public readonly Dictionary<string, SpriteFrame> Frames = new();

        public float ReferenceHeight => Frames[Reference].H;
        public bool Has(string pose) => pose != null && Frames.ContainsKey(pose);
        public SpriteFrame Get(string pose) => Has(pose) ? Frames[pose] : Frames[Reference];
    }

    // Loads sheets described by Art/Sheets/*.json (see Art/README.md). Artists can lay poses out
    // anywhere at any size: each pose only needs a rough box, which is trimmed automatically,
    // and optionally a foot point. Plain Art/<id>.png files also work as single-pose sets.
    public static class Sprites
    {
        private sealed class SheetFile
        {
            public string Id { get; set; }
            public string Image { get; set; }
            public string Reference { get; set; }
            public float Scale { get; set; } = 1f;   // in-game size multiplier (e.g. a prone sniper drawn smaller)
            public List<FrameFile> Frames { get; set; } = new();
        }

        private sealed class FrameFile
        {
            public string Name { get; set; }
            public int[] Box { get; set; }   // x, y, w, h in sheet pixels (rough is fine)
            public int[] Foot { get; set; }  // x, y in sheet pixels; default = bottom centre of the trimmed pose
            public int? FootX { get; set; }  // only x; y = bottom of the trimmed pose
        }

        // Frames are stored no taller than this; the game never draws characters bigger.
        private const int MaxStoredHeight = 720; // tall enough for big poses at 3x (4K)

        // Brightness that maps exactly to the recolour colour (the mock-up monster body is ~0.8 grey).
        private const float RecolorMid = 0.78f;

        private static GraphicsDevice _gd;
        private static string _root;
        private static readonly Dictionary<string, string> _sheetFiles = new();
        private static readonly Dictionary<string, SpriteSet> _cache = new();
        private static readonly Dictionary<(string, uint), SpriteSet> _recolored = new();

        public static IEnumerable<string> SheetIds => _sheetFiles.Keys;

        public static void Init(GraphicsDevice gd)
        {
            _gd = gd;
            _root = Path.Combine(AppContext.BaseDirectory, "Art");
            _sheetFiles.Clear();
            var dir = Path.Combine(_root, "Sheets");
            if (!Directory.Exists(dir)) return;
            foreach (var json in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    var sheet = Parse(json);
                    if (!string.IsNullOrEmpty(sheet.Id)) _sheetFiles[sheet.Id] = json;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[Sprites] bad sheet file {Path.GetFileName(json)}: {e.Message}");
                }
            }
        }

        private static SheetFile Parse(string path) =>
            JsonSerializer.Deserialize<SheetFile>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        // Null when neither a sheet nor Art/<id>.png exists (callers draw placeholders).
        public static SpriteSet Get(string id)
        {
            if (string.IsNullOrEmpty(id) || _gd == null) return null;
            if (_cache.TryGetValue(id, out var set)) return set;
            try
            {
                if (_sheetFiles.TryGetValue(id, out var json)) set = LoadSheet(json);
                else
                {
                    var png = Path.Combine(_root, id + ".png");
                    if (File.Exists(png))
                        set = Build(id, png, "default", new List<FrameFile> { new() { Name = "default" } });
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Sprites] failed to load '{id}': {e.Message}");
                set = null;
            }
            _cache[id] = set;
            return set;
        }

        // The same set with every pixel remapped through black -> colour -> white by brightness.
        // Used to make monster variants out of one greyscale drawing.
        public static SpriteSet Recolored(string id, Color mid)
        {
            var src = Get(id);
            if (src == null) return null;
            var key = (id, mid.PackedValue);
            if (_recolored.TryGetValue(key, out var set)) return set;

            set = new SpriteSet { Id = id + "#" + mid.PackedValue.ToString("x8"), Reference = src.Reference, DisplayScale = src.DisplayScale };
            foreach (var (name, f) in src.Frames)
            {
                var px = new Color[f.Pixels.Length];
                for (int i = 0; i < px.Length; i++)
                {
                    var c = f.Pixels[i];
                    if (c.A == 0) continue;
                    float a = c.A / 255f;
                    float lum = (0.299f * c.R + 0.587f * c.G + 0.114f * c.B) / 255f / a;
                    var straight = lum < RecolorMid ? Color.Lerp(Color.Black, mid, lum / RecolorMid)
                                                    : Color.Lerp(mid, Color.White, (lum - RecolorMid) / (1f - RecolorMid));
                    px[i] = new Color((int)(straight.R * a), (int)(straight.G * a), (int)(straight.B * a), c.A);
                }
                set.Frames[name] = new SpriteFrame { Name = name, W = f.W, H = f.H, Pixels = px, Pivot = f.Pivot };
            }
            _recolored[key] = set;
            return set;
        }

        // Draws a pose standing on `feet`. refHeight = on-screen height of the reference pose.
        public static void Draw(SpriteBatch sb, SpriteSet set, string pose, Vector2 feet, float refHeight, Color tint, bool flip)
        {
            var f = set.Get(pose);
            float scale = refHeight * set.DisplayScale / set.ReferenceHeight;
            // Resample to the size it will occupy on the real screen, then draw it 1:1 (sharp at any resolution).
            var tex = f.Scaled(_gd, (int)Math.Round(f.H * scale * Core.Draw.Scale));
            float s = tex.Height / (float)f.H;
            var origin = f.Pivot * s;
            if (flip) origin.X = tex.Width - origin.X;
            sb.Draw(tex, Core.Draw.Snap(feet), null, tint, 0f, new Vector2((int)origin.X, (int)origin.Y), 1f / Core.Draw.Scale,
                flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
        }

        // Draws a pose scaled to fit inside a box (portraits, icons, dialogue).
        public static void DrawFit(SpriteBatch sb, SpriteSet set, string pose, Rectangle box, Color tint, bool flip = false)
        {
            var f = set.Get(pose);
            float scale = Math.Min(box.Width / (float)f.W, box.Height / (float)f.H);
            var tex = f.Scaled(_gd, (int)Math.Round(f.H * scale * Core.Draw.Scale));
            float w = tex.Width / Core.Draw.Scale, h = tex.Height / Core.Draw.Scale;
            var pos = Core.Draw.Snap(new Vector2(box.Center.X - w / 2, box.Bottom - h));
            sb.Draw(tex, pos, null, tint, 0f, Vector2.Zero, 1f / Core.Draw.Scale, flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
        }

        // ------------------------------------------------------------------ loading

        private static SpriteSet LoadSheet(string jsonPath)
        {
            var sheet = Parse(jsonPath);
            var image = Path.Combine(Path.GetDirectoryName(jsonPath)!, sheet.Image);
            var set = Build(sheet.Id, image, sheet.Reference ?? sheet.Frames.FirstOrDefault()?.Name, sheet.Frames);
            if (set != null) set.DisplayScale = sheet.Scale;
            return set;
        }

        private static SpriteSet Build(string id, string imagePath, string reference, List<FrameFile> frames)
        {
            ImageResult img;
            using (var stream = File.OpenRead(imagePath))
                img = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

            var cut = new List<(FrameFile def, Rectangle trim)>();
            foreach (var fr in frames)
            {
                var box = fr.Box is { Length: 4 }
                    ? Rectangle.Intersect(new Rectangle(fr.Box[0], fr.Box[1], fr.Box[2], fr.Box[3]), new Rectangle(0, 0, img.Width, img.Height))
                    : new Rectangle(0, 0, img.Width, img.Height);
                var trim = Trim(img, box);
                if (trim.Width > 0) cut.Add((fr, trim));
                else Console.WriteLine($"[Sprites] {id}/{fr.Name}: box is empty");
            }
            if (cut.Count == 0) return null;

            reference ??= cut[0].def.Name;
            var refTrim = cut.FirstOrDefault(c => c.def.Name == reference).trim;
            if (refTrim.Width == 0) { reference = cut[0].def.Name; refTrim = cut[0].trim; }
            float factor = Math.Min(1f, MaxStoredHeight / (float)refTrim.Height);

            var set = new SpriteSet { Id = id, Reference = reference };
            foreach (var (def, trim) in cut)
            {
                var px = Crop(img, trim);
                int w = trim.Width, h = trim.Height;
                if (factor < 1f)
                {
                    int nw = Math.Max(1, (int)Math.Round(w * factor)), nh = Math.Max(1, (int)Math.Round(h * factor));
                    px = Resample(px, w, h, nw, nh);
                    w = nw; h = nh;
                }
                var foot = def.Foot is { Length: 2 }
                    ? new Vector2(def.Foot[0] - trim.X, def.Foot[1] - trim.Y)
                    : new Vector2(def.FootX.HasValue ? def.FootX.Value - trim.X : trim.Width / 2f, trim.Height);
                set.Frames[def.Name] = new SpriteFrame { Name = def.Name, W = w, H = h, Pixels = px, Pivot = foot * (w / (float)trim.Width) };
            }
            return set;
        }

        // Tight bounds of visible pixels inside a box.
        private static Rectangle Trim(ImageResult img, Rectangle box)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int y = box.Top; y < box.Bottom; y++)
            {
                int row = y * img.Width;
                for (int x = box.Left; x < box.Right; x++)
                {
                    if (img.Data[(row + x) * 4 + 3] <= 8) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            return maxX < 0 ? Rectangle.Empty : new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        private static Color[] Crop(ImageResult img, Rectangle r)
        {
            var px = new Color[r.Width * r.Height];
            for (int y = 0; y < r.Height; y++)
            {
                for (int x = 0; x < r.Width; x++)
                {
                    int i = ((r.Y + y) * img.Width + r.X + x) * 4;
                    int a = img.Data[i + 3];
                    px[y * r.Width + x] = new Color(img.Data[i] * a / 255, img.Data[i + 1] * a / 255, img.Data[i + 2] * a / 255, a);
                }
            }
            return px;
        }

        // Area-average resample (premultiplied input), good for large reductions.
        public static Color[] Resample(Color[] src, int sw, int sh, int dw, int dh)
        {
            var dst = new Color[dw * dh];
            float sx = sw / (float)dw, sy = sh / (float)dh;
            for (int y = 0; y < dh; y++)
            {
                int y0 = (int)(y * sy), y1 = Math.Max(y0 + 1, Math.Min(sh, (int)Math.Ceiling((y + 1) * sy)));
                for (int x = 0; x < dw; x++)
                {
                    int x0 = (int)(x * sx), x1 = Math.Max(x0 + 1, Math.Min(sw, (int)Math.Ceiling((x + 1) * sx)));
                    int r = 0, g = 0, b = 0, a = 0, n = 0;
                    for (int yy = y0; yy < y1; yy++)
                    {
                        int row = yy * sw;
                        for (int xx = x0; xx < x1; xx++)
                        {
                            var c = src[row + xx];
                            r += c.R; g += c.G; b += c.B; a += c.A; n++;
                        }
                    }
                    dst[y * dw + x] = new Color(r / n, g / n, b / n, a / n);
                }
            }
            return dst;
        }
    }
}
