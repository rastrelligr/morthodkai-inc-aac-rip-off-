using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Audio;

namespace SERAAC.Core
{
    // Sound effects and music, loaded at runtime from the Audio/ folder next to the exe (like Art/).
    //   Files: an id -> one or more files under Audio/ (no extension; .wav, .ogg and .mp3 all work). With several,
    //          each play picks one at random. A name ending in * means every file starting with it (all the
    //          numbered takes of a sound: "Combat/DSGNMisc_HIT-Gore Pierce_HY_PC-*").
    //   Tracks: a music id -> one or more tracks under Audio/; several = one picked at random each time it starts.
    //   Audio/sfx/<id>.wav and Audio/music/<id>.ogg still work without listing them.
    // WAV can be 8 / 16 / 24 / 32-bit or float, at any sample rate (above 48 kHz it is halved until it fits).
    // Music is decoded on a background thread and crossfades in; the last few tracks stay in memory.
    // Mods can add or replace sounds under Mods/<mod>/Audio/ (same layout). A missing file is silent.
    // The ids the game uses are listed in Audio/README.md.
    public static class Audio
    {
        public static float SfxVolume = 0.7f, MusicVolume = 0.25f;   // set from Settings (Options screen)

        public static readonly Dictionary<string, string[]> Files = new()
        {
            // ---- menus (Audio/UI)
            ["ui_move"] = new[] { "UI/Fantasy/Fantasy_UI (60)" },        // cursor moves / mouse onto something clickable
            ["ui_select"] = new[] { "UI/Fantasy/Fantasy_UI (12)" },      // an option is chosen
            ["ui_back"] = new[] { "UI/Fantasy/Fantasy_UI (13)" },        // back / cancel / close
            ["item"] = new[] { "UI/Fantasy/Fantasy_UI (14)" },           // an item is used
            ["pickup"] = new[] { "UI/Fantasy/Fantasy_UI (10)" },         // loot picked up in a Trial
            ["upgrade"] = new[] { "UI/Piano/Piano_Ui (1)" },             // an upgrade bought / gear equipped
            ["secured"] = new[] { "UI/Piano/Piano_Ui (4)" },             // a Trial extracted (jingle)
            ["objective"] = new[] { "UI/Skyward Hero/SkywardHero_UI (5)" }, // objective complete
            ["victory"] = new[] { "UI/Skyward Hero/SkywardHero_UI (3)" },   // fight won (jingle)
            ["defeat"] = new[] { "UI/Piano/Piano_Ui (6)" },              // the party falls
            ["alert"] = new[] { "Combat/DSGNTonl_INTERFACE-Tonal Click_HY_PC-*" }, // a pack spots the party
            ["type"] = new[] { "Dialogue/Typing1", "Dialogue/Typing2" }, // dialogue typewriter

            // ---- walking (Audio/Move)
            ["step"] = new[] { "Move/footstep_walk" },
            ["step_tile"] = new[] { "Move/TileSteps1", "Move/TileSteps2", "Move/TileSteps3", "Move/TileSteps4" },

            // ---- combat (Audio/Combat)
            ["encounter"] = new[] { "Combat/DSGNImpt_EXPLOSION-Exploding Movement_HY_PC-*" },
            ["dash"] = new[] { "Combat/DSGNMisc_MOVEMENT-Whoosh Sweep_HY_PC-*" },      // a melee attacker runs in
            ["cast"] = new[] { "Combat/DSGNSynth_CAST-Mecha Sword Cast_HY_PC-*" },     // a ranged attack / heal starts
            ["move"] = new[] { "Combat/DSGNMisc_STEP-Bit Step_HY_PC-*" },              // someone changes position
            ["hit"] = new[] { "Combat/DSGNMisc_HIT-Gore Pierce_HY_PC-*", "Combat/DSGNMisc_MELEE-Sword Slash_HY_PC-*" },
            ["hit_magic"] = new[] { "Combat/DSGNMisc_HIT-Spell Hit_HY_PC-*" },
            ["crit"] = new[] { "Combat/DSGNMisc_SKILL IMPACT-Critical Strike_HY_PC-*" },
            ["tick"] = new[] { "Combat/DSGNMisc_HIT-Fleeting Hit_HY_PC-*" },           // Blight, Blood Feast's HP cost
            ["heal"] = new[] { "Combat/DSGNSynth_BUFF-Invigoration_HY_PC-*" },
            ["status"] = new[] { "Combat/DSGNSynth_BUFF-Enemy Debuff_HY_PC-*" },
            ["guard"] = new[] { "Combat/DSGNSynth_BUFF-Bonus Max Shield_HY_PC-*" },
            ["windup"] = new[] { "Combat/DSGNSynth_CAST-Mecha Energy Gathering_HY_PC-*" },
            ["interrupted"] = new[] { "Combat/DSGNImpt_EXPLOSION-Forced Interruption_HY_PC-*" },
            ["enraged"] = new[] { "Combat/DSGNImpt_EXPLOSION-Eruption_HY_PC-*" },
            ["riposte"] = new[] { "Combat/DSGNMisc_MELEE-Sword Parry_HY_PC-*" },
            ["aegis"] = new[] { "Combat/DSGNMisc_MELEE-Sword Deflect_HY_PC-*" },
            ["feast"] = new[] { "Combat/DSGNImpt_EXPLOSION-Crunching_HY_PC-*" },
            ["woven"] = new[] { "Combat/DSGNSynth_BUFF-Water Buff_HY_PC-*" },
            ["stack"] = new[] { "Combat/DSGNSynth_BUFF-Stats Up_HY_PC-*" },
            ["death"] = new[] { "Combat/DSGNImpt_EXPLOSION-Bass Hit_HY_PC-*" },
        };

        public static readonly Dictionary<string, string[]> Tracks = new()
        {
            ["menu"] = new[] { "Music/MainMenu" },
            ["dialogue"] = new[] { "Music/Unicorn Heads" },     // Relle's room, the training floor, the ready room
            ["hub"] = new[] { "Music/Traversing" },             // the Sanctum (hub menu)
            ["airlift"] = new[] { "Music/Stormy" },
            ["explore"] = new[] { "Music/Dungeon_Explore" },
            ["combat"] = new[] { "Music/Fight_1", "Music/Fight_2", "Music/Fight_3", "Music/Fight_4" },
            ["boss"] = new[] { "Music/WyvernFight" },
        };

        private static readonly string[] Extensions = { ".wav", ".ogg", ".mp3" };
        private static string _root;
        private static string Root => _root ??= Path.Combine(AppContext.BaseDirectory, "Audio");

        // ------------------------------------------------------------------ sound effects

        private static readonly Dictionary<string, SoundEffect> _cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, List<string>> _variants = new();
        private static readonly Random _rng = new();

        // The same sound asked for twice within this many ms plays once (a screen and the shared UI hook can both ask).
        private const long RepeatMs = 60;
        private static readonly Dictionary<string, long> _lastPlayed = new();

        public static void Play(string id, float volume = 1f, float pitch = 0f)
        {
            if (string.IsNullOrEmpty(id)) return;
            long now = Environment.TickCount64;
            if (_lastPlayed.TryGetValue(id, out long last) && now - last < RepeatMs) return;
            var files = Variants(id);
            if (files.Count == 0) return;
            var fx = Load(files[_rng.Next(files.Count)]);
            if (fx == null) return;
            _lastPlayed[id] = now;
            try { fx.Play(Math.Clamp(volume * SfxVolume, 0f, 1f), Math.Clamp(pitch, -1f, 1f), 0f); }
            catch (Exception e) { Console.WriteLine($"[Audio] {id}: {e.Message}"); }
        }

        // Every file this id can play (full paths), found once.
        private static List<string> Variants(string id)
        {
            if (_variants.TryGetValue(id, out var list)) return list;
            list = new List<string>();
            var own = Find(Path.Combine("sfx", id));
            if (own != null) list.Add(own);
            else if (Files.TryGetValue(id, out var names))
                foreach (var n in names) list.AddRange(Expand(n));
            _variants[id] = list;
            return list;
        }

        // "Folder/Name" -> the file with any supported extension; "Folder/Prefix*" -> every file starting with Prefix.
        private static IEnumerable<string> Expand(string name)
        {
            name = name.Replace('/', Path.DirectorySeparatorChar);
            if (!name.EndsWith("*"))
            {
                var f = Find(name);
                return f == null ? Array.Empty<string>() : new[] { f };
            }
            string prefix = Path.GetFileName(name.TrimEnd('*'));
            string dir = Path.GetDirectoryName(Mods.ResolveAudio(Root, name.TrimEnd('*') + "x")) ?? Root;
            if (!Directory.Exists(dir)) return Array.Empty<string>();
            return Directory.GetFiles(dir)
                .Where(f => Path.GetFileName(f).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                            && Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f).ToList();
        }

        private static string Find(string relativeNoExt)
        {
            foreach (var ext in Extensions)
            {
                string path = Mods.ResolveAudio(Root, relativeNoExt + ext);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        private static SoundEffect Load(string path)
        {
            lock (_cache)
                if (_cache.TryGetValue(path, out var cached)) return cached;
            SoundEffect fx = null;
            try { fx = Decode(path); }
            catch (Exception e) { Console.WriteLine($"[Audio] failed to load {path}: {e.Message}"); }
            lock (_cache) _cache[path] = fx;
            return fx;
        }

        // Decodes these sounds on a background thread now, so their first play doesn't stutter (loading screens).
        public static void Preload(params string[] ids)
        {
            var files = ids.SelectMany(Variants).Distinct().ToList();
            Task.Run(() => { foreach (var f in files) Load(f); });
        }

        // ------------------------------------------------------------------ music

        private sealed class Playing
        {
            public string Path;
            public SoundEffectInstance Instance;
            public float Fade;      // 0..1 volume share
            public bool Leaving;
        }

        private static string _currentId;
        private static Playing _current;
        private static readonly List<Playing> _leaving = new();
        private static Task<SoundEffect> _loading;
        private static string _loadingPath;
        private static readonly LinkedList<(string path, SoundEffect fx)> _songs = new();   // most recent first
        private const int KeepSongs = 3;
        private const float FadeSeconds = 0.8f;

        // A track is still being decoded (loading screens wait for it).
        public static bool MusicLoading => _loading != null;

        // Starts a looping track (no-op if that id is already playing). Null stops the music.
        public static void Music(string id)
        {
            if (id == _currentId) return;
            _currentId = id;
            string path = null;
            if (id != null)
            {
                var candidates = Tracks.TryGetValue(id, out var t)
                    ? t.Select(n => Find(n.Replace('/', Path.DirectorySeparatorChar))).Where(p => p != null).ToList()
                    : new List<string>();
                if (candidates.Count == 0 && Find(Path.Combine("music", id)) is { } own) candidates.Add(own);
                if (candidates.Count > 0) path = candidates[_rng.Next(candidates.Count)];
            }
            if (_current != null && _current.Path == path) return;   // two ids sharing a track: keep it going
            if (_current != null) { _current.Leaving = true; _leaving.Add(_current); _current = null; }
            _loadingPath = path;
            _loading = path == null ? null : Task.Run(() => SongFor(path));
        }

        private static SoundEffect SongFor(string path)
        {
            lock (_songs)
                foreach (var (p, fx) in _songs)
                    if (p == path) return fx;
            SoundEffect song = null;
            try { song = Decode(path); }
            catch (Exception e) { Console.WriteLine($"[Audio] failed to load music {path}: {e.Message}"); }
            return song;
        }

        // Game1 calls this every frame: starts decoded tracks and runs the crossfades.
        public static void Update(float dt)
        {
            if (_loading is { IsCompleted: true })
            {
                var song = _loading.IsCompletedSuccessfully ? _loading.Result : null;
                string path = _loadingPath;
                _loading = null;
                if (song != null && path == _loadingPath)
                {
                    Remember(path, song);
                    try
                    {
                        var inst = song.CreateInstance();
                        inst.IsLooped = true;
                        inst.Volume = 0;
                        inst.Play();
                        _current = new Playing { Path = path, Instance = inst };
                    }
                    catch (Exception e) { Console.WriteLine($"[Audio] music: {e.Message}"); }
                }
            }
            if (_current != null)
            {
                _current.Fade = Math.Min(1f, _current.Fade + dt / FadeSeconds);
                _current.Instance.Volume = Math.Clamp(_current.Fade * MusicVolume, 0f, 1f);
            }
            for (int i = _leaving.Count - 1; i >= 0; i--)
            {
                var p = _leaving[i];
                p.Fade -= dt / FadeSeconds;
                if (p.Fade <= 0) { p.Instance.Stop(); p.Instance.Dispose(); _leaving.RemoveAt(i); }
                else p.Instance.Volume = Math.Clamp(p.Fade * MusicVolume, 0f, 1f);
            }
        }

        // Keeps the last few decoded tracks; older ones are freed once they're no longer playing.
        private static void Remember(string path, SoundEffect song)
        {
            lock (_songs)
            {
                var node = _songs.First;
                while (node != null) { var next = node.Next; if (node.Value.path == path) _songs.Remove(node); node = next; }
                _songs.AddFirst((path, song));
                while (_songs.Count > KeepSongs)
                {
                    var old = _songs.Last.Value;
                    if (_leaving.Any(l => l.Path == old.path)) break;
                    _songs.RemoveLast();
                    old.fx.Dispose();
                }
            }
        }

        // ------------------------------------------------------------------ decoding

        private const int MaxRate = 48000;

        private static SoundEffect Decode(string path)
        {
            var (pcm, rate, channels) = DecodePcm(path);
            var bytes = new byte[pcm.Length * 2];
            Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
            return new SoundEffect(bytes, rate, channels == 1 ? AudioChannels.Mono : AudioChannels.Stereo);
        }

        // For the self-test: every listed sound id and music track has a file, and each file decodes.
        public static IEnumerable<string> Problems()
        {
            foreach (var (id, _) in Files)
            {
                var files = Variants(id);
                if (files.Count == 0) { yield return $"sound '{id}': no file found"; continue; }
                foreach (var f in files)
                {
                    string err = null;
                    try { DecodePcm(f); } catch (Exception e) { err = e.Message; }
                    if (err != null) yield return $"sound '{id}': {Path.GetFileName(f)} doesn't decode ({err})";
                }
            }
            foreach (var (id, names) in Tracks)
                foreach (var n in names)
                {
                    var f = Find(n.Replace('/', Path.DirectorySeparatorChar));
                    if (f == null) { yield return $"music '{id}': {n} not found"; continue; }
                    string err = null;
                    try { DecodePcm(f); } catch (Exception e) { err = e.Message; }
                    if (err != null) yield return $"music '{id}': {Path.GetFileName(f)} doesn't decode ({err})";
                }
        }

        private static (short[] pcm, int rate, int channels) DecodePcm(string path)
        {
            short[] pcm;
            int rate, channels;
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".ogg": (pcm, rate, channels) = DecodeOgg(path); break;
                case ".mp3": (pcm, rate, channels) = DecodeMp3(path); break;
                default: (pcm, rate, channels) = DecodeWav(path); break;
            }
            if (channels > 2) (pcm, channels) = (DownmixToStereo(pcm, channels), 2);
            while (rate > MaxRate) { pcm = Halve(pcm, channels); rate /= 2; }
            if (pcm.Length == 0) throw new InvalidDataException("no samples");
            return (pcm, rate, channels);
        }

        private static short ToShort(float v) => (short)Math.Clamp((int)(v * 32767f), short.MinValue, short.MaxValue);

        // Vorbis (NVorbis), or Opus (Concentus) when the file turns out to hold an Opus stream.
        private static (short[], int, int) DecodeOgg(string path)
        {
            if (IsOpus(path)) return DecodeOpus(path);
            using var reader = new NVorbis.VorbisReader(path);
            return ReadFloats(reader.Channels, reader.SampleRate, (buf, n) => reader.ReadSamples(buf, 0, n));
        }

        private static bool IsOpus(string path)
        {
            var head = new byte[64];
            using (var f = File.OpenRead(path)) f.Read(head, 0, head.Length);
            return System.Text.Encoding.ASCII.GetString(head).Contains("OpusHead");
        }

        private static (short[], int, int) DecodeOpus(string path)
        {
            using var file = File.OpenRead(path);
            var decoder = Concentus.OpusCodecFactory.CreateDecoder(48000, 2);
            var ogg = new Concentus.Oggfile.OpusOggReadStream(decoder, file);
            var samples = new List<short>(48000 * 2 * 60);
            while (ogg.HasNextPacket)
            {
                var packet = ogg.DecodeNextPacket();
                if (packet != null) samples.AddRange(packet);
            }
            return (samples.ToArray(), 48000, 2);
        }

        private static (short[], int, int) DecodeMp3(string path)
        {
            using var mp3 = new NLayer.MpegFile(path);
            return ReadFloats(mp3.Channels, mp3.SampleRate, (buf, n) => mp3.ReadSamples(buf, 0, n));
        }

        private static (short[], int, int) ReadFloats(int channels, int rate, Func<float[], int, int> read)
        {
            var samples = new List<short>(rate * channels * 60);
            var buffer = new float[4096 * channels];
            int n;
            while ((n = read(buffer, buffer.Length)) > 0)
                for (int i = 0; i < n; i++) samples.Add(ToShort(buffer[i]));
            return (samples.ToArray(), rate, channels);
        }

        // RIFF WAVE: PCM 8 / 16 / 24 / 32-bit, IEEE float 32 / 64, WAVE_FORMAT_EXTENSIBLE.
        private static (short[], int, int) DecodeWav(string path)
        {
            using var br = new BinaryReader(File.OpenRead(path));
            if (new string(br.ReadChars(4)) != "RIFF") throw new InvalidDataException("not a RIFF file");
            br.ReadInt32();
            if (new string(br.ReadChars(4)) != "WAVE") throw new InvalidDataException("not a WAVE file");
            int format = 1, channels = 2, rate = 44100, bits = 16;
            while (br.BaseStream.Position + 8 <= br.BaseStream.Length)
            {
                string chunk = new string(br.ReadChars(4));
                int size = br.ReadInt32();
                long next = br.BaseStream.Position + size + (size & 1);
                if (chunk == "fmt ")
                {
                    format = br.ReadUInt16();
                    channels = br.ReadUInt16();
                    rate = br.ReadInt32();
                    br.ReadInt32(); br.ReadUInt16();
                    bits = br.ReadUInt16();
                    if (format == 0xFFFE && size >= 26) { br.ReadUInt16(); br.ReadUInt16(); br.ReadUInt32(); format = br.ReadUInt16(); }
                }
                else if (chunk == "data")
                {
                    long available = Math.Min(size, br.BaseStream.Length - br.BaseStream.Position);
                    var data = br.ReadBytes((int)available);
                    return (Convert(data, format, bits), rate, channels);
                }
                br.BaseStream.Position = Math.Min(next, br.BaseStream.Length);
            }
            throw new InvalidDataException("no data chunk");
        }

        private static short[] Convert(byte[] d, int format, int bits)
        {
            int step = bits / 8;
            var o = new short[d.Length / step];
            for (int i = 0, j = 0; j < o.Length; i += step, j++)
            {
                o[j] = (format, bits) switch
                {
                    (3, 32) => ToShort(BitConverter.ToSingle(d, i)),
                    (3, 64) => ToShort((float)BitConverter.ToDouble(d, i)),
                    (_, 8) => (short)((d[i] - 128) << 8),
                    (_, 16) => BitConverter.ToInt16(d, i),
                    (_, 24) => (short)((d[i + 1]) | (d[i + 2] << 8)),
                    (_, 32) => (short)(BitConverter.ToInt32(d, i) >> 16),
                    _ => 0,
                };
            }
            return o;
        }

        private static short[] Halve(short[] pcm, int channels)
        {
            int frames = pcm.Length / channels / 2;
            var o = new short[frames * channels];
            for (int f = 0; f < frames; f++)
                for (int c = 0; c < channels; c++)
                    o[f * channels + c] = (short)((pcm[(f * 2) * channels + c] + pcm[(f * 2 + 1) * channels + c]) / 2);
            return o;
        }

        private static short[] DownmixToStereo(short[] pcm, int channels)
        {
            int frames = pcm.Length / channels;
            var o = new short[frames * 2];
            for (int f = 0; f < frames; f++) { o[f * 2] = pcm[f * channels]; o[f * 2 + 1] = pcm[f * channels + 1]; }
            return o;
        }
    }
}
