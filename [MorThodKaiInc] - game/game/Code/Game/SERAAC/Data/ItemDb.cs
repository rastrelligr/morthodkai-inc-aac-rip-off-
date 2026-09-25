using System.Collections.Generic;
using System.Linq;

namespace SERAAC.Data
{
    public enum EquipSlot { Weapon, Charm }

    public sealed class EquipmentDef
    {
        public string Id;
        public string Name;
        public EquipSlot Slot;
        public int Hp, Damage, Speed;
        public string Flavor;
        public int MinTier = 1; // earliest trial tier where it can drop

        public string ArtId => "equip_" + Id;

        public string StatText()
        {
            var parts = new List<string>();
            if (Hp != 0) parts.Add($"{Hp:+#;-#} HP");
            if (Damage != 0) parts.Add($"{Damage:+#;-#} DMG");
            if (Speed != 0) parts.Add($"{Speed:+#;-#} SPD");
            return string.Join("  ", parts);
        }
    }

    public enum ConsumableEffect { HealParty, ReviveFallen }

    // Trial-only item loot: usable during the trial, gone when it ends.
    public sealed class ConsumableDef
    {
        public string Id;
        public string Name;
        public string Description;
        public ConsumableEffect Effect;
        public float Amount; // fraction of max HP

        public string ArtId => "item_" + Id;
    }

    public static class ItemDb
    {
        public static readonly List<EquipmentDef> Equipment = new()
        {
            new() { Id = "rusted_blade", Name = "Rusted Blade", Slot = EquipSlot.Weapon, Damage = 2,
                Flavor = "Standard issue, before the sky broke." },
            new() { Id = "bonded_fang", Name = "Bonded Fang", Slot = EquipSlot.Weapon, Damage = 3, Speed = -1,
                Flavor = "Still twitches when blood is near." },
            new() { Id = "hook_knife", Name = "Hook Knife", Slot = EquipSlot.Weapon, Damage = 1, Speed = 2,
                Flavor = "Light, cruel, quick." },
            new() { Id = "tide_hook", Name = "Tide Hook", Slot = EquipSlot.Weapon, Damage = 5, Speed = -1, MinTier = 3,
                Flavor = "Pulled from the flooded nave. Always wet." },
            // Quest reward only (MinTier above any trial, so it never drops).
            new() { Id = "butcher_hatchet", Name = "Butcher's Hatchet", Slot = EquipSlot.Weapon, Damage = 4, Speed = -1, MinTier = 99,
                Flavor = "Maren's. Don't ask what it butchered." },
            new() { Id = "pale_locket", Name = "Pale Locket", Slot = EquipSlot.Charm, Hp = 8,
                Flavor = "A photo of someone nobody remembers." },
            new() { Id = "chapel_bell", Name = "Chapel Bell", Slot = EquipSlot.Charm, Hp = 3, Speed = 1,
                Flavor = "Rings on its own at midnight." },
            new() { Id = "goat_horn", Name = "Goat Horn Charm", Slot = EquipSlot.Charm, Damage = 2, Hp = -4,
                Flavor = "Warm to the touch. Why does it feel familiar?" },
            new() { Id = "choir_veil", Name = "Choir Veil", Slot = EquipSlot.Charm, Speed = 2, Hp = 4, MinTier = 3,
                Flavor = "Muffles the singing. Mostly." },
            new() { Id = "the_cube", Name = "Default Cube", Slot = EquipSlot.Weapon, Damage = 20, Speed = -1, MinTier = 99,
                Flavor = "It's always there." },
        };

        public static readonly List<ConsumableDef> Consumables = new()
        {
            new() { Id = "tonic", Name = "Tonic", Effect = ConsumableEffect.HealParty, Amount = 0.3f,
                Description = "Heal every living party member for 30% max HP." },
            new() { Id = "ember_salt", Name = "Ember Salt", Effect = ConsumableEffect.ReviveFallen, Amount = 0.3f,
                Description = "Revive every fallen party member at 30% HP." },
        };

        public static EquipmentDef GetEquipment(string id) => Equipment.FirstOrDefault(e => e.Id == id);
        public static ConsumableDef GetConsumable(string id) => Consumables.FirstOrDefault(c => c.Id == id);
    }
}
