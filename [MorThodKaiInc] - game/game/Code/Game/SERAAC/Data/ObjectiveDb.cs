using System.Collections.Generic;
using System.Linq;

namespace SERAAC.Data
{
    public enum ObjectiveType { DefeatTarget, CollectItem, Interact, ReachLocation }

    // A Layer's (Trial's) goal (design: Objective system). One is picked from the Trial's pool when a run
    // starts and shown from the start. Extraction stays sealed until it is complete; completing it does not
    // end the run. Progress belongs to that run only, so a replay can roll a different one.
    public sealed class ObjectiveDef
    {
        public string Id;
        public string Name;              // "Find the Source"
        public string Task;              // what to do, for the HUD: "Interact with the Source"
        public ObjectiveType Type;
        public int Count = 1;            // how many times the condition must be met (shows progress when > 1)

        // Defeat Target: a specific enemy (it waits in an Objective room), or any enemy of a Type.
        public EnemyDef Target;
        public EnemyType TargetType;

        // Collect Item / Interact / Reach Location: the thing in each Objective room ("Medical Cache").
        // Count rooms are placed; each holds one.
        public string Thing;
        public string Prompt;            // Interact only: "[E] <Prompt>"

        // Who stands in the Objective rooms. Guardian leads the room's encounter (with front-pool escorts);
        // Guarded without a Guardian = a normal encounter. Neither = the room is quiet.
        public EnemyDef Guardian;
        public bool Guarded;

        // Completing this also completes the Area's Main Objective (unlocks the next Area, saved at once).
        public bool Main;

        // Number of Objective rooms on the map.
        public int RoomCount => Type == ObjectiveType.DefeatTarget ? (Target != null ? Count : 0) : Count;

        // The enemy leading each Objective room's fight, if any.
        public EnemyDef RoomLeader => Type == ObjectiveType.DefeatTarget ? Target : Guardian;
        public bool RoomHasEnemies => RoomLeader != null || Guarded;

        public bool Counts(EnemyDef e) =>
            Type == ObjectiveType.DefeatTarget && (Target != null ? e == Target : e.Type == TargetType);
    }

    public static class ObjectiveDb
    {
        // ---------------- Area 1: Abandoned City ----------------
        public static readonly ObjectiveDef CullBrutes = new()
        {
            Id = "cull_brutes", Name = "Break Their Front", Task = "Defeat Husks (Brutes)",
            Type = ObjectiveType.DefeatTarget, TargetType = EnemyType.Brute, Count = 4,
        };

        public static readonly ObjectiveDef HuntStalkers = new()
        {
            Id = "hunt_stalkers", Name = "Hunt the Hunters", Task = "Defeat Wraiths (Stalkers)",
            Type = ObjectiveType.DefeatTarget, TargetType = EnemyType.Stalker, Count = 3,
        };

        public static readonly ObjectiveDef SupplyRun = new()
        {
            Id = "supply_run", Name = "Supply Run", Task = "Collect Medical Caches",
            Type = ObjectiveType.CollectItem, Thing = "Medical Cache", Count = 2, Guarded = true,
        };

        public static readonly ObjectiveDef SignalTower = new()
        {
            Id = "signal_tower", Name = "Light the Signal", Task = "Reach the Signal Tower",
            Type = ObjectiveType.ReachLocation, Thing = "Signal Tower", Guarded = true,
        };

        // Fallen Academy Main Objective: the Wyvern waits in the deepest Objective room.
        public static readonly ObjectiveDef SlayWyvern = new()
        {
            Id = "slay_wyvern", Name = "Slay the Wyvern", Task = "Defeat the Wyvern",
            Type = ObjectiveType.DefeatTarget, Target = EnemyDb.Wyvern, Main = true,
        };

        public static readonly ObjectiveDef CullGnawers = new()
        {
            Id = "cull_gnawers", Name = "Thin the Pack", Task = "Defeat Gnawers",
            Type = ObjectiveType.DefeatTarget, TargetType = EnemyType.Normal, Count = 6,
        };

        // Built around the old enemies and the Drowned Cathedral (both removed). Kept for reference / reuse.
        /*
        public static readonly ObjectiveDef DestroySource = new()
        {
            Id = "destroy_source", Name = "Destroy the Source", Task = "Defeat the Source of the Infection",
            Type = ObjectiveType.DefeatTarget, Target = EnemyDb.BlightHeart, Main = true,
        };

        // The design doc's example: Find the Source = interact with it once.
        public static readonly ObjectiveDef FindSource = new()
        {
            Id = "find_source", Name = "Find the Source", Task = "Find and examine the Source",
            Type = ObjectiveType.Interact, Thing = "The Source", Prompt = "Examine the Source",
            Guardian = EnemyDb.HollowWarden, Main = true,
        };

        // ---------------- Area 2: Drowned Cathedral ----------------
        public static readonly ObjectiveDef RingBells = new()
        {
            Id = "ring_bells", Name = "Toll for the Drowned", Task = "Ring the Drowned Bells",
            Type = ObjectiveType.Interact, Thing = "Drowned Bell", Prompt = "Ring the bell", Count = 2, Guarded = true,
        };

        public static readonly ObjectiveDef CullHounds = new()
        {
            Id = "cull_hounds", Name = "Still the Pack", Task = "Defeat Stalkers",
            Type = ObjectiveType.DefeatTarget, TargetType = EnemyType.Stalker, Count = 4,
        };

        public static readonly ObjectiveDef Reliquary = new()
        {
            Id = "reliquary", Name = "Recover the Reliquary", Task = "Collect the Reliquary",
            Type = ObjectiveType.CollectItem, Thing = "Reliquary", Guardian = EnemyDb.BellKeeper,
        };

        public static readonly ObjectiveDef SilenceHymn = new()
        {
            Id = "silence_hymn", Name = "Silence the Hymn Below", Task = "Defeat the Hymn Below",
            Type = ObjectiveType.DefeatTarget, Target = EnemyDb.HymnBelow, Main = true,
        };

        public static readonly ObjectiveDef BreakStones = new()
        {
            Id = "break_stones", Name = "Break the Choir", Task = "Shatter the Hymn Stones",
            Type = ObjectiveType.Interact, Thing = "Hymn Stone", Prompt = "Shatter the stone", Count = 3,
            Guarded = true, Main = true,
        };

        */

        public static readonly List<ObjectiveDef> All = new()
        {
            CullGnawers, CullBrutes, HuntStalkers, SupplyRun, SignalTower, SlayWyvern,
        };

        public static ObjectiveDef Get(string id) => All.FirstOrDefault(o => o.Id == id);
    }
}
