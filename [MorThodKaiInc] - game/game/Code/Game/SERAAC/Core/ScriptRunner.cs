using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Xna.Framework.Input;

namespace SERAAC.Core
{
    // Drives the game with a tiny command script so screens can be captured without a person
    // at the keyboard:  SERAAC.exe --script path/to/script.txt
    //   wait <frames>          press <Key> [times]      hold <Key> <frames>
    //   shot <name>            quit
    public sealed class ScriptRunner
    {
        private readonly Queue<string[]> _commands = new();
        private int _wait;
        private Keys? _holding;
        private int _holdFrames;
        private readonly Queue<Keys> _taps = new();
        private bool _releaseTap;

        public string OutputDir { get; }
        public string PendingShot { get; set; }
        public bool Done { get; private set; }

        public ScriptRunner(string path)
        {
            foreach (var line in File.ReadAllLines(path))
            {
                var t = line.Trim();
                if (t.Length == 0 || t.StartsWith("#")) continue;
                _commands.Enqueue(t.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }
            OutputDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "shots");
            Directory.CreateDirectory(OutputDir);
        }

        // Called once per frame before Input.Update.
        public void Tick()
        {
            if (_releaseTap)
            {
                Input.Injected.Clear();
                _releaseTap = false;
                return;
            }
            if (_taps.Count > 0)
            {
                Input.Injected.Add(_taps.Dequeue());
                _releaseTap = true;
                return;
            }
            if (_holding.HasValue)
            {
                if (--_holdFrames <= 0)
                {
                    Input.Injected.Remove(_holding.Value);
                    _holding = null;
                }
                return;
            }
            if (_wait > 0) { _wait--; return; }
            if (PendingShot != null) return;
            if (_commands.Count == 0) { Done = true; return; }

            var c = _commands.Dequeue();
            switch (c[0].ToLowerInvariant())
            {
                case "wait": _wait = int.Parse(c[1], CultureInfo.InvariantCulture); break;
                case "press":
                    var key = Enum.Parse<Keys>(c[1], true);
                    int times = c.Length > 2 ? int.Parse(c[2], CultureInfo.InvariantCulture) : 1;
                    for (int i = 0; i < times; i++) _taps.Enqueue(key);
                    break;
                case "hold":
                    _holding = Enum.Parse<Keys>(c[1], true);
                    _holdFrames = int.Parse(c[2], CultureInfo.InvariantCulture);
                    Input.Injected.Add(_holding.Value);
                    break;
                case "shot": PendingShot = c[1]; break;
                case "quit": Done = true; break;
            }
        }
    }
}
