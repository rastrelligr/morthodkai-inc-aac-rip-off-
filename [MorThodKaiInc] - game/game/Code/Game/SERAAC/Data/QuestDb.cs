using System.Collections.Generic;
using System.Linq;

namespace SERAAC.Data
{
    public enum QuestGoal
    {
        KillInfected,        // defeat Target enemies (any trials, counted as you go)
        WinEliteWith,        // win an Elite fight with GoalCharacter in the party

        KillMathCGBoss
    }

    public sealed class QuestDef
    {
        public string Id;
        public string Name;
        public string Giver;
        public string Description;
        public QuestGoal Goal;
        public int Target = 1;
        public string GoalCharacter;
        public string RewardText;
    }

    public static class QuestDb
    {
        public static readonly QuestDef MarenHunt = new()
        {
            Id = "maren_hunt", Name = "Thin the Herd", Giver = "Maren",
            Description = "Defeat 8 infected in any Trials, then find Maren again.",
            Goal = QuestGoal.KillInfected, Target = 8,
            RewardText = "Butcher's Hatchet (weapon)",
        };

        public static readonly QuestDef Reflection = new()
        {
            Id = "reflection_trial", Name = "Through the Glass", Giver = "The Reflection",
            Description = "Win a fight against an Elite with Relle in your party, then find the Reflection again.",
            Goal = QuestGoal.WinEliteWith, GoalCharacter = "relle",
            RewardText = "Unlocks Elle (Inverted Relle)",
        };

        public static readonly QuestDef MathCG = new()
        {
            Id = "mathcg_trial",
            Name = "Finish the fight",
            Giver = "Topsala Tap",
            Description = "Defeat the MathCG Boss, then find Topsala Tap again.",
            Goal = QuestGoal.KillMathCGBoss,
            RewardText = " Default Cube (weapon)",
        };

        public static readonly List<QuestDef> All = new() { MarenHunt, Reflection, MathCG };

        public static QuestDef Get(string id) => All.FirstOrDefault(q => q.Id == id);
    }
}
