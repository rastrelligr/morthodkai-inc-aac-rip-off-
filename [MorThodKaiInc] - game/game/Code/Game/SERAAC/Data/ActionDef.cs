using System.Text;

namespace SERAAC.Data
{
    // Move: step one slot (enemies). Charge: gain SP. Swap: move to any formation slot. Item: use a consumable.
    // Protect: shield one ally (attacks on it hit the user instead). Wall: the user and adjacent allies take less damage.
    // Reweave: pull one ally 1 position toward the user (swapping if that spot is taken).
    // WindUp: spend the turn rearing up; next turn the user unleashes ActionDef.WindUp (telegraphed, can be interrupted).
    public enum ActionKind { Attack, Heal, Defend, Taunt, Move, Charge, Swap, Item, Protect, Wall, Reweave, WindUp }
    public enum TargetSide { Enemy, Ally, Self }
    // Slow: -Power Speed. Protected / Wall: set by Mara, last until she acts again (StatusInstance.Source).
    // Charging: winding up a big attack (Combatant.WindUpNext); an Interrupts hit cancels it.
    public enum StatusKind { Guard, Taunt, Blight, Mark, Slow, Protected, Wall, Charging }

    // Formation slots as a bitmask. Slot 0 = Front 1, 1 = Front 2, 2 = Back 1, 3 = Back 2.
    public readonly struct Ranks
    {
        private readonly byte _bits;
        private Ranks(byte bits) { _bits = bits; }

        public static readonly Ranks All = new Ranks(0b1111);
        public static readonly Ranks None = new Ranks(0);

        // "12" = Front 1 + Front 2, "34" = the back row, etc. (1-based, like the design doc).
        public static Ranks Of(string ranks)
        {
            byte b = 0;
            foreach (char ch in ranks)
                if (ch >= '1' && ch <= '4') b |= (byte)(1 << (ch - '1'));
            return new Ranks(b);
        }

        public bool Has(int slot) => slot >= 0 && slot < 4 && (_bits & (1 << slot)) != 0;

        // True when every slot here is also in `other` (and there is at least one).
        public bool Within(Ranks other) => _bits != 0 && (_bits & ~other._bits) == 0;

        // Four pips, front first: "##--" style for text-only UI.
        public override string ToString()
        {
            var sb = new StringBuilder(4);
            for (int i = 0; i < 4; i++) sb.Append(Has(i) ? '#' : '-');
            return sb.ToString();
        }
    }

    // Data for one combat action. Characters and enemies share the same action model.
    public sealed class ActionDef
    {
        public string Id;
        public string Name;
        public string Description;
        public ActionKind Kind;
        public TargetSide Side = TargetSide.Enemy;

        public Ranks UsableFrom = Ranks.All; // slots the user must stand in
        public Ranks TargetRanks = Ranks.All; // slots on the target side that can be hit
        public bool AreaEffect;               // hits every valid target instead of one

        public float Power = 1f;              // multiplier on the user's Damage stat (attack & heal)
        public int MoveBy;                    // Move: -1 forward, +1 backward
        public int SelfMoveAfter;             // recoil: user shifts this many slots after acting
        public float Lifesteal;               // fraction of damage dealt returned as healing

        public StatusKind? Applies;
        public int StatusTurns;
        public int StatusPower;
        public bool Cleanse;                  // removes Blight and Mark from the target

        public int SpCost;                    // skill points spent on use (see Battle.SpPerTurn)
        public int Hits = 1;                  // multi-hit: every target is hit this many times
        public bool ShuffleTargets;           // on hit, the target side's positions are randomized
        public int KnockBack;                 // on hit, the target is pushed this many slots back
        public int GainAegis;                 // Ironbound Aegis: Guard stacks the user gains on use
        public float PowerPerAegis;           // spends all the user's Guard stacks: +this Power per stack (Iron Rebuke)
        public int PullForward;               // on hit, the target is pulled this many slots toward the front (Snare Line)
        public int GainThread;                // Threadweaver: Thread stacks the user gains on use
        public bool Priority;                 // resolves before every normal action this turn (guarding, pulling)
        public int HpCost;                    // HP the user pays on use (never below 1 HP)
        public bool Interrupts;               // a hit cancels the target's wind-up (Charging)
        public ActionDef WindUp;              // WindUp kind: the attack unleashed next turn

        // Pose(s) played from the user's sprite sheet while acting. Comma-separated = one per hit.
        public string Anim;

        // Melee attackers run up to their target to hit it, then return. Null = decided from the ranks:
        // an attack used from the front row that only reaches the enemy front row.
        public bool? Melee;

        // Particle presets (Art/Fx/particles.json). Null = the default for the action's kind.
        public string HitFx;     // on each target hit
        public string CastFx;    // on the user when the action starts

        public bool IsSingleTarget => Side != TargetSide.Self && !AreaEffect;
        public bool IsMelee => Melee ?? (Kind == ActionKind.Attack && Side == TargetSide.Enemy
                                         && UsableFrom.Within(Ranks.Of("12")) && TargetRanks.Within(Ranks.Of("12")));
        public string[] AnimSteps => string.IsNullOrEmpty(Anim) ? System.Array.Empty<string>() : Anim.Split(',');
    }

    // Actions outside a character's skill list. The party's Special Abilities (battle UI bottom row) are
    // Guard, Item, Focus (gather energy) and Position (swap places); enemies step forward instead.
    public static class CommonActions
    {
        // True for the common actions; everything else is a Skill (Feast and Knight's Stance count skills).
        public static bool IsCommon(ActionDef a) => a == Defend || a == StepForward || a == Charge || a == Swap || a == UseItem;

        // The party's Special Abilities, in the order the battle UI shows them.
        public static readonly ActionDef Defend = new()
        {
            Id = "defend", Name = "Guard", Kind = ActionKind.Defend, Side = TargetSide.Self, Priority = true,
            Description = "Brace before anyone else moves: halve damage taken until this character acts again.",
        };

        public static readonly ActionDef UseItem = new()
        {
            Id = "item", Name = "Item", Kind = ActionKind.Item, Side = TargetSide.Self,
            Description = "Use one of the trial's consumables. Takes this character's turn, at their speed.",
        };

        // Called "Advance" in the design doc; renamed Focus after playtesters read it as "move forward".
        public static readonly ActionDef Charge = new()
        {
            Id = "charge", Name = "Focus", Kind = ActionKind.Charge, Side = TargetSide.Self,
            Description = "Hold still and gather energy: gain 2 extra SP.",
        };

        public static readonly ActionDef Swap = new()
        {
            Id = "position", Name = "Position", Kind = ActionKind.Swap, Side = TargetSide.Ally,
            Description = "Move to another position, swapping places with the ally standing there.",
        };

        public static readonly ActionDef[] PartySpecials = { Defend, UseItem, Charge, Swap };

        // Enemies only: step one position toward the front.
        public static readonly ActionDef StepForward = new()
        {
            Id = "step_forward", Name = "Step Forward", Kind = ActionKind.Move, Side = TargetSide.Self, MoveBy = -1,
            UsableFrom = Ranks.Of("234"),
            Description = "Move 1 position forward. Swaps with an ally standing there.",
        };
    }
}
