using System.Collections.Generic;

namespace SERAAC.Data.Minigames
{
    public static class MinigameRegistry
    {
        public static readonly Dictionary<MinigameType, MinigameConfig> All = new Dictionary<MinigameType, MinigameConfig>();
        // TODO: populate at game startup (e.g. in Game1.LoadContent())
    }
}
