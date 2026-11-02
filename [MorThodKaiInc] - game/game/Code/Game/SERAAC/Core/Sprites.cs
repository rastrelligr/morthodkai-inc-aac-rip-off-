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
        public bool PixelArt;           // small hand-placed pixels: point-sampled both ways (never smoothed when shrunk)
        private readonly Dictionary<int, Texture2D> _scaled = new();

        // A copy resampled to exactly this height (cached), so point sampling stays crisp at any size.
        // Pixel art is point-sampled up and down; large painted art is area-averaged when shrunk.
        public Texture2D Scaled(GraphicsDevice gd, int height)
        {
            height = Math.Max(1, height);
            if (_scaled.TryGetValue(height, out var tex)) return tex;
            int w = Math.Max(1, (int)Math.Round(W * (height / (float)H)));
            var data = height > H || PixelArt ? Sprites.Nearest(Pixels, W, H, w, height) : Sprites.Resample(Pixels, W, H, w, height);
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
        public bool FacesLeft;           // drawn facing left on the sheet (the convention is facing right)
        public readonly Dictionary<string, SpriteFrame> Frames = new();

        // Animated poses: name -> its frames in order and frames per second. Idle = the animation played
        // whenever the reference (resting) pose is drawn; null = a still picture.
        public readonly Dictionary<string, (string[] frames, float fps)> Animations = new();
        public string Idle;

        // Sheet pixels that make a standard-height character (0 = the reference pose is standard height).
        // Sheets drawn at the same pixel size share one value, so their characters keep their drawn heights.
        public float PixelBase;
        public float StoredFactor = 1f;   // stored frames are this fraction of the sheet's size (very tall art is shrunk)
        public float ReferenceHeight => PixelBase > 0 ? PixelBase : Frames[Reference].H;
        public bool Has(string pose) => pose != null && (Frames.ContainsKey(pose) || Animations.ContainsKey(pose));

        // Animations play on one shared clock, so every copy of a character breathes in step.
        public SpriteFrame Get(string pose)
        {
            string name = Has(pose) ? pose : Idle ?? Reference;
            if (Animations.TryGetValue(name, out var anim))
            {
                int i = (int)(Environment.TickCount64 / 1000.0 * anim.fps) % anim.frames.Length;
                return Frames[anim.frames[i]];
            }
            return Frames[name];
        }
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
            public float PixelBase { get; set; }     // see SpriteSet.PixelBase
            public bool FacesLeft { get; set; }      // the art faces left (the game mirrors it so it faces the right way)
            public List<FrameFile> Frames { get; set; } = new();
        }

        private sealed class FrameFile
        {
            public string Name { get; set; }
            public int[] Box { get; set; }   // x, y, w, h in sheet pixels (rough is fine)
            public int[] Foot { get; set; }  // x, y in sheet pixels; default = bottom centre of the trimmed pose
            public int? FootX { get; set; }  // only x; y = bottom of the trimmed pose

            // An animation laid out as a grid of equal cells (left to right, top to bottom) inside Box
            // (or the whole image): [columns, rows]. Count = how many cells are used (default all), Fps = speed.
            // The cells become frames "<name>_0", "<name>_1"... and "<name>" plays them in a loop.
            public int[] Grid { get; set; }
            public int? Count { get; set; }
            public float Fps { get; set; } = 8f;
        }

        // Frames are stored no taller than this; the game never draws characters bigger.
        private const int MaxStoredHeight = 720; // tall enough for big poses at 3x (4K)
        private const int PixelArtMaxHeight = 400;   // poses this short (in sheet pixels) are pixel art

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
            // The game's sheets first, then each mod's in load order, so a mod's sheet replaces one with the same id.
            foreach (var root in Mods.ArtRoots(_root).Reverse())
                AddSheets(Path.Combine(root, "Sheets"));
        }

        private static void AddSheets(string dir)
        {
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
                    var png = Mods.ResolveArt(_root, id + ".png");
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

            set = new SpriteSet { Id = id + "#" + mid.PackedValue.ToString("x8"), Reference = src.Reference, DisplayScale = src.DisplayScale, FacesLeft = src.FacesLeft };
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
                set.Frames[name] = new SpriteFrame { Name = name, W = f.W, H = f.H, Pixels = px, Pivot = f.Pivot, PixelArt = f.PixelArt };
            }
            _recolored[key] = set;
            return set;
        }

        // Draws a pose standing on `feet`. refHeight = on-screen height of the reference pose.
        public static void Draw(SpriteBatch sb, SpriteSet set, string pose, Vector2 feet, float refHeight, Color tint, bool flip)
        {
            var f = set.Get(pose);
            flip ^= set.FacesLeft;   // left-facing art: mirror it so "flip" still means "face left"
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
            flip ^= set.FacesLeft;   // left-facing art: mirror it so "flip" still means "face left"
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
            var frames = ExpandGrids(sheet.Frames, image, out var animations);
            string reference = sheet.Reference ?? sheet.Frames.FirstOrDefault()?.Name;
            string idle = reference != null && animations.ContainsKey(reference) ? reference : null;
            if (idle != null) reference = animations[idle].frames[0];
            var set = Build(sheet.Id, image, reference, frames);
            if (set != null)
            {
                set.DisplayScale = sheet.Scale;
                if (sheet.PixelBase > 0) set.PixelBase = sheet.PixelBase * set.StoredFactor;
                set.FacesLeft = sheet.FacesLeft;
                foreach (var (name, anim) in animations) set.Animations[name] = anim;
                set.Idle = idle;
            }
            return set;
        }

        // Grid entries -> one frame per cell. Every cell gets the same foot point (the bottom centre of the area
        // used by all the cells together), so the character doesn't jitter from frame to frame.
        private static List<FrameFile> ExpandGrids(List<FrameFile> frames, string imagePath, out Dictionary<string, (string[] frames, float fps)> animations)
        {
            animations = new Dictionary<string, (string[], float)>();
            if (!frames.Any(f => f.Grid is { Length: 2 })) return frames;
            ImageResult img;
            using (var stream = File.OpenRead(imagePath))
                img = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

            var list = new List<FrameFile>();
            foreach (var f in frames)
            {
                if (f.Grid is not { Length: 2 } || f.Grid[0] <= 0 || f.Grid[1] <= 0) { list.Add(f); continue; }
                var area = f.Box is { Length: 4 } ? new Rectangle(f.Box[0], f.Box[1], f.Box[2], f.Box[3]) : new Rectangle(0, 0, img.Width, img.Height);
                int cols = f.Grid[0], rows = f.Grid[1], cw = area.Width / cols, ch = area.Height / rows;
                int count = Math.Min(f.Count ?? cols * rows, cols * rows);
                var cells = Enumerable.Range(0, count).Select(i => new Rectangle(area.X + i % cols * cw, area.Y + i / cols * ch, cw, ch)).ToList();

                // Union of what each cell shows, in cell-local coordinates.
                int x0 = cw, y0 = ch, x1 = 0, y1 = 0;
                foreach (var c in cells)
                {
                    var t = Trim(img, c);
                    if (t.Width == 0) continue;
                    x0 = Math.Min(x0, t.X - c.X); y0 = Math.Min(y0, t.Y - c.Y);
                    x1 = Math.Max(x1, t.Right - c.X); y1 = Math.Max(y1, t.Bottom - c.Y);
                }
                if (x1 <= x0) continue;
                var names = new string[cells.Count];
                for (int i = 0; i < cells.Count; i++)
                {
                    var c = cells[i];
                    names[i] = $"{f.Name}_{i}";
                    int footX = f.FootX.HasValue ? c.X + f.FootX.Value : c.X + (x0 + x1) / 2;
                    list.Add(new FrameFile { Name = names[i], Box = new[] { c.X, c.Y, cw, ch }, Foot = new[] { footX, c.Y + y1 } });
                }
                animations[f.Name] = (names, f.Fps > 0 ? f.Fps : 8f);
            }
            return list;
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

            var set = new SpriteSet { Id = id, Reference = reference, StoredFactor = factor };
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
                set.Frames[def.Name] = new SpriteFrame { Name = def.Name, W = w, H = h, Pixels = px, Pivot = foot * (w / (float)trim.Width), PixelArt = trim.Height <= PixelArtMaxHeight };
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

        // Nearest-neighbour enlarge: every source pixel becomes a crisp block (pixel art).
        public static Color[] Nearest(Color[] src, int sw, int sh, int dw, int dh)
        {
            var dst = new Color[dw * dh];
            for (int y = 0; y < dh; y++)
            {
                int row = Math.Min(sh - 1, (int)(y * sh / (float)dh)) * sw;
                for (int x = 0; x < dw; x++) dst[y * dw + x] = src[row + Math.Min(sw - 1, (int)(x * sw / (float)dw))];
            }
            return dst;
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
