namespace SERAAC.Data.UI
{
    public class SyncedBattleTurn
    {
        public string BondId { get; set; }
        public SplitScreenPanel LeftPanel { get; set; }
        public SplitScreenPanel RightPanel { get; set; }
        public int SyncDeadlineMs { get; set; }
        public bool? ResultSyncedSuccess { get; set; } // did both sides land it together?
    }
}
