using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using SERAAC.Data;
using SERAAC.Trial;

namespace SERAAC.Combat
{
    public sealed class StatusInstance
    {
        public StatusKind Kind;
        public int Turns;   // Guard/Taunt ignore this: they last until the owner acts again
        public int Power;
    }

    public sealed class PlannedAction
    {
        public ActionDef Action;
        public Combatant Target; // null for self / area actions
    }

    public sealed class Combatant
    {
        public string Name;
        public bool IsParty;
        public int MaxHp, Hp, Damage, Speed;
        public Color Color;
        public float Scale = 1f;
        public string ArtId;
        public List<ActionDef> Actions;
        public int Slot;                  // 0..3, front first
        public int Order;                 // stable tie-break for equal speed
        public readonly List<StatusInstance> Statuses = new();
        public PlannedAction Plan;

        public int Sp, MaxSp;                 // skill points (MaxSp 0 = unit does not use SP)
        public int Feast;                     // Feast passive stacks
        public float CritChance = Battle.BaseCritChance;
        public List<PassiveDef> Passives = new();

        public PartyMember Member;        // party side only: HP is written back after combat
        public EnemyDef Enemy;            // enemy side only

        public bool Alive => Hp > 0;
        public bool Has(PassiveKind k) => Passives.Any(p => p.Kind == k);

        public bool Has(StatusKind k) => Statuses.Any(s => s.Kind == k);

        public void AddStatus(StatusKind kind, int turns, int power)
        {
            var existing = Statuses.FirstOrDefault(s => s.Kind == kind);
            if (existing != null)
            {
                existing.Turns = System.Math.Max(existing.Turns, turns);
                existing.Power = System.Math.Max(existing.Power, power);
            }
            else Statuses.Add(new StatusInstance { Kind = kind, Turns = turns, Power = power });
        }

        public static Combatant FromMember(PartyMember m) => new()
        {
            Name = m.Def.Name, IsParty = true, MaxHp = m.MaxHp, Hp = m.Hp, Damage = m.Damage, Speed = m.Speed,
            Color = m.Def.Color, ArtId = m.Def.ArtId, Actions = m.Def.AllActions.ToList(), Member = m,
            MaxSp = m.Def.MaxSp, Sp = System.Math.Min(m.Def.MaxSp, Battle.StartingSp), Passives = m.Def.Passives,
        };

        // Depth/tier scaling keeps later rooms and harder trials dangerous.
        public static Combatant FromEnemy(EnemyDef e, float statScale)
        {
            int hp = (int)System.Math.Round(e.Hp * statScale);
            var actions = new List<ActionDef>(e.Skills);
            if (!actions.Contains(CommonActions.Advance)) actions.Add(CommonActions.Advance);
            return new Combatant
            {
                Name = e.Name, IsParty = false, MaxHp = hp, Hp = hp,
                Damage = (int)System.Math.Round(e.Damage * (1f + (statScale - 1f) * 0.6f)),
                Speed = e.Speed, Color = e.Color, Scale = e.Scale, ArtId = e.ArtId, Actions = actions, Enemy = e,
            };
        }
    }
}
