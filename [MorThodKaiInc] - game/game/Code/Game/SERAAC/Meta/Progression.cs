using SERAAC.Data;

namespace SERAAC.Meta
{
    public readonly struct Stats
    {
        public readonly int Hp, Damage, Speed;
        public Stats(int hp, int dmg, int spd) { Hp = hp; Damage = dmg; Speed = spd; }
    }

    // Character Progression rules: permanent upgrades bought with Secured Loot, plus equipment.
    public static class Progression
    {
        public const int MaxLevel = 5;
        public const int HpPerLevel = 4;
        public const int DamagePerLevel = 1;
        public const int SpeedPerLevel = 1;

        // Cost rises with level: 15, 35, 65, 105, 155.
        public static int UpgradeCost(int currentLevel) => 15 + 15 * currentLevel + 5 * currentLevel * currentLevel;

        public static int PerLevel(UpgradeStat s) => s switch
        {
            UpgradeStat.Hp => HpPerLevel,
            UpgradeStat.Damage => DamagePerLevel,
            _ => SpeedPerLevel,
        };

        public static Stats Base(CharacterDef c, CharacterProgress p) => new(
            c.BaseHp + p.HpLevel * HpPerLevel,
            c.BaseDamage + p.DamageLevel * DamagePerLevel,
            c.BaseSpeed + p.SpeedLevel * SpeedPerLevel);

        // Final stats used in a trial: base + upgrades + equipped items.
        public static Stats Final(CharacterDef c, CharacterProgress p)
        {
            var b = Base(c, p);
            int hp = b.Hp, dmg = b.Damage, spd = b.Speed;
            foreach (var id in new[] { p.Weapon, p.Charm })
            {
                var e = ItemDb.GetEquipment(id);
                if (e == null) continue;
                hp += e.Hp; dmg += e.Damage; spd += e.Speed;
            }
            return new Stats(System.Math.Max(1, hp), System.Math.Max(1, dmg), System.Math.Max(0, spd));
        }

        public static bool TryBuyUpgrade(Profile profile, string charId, UpgradeStat stat)
        {
            var p = profile.Progress(charId);
            int lvl = p.Level(stat);
            if (lvl >= MaxLevel) return false;
            int cost = UpgradeCost(lvl);
            if (profile.SecuredShards < cost) return false;
            profile.SecuredShards -= cost;
            p.AddLevel(stat);
            ProfileStore.Save(profile);
            return true;
        }

        // Swap an item from the stash into a slot; the previous item goes back to the stash.
        public static void Equip(Profile profile, string charId, EquipSlot slot, string itemId)
        {
            var p = profile.Progress(charId);
            string old = slot == EquipSlot.Weapon ? p.Weapon : p.Charm;
            if (itemId != null && !profile.Stash.Remove(itemId)) return;
            if (old != null) profile.Stash.Add(old);
            if (slot == EquipSlot.Weapon) p.Weapon = itemId; else p.Charm = itemId;
            ProfileStore.Save(profile);
        }
    }
}
