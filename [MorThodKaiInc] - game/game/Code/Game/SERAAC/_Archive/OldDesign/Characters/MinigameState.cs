using System.Collections.Generic;

namespace SERAAC.Data.Characters
{
    // Runtime state of an in-progress microgame on one split-screen side.
    public class MinigameState
    {
        public MinigameType MinigameType { get; set; }
        public MinigameDifficulty Difficulty { get; set; }
        public int TimeLimitMs { get; set; }
        public List<string> InputBuffer { get; set; } = new List<string>();
        public List<string> TargetPattern { get; set; } = new List<string>();
        public bool? IsSuccess { get; set; }        // null = still in progress
        public float ElapsedMs { get; set; }         // drive via GameTime in Update()
    }
}
