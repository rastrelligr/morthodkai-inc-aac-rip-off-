using System.Collections.Generic;
using System.Linq;
using SERAAC.Data;

namespace SERAAC.Meta
{
    public enum PlaytestStrength { Fresh, Veteran, Maxed }

    // A playtest session (Title > Playtest): a throwaway profile with everyone unlocked and saving switched off,
    // so a tester can jump straight to one part of the game without touching their real save.
    // The Playtest menu starts it; the title screen ends it and puts the real profile back.
    public static class Playtest
    {
        public static bool Active { get; private set; }
        public static string LastResult;   // shown on the Playtest menu after a test

        private static Profile _real;

        public static void Begin(Profile sandbox)
        {
            if (!Active)
            {
                _real = Game1.Instance.Profile;
                Active = true;
            }
            ProfileStore.Enabled = false;
            Game1.Instance.Profile = sandbox;
        }

        public static void Exit()
        {
            if (!Active) return;
            Active = false;
            Game1.Instance.Profile = _real;
            ProfileStore.Enabled = true;
        }

        // Everyone unlocked, every item in the stash, plenty of shards; upgrades / gear by strength.
        public static Profile MakeProfile(IEnumerable<string> party, PlaytestStrength strength)
        {
            var p = Profile.CreateNew();
            p.UnlockedCharacters = CharacterDb.All.Select(c => c.Id).ToList();
            p.PartyOrder = party.Where(id => id != null).Distinct().Take(CharacterDb.PartySize).ToList();
            p.SecuredShards = 9999;
            p.Stash = ItemDb.Equipment.Select(e => e.Id).ToList();
            foreach (var c in CharacterDb.All)
            {
                var prog = p.Progress(c.Id);
                if (strength == PlaytestStrength.Fresh) continue;
                bool max = strength == PlaytestStrength.Maxed;
                prog.HpLevel = max ? Progression.MaxLevel : 3;
                prog.DamageLevel = max ? Progression.MaxLevel : 3;
                prog.SpeedLevel = max ? Progression.MaxLevel : 2;
                prog.Weapon = "bonded_fang";   // the same kit the balance test's veteran party uses
                prog.Charm = "pale_locket";
            }
            return p;
        }
    }
}
