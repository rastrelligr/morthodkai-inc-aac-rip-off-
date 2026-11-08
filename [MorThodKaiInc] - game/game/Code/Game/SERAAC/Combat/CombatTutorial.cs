using System;
using System.Collections.Generic;
using System.Linq;
using SERAAC.Data;
using SERAAC.UI;

namespace SERAAC.Combat
{
    // A scripted fight that teaches as it goes (the sparring match with Naomi). Each turn can open with a
    // coaching scene, allow only one action, and choose what the enemy does. After the last step the fight
    // plays normally. The combat screen asks it at the start of every turn and before committing a plan.
    public sealed class CombatTutorial
    {
        public sealed class Step
        {
            public List<VnLine> Lines;                 // shown when the turn opens
            public string Allow;                       // the only action id the player may pick (null = any)
            public string Instruction;                 // the coach line on screen while choosing
            public string EnemyAction;                 // enemy action id this turn (null = the AI decides)
            public List<Hint> Hints;                   // spotlights to walk through while choosing (UI/Coach.cs)
        }

        // What a hint points at. Pick and Enemies wait for the player to choose; the rest are read and dismissed.
        public enum Spot { Pick, Enemies, TurnOrder, Ethergy, Ranks, Specials, WindUp, Guard }

        public sealed class Hint
        {
            public Spot Spot;
            public int Nth;                            // on whose choice it appears: 0 = whoever chooses first
            public string Text;
        }

        public readonly List<Hint> OnWindUp = new();   // once, the first turn an enemy is winding up
        private bool _windUpShown;

        public readonly List<Step> Steps = new();
        public string Banner = "SPARRING";             // null = the fight's usual banner
        public string Objective = "Spar with Naomi";   // null = the run's usual objective
        public string Coach = "Naomi";

        private int _turn = -1;

        public Step Current => _turn >= 0 && _turn < Steps.Count ? Steps[_turn] : null;
        public string Instruction => Current?.Instruction;

        // Called when Choose Action begins (enemies have planned). Returns the coaching lines to play, if any.
        public List<VnLine> OnTurnStart(Battle b)
        {
            _turn = b.Turn - 1;
            var step = Current;
            if (step == null) return null;
            foreach (var e in b.LivingEnemies)
            {
                if (e.WindUpNext != null || step.EnemyAction == null) continue;   // a wound-up attack still lands
                var a = e.Actions.FirstOrDefault(x => x.Id == step.EnemyAction);
                if (a == null) continue;
                var target = a.IsSingleTarget ? b.ValidTargets(e, a).OrderBy(t => t.Slot).FirstOrDefault() : null;
                e.Plan = new PlannedAction { Action = a, Target = target };
            }
            return step.Lines is { Count: > 0 } ? step.Lines : null;
        }

        // The spotlights for this turn. Call after OnTurnStart.
        public List<Hint> HintsFor(Battle b)
        {
            if (OnWindUp.Count > 0 && !_windUpShown && b.LivingEnemies.Any(e => e.WindUpNext != null))
            {
                _windUpShown = true;
                return OnWindUp;
            }
            return Current?.Hints;
        }

        // Why this plan isn't allowed right now, or null.
        public string Blocked(Combatant c, PlannedAction plan)
        {
            var step = Current;
            if (step?.Allow == null || plan?.Action == null) return null;
            if (plan.Action.Id == step.Allow) return null;
            var wanted = c.Actions.FirstOrDefault(a => a.Id == step.Allow);
            return wanted != null ? $"{Coach}: \"Use {wanted.Name}.\"" : null;
        }

        public bool IsAllowed(ActionDef a) => Current?.Allow == null || a.Id == Current.Allow;
    }
}
