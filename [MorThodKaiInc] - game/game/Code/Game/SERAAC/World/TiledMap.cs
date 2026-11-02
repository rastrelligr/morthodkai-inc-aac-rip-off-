using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SERAAC.Core;

namespace SERAAC.World
{
    // A Tiled map (.tmx, with external .tsx tilesets), read straight from Art/Maps at runtime so a map saved
    // in Tiled shows up without a rebuild (the overworld even reloads it while the game runs).
    // Supported: orthogonal maps, CSV / XML tile data, image tilesets, flipped tiles, tile layers, object
    // layers (rectangles, ellipses, points, polygons) and custom properties. See Art/Maps/README.md.
    public sealed class TiledMap
    {
        public sealed class Tileset
        {
            public int FirstGid, TileWidth, TileHeight, Columns, TileCount;
            public bool GridSize;      // tilerendersize="grid": stretched to the map's grid
            public Texture2D Texture;  // null when the image can't be found (its tiles are skipped)
        }

        public sealed class TileLayer
        {
            public string Name;
            public int Width, Height;
            public uint[] Gids;
            public bool Visible = true;
            public float Opacity = 1f;
            public Dictionary<string, string> Props = new();
            public bool Above => Props.TryGetValue("above", out var v) && v == "true";
        }

        public sealed class MapObject
        {
            public int Id;
            public string Name = "", Class = "", Group = "";
            public float X, Y, Width, Height;
            public bool IsPoint, IsEllipse;
            public List<Vector2> Polygon;   // absolute map pixels
            public Dictionary<string, string> Props = new();

            public RectangleF Bounds => Polygon != null
                ? RectangleF.FromPoints(Polygon)
                : new RectangleF(X, Y, Width, Height);
            public Vector2 Center => Bounds.Center;
            public string Prop(string key, string fallback = null) => Props.TryGetValue(key, out var v) ? v : fallback;

            // Point inside this shape (points never contain anything).
            public bool Contains(Vector2 p)
            {
                if (IsPoint) return false;
                if (Polygon != null) return InPolygon(Polygon, p);
                if (IsEllipse)
                {
                    float rx = Width / 2f, ry = Height / 2f;
                    if (rx <= 0 || ry <= 0) return false;
                    float dx = (p.X - (X + rx)) / rx, dy = (p.Y - (Y + ry)) / ry;
                    return dx * dx + dy * dy <= 1f;
                }
                return p.X >= X && p.X < X + Width && p.Y >= Y && p.Y < Y + Height;
            }

            // Distance from a point to the shape's bounds (0 inside).
            public float DistanceTo(Vector2 p)
            {
                if (IsPoint) return Vector2.Distance(p, new Vector2(X, Y));
                var b = Bounds;
                float dx = Math.Max(Math.Max(b.X - p.X, 0), p.X - (b.X + b.Width));
                float dy = Math.Max(Math.Max(b.Y - p.Y, 0), p.Y - (b.Y + b.Height));
                return MathF.Sqrt(dx * dx + dy * dy);
            }

            private static bool InPolygon(List<Vector2> poly, Vector2 p)
            {
                bool inside = false;
                for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                {
                    if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y)
                        && p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                        inside = !inside;
                }
                return inside;
            }
        }

        // A float rectangle (map pixels).
        public readonly record struct RectangleF(float X, float Y, float Width, float Height)
        {
            public Vector2 Center => new(X + Width / 2f, Y + Height / 2f);
            public static RectangleF FromPoints(List<Vector2> pts)
            {
                float x0 = pts.Min(p => p.X), y0 = pts.Min(p => p.Y), x1 = pts.Max(p => p.X), y1 = pts.Max(p => p.Y);
                return new RectangleF(x0, y0, x1 - x0, y1 - y0);
            }
        }

        public string Path { get; private set; }
        public int Width, Height, TileWidth, TileHeight;
        public Color? BackgroundColor;
        public readonly List<Tileset> Tilesets = new();
        public readonly List<TileLayer> Layers = new();      // in drawing order
        public readonly List<MapObject> Objects = new();     // every object of every object layer
        public Dictionary<string, string> Props = new();

        public int PixelWidth => Width * TileWidth;
        public int PixelHeight => Height * TileHeight;

        public IEnumerable<MapObject> InGroup(string group) =>
            Objects.Where(o => string.Equals(o.Group, group, StringComparison.OrdinalIgnoreCase));

        public IEnumerable<MapObject> OfClass(string cls) =>
            Objects.Where(o => string.Equals(o.Class, cls, StringComparison.OrdinalIgnoreCase));

        public MapObject Named(string name) =>
            Objects.FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));

        // ------------------------------------------------------------------ loading

        public static TiledMap Load(string path)
        {
            var doc = XDocument.Load(path);
            var root = doc.Root!;
            var map = new TiledMap
            {
                Path = path,
                Width = Int(root, "width"),
                Height = Int(root, "height"),
                TileWidth = Int(root, "tilewidth"),
                TileHeight = Int(root, "tileheight"),
                Props = ReadProps(root),
            };
            if (root.Attribute("backgroundcolor") is { } bg) map.BackgroundColor = ParseColor(bg.Value);
            string dir = System.IO.Path.GetDirectoryName(path)!;

            foreach (var ts in root.Elements("tileset"))
                map.Tilesets.Add(ReadTileset(ts, dir));
            map.Tilesets.Sort((a, b) => a.FirstGid.CompareTo(b.FirstGid));

            ReadLayers(map, root, "");
            return map;
        }

        // Layers can sit inside groups (<group>): their names are kept, and they draw in document order.
        private static void ReadLayers(TiledMap map, XElement parent, string prefix)
        {
            foreach (var el in parent.Elements())
            {
                switch (el.Name.LocalName)
                {
                    case "layer":
                        map.Layers.Add(ReadTileLayer(el));
                        break;
                    case "objectgroup":
                        string group = (string)el.Attribute("name") ?? "";
                        foreach (var o in el.Elements("object")) map.Objects.Add(ReadObject(o, group));
                        break;
                    case "group":
                        ReadLayers(map, el, prefix + ((string)el.Attribute("name") ?? "") + "/");
                        break;
                }
            }
        }

        private static Tileset ReadTileset(XElement el, string mapDir)
        {
            int firstGid = Int(el, "firstgid");
            string baseDir = mapDir;
            if (el.Attribute("source") is { } src)
            {
                string tsxPath = System.IO.Path.Combine(mapDir, src.Value);
                if (!File.Exists(tsxPath)) return new Tileset { FirstGid = firstGid };
                el = XDocument.Load(tsxPath).Root!;
                baseDir = System.IO.Path.GetDirectoryName(tsxPath)!;
            }
            var ts = new Tileset
            {
                FirstGid = firstGid,
                TileWidth = Int(el, "tilewidth"),
                TileHeight = Int(el, "tileheight"),
                Columns = Int(el, "columns"),
                TileCount = Int(el, "tilecount"),
                GridSize = (string)el.Attribute("tilerendersize") == "grid",
            };
            var image = el.Element("image");
            if (image != null)
            {
                string imgPath = (string)image.Attribute("source") ?? "";
                // Relative to the tileset, else (when the tileset points somewhere that isn't there) the same
                // file name next to the map.
                string full = System.IO.Path.IsPathRooted(imgPath) ? imgPath : System.IO.Path.Combine(baseDir, imgPath);
                if (!File.Exists(full)) full = System.IO.Path.Combine(mapDir, System.IO.Path.GetFileName(imgPath));
                ts.Texture = Assets.LoadFile(full);
                if (ts.Columns <= 0 && ts.Texture != null && ts.TileWidth > 0) ts.Columns = ts.Texture.Width / ts.TileWidth;
            }
            return ts;
        }

        private static TileLayer ReadTileLayer(XElement el)
        {
            var layer = new TileLayer
            {
                Name = (string)el.Attribute("name") ?? "",
                Width = Int(el, "width"),
                Height = Int(el, "height"),
                Visible = (string)el.Attribute("visible") != "0",
                Opacity = el.Attribute("opacity") is { } op ? float.Parse(op.Value, CultureInfo.InvariantCulture) : 1f,
                Props = ReadProps(el),
            };
            layer.Gids = new uint[layer.Width * layer.Height];
            var data = el.Element("data");
            if (data == null) return layer;
            string enc = (string)data.Attribute("encoding");
            if (enc == "csv")
            {
                var parts = data.Value.Split(new[] { ',', '\n', '\r', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length && i < layer.Gids.Length; i++) layer.Gids[i] = uint.Parse(parts[i], CultureInfo.InvariantCulture);
            }
            else if (enc == null)
            {
                int i = 0;
                foreach (var t in data.Elements("tile"))
                    if (i < layer.Gids.Length) layer.Gids[i++] = (uint?)t.Attribute("gid") ?? 0;
            }
            else Console.WriteLine($"[Tiled] layer '{layer.Name}': encoding '{enc}' isn't supported, save the map with CSV layer format");
            return layer;
        }

        private static MapObject ReadObject(XElement el, string group)
        {
            var o = new MapObject
            {
                Id = Int(el, "id"),
                Name = (string)el.Attribute("name") ?? "",
                Class = (string)el.Attribute("class") ?? (string)el.Attribute("type") ?? "",
                Group = group,
                X = Float(el, "x"),
                Y = Float(el, "y"),
                Width = Float(el, "width"),
                Height = Float(el, "height"),
                IsPoint = el.Element("point") != null,
                IsEllipse = el.Element("ellipse") != null,
                Props = ReadProps(el),
            };
            if (el.Element("polygon") is { } poly)
            {
                o.Polygon = ((string)poly.Attribute("points") ?? "")
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Split(','))
                    .Select(p => new Vector2(o.X + float.Parse(p[0], CultureInfo.InvariantCulture), o.Y + float.Parse(p[1], CultureInfo.InvariantCulture)))
                    .ToList();
                if (o.Polygon.Count < 3) o.Polygon = null;
            }
            // A plain rectangle with no size is a point in practice.
            if (o.Polygon == null && !o.IsEllipse && o.Width <= 0 && o.Height <= 0) o.IsPoint = true;
            return o;
        }

        private static Dictionary<string, string> ReadProps(XElement el)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var props = el.Element("properties");
            if (props == null) return d;
            foreach (var p in props.Elements("property"))
                d[(string)p.Attribute("name") ?? ""] = (string)p.Attribute("value") ?? p.Value;
            return d;
        }

        private static int Int(XElement el, string attr) => el.Attribute(attr) is { } a ? (int)Math.Round(double.Parse(a.Value, CultureInfo.InvariantCulture)) : 0;
        private static float Float(XElement el, string attr) => el.Attribute(attr) is { } a ? float.Parse(a.Value, CultureInfo.InvariantCulture) : 0f;

        private static Color? ParseColor(string hex)
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 8) hex = hex.Substring(2);   // #AARRGGBB
            if (hex.Length != 6) return null;
            return new Color(Convert.ToInt32(hex.Substring(0, 2), 16), Convert.ToInt32(hex.Substring(2, 2), 16), Convert.ToInt32(hex.Substring(4, 2), 16));
        }

        // ------------------------------------------------------------------ drawing

        private const uint FlipH = 0x80000000, FlipV = 0x40000000, FlipD = 0x20000000, GidMask = 0x0FFFFFFF;

        private Tileset TilesetFor(uint gid)
        {
            Tileset found = null;
            foreach (var ts in Tilesets)
            {
                if (ts.FirstGid <= gid) found = ts;
                else break;
            }
            return found;
        }

        // Draws a tile layer. The batch's matrix maps map pixels to the screen; `view` (map pixels) culls tiles.
        public void DrawLayer(SpriteBatch sb, TileLayer layer, Rectangle view, float alpha = 1f)
        {
            if (!layer.Visible) return;
            var tint = Color.White * (layer.Opacity * alpha);
            int x0 = Math.Max(0, view.Left / TileWidth - 4), x1 = Math.Min(layer.Width - 1, view.Right / TileWidth + 4);
            int y0 = Math.Max(0, view.Top / TileHeight - 4), y1 = Math.Min(layer.Height - 1, view.Bottom / TileHeight + 8);
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    uint raw = layer.Gids[y * layer.Width + x];
                    uint gid = raw & GidMask;
                    if (gid == 0) continue;
                    var ts = TilesetFor(gid);
                    if (ts?.Texture == null || ts.Columns <= 0) continue;
                    int local = (int)(gid - ts.FirstGid);
                    var src = new Rectangle(local % ts.Columns * ts.TileWidth, local / ts.Columns * ts.TileHeight, ts.TileWidth, ts.TileHeight);
                    // Grid-sized tiles fill the cell; others keep their size, anchored at the cell's bottom-left.
                    int w = ts.GridSize ? TileWidth : ts.TileWidth, h = ts.GridSize ? TileHeight : ts.TileHeight;
                    var dest = new Rectangle(x * TileWidth, (y + 1) * TileHeight - h, w, h);
                    var fx = SpriteEffects.None;
                    if ((raw & FlipH) != 0) fx |= SpriteEffects.FlipHorizontally;
                    if ((raw & FlipV) != 0) fx |= SpriteEffects.FlipVertically;
                    if ((raw & FlipD) != 0)
                    {
                        // Diagonal flip = rotate 90 degrees and flip horizontally.
                        sb.Draw(ts.Texture, new Vector2(dest.X + w / 2f, dest.Y + h / 2f), src, tint, MathHelper.PiOver2,
                            new Vector2(ts.TileWidth / 2f, ts.TileHeight / 2f), new Vector2(w / (float)ts.TileWidth, h / (float)ts.TileHeight),
                            fx ^ SpriteEffects.FlipVertically, 0);
                        continue;
                    }
                    sb.Draw(ts.Texture, dest, src, tint, 0, Vector2.Zero, fx, 0);
                }
            }
        }
    }
}
