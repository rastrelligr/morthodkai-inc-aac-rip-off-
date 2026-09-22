using System;
using System.Collections.Generic;
using SERAAC.Data.Characters;

namespace SERAAC.Data.Minigames
{
    public class MinigameConfig
    {
        public MinigameType MinigameType { get; set; }
        // factory per difficulty -> builds a fresh MinigameState instance
        public Dictionary<MinigameDifficulty, Func<MinigameState>> DifficultyVariants { get; set; }
            = new Dictionary<MinigameDifficulty, Func<MinigameState>>();
    }
}
