using System.Text;

namespace SERAAC.Data
{
    public enum ActionKind { Attack, Heal, Defend, Taunt, Move }
    public enum TargetSide { Enemy, Ally, Self }
    public enum StatusKind { Guard, Taunt, Blight, Mark }

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

        // Pose(s) played from the user's sprite sheet while acting. Comma-separated = one per hit.
        public string Anim;

        public bool IsSingleTarget => Side != TargetSide.Self && !AreaEffect;
        public string[] AnimSteps => string.IsNullOrEmpty(Anim) ? System.Array.Empty<string>() : Anim.Split(',');
    }

    // Actions every character has, per the design doc example (Attack / Defend / Move).
    public static class CommonActions
    {
        // True for Defend/Advance/Retreat; everything else is a character's Skill.
        public static bool IsCommon(ActionDef a) => a == Defend || a == Advance || a == Retreat;

        public static readonly ActionDef Defend = new()
        {
            Id = "defend", Name = "Defend", Kind = ActionKind.Defend, Side = TargetSide.Self,
            Description = "Halve damage taken until this character acts again.",
        };

        public static readonly ActionDef Advance = new()
        {
            Id = "advance", Name = "Advance", Kind = ActionKind.Move, Side = TargetSide.Self, MoveBy = -1,
            UsableFrom = Ranks.Of("234"),
            Description = "Move 1 position forward. Swaps with an ally standing there.",
        };

        public static readonly ActionDef Retreat = new()
        {
            Id = "retreat", Name = "Retreat", Kind = ActionKind.Move, Side = TargetSide.Self, MoveBy = 1,
            UsableFrom = Ranks.Of("123"),
            Description = "Move 1 position back. Swaps with an ally standing there.",
        };
    }
}
