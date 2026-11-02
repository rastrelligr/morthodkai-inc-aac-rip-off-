using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;

namespace SERAAC.Core
{
    // One folder in Mods/. Its mod.json fills the fields below (see Mods/README.md).
    public sealed class ModInfo
    {
        public string Id;
        public string Name;
        public string Version = "1.0";
        public string Author;
        public string Description;
        public string Website;
        public string Icon = "icon.png";                     // in the mod's folder; square works best
        public string[] Tags = Array.Empty<string>();         // "content", "art", "balance", "qol"...
        public string[] Dependencies = Array.Empty<string>(); // ids of mods that must be on and load first
        public bool Enabled = true;                           // the default; the Mods screen's choice wins (load_order.json)
        public int Priority;                                  // default load order: higher loads later and wins

        [JsonIgnore] public string Folder;
        [JsonIgnore] public bool Loaded;                      // actually in use this session (Enabled, and nothing stopped it)
        [JsonIgnore] public readonly List<string> Errors = new();
        [JsonIgnore] public readonly List<string> Notes = new();   // what it changed, for the Mods screen / self-test

        public string Title => string.IsNullOrEmpty(Name) ? Id : Name;
        public string ArtDir => Path.Combine(Folder, "Art");
        public string AudioDir => Path.Combine(Folder, "Audio");
        public string DataDir => Path.Combine(Folder, "Data");
        public string IconPath => string.IsNullOrEmpty(Icon) ? null : Path.Combine(Folder, Icon);
        public bool HasCode => Directory.Exists(Folder) && Directory.GetFiles(Folder, "*.dll").Length > 0;

        public void Error(string message)
        {
            Errors.Add(message);
            Console.WriteLine($"[Mods] {Id}: {message}");
        }
    }

    // Code mods: a DLL in a mod's folder with a public class implementing this. OnLoad runs once,
    // after every mod's Data/*.json has been applied, so it can read and change any *Db.
    public interface ISeraacMod
    {
        void OnLoad(ModInfo mod);
    }

    // Finds mods in Mods/ and lets the rest of the game look files up through them.
    //   Mods/<folder>/mod.json      required: id, name, version, author, icon... (Mods/README.md)
    //   Mods/<folder>/icon.png      shown in the Mods screen
    //   Mods/<folder>/Art/...       same layout as Art/: adds new files, replaces ones with the same name
    //   Mods/<folder>/Audio/...     same layout as Audio/
    //   Mods/<folder>/Data/*.json   new and changed content (Data/ModContent.cs)
    //   Mods/<folder>/*.dll         optional code (ISeraacMod)
    //   Mods/load_order.json        written by the Mods screen: which mods are on, in what order
    // Folders starting with "_" or "." are skipped. Start the game with --no-mods to ignore every mod.
    // Mods are read once at startup: switching or reordering them takes a restart.
    public static class Mods
    {
        private sealed class LoadOrderFile
        {
            public List<Entry> Mods = new();
            public sealed class Entry { public string Id; public bool Enabled; }
        }

        private static List<ModInfo> _all;
        private static string _root;
        private static bool _dataApplied;

        public static bool Disabled => Environment.GetCommandLineArgs().Contains("--no-mods");

        // Mods/ in the project while developing (like LiveJson.ArtRoot), else Mods/ next to the exe.
        public static string Root
        {
            get
            {
                if (_root != null) return _root;
                var art = new DirectoryInfo(LiveJson.ArtRoot);
                string project = art.Parent?.FullName;
                return _root = project != null && File.Exists(Path.Combine(project, "SERAAC.csproj"))
                    ? Path.Combine(project, "Mods")
                    : Path.Combine(AppContext.BaseDirectory, "Mods");
            }
        }

        private static string LoadOrderPath => Path.Combine(Root, "load_order.json");

        // Every mod found, on or off, in load order.
        public static IReadOnlyList<ModInfo> All => _all ??= Discover();

        // Mods in use this session, in load order (lowest priority first).
        public static IEnumerable<ModInfo> Active => All.Where(m => m.Loaded);

        public static int ErrorCount => All.Sum(m => m.Errors.Count);

        private static List<ModInfo> Discover()
        {
            var found = new List<ModInfo>();
            if (Disabled || !Directory.Exists(Root)) return found;
            foreach (var dir in Directory.GetDirectories(Root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileName(dir);
                if (name.StartsWith("_") || name.StartsWith(".")) continue;
                string manifest = Path.Combine(dir, "mod.json");
                if (!File.Exists(manifest)) continue;
                ModInfo mod;
                try
                {
                    mod = LiveJson.Read<ModInfo>(manifest) ?? new ModInfo();
                    mod.Folder = dir;
                }
                catch (Exception e)
                {
                    mod = new ModInfo { Enabled = false, Folder = dir, Id = name };
                    mod.Error("mod.json: " + e.Message);
                }
                if (string.IsNullOrWhiteSpace(mod.Id)) mod.Id = name;
                mod.Tags ??= Array.Empty<string>();
                mod.Dependencies ??= Array.Empty<string>();
                if (found.Any(m => m.Id == mod.Id))
                {
                    mod.Enabled = false;
                    mod.Error($"another mod already uses the id '{mod.Id}'; this one is skipped");
                }
                found.Add(mod);
            }

            // Default order: priority, then folder name. The Mods screen's saved order and switches win.
            found = found.OrderBy(m => m.Priority).ToList();
            var saved = ReadLoadOrder();
            if (saved != null)
            {
                var rank = saved.Mods.Select((e, i) => (e.Id, i)).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().i);
                found = found.OrderBy(m => rank.TryGetValue(m.Id, out int r) ? r : int.MaxValue).ToList();
                foreach (var e in saved.Mods)
                    if (found.FirstOrDefault(m => m.Id == e.Id) is { } m && m.Errors.Count == 0) m.Enabled = e.Enabled;
            }

            PlaceDependenciesFirst(found);
            foreach (var m in found) m.Loaded = m.Enabled && m.Errors.Count == 0;
            CheckDependencies(found);
            foreach (var m in found.Where(m => m.Loaded)) Console.WriteLine($"[Mods] found {m.Title} {m.Version} ({Path.GetFileName(m.Folder)})");
            return found;
        }

        // A mod loads after everything it depends on, whatever order was saved.
        private static void PlaceDependenciesFirst(List<ModInfo> mods)
        {
            for (int pass = 0; pass <= mods.Count * mods.Count; pass++)
            {
                bool moved = false;
                for (int i = 0; i < mods.Count; i++)
                {
                    int last = mods[i].Dependencies.Select(d => mods.FindIndex(m => m.Id == d)).DefaultIfEmpty(-1).Max();
                    if (last <= i) continue;
                    var m = mods[i];
                    mods.RemoveAt(i);
                    mods.Insert(last, m);
                    moved = true;
                    break;
                }
                if (!moved) return;
            }
            foreach (var m in mods) if (m.Dependencies.Any()) m.Error("dependencies loop back to each other; check their \"dependencies\"");
        }

        // A mod whose dependency is missing or off doesn't load (and neither does anything needing it).
        private static void CheckDependencies(List<ModInfo> mods)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var m in mods.Where(m => m.Loaded))
                    foreach (var dep in m.Dependencies)
                    {
                        var d = mods.FirstOrDefault(x => x.Id == dep);
                        if (d != null && d.Loaded) continue;
                        m.Error(d == null ? $"needs the mod '{dep}', which isn't installed" : $"needs '{d.Title}', which is off or broken");
                        m.Loaded = false;
                        changed = true;
                        break;
                    }
            }
        }

        private static LoadOrderFile ReadLoadOrder()
        {
            try { return File.Exists(LoadOrderPath) ? LiveJson.Read<LoadOrderFile>(LoadOrderPath) : null; }
            catch (Exception e) { Console.WriteLine($"[Mods] load_order.json: {e.Message} (ignored)"); return null; }
        }

        // Saves the Mods screen's choices; they take effect on the next start.
        public static void SaveLoadOrder(IEnumerable<(ModInfo mod, bool enabled)> order)
        {
            var file = new LoadOrderFile { Mods = order.Select(o => new LoadOrderFile.Entry { Id = o.mod.Id, Enabled = o.enabled }).ToList() };
            try { LiveJson.Write(LoadOrderPath, file); }
            catch (Exception e) { Console.WriteLine($"[Mods] couldn't save load_order.json: {e.Message}"); }
        }

        // Starts a fresh copy of the game (same arguments) and closes this one, so mod changes apply.
        public static bool Restart()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                string exe = Environment.ProcessPath;
                var info = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory };
                // Started as "dotnet SERAAC.dll": args[0] is the dll, which dotnet needs again.
                if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) info.ArgumentList.Add(args[0]);
                foreach (var a in args.Skip(1)) info.ArgumentList.Add(a);
                Process.Start(info);
                Game1.Instance.Exit();
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Mods] restart failed: {e.Message}");
                return false;
            }
        }

        // Opens a folder in the system file browser.
        public static void OpenFolder(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception e) { Console.WriteLine($"[Mods] couldn't open {path}: {e.Message}"); }
        }

        // A file under Art/ (or Audio/): the highest-priority mod that has it, else the game's own path.
        public static string ResolveArt(string baseArtRoot, string relative) => Resolve(m => m.ArtDir, baseArtRoot, relative);
        public static string ResolveAudio(string baseAudioRoot, string relative) => Resolve(m => m.AudioDir, baseAudioRoot, relative);

        private static string Resolve(Func<ModInfo, string> dir, string baseRoot, string relative)
        {
            foreach (var mod in Active.Reverse())
            {
                string path = Path.Combine(dir(mod), relative);
                if (File.Exists(path)) return path;
            }
            return Path.Combine(baseRoot, relative);
        }

        // Folders to search for Art, highest priority first, ending with the game's own.
        public static IEnumerable<string> ArtRoots(string baseArtRoot) =>
            Active.Reverse().Select(m => m.ArtDir).Where(Directory.Exists).Append(baseArtRoot);

        // Applies every active mod's Data/*.json, then runs code mods. Safe to call more than once.
        public static void LoadContent()
        {
            if (_dataApplied) return;
            _dataApplied = true;
            var active = Active.ToList();
            if (active.Count == 0) return;

            Data.ModContent.Apply(active);

            foreach (var mod in active)
                foreach (var dll in Directory.GetFiles(mod.Folder, "*.dll"))
                    RunCode(mod, dll);

            foreach (var mod in active)
                Console.WriteLine($"[Mods] loaded {mod.Title}" + (mod.Errors.Count > 0 ? $" with {mod.Errors.Count} error(s)" : ""));
        }

        private static void RunCode(ModInfo mod, string dll)
        {
            string file = Path.GetFileName(dll);
            try
            {
                var asm = Assembly.LoadFrom(dll);
                var types = asm.GetTypes().Where(t => typeof(ISeraacMod).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface).ToList();
                if (types.Count == 0) { mod.Notes.Add($"{file}: no ISeraacMod class, nothing to run"); return; }
                foreach (var type in types)
                {
                    ((ISeraacMod)Activator.CreateInstance(type)).OnLoad(mod);
                    mod.Notes.Add($"ran {type.Name} from {file}");
                }
            }
            catch (ReflectionTypeLoadException e)
            {
                mod.Error($"{file}: {e.LoaderExceptions.FirstOrDefault()?.Message ?? e.Message}");
            }
            catch (Exception e)
            {
                mod.Error($"{file}: {(e is TargetInvocationException t ? t.InnerException : e)?.Message}");
            }
        }
    }
}
