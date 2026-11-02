using System;
using System.Collections.Generic;
using System.Linq;
using SERAAC.Data;

namespace SERAAC.Combat
{
    public enum BattleEventType { ActionStart, NextHit, Damage, Heal, Status, Cleanse, Move, Death, Fizzle, Info, Passive, Revive }
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
        public const float FeastLifesteal = 0.1f;              // fraction of each hit healed back
        public const int FeastThreshold = 3;
        public const float ToughMultiplier = 0.8f;             // Thick Hide: damage taken
        public const float DevotionMultiplier = 1.3f;          // Devotion: healing done
        public const float DeadeyeCritBonus = 0.2f;
        public const float ExploitMultiplier = 1.3f;           // vs Blighted or Marked targets
        public const int ChargeSp = 2;                         // "Focus" (gather energy; "Advance" in the design doc): extra SP on top of SpPerTurn

        // Mara (Ironbound Aegis / Bulwark / Aegis Wall).
        public const int AegisThreshold = 2;                   // Guard stacks needed to intercept
        public const float AegisRedirectMultiplier = 0.5f;     // damage Mara takes from an intercepted attack
        public const float BulwarkMultiplier = 0.8f;           // damage taken in Front 1 / Front 2
        public const float WallMultiplier = 0.6f;              // damage taken under Aegis Wall
        // Elian (Sympathetic Thread / Backline support / Threadweaver).
        public const float SympatheticMultiplier = 1.5f;       // skill effect on an ally under the threshold
        public const float SympatheticHpThreshold = 0.5f;
        public const float SupportMultiplier = 1.2f;           // healing from Back 1 / Back 2
        public const int ThreadMax = 3;                        // Thread for a Woven skill
        public const float WovenHealMultiplier = 0.6f;         // a Woven heal reaches everyone, at this share each
        // Counter-attacks: Mara after intercepting, Pillow while Guarded (Riposte). Power x the defender's Damage.
        public const float CounterPower = 0.6f;
        // Apex bosses: second phase at this share of HP (cleanses, gets faster, winds up more often).
        public const float EnrageHpShare = 0.5f;
        public const int EnrageSpeed = 2;

        public readonly Combatant[] Party = new Combatant[Slots];
        public readonly Combatant[] Enemies = new Combatant[Slots];
        public readonly List<Combatant> Fallen = new();
        public int Turn { get; private set; } = 1;

        // The party's consumables (consumable ids, one entry per item). The combat screen passes the
        // trial's own list, so items used in a fight are gone from the trial too.
        public List<string> Items = new();

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
                case TargetSide.Ally when a.Kind is ActionKind.Swap or ActionKind.Protect or ActionKind.Reweave:
                    return Allies(user).Where(c => c != null && c.Alive && c != user).ToList();
                case TargetSide.Ally: return Allies(user).Where(c => c != null && c.Alive && a.TargetRanks.Has(c.Slot)).ToList();
                default: return Foes(user).Where(c => c != null && c.Alive && a.TargetRanks.Has(c.Slot)).ToList();
            }
        }

        // Why an action can't be chosen right now, or null if it can.
        public string Unusable(Combatant user, ActionDef a)
        {
            if (!a.UsableFrom.Has(user.Slot)) return "Can't use from this position";
            if (user.Sp < a.SpCost) return $"Needs {a.SpCost} SP (has {user.Sp})";
            // Aegis Wall: Mara stays planted until she acts again.
            if (HoldsWall(user) && (a.Kind is ActionKind.Swap or ActionKind.Move || a.SelfMoveAfter != 0))
                return "Holding the Aegis Wall";
            if (a.Kind == ActionKind.Move)
            {
                int to = user.Slot + a.MoveBy;
                if (to < 0 || to >= Slots) return "Can't move further";
                return null;
            }
            if (a.Kind == ActionKind.Swap) return null;            // there is always another slot
            if (a.Kind == ActionKind.Item) return Items.Count == 0 ? "No items left" : null;
            if (a.Kind == ActionKind.Charge)
                return user.MaxSp == 0 ? "This character doesn't use SP" : user.Sp >= user.MaxSp ? "SP is already full" : null;
            if (ValidTargets(user, a).Count == 0) return "No target in reach";
            return null;
        }

        public bool CanUse(Combatant user, ActionDef a) => Unusable(user, a) == null;

        private static bool HoldsWall(Combatant c) => c.Status(StatusKind.Wall)?.Source == c;

        // Priority actions (Guard, Mara's shields, Snare Line) first, then Speed high -> low (after Slow).
        // Ties: party first, then front slots first, then spawn order.
        public List<Combatant> ResolveOrder() =>
            Living.Where(c => c.Plan != null)
                .OrderByDescending(c => c.Plan.Action.Priority)
                .ThenByDescending(c => c.EffectiveSpeed)
                .ThenBy(c => c.IsParty ? 0 : 1)
                .ThenBy(c => c.Slot)
                .ThenBy(c => c.Order)
                .ToList();

        // Same ordering for the turn-order preview while the player is still choosing (planned priority actions move up).
        public List<Combatant> PredictedOrder() =>
            Living.OrderByDescending(c => c.Plan?.Action.Priority == true)
                .ThenByDescending(c => c.EffectiveSpeed)
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

            // Guard and Taunt last "until this character acts again". So do the Protected / Wall this
            // character set on others (Mara acts late, so "until end of turn" would protect almost nothing).
            actor.Statuses.RemoveAll(s => s.Kind == StatusKind.Guard || s.Kind == StatusKind.Taunt);
            ClearSourcedBy(actor);
            _aegisCatch = null;
            _intercepted = null;
            _aegisSpent = 0;

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

            // Blood price (Blood Feast): paid up front, never fatal.
            if (a.HpCost > 0 && actor.Hp > 1)
            {
                int paid = Math.Min(a.HpCost, actor.Hp - 1);
                actor.Hp -= paid;
                ev.Add(new BattleEvent { Type = BattleEventType.Damage, Actor = actor, Target = actor, Amount = paid });
            }

            // Unleashing a wound-up attack ends the wind-up.
            if (actor.WindUpNext == a)
            {
                actor.WindUpNext = null;
                actor.Statuses.RemoveAll(s => s.Kind == StatusKind.Charging);
            }

            // Feast passive: at 3 stacks this skill always crits and consumes the stacks.
            bool isSkill = !CommonActions.IsCommon(a);
            bool feastCrit = isSkill && actor.Has(PassiveKind.Feast) && actor.Feast >= FeastThreshold;
            if (feastCrit)
            {
                actor.Feast = 0;
                ev.Add(new BattleEvent { Type = BattleEventType.Passive, Actor = actor, Target = actor, Text = "FEAST!" });
            }

            // Threadweaver: at max Thread the next single-target heal / attack is Woven and reaches every valid target.
            bool woven = isSkill && actor.Has(PassiveKind.Weave) && actor.Thread >= ThreadMax
                         && a.Kind is ActionKind.Attack or ActionKind.Heal && !a.AreaEffect;
            if (woven)
            {
                actor.Thread = 0;
                ev.Add(new BattleEvent { Type = BattleEventType.Passive, Actor = actor, Target = actor, Text = "WOVEN!" });
            }

            // Iron Rebuke: spends every Guard stack for extra power.
            if (a.PowerPerAegis > 0)
            {
                _aegisSpent = actor.Aegis;
                actor.Aegis = 0;
                if (_aegisSpent > 0)
                    ev.Add(new BattleEvent { Type = BattleEventType.Passive, Actor = actor, Target = actor, Amount = _aegisSpent, Text = $"Spends {_aegisSpent} Guard" });
            }

            switch (a.Kind)
            {
                case ActionKind.Move:
                    DoMove(actor, a.MoveBy, ev);
                    break;

                case ActionKind.Charge:
                {
                    int before = actor.Sp;
                    actor.Sp = Math.Min(actor.MaxSp, actor.Sp + ChargeSp);
                    ev.Add(new BattleEvent { Type = BattleEventType.Info, Actor = actor, Target = actor, Amount = actor.Sp - before,
                        Text = $"+{actor.Sp - before} SP" });
                    break;
                }

                case ActionKind.Swap:
                {
                    // Moves to the chosen slot, swapping with whoever stands there by now (or stepping into it if empty).
                    int slot = plan.TargetSlot >= 0 ? plan.TargetSlot : plan.Target?.Slot ?? -1;
                    if (slot < 0 || slot >= Slots || slot == actor.Slot)
                        ev.Add(new BattleEvent { Type = BattleEventType.Fizzle, Actor = actor, Text = "Already in place" });
                    else SwapTo(actor, slot, ev);
                    break;
                }

                case ActionKind.Item:
                    UseItem(actor, plan.ItemId, ev);
                    break;

                case ActionKind.WindUp:
                    // Telegraphed: next turn the user unleashes a.WindUp unless an Interrupts hit stops it first.
                    actor.WindUpNext = a.WindUp;
                    actor.LastWindUpTurn = Turn;
                    actor.AddStatus(StatusKind.Charging, 1, 0);
                    ev.Add(new BattleEvent { Type = BattleEventType.Status, Actor = actor, Target = actor, Status = StatusKind.Charging,
                        Text = $"{a.WindUp?.Name ?? "Something"} next turn!" });
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

                case ActionKind.Protect:
                {
                    // Attacks aimed at this ally hit the user instead, until the user acts again.
                    var ally = plan.Target;
                    if (ally == null || !ally.Alive || ally == actor || !ValidTargets(actor, a).Contains(ally))
                    {
                        ev.Add(new BattleEvent { Type = BattleEventType.Fizzle, Actor = actor, Text = "No ally to protect" });
                        break;
                    }
                    ally.AddStatus(StatusKind.Protected, 1, 0, actor);
                    ev.Add(new BattleEvent { Type = BattleEventType.Status, Actor = actor, Target = ally, Status = StatusKind.Protected, Text = "Protected" });
                    break;
                }

                case ActionKind.Wall:
                    // The user and the allies next to it take less damage until the user acts again.
                    foreach (var c in Allies(actor).Where(c => c != null && c.Alive && Math.Abs(c.Slot - actor.Slot) <= 1))
                    {
                        c.AddStatus(StatusKind.Wall, 1, 0, actor);
                        ev.Add(new BattleEvent { Type = BattleEventType.Status, Actor = actor, Target = c, Status = StatusKind.Wall, Text = "Aegis Wall" });
                    }
                    break;

                case ActionKind.Reweave:
                {
                    // Pull the ally 1 position toward the user; if that spot is taken they swap.
                    var ally = plan.Target;
                    if (ally == null || !ally.Alive || ally == actor || !ValidTargets(actor, a).Contains(ally))
                    {
                        ev.Add(new BattleEvent { Type = BattleEventType.Fizzle, Actor = actor, Text = "No ally to pull" });
                        break;
                    }
                    DoMove(ally, Math.Sign(actor.Slot - ally.Slot), ev);
                    break;
                }

                case ActionKind.Attack:
                case ActionKind.Heal:
                    var targets = woven ? ValidTargets(actor, a) : ResolveTargets(actor, plan, ev);
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
                            else ApplyHeal(actor, t, a, ev, woven ? WovenHealMultiplier : 1f);
                        }
                        landed = true;
                    }
                    if (landed && a.ShuffleTargets) Shuffle(Foes(actor), ev);
                    break;
            }

            if (a.SelfMoveAfter != 0 && actor.Alive)
                DoMove(actor, a.SelfMoveAfter, ev, quietIfBlocked: true);

            if (isSkill && actor.Alive && (actor.Has(PassiveKind.GuardAfterSkill) || actor.Has(PassiveKind.Riposte)) && a.Kind != ActionKind.Taunt)
            {
                actor.AddStatus(StatusKind.Guard, 1, 0);
                ev.Add(new BattleEvent { Type = BattleEventType.Status, Actor = actor, Target = actor, Status = StatusKind.Guard, Text = "Guard" });
            }

            if (a.GainAegis > 0 && actor.Alive && actor.Has(PassiveKind.Aegis))
            {
                actor.Aegis = Math.Min(AegisThreshold, actor.Aegis + a.GainAegis);
                ev.Add(new BattleEvent { Type = BattleEventType.Passive, Actor = actor, Target = actor, Amount = actor.Aegis,
                    Text = actor.Aegis >= AegisThreshold ? "Aegis ready!" : $"Guard {actor.Aegis}" });
            }

            if (a.GainThread > 0 && actor.Alive && !woven)
            {
                actor.Thread = Math.Min(ThreadMax, actor.Thread + a.GainThread);
                ev.Add(new BattleEvent { Type = BattleEventType.Passive, Actor = actor, Target = actor, Amount = actor.Thread, Text = $"Thread {actor.Thread}" });
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

            if (target == null || !valid.Contains(target))
            {
                // Chosen target died or moved out of reach: pick the next best valid one.
                target = a.Kind == ActionKind.Heal
                    ? valid.OrderBy(c => (float)c.Hp / c.MaxHp).First()
                    : valid.OrderBy(c => c.Slot).First();
                ev.Add(new BattleEvent { Type = BattleEventType.Info, Actor = actor, Target = target, Text = $"Retargets {target.Name}" });
            }
            if (a.Kind == ActionKind.Attack) target = Intercept(actor, target, ev);
            return new List<Combatant> { target };
        }

        // Mara stepping in front of a single-target attack: first her Guard skill (Protected), then the
        // Ironbound Aegis at 2 stacks (an attack on an ally next to her; she takes reduced damage).
        private Combatant Intercept(Combatant actor, Combatant target, List<BattleEvent> ev)
        {
            var guard = target.Status(StatusKind.Protected)?.Source;
            if (guard != null && guard.Alive && guard != target && Allies(guard)[guard.Slot] == guard)
            {
                ev.Add(new BattleEvent { Type = BattleEventType.Info, Actor = actor, Target = guard, Text = $"{guard.Name} intercepts!" });
                _intercepted = guard;
                return guard;
            }
            var aegis = Allies(target).FirstOrDefault(c => c != null && c != target && c.Alive && c.Has(PassiveKind.Aegis)
                                                           && c.Aegis >= AegisThreshold && Math.Abs(c.Slot - target.Slot) == 1);
            if (aegis != null)
            {
                aegis.Aegis = 0;
                _aegisCatch = aegis;
                _intercepted = aegis;
                ev.Add(new BattleEvent { Type = BattleEventType.Passive, Actor = actor, Target = aegis, Text = "Aegis!" });
                return aegis;
            }
            return target;
        }

        private Combatant _aegisCatch;   // took this action's hit through the Ironbound Aegis
        private Combatant _intercepted;  // stepped in front of this action (Guard skill or Aegis): may counter
        private int _aegisSpent;         // Guard stacks spent by this action (Iron Rebuke)

        // Power of a skill as used now (Iron Rebuke adds per Guard stack spent; previews use the stacks held).
        private static float PowerOf(ActionDef a, int aegis) => a.Power + a.PowerPerAegis * aegis;

        // Protected / Wall end when whoever set them acts again or falls.
        private void ClearSourcedBy(Combatant source)
        {
            foreach (var c in Party.Concat(Enemies))
                c?.Statuses.RemoveAll(s => s.Source == source && s.Kind is StatusKind.Protected or StatusKind.Wall);
        }

        // Everything about the target that scales an attacker's damage (Mark, Guard, Thick Hide, Exploit).
        // Vulnerabilities / resistances would go here too.
        public static float TargetMultiplier(Combatant actor, Combatant t)
        {
            float m = 1f;
            if (t.Has(StatusKind.Mark)) m *= MarkMultiplier;
            if (t.Has(StatusKind.Guard)) m *= GuardMultiplier;
            if (t.Has(PassiveKind.Tough)) m *= ToughMultiplier;
            if (t.Has(PassiveKind.Bulwark) && t.Slot <= 1) m *= BulwarkMultiplier;
            if (t.Has(StatusKind.Wall)) m *= WallMultiplier;
            if (actor.Has(PassiveKind.Exploit) && (t.Has(StatusKind.Blight) || t.Has(StatusKind.Mark) || t.Has(StatusKind.Slow))) m *= ExploitMultiplier;
            return m;
        }

        // Damage range of one hit (no crit), the same formula as ApplyAttack. Target null = before modifiers.
        public static (int min, int max) EstimateDamage(Combatant actor, Combatant t, ActionDef a)
        {
            double baseDmg = actor.Damage * PowerOf(a, actor.Aegis) * PositionBonus(actor, healing: false) * (t == null ? 1f : TargetMultiplier(actor, t));
            return (Math.Max(1, (int)Math.Round(baseDmg * 0.85)), Math.Max(1, (int)Math.Round(baseDmg * 1.15)));
        }

        // Healing of one heal, the same formula as ApplyHeal. Target null = before Sympathetic Thread.
        public static int EstimateHeal(Combatant actor, ActionDef a, Combatant target = null)
        {
            float devotion = actor.Has(PassiveKind.Devotion) ? DevotionMultiplier : 1f;
            float support = actor.Has(PassiveKind.Support) && actor.Slot >= 2 ? SupportMultiplier : 1f;
            float sympathy = target != null && Sympathetic(actor, target) ? SympatheticMultiplier : 1f;
            return Math.Max(1, (int)Math.Round(actor.Damage * a.Power * devotion * support * sympathy * PositionBonus(actor, healing: true)));
        }

        // Sympathetic Thread: Elian's skills on a badly hurt ally work better.
        public static bool Sympathetic(Combatant actor, Combatant t) =>
            actor.Has(PassiveKind.Sympathetic) && t.IsParty == actor.IsParty && t.Hp < t.MaxHp * SympatheticHpThreshold;

        private void ApplyAttack(Combatant actor, Combatant t, ActionDef a, bool forceCrit, List<BattleEvent> ev)
        {
            double roll = 0.85 + _rng.NextDouble() * 0.30;
            double dmg = actor.Damage * PowerOf(a, _aegisSpent) * roll * PositionBonus(actor, healing: false);
            float critChance = actor.CritChance + (actor.Has(PassiveKind.Deadeye) ? DeadeyeCritBonus : 0f);
            bool crit = forceCrit || _rng.NextDouble() < critChance;
            if (crit) dmg *= CritMultiplier;
            dmg *= TargetMultiplier(actor, t);
            if (t == _aegisCatch) dmg *= AegisRedirectMultiplier;
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

            // A hit that Interrupts stops a wind-up: the big attack never comes.
            if (a.Interrupts && t.Alive && t.WindUpNext != null)
            {
                if (t.Plan?.Action == t.WindUpNext) t.Plan = null;
                t.WindUpNext = null;
                t.Statuses.RemoveAll(s => s.Kind == StatusKind.Charging);
                ev.Add(new BattleEvent { Type = BattleEventType.Passive, Actor = actor, Target = t, Text = "INTERRUPTED!" });
            }

            if (a.KnockBack > 0 && t.Alive && t.Enemy?.TypeDef?.Steadfast == true)
                ev.Add(new BattleEvent { Type = BattleEventType.Info, Actor = actor, Target = t, Text = "Too heavy to push!" });
            else
                for (int i = 0; i < a.KnockBack && t.Alive; i++)
                    DoMove(t, 1, ev, quietIfBlocked: true);
            for (int i = 0; i < a.PullForward && t.Alive; i++)
                DoMove(t, -1, ev, quietIfBlocked: true);

            // Counter-attacks on single-target attacks: Mara after stepping in front of one, Pillow while Guarded.
            bool counters = t.Alive && actor.Alive && t.IsParty != actor.IsParty && a.IsSingleTarget
                            && ((t == _intercepted && t.Has(PassiveKind.Aegis)) || (t.Has(PassiveKind.Riposte) && t.Has(StatusKind.Guard)));
            if (counters)
            {
                double back = t.Damage * CounterPower * (0.85 + _rng.NextDouble() * 0.30) * TargetMultiplier(t, actor);
                ev.Add(new BattleEvent { Type = BattleEventType.Passive, Actor = t, Target = t, Text = "Riposte!" });
                Hurt(actor, Math.Max(1, (int)Math.Round(back)), t, ev);
            }
        }

        private void ApplyHeal(Combatant actor, Combatant t, ActionDef a, List<BattleEvent> ev, float share = 1f)
        {
            Restore(t, Math.Max(1, (int)Math.Round(EstimateHeal(actor, a, t) * share)), actor, ev);
            if (a.Cleanse && t.Statuses.RemoveAll(s => s.Kind == StatusKind.Blight || s.Kind == StatusKind.Mark) > 0)
                ev.Add(new BattleEvent { Type = BattleEventType.Cleanse, Actor = actor, Target = t, Text = "Cleansed" });
        }

        private void Hurt(Combatant t, int amount, Combatant source, List<BattleEvent> ev, bool crit = false)
        {
            t.Hp = Math.Max(0, t.Hp - amount);
            ev.Add(new BattleEvent { Type = BattleEventType.Damage, Actor = source, Target = t, Amount = amount, Crit = crit });
            if (!t.Alive) Kill(t, ev);
            else if (!t.Enraged && t.Enemy?.Type == EnemyType.Apex && t.Hp <= t.MaxHp * EnrageHpShare) Enrage(t, ev);
        }

        // Apex second phase: sheds Blight / Mark / Slow, gets faster, and winds up again sooner (CombatAI).
        private static void Enrage(Combatant t, List<BattleEvent> ev)
        {
            t.Enraged = true;
            t.Speed += EnrageSpeed;
            t.Statuses.RemoveAll(s => s.Kind is StatusKind.Blight or StatusKind.Mark or StatusKind.Slow);
            ev.Add(new BattleEvent { Type = BattleEventType.Passive, Actor = t, Target = t, Text = "ENRAGED!" });
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
            ClearSourcedBy(t);
            t.Aegis = 0;
            Fallen.Add(t);
            ev.Add(new BattleEvent { Type = BattleEventType.Death, Target = t, Text = $"{t.Name} falls" });
        }

        // Position: jump to any slot on the same side, trading places with its occupant.
        private void SwapTo(Combatant c, int slot, List<BattleEvent> ev)
        {
            var side = Allies(c);
            int from = c.Slot;
            var other = side[slot];
            side[slot] = c;
            side[from] = other;
            c.Slot = slot;
            if (other != null) other.Slot = from;
            ev.Add(new BattleEvent { Type = BattleEventType.Move, Actor = c, Target = other });
        }

        // Item: spends one consumable from Items. Missing (already used this turn) = the action fizzles.
        private void UseItem(Combatant actor, string itemId, List<BattleEvent> ev)
        {
            var item = ItemDb.GetConsumable(itemId);
            if (item == null || !Items.Remove(itemId))
            {
                ev.Add(new BattleEvent { Type = BattleEventType.Fizzle, Actor = actor, Text = "No item left" });
                return;
            }
            ev.Add(new BattleEvent { Type = BattleEventType.Info, Actor = actor, Target = actor, Text = item.Name });
            var side = Allies(actor);
            switch (item.Effect)
            {
                case ConsumableEffect.HealParty:
                    foreach (var c in side.Where(c => c != null && c.Alive).ToList())
                        Restore(c, Math.Max(1, (int)Math.Round(c.MaxHp * item.Amount)), actor, ev);
                    break;
                case ConsumableEffect.ReviveFallen:
                    // The fallen stand back up in the rearmost free positions.
                    foreach (var c in Fallen.Where(f => f.IsParty == actor.IsParty).ToList())
                    {
                        int slot = Enumerable.Range(0, Slots).Reverse().FirstOrDefault(s => side[s] == null, -1);
                        if (slot < 0) break;
                        Fallen.Remove(c);
                        c.Hp = Math.Max(1, (int)Math.Round(c.MaxHp * item.Amount));
                        c.Slot = slot;
                        side[slot] = c;
                        ev.Add(new BattleEvent { Type = BattleEventType.Revive, Actor = actor, Target = c, Amount = c.Hp });
                    }
                    break;
            }
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
                    if (s.Kind is StatusKind.Blight or StatusKind.Mark or StatusKind.Slow) s.Turns--;
                c.Statuses.RemoveAll(s => s.Kind is StatusKind.Blight or StatusKind.Mark or StatusKind.Slow && s.Turns <= 0);
            }
            foreach (var c in Party.Concat(Enemies))
                if (c != null) c.Plan = null;
            Turn++;
            return ev;
        }
    }
}
