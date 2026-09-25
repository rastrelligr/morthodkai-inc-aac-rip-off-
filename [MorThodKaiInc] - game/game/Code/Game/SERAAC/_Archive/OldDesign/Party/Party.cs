using System;
using System.Collections.Generic;
using SERAAC.Data.Characters;

namespace SERAAC.Data.Party
{
    public class Party
    {
        public List<SoulBond> Bonds { get; set; } = new List<SoulBond>();
        public MindLink MindLink { get; set; } = new MindLink();
        public List<string> Inventory { get; set; } = new List<string>(); // item ids

        public SoulBond GetBond(string bondId)
        {
            // TODO: Bonds.Find(b => b.BondId == bondId)
            throw new NotImplementedException();
        }
    }
}
