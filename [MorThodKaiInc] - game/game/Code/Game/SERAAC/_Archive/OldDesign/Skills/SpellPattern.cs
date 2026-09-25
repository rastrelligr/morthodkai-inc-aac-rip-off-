using SERAAC;
using System.Collections.Generic;

namespace SERAAC.Data.Skills
{
    public class SpellPattern
    {
        public List<string> WielderSequence { get; set; } = new List<string>();
        public List<string> WeaponRuneLayout { get; set; } = new List<string>();
        public MinigameType MinigameType { get; set; } = MinigameType.SpellTyping;
    }
}
