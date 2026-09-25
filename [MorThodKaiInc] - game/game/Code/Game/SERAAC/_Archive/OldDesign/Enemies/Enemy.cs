using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;

namespace SERAAC.Data.Enemies
{
    public class Enemy
    {
        public string EnemyId { get; set; }
        public string Name { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public List<string> WeaknessTags { get; set; } = new List<string>();
        public List<BuffInstance> ActiveEffects { get; set; } = new List<BuffInstance>();
        public Queue<EnemyAction> ActionQueue { get; set; } = new Queue<EnemyAction>(); // queued intent pattern
        public Texture2D Sprite { get; set; }
        public bool IsAlive { get; set; } = true;
    }
}
