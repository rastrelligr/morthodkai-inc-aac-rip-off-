using MonoGame.Extended.Graphics;
using SERAAC.Data.Skills;

namespace SERAAC.Data.Characters
{
    public class Wielder
    {
        public string CharId { get; set; }
        public string Name { get; set; }
        public Role Role { get; } = Role.Wielder;
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public Texture2DRegion Portrait { get; set; }
        public SkillLoadout Skills { get; set; } = new SkillLoadout();
        public LevelProgress Progression { get; set; } = new LevelProgress();
        public MinigameState ActiveMinigame { get; set; }
        public string PairedWeaponId { get; set; }
    }
}
