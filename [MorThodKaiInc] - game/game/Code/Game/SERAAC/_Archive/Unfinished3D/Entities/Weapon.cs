namespace SERAAC.Entities
{
    // The attack half of the SoulBond. Player 2 controls this: aiming and attacking.
    public class Weapon
    {
        public string Name { get; set; } = "Rusty Blade";
        public string WeaponType { get; set; } = "Sword";
        public int BaseDamage { get; set; } = 8;
        public int WeakPointBonus { get; set; } = 10;
    }
}
