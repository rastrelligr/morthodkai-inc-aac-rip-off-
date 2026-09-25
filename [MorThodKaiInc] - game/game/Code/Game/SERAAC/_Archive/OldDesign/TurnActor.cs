namespace SERAAC.Data.Combat
{
    public class TurnActor
    {
        public TurnActorType ActorType { get; set; }
        public string ActorRefId { get; set; }        // bondId or enemyId
    }
}
