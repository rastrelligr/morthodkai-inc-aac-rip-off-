using System;

namespace SERAAC
{
    public enum Role { Wielder, Weapon }

    public enum MinigameType
    {
        SpellTyping,   //ร่ายคาถา
        RuneCircle,    // เรียงรูนในวงกลม
        RhythmTap,  // กดจังหวะแบบ rhythm game
        ReflexDodge,    // QTE
        PatternMatch    // จับคู่ pattern
    }

    public enum MinigameDifficulty { Easy, Normal, Hard }

    public enum BuffType { AttackUp, DefenseUp, Haste, Poison, Stun, SyncBonus }

    public enum RoomType { Battle, Elite, Treasure, Rest, Boss, Event }

    public enum TurnActorType { PartyPair, Enemy }
}
