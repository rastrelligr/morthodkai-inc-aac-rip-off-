using System;
using System.Collections.Generic;

namespace SERAAC.Data.Skills
{
    public class SkillLoadout
    {
        public const int SlotCount = 4;// fixed 4 slot ตอนเริ่ม แต่อาจจะเพิ่มในอนาคต
        public Skill[] Slots { get; set; } = new Skill[SlotCount];

        public bool LearnNewSkill(Skill newSkill, int? replaceIndex = null)
        {
            // TODO: if replaceIndex is null -> discard newSkill
            // TODO: else Slots[replaceIndex.Value] = newSkill
            throw new NotImplementedException();
        }

        public List<Skill> GetActiveSkills()
        {
            // TODO: return non-null entries in Slots as a List<Skill>
            throw new NotImplementedException();
        }
    }
}
