using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using SERAAC.Core;
using SERAAC.Data;
using SERAAC.Meta;
using SERAAC.UI;

namespace SERAAC.Story
{
    // How the squad feels about Relle. Every squadmate keeps a rapport score that moves when they react to
    // something she says or does; each reaction shows as a notice on screen (VnReactions). Flirting marks a
    // romance interest. Conversations (Story/SquadTalk.cs) unlock one at a time as the main story moves on,
    // like talking to the crew between missions: one talk per squadmate per stop, then they have nothing new.
    //
    // Pillow (knight) is a developer cameo and is left out of all of this.
    public static class Squad
    {
        public const string Mara = "warden", Elian = "priest", Naomi = "naomi", Pillow = "knight";
        public static readonly string[] Members = { Mara, Elian, Naomi };

        public enum Reaction { Approve, Disapprove, Remember, Romance }

        private static Profile Profile => Game1.Instance.Profile;

        public static string Name(string id) => CharacterDb.Get(id)?.Name ?? id;

        public static int Rapport(string id) => Profile.Rapport.TryGetValue(id, out var v) ? v : 0;

        // Moves rapport and shows how they took it. delta 0 with Remember: no score change, but it is noted.
        public static void React(string id, int delta, Reaction? shown = null)
        {
            Profile.Rapport[id] = Rapport(id) + delta;
            ProfileStore.Save(Profile);
            var kind = shown ?? (delta >= 0 ? Reaction.Approve : Reaction.Disapprove);
            string name = Name(id);
            switch (kind)
            {
                case Reaction.Approve: VnReactions.Post(id, $"{name} approves", Palette.KwHeal, "ui_speed"); break;
                case Reaction.Disapprove: VnReactions.Post(id, $"{name} disapproves", Palette.Coral, "ui_damage", "ui_back"); break;
                case Reaction.Remember: VnReactions.Post(id, $"{name} will remember that", Palette.KwPriority, "ui_objective"); break;
                case Reaction.Romance: VnReactions.Post(id, $"{name} liked that", Palette.Rose, "ui_hp", "secured"); break;
            }
        }

        public static Action Reacts(string id, int delta, Reaction? shown = null) => () => React(id, delta, shown);

        public static bool Interested(string id) => Profile.RomanceInterest.Contains(id);

        public static void Flirt(string id)
        {
            if (!Profile.RomanceInterest.Contains(id)) Profile.RomanceInterest.Add(id);
            React(id, 1, Reaction.Romance);
        }

        // The squadmate closest to Relle (highest rapport, ties go to romance interest), or null if nobody is above 0.
        public static string Closest() => Members
            .Where(id => Rapport(id) > 0)
            .OrderByDescending(Rapport).ThenByDescending(id => Interested(id) ? 1 : 0)
            .FirstOrDefault();

        public static string Bond(string id) => Rapport(id) switch
        {
            <= -2 => "Cold",
            < 1 => "Neutral",
            < 3 => "Friendly",
            _ => "Close",
        };

        // One talk per squadmate per story stop.
        public static bool Talked(string id, string stop) => Profile.StoryFlags.Contains($"talk:{id}:{stop}");

        public static void MarkTalked(string id, string stop)
        {
            if (!Talked(id, stop)) Profile.StoryFlags.Add($"talk:{id}:{stop}");
            ProfileStore.Save(Profile);
        }

        public static bool Flag(string flag) => Profile.StoryFlags.Contains(flag);

        public static void SetFlag(string flag)
        {
            if (!Profile.StoryFlags.Contains(flag)) Profile.StoryFlags.Add(flag);
            ProfileStore.Save(Profile);
        }
    }
}
