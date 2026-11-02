using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SERAAC.Core;
using SERAAC.Trial;

namespace SERAAC.Stage
{
    // One pre-rendered combat background, listed in Art/Stages/backgrounds.json (see Art/Stages/README.md).
    public sealed class BackgroundDef
    {
        public string Image;                       // file name in Art/Stages, without ".png"
        public string Area;                        // area id ("city", "cathedral"); null = any area

        // Trial id(s) from Data/WorldDb.cs ("city_outskirt", or ["city_outskirt", "city_academy"]);
        // empty = every trial of the area.
        [JsonConverter(typeof(OneOrManyConverter))]
        public List<string> Trial = new();

        public List<string> Use = new() { "normal" }; // "normal", "elite", "boss" (Objective room)
        public List<string> Enemies = new();       // if set, only for fights containing one of these enemy ids
        public int Weight = 1;                     // relative chance among equally good matches

        // Framing. The image is scaled to cover the screen, then by Overscan so the camera can pan,
        // shake and zoom out without showing an edge. OffsetY moves it (pixels at 1280x720) to line
        // the floor up with the characters' feet (y = 430).
        public float Overscan = 1.12f;
        public float OffsetY;

        // Parallax layers, back to front. Usually left out: files named <Image>_Backdrop.png,
        // <Image>_Background.png, <Image>_Middleground.png and <Image>_Foreground.png are found and used automatically, with
        // the depths from _camera.json. List them here to use other files or depths.
        public List<LayerDef> Layers;

        // Atmosphere over this background: a preset in Art/Fx/ambience.json ("none" = nothing). Null = the trial's.
        public string Ambience;
    }

    // One parallax layer of a background.
    public sealed class LayerDef
    {
        public string Image;           // file name in Art/Stages, without ".png"
        public float Depth = 0.6f;     // how much of the camera movement it follows: 0 = fixed, 1 = moves with
                                       // the characters, over 1 = in front of them (drawn over the characters)
        public float OffsetY;          // pixels at 1280x720
    }

    // A layer ready to draw.
    public readonly record struct StageLayer(string ArtId, float Depth, float OffsetY);

    public sealed class BackgroundFile
    {
        public List<BackgroundDef> Backgrounds = new();
    }

    // Picks the background for a fight. Reloads when backgrounds.json is saved.
    public static class BackgroundDb
    {
        public static readonly LiveFile<BackgroundFile> File = new("Stages/backgrounds.json");

        public static List<BackgroundDef> All => File.Value.Backgrounds;

        public static string ArtId(BackgroundDef b) => b == null ? null : "Stages/" + b.Image;

        public static BackgroundDef Get(string image) => image == null ? null : All.FirstOrDefault(b => b.Image == image);

        public static string KindFor(RoomType t) => t switch
        {
            RoomType.Elite => "elite",
            RoomType.Objective => "boss",
            _ => "normal",
        };

        // Best background for a fight. Backgrounds for another area, trial or enemy are skipped. The rest
        // are ranked, most important first:
        //   1. made for one of these enemies (a specific elite or boss),
        //   2. made for this kind of fight (an elite fight prefers an "elite" background),
        //   3. made for this trial,
        //   4. made for this area (rather than for any area).
        // An elite or boss fight with nothing of its kind uses a "normal" one. Ties are broken by Weight
        // at random (Pick). Candidates returns every background tied for best; empty when nothing fits.
        public static List<BackgroundDef> Candidates(string areaId, string trialId, string kind, IEnumerable<string> enemyIds)
        {
            var enemies = enemyIds.ToHashSet();
            int Score(BackgroundDef b)
            {
                if (b.Area != null && b.Area != areaId) return -1;
                bool forTrial = b.Trial.Count > 0 && b.Trial.Contains(trialId);
                if (b.Trial.Count > 0 && !forTrial) return -1;     // made for another trial
                bool forEnemy = b.Enemies.Count > 0 && b.Enemies.Any(enemies.Contains);
                if (b.Enemies.Count > 0 && !forEnemy) return -1;   // made for someone else
                int score = forEnemy ? 100 : 0;
                if (b.Use.Contains(kind)) score += 20;
                else if (kind != "normal" && b.Use.Contains("normal")) score += 1; // no special one yet
                else return -1;
                if (forTrial) score += 10;
                if (b.Area == areaId) score += 5;
                return score;
            }

            var ranked = All.Where(b => !string.IsNullOrEmpty(b.Image))
                            .Select(b => (b, score: Score(b))).Where(x => x.score >= 0).ToList();
            if (ranked.Count == 0) return new List<BackgroundDef>();
            int best = ranked.Max(x => x.score);
            return ranked.Where(x => x.score == best).Select(x => x.b).ToList();
        }

        public static BackgroundDef Pick(string areaId, string trialId, string kind, IEnumerable<string> enemyIds, Random rng)
        {
            var top = Candidates(areaId, trialId, kind, enemyIds);
            if (top.Count == 0) return null;
            int roll = rng.Next(top.Sum(b => Math.Max(1, b.Weight)));
            foreach (var b in top)
            {
                roll -= Math.Max(1, b.Weight);
                if (roll < 0) return b;
            }
            return top[^1];
        }

        // ---- layers

        // Suffixes of automatically found layer files, and which depth (from _camera.json) each uses.
        public static readonly string[] LayerSuffixes = { "_Backdrop", "_Background", "_Middleground", "_Foreground" };

        private static bool StageFileExists(string name) =>
            name != null && System.IO.File.Exists(LiveJson.ArtPath(Path.Combine("Stages", name + ".png")));

        // Whether a file exists is cached (this runs every frame); the cache resets when backgrounds.json reloads.
        private static readonly Dictionary<string, bool> _exists = new();
        private static int _existsVersion = -1;

        private static bool Exists(string name)
        {
            if (_existsVersion != File.Version) { _exists.Clear(); _existsVersion = File.Version; }
            if (name == null) return false;
            if (!_exists.TryGetValue(name, out bool e)) _exists[name] = e = StageFileExists(name);
            return e;
        }

        // What to draw for a background, back to front: its listed Layers, else the <Image>_Backdrop /
        // _Background / _Middleground / _Foreground files that exist, else the single <Image> picture (which gets
        // CameraTuning.BackdropFollow as its depth).
        public static List<StageLayer> LayersFor(BackgroundDef b, CameraTuning t) => LayersFor(b?.Image, b, t);

        public static List<StageLayer> LayersFor(string image, BackgroundDef b, CameraTuning t)
        {
            var list = new List<StageLayer>();
            if (b?.Layers is { Count: > 0 })
            {
                foreach (var l in b.Layers.Where(l => Exists(l.Image)))
                    list.Add(new StageLayer("Stages/" + l.Image, l.Depth, l.OffsetY + b.OffsetY));
                return list.OrderBy(l => l.Depth).ToList();
            }
            if (image == null) return list;
            float[] depths = { t.BackdropDepth, t.BackgroundDepth, t.MiddlegroundDepth, t.ForegroundDepth };
            for (int i = 0; i < LayerSuffixes.Length; i++)
                if (Exists(image + LayerSuffixes[i]))
                    list.Add(new StageLayer("Stages/" + image + LayerSuffixes[i], depths[i], b?.OffsetY ?? 0));
            if (list.Count == 0 && Exists(image))
                list.Add(new StageLayer("Stages/" + image, t.BackdropFollow, b?.OffsetY ?? 0));
            return list;
        }

        // For the self-test: listed backgrounds with nothing to draw (no picture and no layer files),
        // and listed layer files that don't exist.
        public static IEnumerable<string> MissingImages()
        {
            foreach (var b in All)
            {
                if (b.Layers is { Count: > 0 })
                {
                    foreach (var l in b.Layers.Where(l => !StageFileExists(l.Image))) yield return $"{b.Image}: layer {l.Image}";
                }
                else if (!StageFileExists(b.Image) && !LayerSuffixes.Any(s => StageFileExists(b.Image + s)))
                    yield return b.Image;
            }
        }
    }

    // Lets a JSON field be written as one value ("city_outskirt") or a list (["a", "b"]).
    public sealed class OneOrManyConverter : JsonConverter<List<string>>
    {
        public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return new List<string>();
            if (reader.TokenType == JsonTokenType.String) return new List<string> { reader.GetString() };
            return JsonSerializer.Deserialize<List<string>>(ref reader, options) ?? new List<string>();
        }

        public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value, options);
    }
}
