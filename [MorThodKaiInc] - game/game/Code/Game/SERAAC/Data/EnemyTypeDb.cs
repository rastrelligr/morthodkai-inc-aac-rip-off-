using System.Collections.Generic;

namespace SERAAC.Data
{
    // None only exists so a forgotten Type is caught by the self-test.
    public enum EnemyType { None, Normal, Brute, Stalker, Apex }

    // Enemy Type (design: Enemies and Behaviour) = an enemy's main job in combat. It sets who the enemy
    // prefers to hit and how it weighs its actions, and how it hunts the party while exploring.
    // It does NOT set Position: that is still EnemyDef.Row. Enemies may still use actions outside their job.
    public sealed class EnemyTypeDef
    {
        public EnemyType Type;
        public string Name;
        public string Summary;

        // Combat behaviour.
        public float[] SlotPreference;   // target weight by party slot: Front 1, Front 2, Back 1, Back 2
        public float WoundedBias;        // extra weight on hurt targets (finishing them off)
        public float DefendBias;         // multiplier on Guard-style actions
        public bool Steadfast;           // can't be knocked back (pulls and scrambles still move it)

        // Exploration behaviour (party walks at 230 px/s).
        public float ChaseSpeed;         // pixels per second once it has noticed the party
        public float SightTiles;         // how far it notices the party (with a clear line)
    }

    public static class EnemyTypeDb
    {
        // Gnawer's type (design: "Normal"): plain, steady pressure on the front with no tricks.
        public static readonly EnemyTypeDef Normal = new()
        {
            Type = EnemyType.Normal, Name = "Normal",
            Summary = "Common infected. Steady, simple attacks on the front line. No special abilities.",
            SlotPreference = new[] { 2.2f, 1.6f, 0.5f, 0.4f },
            WoundedBias = 1.2f,
            DefendBias = 0.5f,
            ChaseSpeed = 185f,
            SightTiles = 7f,
        };

        public static readonly EnemyTypeDef Brute = new()
        {
            Type = EnemyType.Brute, Name = "Brute",
            Summary = "High HP and damage. Pressures the front line. Too heavy to knock back.",
            SlotPreference = new[] { 3.0f, 1.6f, 0.6f, 0.4f },
            WoundedBias = 0.6f,
            DefendBias = 0.4f,
            Steadfast = true,
            ChaseSpeed = 160f,
            SightTiles = 6f,
        };

        public static readonly EnemyTypeDef Stalker = new()
        {
            Type = EnemyType.Stalker, Name = "Stalker",
            Summary = "Fragile, medium damage. Slips past the front line to hunt the back line.",
            SlotPreference = new[] { 0.4f, 0.8f, 2.6f, 3.0f },
            WoundedBias = 2.5f,
            DefendBias = 0.6f,
            ChaseSpeed = 205f,
            SightTiles = 8f,
        };

        // New for the Wyvern: a boss type. It spreads its pressure over the whole formation (no favourite
        // position), picks off whoever is weakest, rarely defends, and mixes sweeping and crippling attacks.
        public static readonly EnemyTypeDef Apex = new()
        {
            Type = EnemyType.Apex, Name = "Apex",
            Summary = "Boss. Winds up huge attacks you must brace for or interrupt, hunts the weakest, and enrages at half HP. Too heavy to knock back.",
            SlotPreference = new[] { 1.2f, 1.2f, 1.2f, 1.2f },
            WoundedBias = 3.0f,
            DefendBias = 0.2f,
            Steadfast = true,
            ChaseSpeed = 170f,
            SightTiles = 9f,
        };

        public static readonly List<EnemyTypeDef> All = new() { Normal, Brute, Stalker, Apex };

        public static EnemyTypeDef Get(EnemyType type) => type switch
        {
            EnemyType.Normal => Normal,
            EnemyType.Brute => Brute,
            EnemyType.Stalker => Stalker,
            EnemyType.Apex => Apex,
            _ => null,
        };
    }
}
