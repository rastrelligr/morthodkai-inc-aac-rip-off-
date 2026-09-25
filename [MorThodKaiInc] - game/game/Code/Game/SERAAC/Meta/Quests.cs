using System.Collections.Generic;
using System.Linq;
using SERAAC.Combat;
using SERAAC.Data;
using SERAAC.Trial;

namespace SERAAC.Meta
{
    // Quest progress rules. Quests are taken and claimed through NPC dialogue (Data/NpcDb.cs).
    public static class Quests
    {
        public static bool IsActive(Profile p, QuestDef q) => p.Quest(q.Id) is { Claimed: false };
        public static bool IsDone(Profile p, QuestDef q) => p.Quest(q.Id) is { } s && s.Progress >= q.Target;
        public static bool IsClaimed(Profile p, QuestDef q) => p.Quest(q.Id) is { Claimed: true };
        public static int Progress(Profile p, QuestDef q) => p.Quest(q.Id)?.Progress ?? 0;

        public static void Start(Profile p, QuestDef q)
        {
            if (p.Quest(q.Id) == null) p.Quests[q.Id] = new QuestState();
            ProfileStore.Save(p);
        }

        public static void Claim(Profile p, QuestDef q)
        {
            var s = p.Quest(q.Id);
            if (s == null) return;
            s.Claimed = true;
            ProfileStore.Save(p);
        }

        // Called after every won fight. Returns names of quests that just became complete.
        public static List<string> OnVictory(Profile p, Room room, Battle battle)
        {
            var finished = new List<string>();
            foreach (var q in QuestDb.All.Where(q => IsActive(p, q) && !IsDone(p, q)))
            {
                var s = p.Quest(q.Id);
                switch (q.Goal)
                {
                    case QuestGoal.KillInfected:
                        s.Progress = System.Math.Min(q.Target, s.Progress + battle.Fallen.Count(c => !c.IsParty));
                        break;
                    case QuestGoal.WinEliteWith:
                        if (room.Type == RoomType.Elite && battle.AllPartyUnits.Any(c => c.Member.Def.Id == q.GoalCharacter))
                            s.Progress = q.Target;
                        break;
                }
                if (IsDone(p, q)) finished.Add(q.Name);
            }
            ProfileStore.Save(p);
            return finished;
        }
    }
}
