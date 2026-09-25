using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SERAAC.Data;

namespace SERAAC.Meta
{
    public enum UpgradeStat { Hp, Damage, Speed }

    public sealed class CharacterProgress
    {
        public int HpLevel { get; set; }
        public int DamageLevel { get; set; }
        public int SpeedLevel { get; set; }
        public string Weapon { get; set; }
        public string Charm { get; set; }

        public int Level(UpgradeStat s) => s switch
        {
            UpgradeStat.Hp => HpLevel,
            UpgradeStat.Damage => DamageLevel,
            _ => SpeedLevel,
        };

        public void AddLevel(UpgradeStat s)
        {
            if (s == UpgradeStat.Hp) HpLevel++;
            else if (s == UpgradeStat.Damage) DamageLevel++;
            else SpeedLevel++;
        }
    }

    // Progress on one quest (see Data/QuestDb.cs). Saved immediately, like Main Objectives.
    public sealed class QuestState
    {
        public int Progress { get; set; }
        public bool Claimed { get; set; }
    }

    // Everything that survives between trials. Trial loot is NOT here until extraction.
    public sealed class Profile
    {
        public int SecuredShards { get; set; }
        public Dictionary<string, CharacterProgress> Characters { get; set; } = new();
        public List<string> Stash { get; set; } = new();             // unequipped equipment ids
        public List<string> PartyOrder { get; set; } = new();        // slot order, front first
        public List<string> CompletedObjectives { get; set; } = new();
        public List<string> UnlockedCharacters { get; set; } = new();
        public Dictionary<string, QuestState> Quests { get; set; } = new();
        public int TrialsStarted { get; set; }
        public int Extractions { get; set; }
        public int Defeats { get; set; }

        public static Profile CreateNew()
        {
            var p = new Profile
            {
                PartyOrder = new() { "relle", "warden", "hunter", "priest" },
                Stash = new() { "rusted_blade", "pale_locket" },
            };
            p.EnsureCharacters();
            return p;
        }

        public void EnsureCharacters()
        {
            foreach (var c in CharacterDb.All)
                if (!Characters.ContainsKey(c.Id)) Characters[c.Id] = new CharacterProgress();
            PartyOrder = PartyOrder.Where(id => CharacterDb.Get(id) != null).Distinct().Take(CharacterDb.PartySize).ToList();
            // Everyone who starts unlocked is always available, including characters added after this save was made.
            foreach (var c in CharacterDb.All.Where(c => c.StartsUnlocked && !UnlockedCharacters.Contains(c.Id)))
                UnlockedCharacters.Add(c.Id);
            PartyOrder = PartyOrder.Where(IsUnlocked).ToList();
        }

        public CharacterProgress Progress(string charId)
        {
            if (!Characters.TryGetValue(charId, out var p)) Characters[charId] = p = new CharacterProgress();
            return p;
        }

        public bool IsAreaUnlocked(AreaDef a) => a.UnlockedBy == null || CompletedObjectives.Contains(a.UnlockedBy);

        public bool IsUnlocked(string charId) => UnlockedCharacters.Contains(charId);

        public void Unlock(string charId)
        {
            if (!IsUnlocked(charId)) UnlockedCharacters.Add(charId);
            ProfileStore.Save(this);
        }

        public QuestState Quest(string questId) => Quests.TryGetValue(questId, out var q) ? q : null;
    }

    public static class ProfileStore
    {
        private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

        // Scripted capture runs point this at a temp folder so the real save is never touched.
        public static string OverrideDir;

        public static string SavePath => OverrideDir != null ? Path.Combine(OverrideDir, "profile.json") : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SERAAC", "profile.json");

        public static bool Exists => File.Exists(SavePath);

        // The self-test turns saving off so simulated runs never touch the real save.
        public static bool Enabled = true;

        public static Profile Load()
        {
            try
            {
                if (File.Exists(SavePath))
                {
                    var p = JsonSerializer.Deserialize<Profile>(File.ReadAllText(SavePath), Options);
                    if (p != null)
                    {
                        p.EnsureCharacters();
                        return p;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Save] could not read profile, starting fresh: {e.Message}");
            }
            return Profile.CreateNew();
        }

        public static void Save(Profile p)
        {
            if (!Enabled) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SavePath)!);
                File.WriteAllText(SavePath, JsonSerializer.Serialize(p, Options));
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Save] failed: {e.Message}");
            }
        }

        public static void Delete()
        {
            if (File.Exists(SavePath)) File.Delete(SavePath);
        }
    }
}
