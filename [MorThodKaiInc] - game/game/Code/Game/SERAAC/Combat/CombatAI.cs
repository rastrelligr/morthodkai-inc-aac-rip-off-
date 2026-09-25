using System;
using System.Collections.Generic;
using System.Linq;
using SERAAC.Data;

namespace SERAAC.Combat
{
    // Picks an action + target for a combatant. Used for every enemy each turn, and for
    // the party too when the self-test auto-plays battles.
    public static class CombatAI
    {
        public static void PlanAll(Battle b, IEnumerable<Combatant> units, Random rng)
        {
            foreach (var c in units.ToList())
                if (c.Alive) c.Plan = Choose(b, c, rng);
        }

        public static PlannedAction Choose(Battle b, Combatant c, Random rng)
        {
            var usable = c.Actions.Where(a => b.CanUse(c, a)).ToList();
            var allies = b.Allies(c);
            bool slotAheadEmpty = c.Slot > 0 && allies[c.Slot - 1] == null;
            bool wantsFront = c.IsParty ? c.Actions.Any(a => a.Kind == ActionKind.Attack && a.UsableFrom.Has(0))
                                        : c.Enemy?.Row == EnemyRow.Front;

            var advance = usable.FirstOrDefault(a => a.Kind == ActionKind.Move && a.MoveBy < 0);
            bool canAct = usable.Any(a => a.Kind == ActionKind.Attack || a.Kind == ActionKind.Heal);

            // Close gaps left by the dead, and get back in reach if nothing can be hit from here.
            if (advance != null && ((!canAct) || (slotAheadEmpty && wantsFront && rng.NextDouble() < 0.7)))
                return new PlannedAction { Action = advance };

            var weighted = new List<(ActionDef a, double w)>();
            float hpRatio = (float)c.Hp / c.MaxHp;
            bool allyHurt = allies.Any(x => x != null && x.Alive && (float)x.Hp / x.MaxHp < 0.6f);

            foreach (var a in usable)
            {
                double w = a.Kind switch
                {
                    ActionKind.Attack => (a.AreaEffect ? 1.0 + b.ValidTargets(c, a).Count : 3.0) * (1.0 + a.SpCost * 0.4),
                    ActionKind.Heal => allyHurt ? 5.0 : 0.0,
                    ActionKind.Defend => hpRatio < 0.35f ? 1.0 : 0.15,
                    ActionKind.Taunt => allyHurt ? 2.5 : 0.8,
                    _ => 0.0,
                };
                if (a.Applies == StatusKind.Mark && b.ValidTargets(c, a).All(t => t.Has(StatusKind.Mark))) w *= 0.2;
                if (a.Applies == StatusKind.Blight) w *= 0.8;
                if (w > 0) weighted.Add((a, w));
            }

            if (weighted.Count == 0)
            {
                var fallback = usable.FirstOrDefault() ?? CommonActions.Defend;
                return new PlannedAction { Action = fallback };
            }

            var chosen = Pick(weighted, rng);
            return new PlannedAction { Action = chosen, Target = PickTarget(b, c, chosen, rng) };
        }

        private static Combatant PickTarget(Battle b, Combatant c, ActionDef a, Random rng)
        {
            if (!a.IsSingleTarget) return null;
            var targets = b.ValidTargets(c, a);
            if (targets.Count == 0) return null;

            if (a.Kind == ActionKind.Heal)
                return targets.OrderBy(t => (float)t.Hp / t.MaxHp).First();

            // Prefer wounded and Marked targets, with some randomness so it isn't predictable.
            var weighted = targets.Select(t => (t, 1.0 + (1.0 - (double)t.Hp / t.MaxHp) * 2.0 + (t.Has(StatusKind.Mark) ? 1.5 : 0)
                                                     - (t.Has(StatusKind.Guard) ? 0.5 : 0))).ToList();
            return Pick(weighted, rng);
        }

        private static T Pick<T>(List<(T item, double w)> items, Random rng)
        {
            double total = items.Sum(i => Math.Max(0.01, i.w));
            double r = rng.NextDouble() * total;
            foreach (var (item, w) in items)
            {
                r -= Math.Max(0.01, w);
                if (r <= 0) return item;
            }
            return items[^1].item;
        }
    }
}
