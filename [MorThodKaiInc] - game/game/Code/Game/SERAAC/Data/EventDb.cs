using System.Collections.Generic;

namespace SERAAC.Data
{
    public enum EventOutcome
    {
        Nothing,
        HurtPartyForShards,   // lose Amount (fraction of current HP) on each member, gain shards
        GainConsumable,
        GainEquipment,
        HealParty,
        Gamble,               // 50%: equipment, 50%: a fight breaks out
    }

    public sealed class EventOption
    {
        public string Label;
        public string Hint;
        public EventOutcome Outcome;
        public float Amount;
        public int Shards;
    }

    // Special Room "Event": a short choice with a consequence.
    public sealed class EventDef
    {
        public string Id;
        public string Title;
        public string Text;
        public List<EventOption> Options;
    }

    public static class EventDb
    {
        public static readonly List<EventDef> All = new()
        {
            new()
            {
                Id = "idol", Title = "Whispering Idol",
                Text = "A goat-headed statue hums in the dark. It offers shards... for a taste of blood.",
                Options = new()
                {
                    new() { Label = "Offer blood", Hint = "Every member loses 25% current HP. Gain shards.",
                        Outcome = EventOutcome.HurtPartyForShards, Amount = 0.25f, Shards = 30 },
                    new() { Label = "Walk away", Hint = "Nothing happens.", Outcome = EventOutcome.Nothing },
                },
            },
            new()
            {
                Id = "camp", Title = "Abandoned Camp",
                Text = "Someone sheltered here recently. Their packs are still full. They are not coming back.",
                Options = new()
                {
                    new() { Label = "Search the packs", Hint = "Find a consumable.", Outcome = EventOutcome.GainConsumable },
                    new() { Label = "Rest by the embers", Hint = "Heal the party 20%.", Outcome = EventOutcome.HealParty, Amount = 0.2f },
                },
            },
            new()
            {
                Id = "cocoon", Title = "Pulsing Cocoon",
                Text = "Something glints inside a cocoon of infected flesh. Something else is still moving in there.",
                Options = new()
                {
                    new() { Label = "Cut it open", Hint = "50%: equipment. 50%: a fight.", Outcome = EventOutcome.Gamble },
                    new() { Label = "Leave it", Hint = "Nothing happens.", Outcome = EventOutcome.Nothing },
                },
            },
        };
    }
}
