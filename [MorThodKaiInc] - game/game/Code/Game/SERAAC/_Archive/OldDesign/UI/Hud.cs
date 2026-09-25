using System.Collections.Generic;
using MonoGame.Extended.BitmapFonts;
using SERAAC.Data.Enemies;

namespace SERAAC.Data.UI
{
    public class Hud
    {
        public Dictionary<string, (int hp, int maxHp)> PartyHpDisplay { get; set; } = new Dictionary<string, (int, int)>();
        public Dictionary<string, (int hp, int maxHp)> EnemyHpDisplay { get; set; } = new Dictionary<string, (int, int)>();
        public string SpellHintText { get; set; } = "";
        public List<BuffInstance> CurrentBuffs { get; set; } = new List<BuffInstance>();
        public List<BuffInstance> CurrentDebuffs { get; set; } = new List<BuffInstance>();
        public string SkillDescription { get; set; }
        public string EnemyWeaknessHint { get; set; }
        public BitmapFont Font { get; set; }          // MonoGame.Extended bitmap font for HUD text
    }
}
