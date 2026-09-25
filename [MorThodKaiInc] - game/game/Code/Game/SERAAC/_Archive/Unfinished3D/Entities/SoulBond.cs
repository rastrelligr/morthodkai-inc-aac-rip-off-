namespace SERAAC.Entities
{
    // The single character both players share: Player 1 drives the Vessel, Player 2 drives the Weapon.
    public class SoulBond
    {
        public Vessel Vessel { get; set; } = new Vessel();
        public Weapon Weapon { get; set; } = new Weapon();

        // Rises when both players act well together in the same round (coordination pillar).
        public float SyncMeter { get; set; }

        public bool IsAlive => Vessel.IsAlive;

        public void AddSync(float amount) => SyncMeter = System.Math.Clamp(SyncMeter + amount, 0f, 100f);
    }
}
