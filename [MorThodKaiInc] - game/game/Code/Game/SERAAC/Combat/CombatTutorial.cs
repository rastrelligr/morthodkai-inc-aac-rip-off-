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
        }

        public readonly List<Step> Steps = new();
        public string Banner = "SPARRING";
        public string Objective = "Spar with Naomi";
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
