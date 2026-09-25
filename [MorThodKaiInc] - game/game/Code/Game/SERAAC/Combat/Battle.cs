using System;
using System.Collections.Generic;
using System.Linq;
using SERAAC.Data;

namespace SERAAC.Combat
{
    public enum BattleEventType { ActionStart, NextHit, Damage, Heal, Status, Cleanse, Move, Death, Fizzle, Info, Passive }
    public enum BattleOutcome { Ongoing, Victory, Defeat }

    // One thing that happened while resolving; the combat screen animates these in order.
    public sealed class BattleEvent
    {
        public BattleEventType Type;
        public Combatant Actor;
        public Combatant Target;
        public int Amount;
        public StatusKind Status;
        public string Text;
        public bool Crit;
        public ActionDef Action;   // ActionStart: the action being used (for animation)
    }

    // Pure combat rules (no drawing, no input): formation, targeting, speed-ordered resolution.
    //   Turn: Choose Action (player plans every party member, AI plans enemies)
    //         -> Resolve in Speed order -> End of turn ticks -> next Turn.
    public sealed class Battle
    {
        public const int Slots = 4;
        public const float MarkMultiplier = 1.35f;
        public const float GuardMultiplier = 0.5f;
        public const float BaseCritChance = 0.05f;
        public const float CritMultiplier = 1.5f;
        public const int StartingSp = 1;
        public const int SpPerTurn = 1;
        public const float PositionPassiveMultiplier = 1.25f; // Frontline / Backline
        public const float FeastLifesteal = 0.2f;              // fraction of each hit healed back
        public const int FeastThreshold = 3;
        public const float ToughMultiplier = 0.8f;             // Thick Hide: damage taken
        public const float DevotionMultiplier = 1.3f;          // Devotion: healing done
        public const float DeadeyeCritBonus = 0.2f;
        public const float ExploitMultiplier = 1.3f;           // vs Blighted or Marked targets

        public readonly Combatant[] Party = new Combatant[Slots];
        public readonly Combatant[] Enemies = new Combatant[Slots];
        public readonly List<Combatant> Fallen = new();
        public int Turn { get; private set; } = 1;

        private readonly Random _rng;

        public Battle(IEnumerable<Combatant> party, IEnumerable<Combatant> enemies, Random rng)
        {
            _rng = rng;
            int order = 0;
            Place(Party, party, ref order);
            Place(Enemies, enemies, ref order);
        }

        private static void Place(Combatant[] side, IEnumerable<Combatant> units, ref int order)
        {
            int slot = 0;
            foreach (var u in units)
            {
                if (slot >= Slots) break;
                u.Slot = slot;
                u.Order = order++;
                side[slot++] = u;
            }
        }

        public Combatant[] Allies(Combatant c) => c.IsParty ? Party : Enemies;
        public Combatant[] Foes(Combatant c) => c.IsParty ? Enemies : Party;

        public IEnumerable<Combatant> Living => Party.Concat(Enemies).Where(c => c != null && c.Alive);
        public IEnumerable<Combatant> LivingParty => Party.Where(c => c != null && c.Alive);
        public IEnumerable<Combatant> LivingEnemies => Enemies.Where(c => c != null && c.Alive);

        // Every party combatant, alive or fallen, for writing HP back to the trial.
        public IEnumerable<Combatant> AllPartyUnits => Party.Where(c => c != null).Concat(Fallen.Where(f => f.IsParty));

        public BattleOutcome Outcome =>
            !LivingEnemies.Any() ? BattleOutcome.Victory :
            !LivingParty.Any() ? BattleOutcome.Defeat : BattleOutcome.Ongoing;

        // ------------------------------------------------------------------ targeting

        public List<Combatant> ValidTargets(Combatant user, ActionDef a)
        {
            switch (a.Side)
            {
                case TargetSide.Self: return new List<Combatant> { user };
                case TargetSide.Ally: return Allies(user).Where(c => c != null && c.Alive && a.TargetRanks.Has(c.Slot)).ToList();
                default: return Foes(user).Where(c => c != null && c.Alive && a.TargetRanks.Has(c.Slot)).ToList();
            }
        }

        // Why an action can't be chosen right now, or null if it can.
        public string Unusable(Combatant user, ActionDef a)
        {
            if (!a.UsableFrom.Has(user.Slot)) return "Can't use from this position";
            if (user.Sp < a.SpCost) return $"Needs {a.SpCost} SP (has {user.Sp})";
            if (a.Kind == ActionKind.Move)
            {
                int to = user.Slot + a.MoveBy;
                if (to < 0 || to >= Slots) return "Can't move further";
                return null;
            }
            if (ValidTargets(user, a).Count == 0) return "No target in reach";
            return null;
        }

        public bool CanUse(Combatant user, ActionDef a) => Unusable(user, a) == null;

        // Speed high -> low. Ties: party first, then front slots first, then spawn order.
        public List<Combatant> ResolveOrder() =>
            Living.Where(c => c.Plan != null)
                .OrderByDescending(c => c.Speed)
                .ThenBy(c => c.IsParty ? 0 : 1)
                .ThenBy(c => c.Slot)
                .ThenBy(c => c.Order)
                .ToList();

        // Same ordering for the turn-order preview while the player is still choosing.
        public List<Combatant> PredictedOrder() =>
            Living.OrderByDescending(c => c.Speed)
                .ThenBy(c => c.IsParty ? 0 : 1)
                .ThenBy(c => c.Slot)
                .ThenBy(c => c.Order)
                .ToList();

        // ------------------------------------------------------------------ resolution

        public List<BattleEvent> Execute(Combatant actor)
        {
            var ev = new List<BattleEvent>();
            var plan = actor.Plan;
            if (plan == null) return ev;
            actor.Plan = null;

            if (!actor.Alive)
            {
                // Design rule: a user eliminated before its turn loses the action.
                ev.Add(new BattleEvent { Type = BattleEventType.Fizzle, Actor = actor, Text = $"{actor.Name} is down: action cancelled" });
                return ev;
            }

            // Guard and Taunt last "until this character acts again".
            actor.Statuses.RemoveAll(s => s.Kind == StatusKind.Guard || s.Kind == StatusKind.Taunt);

            var a = plan.Action;
            ev.Add(new BattleEvent { Type = BattleEventType.ActionStart, Actor = actor, Text = a.Name, Action = a });

            // Positions may have changed since the action was chosen (earlier moves, deaths).
            if (!a.UsableFrom.Has(actor.Slot))
            {
                ev.Add(new BattleEvent { Type = BattleEventType.Fizzle, Actor = actor, Text = "Out of position!" });
                return ev;
            }

            if (actor.Sp < a.SpCost)
            {
                ev.Add(new BattleEvent { Type = BattleEventType.Fizzle, Actor = actor, Text = "Not enough SP" });
                return ev;
            }
            actor.Sp -= a.SpCost;

            // Feast passive: at 3 stacks this skill always crits and consumes the stacks.
            bool isSkill = !CommonActions.IsCommon(a);
            bool feastCrit = isSkill && actor.Has(PassiveKind.Feast) && actor.Feast >= FeastThreshold;
            if (feastCrit)
            {
                actor.Feast = 0;
                ev.Add(new BattleEvent { Type = BattleEventType.Passive, Actor = actor, Target = actor, Text = "FEAST!" });
            }

            switch (a.Kind)
            {
                case ActionKind.Move:
                    DoMove(actor, a.MoveBy, ev);
                    break;

                case ActionKind.Defend:
                    actor.AddStatus(StatusKind.Guard, 1, 0);
                    ev.Add(new BattleEvent { Type = BattleEventType.Status, Actor = actor, Target = actor, Status = StatusKind.Guard, Text = "Guard" });
                    break;

                case ActionKind.Taunt:
                    actor.AddStatus(StatusKind.Guard, 1, 0);
                    actor.AddStatus(StatusKind.Taunt, 1, 0);
                    ev.Add(new BattleEvent { Type = BattleEventType.Status, Actor = actor, Target = actor, Status = StatusKind.Taunt, Text = "Taunt" });
                    break;

                case ActionKind.Attack:
                case ActionKind.Heal:
                    var targets = ResolveTargets(actor, plan, ev);
                    if (targets.Count == 0)
                    {
                        ev.Add(new BattleEvent { Type = BattleEventType.Fizzle, Actor = actor, Text = "No target in reach" });
                        break;
                    }
                    bool landed = false;
                    for (int hit = 0; hit < Math.Max(1, a.Hits); hit++)
                    {
                        // Later hits skip anyone who already fell; area skills re-sweep whoever is still standing.
                        var hitList = hit == 0 ? targets : a.AreaEffect ? ValidTargets(actor, a) : targets.Where(t => t.Alive).ToList();
                        if (hitList.Count == 0 || !actor.Alive) break;
                        if (hit > 0) ev.Add(new BattleEvent { Type = BattleEventType.NextHit, Actor = actor, Amount = hit });
                        foreach (var t in hitList)
                        {
                            if (a.Kind == ActionKind.Attack) ApplyAttack(actor, t, a, feastCrit, ev);
                            else ApplyHeal(actor, t, a, ev);
                        }
                        landed = true;
                    }
                    if (landed && a.ShuffleTargets) Shuffle(Foes(actor), ev);
                    break;
            }

            if (a.SelfMoveAfter != 0 && actor.Alive)
                DoMove(actor, a.SelfMoveAfter, ev, quietIfBlocked: true);

            if (isSkill && actor.Alive && actor.Has(PassiveKind.GuardAfterSkill) && a.Kind != ActionKind.Taunt)
            {
                actor.AddStatus(StatusKind.Guard, 1, 0);
                ev.Add(new BattleEvent { Type = BattleEventType.Status, Actor = actor, Target = actor, Status = StatusKind.Guard, Text = "Guard" });
            }

            if (isSkill && !feastCrit && actor.Alive && actor.Has(PassiveKind.Feast))
            {
                actor.Feast = Math.Min(FeastThreshold, actor.Feast + 1);
                ev.Add(new BattleEvent { Type = BattleEventType.Passive, Actor = actor, Target = actor, Amount = actor.Feast,
                    Text = actor.Feast >= FeastThreshold ? "Feast ready!" : $"Feast {actor.Feast}" });
            }

            return ev;
        }

        // Frontline / Backline passives.
        public static float PositionBonus(Combatant c, bool healing)
        {
            if (!healing && c.Has(PassiveKind.Frontline) && c.Slot <= 1) return PositionPassiveMultiplier;
            if (c.Has(PassiveKind.Backline) && c.Slot >= 2) return PositionPassiveMultiplier;
            return 1f;
        }

        // Every living unit on a side gets a random position (empty slots included).
        private void Shuffle(Combatant[] side, List<BattleEvent> ev)
        {
            var units = side.Where(c => c != null && c.Alive).ToList();
            if (units.Count == 0) return;
            var slots = Enumerable.Range(0, Slots).OrderBy(_ => _rng.Next()).Take(units.Count).ToList();
            Array.Clear(side, 0, Slots);
            ev.Add(new BattleEvent { Type = BattleEventType.Info, Target = units[0], Text = "Positions scrambled!" });
            for (int i = 0; i < units.Count; i++)
            {
                bool moved = units[i].Slot != slots[i];
                units[i].Slot = slots[i];
                side[slots[i]] = units[i];
                if (moved) ev.Add(new BattleEvent { Type = BattleEventType.Move, Actor = units[i] });
            }
        }

        private List<Combatant> ResolveTargets(Combatant actor, PlannedAction plan, List<BattleEvent> ev)
        {
            var a = plan.Action;
            var valid = ValidTargets(actor, a);
            if (a.AreaEffect || a.Side == TargetSide.Self) return valid;
            if (valid.Count == 0) return valid;

            var target = plan.Target;

            if (a.Kind == ActionKind.Attack)
            {
                var taunter = valid.FirstOrDefault(c => c.Has(StatusKind.Taunt));
                if (taunter != null && taunter != target)
                {
                    ev.Add(new BattleEvent { Type = BattleEventType.Info, Actor = actor, Target = taunter, Text = $"{taunter.Name} draws the attack!" });
                    return new List<Combatant> { taunter };
                }
            }

            if (target != null && valid.Contains(target)) return new List<Combatant> { target };

            // Chosen target died or moved out of reach: pick the next best valid one.
            var fallback = a.Kind == ActionKind.Heal
                ? valid.OrderBy(c => (float)c.Hp / c.MaxHp).First()
                : valid.OrderBy(c => c.Slot).First();
            ev.Add(new BattleEvent { Type = BattleEventType.Info, Actor = actor, Target = fallback, Text = $"Retargets {fallback.Name}" });
            return new List<Combatant> { fallback };
        }

        private void ApplyAttack(Combatant actor, Combatant t, ActionDef a, bool forceCrit, List<BattleEvent> ev)
        {
            double roll = 0.85 + _rng.NextDouble() * 0.30;
            double dmg = actor.Damage * a.Power * roll * PositionBonus(actor, healing: false);
            float critChance = actor.CritChance + (actor.Has(PassiveKind.Deadeye) ? DeadeyeCritBonus : 0f);
            bool crit = forceCrit || _rng.NextDouble() < critChance;
            if (crit) dmg *= CritMultiplier;
            if (t.Has(StatusKind.Mark)) dmg *= MarkMultiplier;
            if (t.Has(StatusKind.Guard)) dmg *= GuardMultiplier;
            if (t.Has(PassiveKind.Tough)) dmg *= ToughMultiplier;
            if (actor.Has(PassiveKind.Exploit) && (t.Has(StatusKind.Blight) || t.Has(StatusKind.Mark))) dmg *= ExploitMultiplier;
            int amount = Math.Max(1, (int)Math.Round(dmg));

            Hurt(t, amount, actor, ev, crit);

            float lifesteal = a.Lifesteal + (actor.Has(PassiveKind.Feast) ? FeastLifesteal : 0f);
            if (lifesteal > 0 && actor.Alive && actor.Hp < actor.MaxHp)
                Restore(actor, Math.Max(1, (int)Math.Round(amount * lifesteal)), actor, ev);

            if (t.Alive && a.Applies.HasValue)
            {
                t.AddStatus(a.Applies.Value, a.StatusTurns, a.StatusPower);
                ev.Add(new BattleEvent { Type = BattleEventType.Status, Actor = actor, Target = t, Status = a.Applies.Value, Text = a.Applies.Value.ToString() });
            }

            for (int i = 0; i < a.KnockBack && t.Alive; i++)
                DoMove(t, 1, ev, quietIfBlocked: true);
        }

        private void ApplyHeal(Combatant actor, Combatant t, ActionDef a, List<BattleEvent> ev)
        {
            float devotion = actor.Has(PassiveKind.Devotion) ? DevotionMultiplier : 1f;
            Restore(t, Math.Max(1, (int)Math.Round(actor.Damage * a.Power * devotion * PositionBonus(actor, healing: true))), actor, ev);
            if (a.Cleanse && t.Statuses.RemoveAll(s => s.Kind == StatusKind.Blight || s.Kind == StatusKind.Mark) > 0)
                ev.Add(new BattleEvent { Type = BattleEventType.Cleanse, Actor = actor, Target = t, Text = "Cleansed" });
        }

        private void Hurt(Combatant t, int amount, Combatant source, List<BattleEvent> ev, bool crit = false)
        {
            t.Hp = Math.Max(0, t.Hp - amount);
            ev.Add(new BattleEvent { Type = BattleEventType.Damage, Actor = source, Target = t, Amount = amount, Crit = crit });
            if (!t.Alive) Kill(t, ev);
        }

        private static void Restore(Combatant t, int amount, Combatant source, List<BattleEvent> ev)
        {
            int before = t.Hp;
            t.Hp = Math.Min(t.MaxHp, t.Hp + amount);
            ev.Add(new BattleEvent { Type = BattleEventType.Heal, Actor = source, Target = t, Amount = t.Hp - before });
        }

        // Eliminated units leave the formation; their position becomes empty.
        private void Kill(Combatant t, List<BattleEvent> ev)
        {
            var side = Allies(t);
            if (side[t.Slot] == t) side[t.Slot] = null;
            t.Statuses.Clear();
            Fallen.Add(t);
            ev.Add(new BattleEvent { Type = BattleEventType.Death, Target = t, Text = $"{t.Name} falls" });
        }

        // Move one slot; swap if the destination is occupied, step in if it's empty.
        private void DoMove(Combatant c, int delta, List<BattleEvent> ev, bool quietIfBlocked = false)
        {
            var side = Allies(c);
            int from = c.Slot, to = from + Math.Sign(delta);
            if (to < 0 || to >= Slots)
            {
                if (!quietIfBlocked)
                    ev.Add(new BattleEvent { Type = BattleEventType.Fizzle, Actor = c, Text = "Can't move further" });
                return;
            }
            var other = side[to];
            side[to] = c;
            side[from] = other;
            c.Slot = to;
            if (other != null) other.Slot = from;
            ev.Add(new BattleEvent { Type = BattleEventType.Move, Actor = c, Target = other });
        }

        // End of turn: Blight ticks, timed statuses count down.
        public List<BattleEvent> EndTurn()
        {
            var ev = new List<BattleEvent>();
            foreach (var c in Living.ToList())
            {
                if (c.MaxSp > 0) c.Sp = Math.Min(c.MaxSp, c.Sp + SpPerTurn);

                var blight = c.Statuses.FirstOrDefault(s => s.Kind == StatusKind.Blight);
                if (blight != null)
                {
                    ev.Add(new BattleEvent { Type = BattleEventType.Info, Target = c, Text = "Blight" });
                    Hurt(c, Math.Max(1, blight.Power), null, ev);
                }
                foreach (var s in c.Statuses)
                    if (s.Kind == StatusKind.Blight || s.Kind == StatusKind.Mark) s.Turns--;
                c.Statuses.RemoveAll(s => (s.Kind == StatusKind.Blight || s.Kind == StatusKind.Mark) && s.Turns <= 0);
            }
            foreach (var c in Party.Concat(Enemies))
                if (c != null) c.Plan = null;
            Turn++;
            return ev;
        }
    }
}
