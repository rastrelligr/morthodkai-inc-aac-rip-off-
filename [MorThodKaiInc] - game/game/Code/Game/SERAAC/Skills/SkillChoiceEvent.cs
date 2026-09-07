namespace SoulLinkGame.Data.Skills
{
    public class SkillChoiceEvent
    {
        public Skill CandidateSkill { get; set; }
        public SkillLoadout CurrentLoadout { get; set; }
        public bool Resolved { get; set; }
    }
}
