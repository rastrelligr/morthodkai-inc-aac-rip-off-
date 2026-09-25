namespace SERAAC.Data.Characters
{
    // The synced Wielder+Weapon pair — one 'unit' in battle & turn order.
    public class SoulBond
    {
        public string BondId { get; set; }
        public Wielder Wielder { get; set; }
        public Weapon Weapon { get; set; }
        public float SyncMeter { get; set; }          // fills when both sides succeed together
        public bool IsAlive { get; set; } = true;
    }
}
