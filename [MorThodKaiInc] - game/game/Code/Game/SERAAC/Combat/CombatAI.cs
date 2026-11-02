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
            // A wound-up attack is always unleashed the turn after.
            if (c.WindUpNext != null)
                return new PlannedAction { Action = c.WindUpNext, Target = PickTarget(b, c, c.WindUpNext, rng) };

            var usable = c.Actions.Where(a => b.CanUse(c, a)).ToList();
            var foeCharging = b.Foes(c).Any(f => f != null && f.Alive && f.WindUpNext != null);
            var allies = b.Allies(c);
            bool slotAheadEmpty = c.Slot > 0 && allies[c.Slot - 1] == null;
            bool wantsFront = c.IsParty ? c.Actions.Any(a => a.Kind == ActionKind.Attack && a.UsableFrom.Has(0))
                                        : c.Enemy?.Row == EnemyRow.Front;

            bool canAct = CanActFrom(b, c, c.Slot);
            // Step up when nothing can be hit from here, or (from the back row) to fill a gap the dead left in front.
            // Already in reach from Front 2, stepping into an empty Front 1 would only waste the turn.
            bool moveUp = !canAct || (slotAheadEmpty && wantsFront && c.Slot >= 2 && rng.NextDouble() < 0.7);
            // Stepping up swaps with whoever stands ahead. Never push an ally out of the only spot it can attack
            // from: two melee enemies would otherwise trade places every turn and both lose their actions.
            var ahead = c.Slot > 0 ? allies[c.Slot - 1] : null;
            bool swapOk = ahead == null || !ahead.Alive || !CanActFrom(b, ahead, ahead.Slot) || CanActFrom(b, ahead, c.Slot);

            // Close gaps left by the dead, and get back in reach if nothing can be hit from here.
            // Enemies step forward one slot; the party uses Position to step into the slot ahead.
            var step = usable.FirstOrDefault(a => a.Kind == ActionKind.Move && a.MoveBy < 0);
            if (step != null && moveUp && swapOk)
                return new PlannedAction { Action = step };
            var swap = usable.FirstOrDefault(a => a.Kind == ActionKind.Swap);
            if (swap != null && moveUp && swapOk && c.Slot > 0)
                return new PlannedAction { Action = swap, TargetSlot = c.Slot - 1, Target = allies[c.Slot - 1] };

            var weighted = new List<(ActionDef a, double w)>();
            float hpRatio = (float)c.Hp / c.MaxHp;
            bool allyHurt = allies.Any(x => x != null && x.Alive && (float)x.Hp / x.MaxHp < 0.6f);
            var type = c.Enemy?.TypeDef;

            foreach (var a in usable)
            {
                double w = a.Kind switch
                {
                    ActionKind.Attack => (a.AreaEffect ? 1.0 + b.ValidTargets(c, a).Count : 3.0) * (1.0 + a.SpCost * 0.4),
                    ActionKind.Heal => allyHurt ? 5.0 : 0.0,
                    // Brace when a foe is winding up (Guard acts first), otherwise only when low.
                    ActionKind.Defend => foeCharging ? (hpRatio < 0.6f ? 4.0 : 1.5) : hpRatio < 0.35f ? 1.0 : 0.15,
                    // Bosses rear up every few turns (more often once enraged).
                    ActionKind.WindUp => b.Turn - c.LastWindUpTurn >= (c.Enraged ? 2 : 3) ? 14.0 : 0.0,
                    ActionKind.Taunt => allyHurt ? 2.5 : 0.8,
                    // Gather energy when there's an SP skill worth saving for.
                    ActionKind.Charge => c.Actions.Any(x => x.SpCost > c.Sp) ? 0.6 : 0.0,
                    // Mara: shield a hurt ally; wall up when several allies are hurt.
                    ActionKind.Protect => allyHurt ? 2.0 : 0.3,
                    ActionKind.Wall => foeCharging ? 6.0 : allies.Count(x => x != null && x.Alive && (float)x.Hp / x.MaxHp < 0.6f) >= 2 ? 2.5 : 0.3,
                    // Elian: pull a badly hurt front-liner back toward him.
                    ActionKind.Reweave => allies.Any(x => x != null && x.Alive && x != c && x.Slot < c.Slot && (float)x.Hp / x.MaxHp < 0.35f) ? 1.5 : 0.0,
                    _ => 0.0,
                };
                if (a.Applies == StatusKind.Mark && b.ValidTargets(c, a).All(t => t.Has(StatusKind.Mark))) w *= 0.2;
                if (a.Applies == StatusKind.Blight) w *= 0.8;
                // Iron Rebuke: cash Guard in when it's full, rarely when there's nothing to spend.
                if (a.PowerPerAegis > 0) w *= c.Aegis >= Battle.AegisThreshold ? 1.6 : c.Aegis == 0 ? 0.15 : 0.6;
                // Snare Line: worth it when an enemy is out of the front row (above all a winding-up boss).
                if (a.PullForward > 0) w *= b.Foes(c).Any(f => f != null && f.Alive && f.Slot >= 2 && f.WindUpNext != null) ? 3.0
                                          : b.Foes(c).Any(f => f != null && f.Alive && f.Slot >= 2) ? 0.6 : 0.1;
                // Interrupting hits: top choice when they can reach a foe that is winding up.
                if (a.Interrupts && b.ValidTargets(c, a).Any(t => t.WindUpNext != null)) w *= 5.0;
                // Crowd-control fighters (Read the Opening: Naomi) weigh what their attacks set up, not just damage.
                // Only for them: the rest of the party plays (and was balanced) on damage.
                if (c.IsParty && c.Has(PassiveKind.Exploit) && a.Kind == ActionKind.Attack) w *= ControlValue(b, c, a);
                if (type != null)
                {
                    // Enemy Type: favour attacks that reach the slots this type likes to hit.
                    if (a.Kind == ActionKind.Attack && a.Side == TargetSide.Enemy)
                    {
                        var reach = b.ValidTargets(c, a);
                        if (reach.Count > 0) w *= 0.5 + reach.Max(t => type.SlotPreference[t.Slot]) / type.SlotPreference.Max();
                    }
                    if (a.Kind == ActionKind.Defend) w *= type.DefendBias;
                }
                if (w > 0) weighted.Add((a, w));
            }

            if (weighted.Count == 0)
            {
                // Nothing worth doing: brace rather than take a move that was ruled out above (a swap that would
                // push an ally out of reach).
                var fallback = usable.FirstOrDefault(a => a.Kind == ActionKind.Defend) ?? CommonActions.Defend;
                return new PlannedAction { Action = fallback };
            }

            var chosen = Pick(weighted, rng);
            return new PlannedAction { Action = chosen, Target = PickTarget(b, c, chosen, rng) };
        }

        // How much an attack's control effects are worth right now (1 = plain damage; it only ever adds, so
        // damage dealers keep playing the way they did):
        //   Slow     on foes that aren't Slowed yet, more when they are faster than the party's slowest member
        //   push     front-row foes knocked back that can't attack from one slot further back lose their next turn
        //   Mark     an area Mark on several unmarked foes sets up the whole party
        //   Exploit  (Read the Opening) hitting foes that are already Slowed, Marked or Blighted
        private static double ControlValue(Battle b, Combatant c, ActionDef a)
        {
            var reach = b.ValidTargets(c, a);
            if (reach.Count == 0) return 1.0;
            var hit = a.AreaEffect ? reach : reach.OrderBy(t => t.Slot).Take(1).ToList();
            double v = 1.0;

            if (a.Applies == StatusKind.Slow)
            {
                int fresh = hit.Count(t => !t.Has(StatusKind.Slow));
                int slowest = b.Allies(c).Where(x => x != null && x.Alive).Min(x => x.EffectiveSpeed);
                bool outpaces = reach.Any(t => !t.Has(StatusKind.Slow) && t.EffectiveSpeed >= slowest);
                if (fresh > 0) v *= outpaces ? 1.3 : 1.1;
            }

            if (a.KnockBack > 0)
            {
                int stalled = hit.Count(t => t.Slot + a.KnockBack < Battle.Slots && t.Enemy?.TypeDef?.Steadfast != true
                                            && !CanActFrom(b, t, Math.Min(Battle.Slots - 1, t.Slot + a.KnockBack)));
                v *= 1.0 + 0.5 * stalled;
            }

            if (a.Applies == StatusKind.Mark && a.AreaEffect)
            {
                int unmarked = hit.Count(t => !t.Has(StatusKind.Mark));
                if (unmarked >= 2) v *= 1.0 + 0.1 * unmarked;
            }

            if (c.Has(PassiveKind.Exploit))
            {
                int opened = hit.Count(t => t.Has(StatusKind.Slow) || t.Has(StatusKind.Mark) || t.Has(StatusKind.Blight));
                if (opened > 0) v *= 1.0 + 0.3 * opened / hit.Count;
            }
            return v;
        }

        // Whether u has an attack or heal it could use standing in `slot` (SP and targets included).
        private static bool CanActFrom(Battle b, Combatant u, int slot) =>
            u.Actions.Any(a => a.Kind is ActionKind.Attack or ActionKind.Heal && a.UsableFrom.Has(slot) && u.Sp >= a.SpCost
                               && b.ValidTargets(u, a).Count > 0);

        private static Combatant PickTarget(Battle b, Combatant c, ActionDef a, Random rng)
        {
            if (!a.IsSingleTarget) return null;
            var targets = b.ValidTargets(c, a);
            if (targets.Count == 0) return null;

            if (a.Kind is ActionKind.Heal or ActionKind.Protect or ActionKind.Reweave)
                return targets.OrderBy(t => (float)t.Hp / t.MaxHp).First();

            // Interrupt or drag forward whoever is winding up.
            var charging = targets.FirstOrDefault(t => t.WindUpNext != null);
            if (charging != null && (a.Interrupts || a.PullForward > 0)) return charging;

            // Prefer wounded and Marked targets, with some randomness so it isn't predictable.
            // Enemies also weigh each slot by their Type's Target Preference (Brutes: front, Stalkers: back).
            var type = c.Enemy?.TypeDef;
            double wounded = type?.WoundedBias ?? 2.0;
            // Read the Opening (Naomi) prefers foes that are already Slowed, Marked or Blighted.
            double Control(Combatant t) =>
                c.IsParty && c.Has(PassiveKind.Exploit) && (t.Has(StatusKind.Slow) || t.Has(StatusKind.Mark) || t.Has(StatusKind.Blight)) ? 1.0 : 0;
            var weighted = targets.Select(t => (t, (1.0 + (1.0 - (double)t.Hp / t.MaxHp) * wounded + (t.Has(StatusKind.Mark) ? 1.5 : 0)
                                                     - (t.Has(StatusKind.Guard) ? 0.5 : 0) + Control(t))
                                                    * (type != null && t.IsParty == !c.IsParty ? type.SlotPreference[t.Slot] : 1.0))).ToList();
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
