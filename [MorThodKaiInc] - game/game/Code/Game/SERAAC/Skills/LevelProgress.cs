using System.Collections.Generic;

namespace SERAAC.Data.Skills
{
    public class LevelProgress
    {
        public int Level { get; set; } = 1;
        public int Exp { get; set; }
        public int ExpToNext { get; set; } = 100;
        public int SkillPoints { get; set; }
        public SkillChoiceEvent PendingSkillChoice { get; set; }
        public List<Skill> LearnedSkillPool { get; set; } = new List<Skill>();

        public void CalculateEXP()
        {
            ExpToNext = 100 * (int)(Level * 1.5f);
        }
    }
}
