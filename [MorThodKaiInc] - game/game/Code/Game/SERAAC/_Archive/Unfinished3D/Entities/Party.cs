using System.Collections.Generic;

namespace SERAAC.Entities
{
    // Holds the run's progress. One SoulBond today; List<> leaves room for a second pair later.
    public class Party
    {
        public List<SoulBond> Bonds { get; } = new List<SoulBond>();

        public SoulBond Primary => Bonds.Count > 0 ? Bonds[0] : null;

        public static Party CreateNewRun()
        {
            var party = new Party();
            party.Bonds.Add(new SoulBond());
            return party;
        }
    }
}
