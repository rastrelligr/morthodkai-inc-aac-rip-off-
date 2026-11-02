using System;
using System.IO;
using System.Text.Json;
using SERAAC.Meta;

namespace SERAAC.Core
{
    // Player options (Screens/OptionsScreen.cs), saved next to the save file but separate from it, so
    // "New Game (erase save)" keeps them.
    public sealed class Settings
    {
        public float MusicVolume { get; set; } = 0.25f;
        public float SfxVolume { get; set; } = 0.7f;
        // Dialogue helper: tags each choice with its tone (kind, bold, ask, flirt) and shows how the squad took
        // what Relle said ("Mara approves"). Off by default: players read the characters themselves.
        public bool DialogueHelper { get; set; }

        public static Settings Current { get; private set; } = new();

        private static string PathFor => Path.Combine(Path.GetDirectoryName(ProfileStore.SavePath)!, "settings.json");

        public static void Load()
        {
            try
            {
                if (File.Exists(PathFor)) Current = JsonSerializer.Deserialize<Settings>(File.ReadAllText(PathFor)) ?? new Settings();
            }
            catch (Exception e) { Console.WriteLine($"[Settings] could not read, using defaults: {e.Message}"); }
            Apply();
        }

        public static void Save()
        {
            Apply();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PathFor)!);
                File.WriteAllText(PathFor, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception e) { Console.WriteLine($"[Settings] save failed: {e.Message}"); }
        }

        public static void Apply()
        {
            Current.MusicVolume = Math.Clamp(Current.MusicVolume, 0f, 1f);
            Current.SfxVolume = Math.Clamp(Current.SfxVolume, 0f, 1f);
            Audio.MusicVolume = Current.MusicVolume;
            Audio.SfxVolume = Current.SfxVolume;
        }
    }
}
