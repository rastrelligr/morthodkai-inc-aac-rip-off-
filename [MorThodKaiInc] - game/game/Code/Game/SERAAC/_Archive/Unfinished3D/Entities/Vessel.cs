namespace SERAAC.Entities
{
    // The body half of the SoulBond. Player 1 controls this: movement and defense.
    public class Vessel
    {
        public string Name { get; set; } = "Vessel";
        public int Hp { get; set; } = 30;
        public int MaxHp { get; set; } = 30;
        public int Defense { get; set; } = 2;
        public bool IsGuarding { get; set; }

        public bool IsAlive => Hp > 0;

        public void TakeDamage(int amount)
        {
            int mitigated = IsGuarding ? amount / 2 : amount;
            Hp = System.Math.Max(0, Hp - System.Math.Max(0, mitigated));
        }

        public void Heal(int amount) => Hp = System.Math.Min(MaxHp, Hp + amount);
    }
}
