using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace SERAAC.Core
{
    // A loaded art file. Files named "<id>@<frames>.png" are horizontal strips of equal frames.
    public sealed class ArtSprite
    {
        public Texture2D Texture;
        public int Frames = 1;
        public float Fps = 8f;
        public int FrameWidth => Texture.Width / Frames;

        public Rectangle Source(double time)
        {
            int f = Frames <= 1 ? 0 : (int)(time * Fps) % Frames;
            return new Rectangle(f * FrameWidth, 0, FrameWidth, Texture.Height);
        }
    }

    // Fonts plus a runtime art registry. Art is loaded straight from the Art/ folder next to the
    // exe (no content pipeline), so artists can drop PNGs in and see them without touching code.
    // Mods can add or replace any of it with the same layout under Mods/<mod>/Art/.
    // Anything missing falls back to the placeholder drawing each screen already does.
    public static class Assets
    {
        public static UiFont Title, Header, Body, Small;
        public static Texture2D Pixel;

        private static GraphicsDevice _device;
        private static string _artRoot;
        private static readonly Dictionary<string, ArtSprite> _cache = new();

        public static void Load(GraphicsDevice device, ContentManager content)
        {
            _device = device;
            Title = UiFont.Load(content, "Fonts/Title");
            Header = UiFont.Load(content, "Fonts/Header");
            Body = UiFont.Load(content, "Fonts/Body");
            Small = UiFont.Load(content, "Fonts/Small");

            Pixel = new Texture2D(device, 1, 1);
            Pixel.SetData(new[] { Color.White });

            _artRoot = Path.Combine(AppContext.BaseDirectory, "Art");
        }

        // Returns null when no file exists for this id (callers draw a placeholder instead).
        public static ArtSprite Art(string id)
        {
            if (string.IsNullOrEmpty(id) || _device == null) return null;
            if (_cache.TryGetValue(id, out var cached)) return cached;

            ArtSprite sprite = null;
            try
            {
                // Mods' Art/ folders first (Core/Mods.cs), then the game's own.
                foreach (var root in Mods.ArtRoots(_artRoot))
                    if ((sprite = Find(root, id)) != null) break;
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Art] failed to load '{id}': {e.Message}");
                sprite = null;
            }

            _cache[id] = sprite;
            return sprite;
        }

        private static ArtSprite Find(string root, string id)
        {
            if (!Directory.Exists(root)) return null;
            string plain = Path.Combine(root, id + ".png");
            if (File.Exists(plain)) return new ArtSprite { Texture = LoadPremultiplied(plain) };

            // Ids can include a subfolder ("Stages/City_N_1"): search that folder for "<name>@<frames>.png".
            string dir = Path.GetDirectoryName(plain), name = Path.GetFileName(id);
            var files = Directory.Exists(dir) ? Directory.GetFiles(dir, name + "@*.png", SearchOption.AllDirectories) : Array.Empty<string>();
            foreach (var file in files)
            {
                string suffix = Path.GetFileNameWithoutExtension(file).Substring(name.Length + 1);
                if (int.TryParse(suffix, out int frames) && frames > 0)
                    return new ArtSprite { Texture = LoadPremultiplied(file), Frames = frames };
            }
            return null;
        }

        public static GraphicsDevice Device => _device;

        // A PNG from anywhere on disk (Tiled tilesets), loaded once. Null when the file is missing or unreadable.
        private static readonly Dictionary<string, Texture2D> _files = new(StringComparer.OrdinalIgnoreCase);

        public static Texture2D LoadFile(string path)
        {
            if (_device == null || string.IsNullOrEmpty(path)) return null;
            string full = Path.GetFullPath(path);
            if (_files.TryGetValue(full, out var tex)) return tex;
            try { tex = File.Exists(full) ? LoadPremultiplied(full) : null; }
            catch (Exception e) { Console.WriteLine($"[Art] failed to load '{full}': {e.Message}"); tex = null; }
            _files[full] = tex;
            return tex;
        }

        // Texture2D.FromFile gives straight alpha; SpriteBatch's default blend expects premultiplied.
        private static Texture2D LoadPremultiplied(string path)
        {
            var tex = Texture2D.FromFile(_device, path);
            var data = new Color[tex.Width * tex.Height];
            tex.GetData(data);
            for (int i = 0; i < data.Length; i++)
            {
                var c = data[i];
                data[i] = new Color(c.R * c.A / 255, c.G * c.A / 255, c.B * c.A / 255, c.A);
            }
            tex.SetData(data);
            return tex;
        }
    }
}
