
using MonoGame.Extended.Graphics;
using SERAAC;

namespace SERAAC.Data.Skills
{
    public class Skill
    {
        public string SkillId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public SpellPattern Pattern { get; set; }
        public int BasePower { get; set; }
        public BuffType? BuffOnSuccess { get; set; }
        public BuffType? DebuffOnEnemy { get; set; }
        public string WeaknessTag { get; set; }
        public Texture2DRegion Icon { get; set; } // จาก Texture2DAtlas ของพวก Icon หรือ UI
    }
}
