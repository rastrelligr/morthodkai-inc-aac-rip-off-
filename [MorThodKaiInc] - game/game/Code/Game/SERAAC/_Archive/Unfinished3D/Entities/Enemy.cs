namespace SERAAC.Entities
{
    // A 3x3 combat grid has 9 cells (0-8, row-major). Enemies expose a WeakCell that
    // the Weapon must aim at to land the bonus "exploit the weak point" damage.
    public class Enemy
    {
        public string Name { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public int AttackDamage { get; set; } = 4;
        public double HitChance { get; set; } = 0.65;
        public int WeakCell { get; set; }

        public bool IsAlive => Hp > 0;

        public void TakeDamage(int amount) => Hp = System.Math.Max(0, Hp - System.Math.Max(0, amount));
    }
}
