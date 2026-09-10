namespace SERAAC.Data.Characters
{
    public class Weapon
    {
        public string WeaponId { get; set; }
        public string Name { get; set; }
        public Role Role { get; } = Role.Weapon;
        public string WeaponType { get; set; }       // sword / scythe / gun / etc.
        public BuffType? PassiveBonus { get; set; }
        public MinigameState ActiveMinigame { get; set; }
        public string PairedWielderId { get; set; }
    }
}
