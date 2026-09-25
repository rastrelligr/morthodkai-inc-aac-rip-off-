using System;
using System.Collections.Generic;

namespace SERAAC.Data.Party
{
    // 'จิตใจเชื่อมหากัน' — party members are linked; one falling affects everyone.
    public class MindLink
    {
        public List<string> LinkedBondIds { get; set; } = new List<string>();
        public int? SharedHpPool { get; set; }        // optional alt HP model
        public bool FateShared { get; set; } = true;

        public void OnMemberDown(string bondId)
        {
            // TODO: propagate debuff / morale loss / shared-HP drain to rest of party
            throw new NotImplementedException();
        }

        public bool CheckPartyWipe(Party party)
        {
            // TODO: define wipe condition (all down, or link-severed threshold)
            throw new NotImplementedException();
        }
    }
}
