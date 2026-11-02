using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SERAAC.Core
{
    // Tuning files under Art/ that reload while the game runs: edit, save, and the next frame
    // uses the new numbers. Files are read from the project's Art/ folder when the game runs
    // from a build folder inside the project (so edits never get overwritten by the next build),
    // otherwise from the Art/ copy next to the exe.
    public static class LiveJson
    {
        public static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            IncludeFields = true,
        };

        private static string _artRoot;

        // Art/ in the project (walks up from the exe looking for SERAAC.csproj), else Art/ next to the exe.
        public static string ArtRoot
        {
            get
            {
                if (_artRoot != null) return _artRoot;
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "SERAAC.csproj")) && Directory.Exists(Path.Combine(dir.FullName, "Art")))
                        return _artRoot = Path.Combine(dir.FullName, "Art");
                }
                return _artRoot = Path.Combine(AppContext.BaseDirectory, "Art");
            }
        }

        // A mod's copy of the file wins over the game's (Core/Mods.cs).
        public static string ArtPath(string relative) => Mods.ResolveArt(ArtRoot, relative);

        public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);

        public static void Write<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(value, Options));
        }
    }

    // One hot-reloaded file. Value is never null: a missing or broken file keeps the last good
    // value (or the defaults) and prints why to the console.
    public sealed class LiveFile<T> where T : class, new()
    {
        private readonly string _path;
        private DateTime _stamp;
        private double _nextCheck;

        public T Value { get; private set; } = new();
        public string Error { get; private set; }
        public int Version { get; private set; }   // bumps on every successful reload

        public LiveFile(string artRelativePath)
        {
            _path = LiveJson.ArtPath(artRelativePath);
            Load();
        }

        public string Path => _path;

        // Call once per frame; only touches the disk twice a second.
        public void Poll(double time)
        {
            if (time < _nextCheck) return;
            _nextCheck = time + 0.5;
            if (!File.Exists(_path)) return;
            if (File.GetLastWriteTimeUtc(_path) != _stamp) Load();
        }

        private void Load()
        {
            if (!File.Exists(_path)) return;
            try
            {
                _stamp = File.GetLastWriteTimeUtc(_path);
                Value = LiveJson.Read<T>(_path) ?? new T();
                Error = null;
                Version++;
            }
            catch (Exception e)
            {
                Error = e.Message;
                Console.WriteLine($"[LiveJson] {System.IO.Path.GetFileName(_path)}: {e.Message}");
            }
        }
    }
}
