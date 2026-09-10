using MonoGame.Extended;
using SERAAC.Data.Characters;

namespace SERAAC.Data.UI
{
    public class SplitScreenPanel
    {
        public Role Side { get; set; }                // Wielder = left, Weapon = right
        public MinigameState Minigame { get; set; }
        public string OwnerBondId { get; set; }
        public RectangleF Bounds { get; set; }         // viewport region for this half of the screen
    }
}
