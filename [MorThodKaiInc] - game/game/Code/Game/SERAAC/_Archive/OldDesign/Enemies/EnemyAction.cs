namespace SERAAC.Data.Enemies
{
    public class EnemyAction
    {
        public string ActionId { get; set; }
        public string Name { get; set; }
        public string TelegraphHint { get; set; }     // shown on HUD before it resolves
        public int Power { get; set; }
    }
}
