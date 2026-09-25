using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using SERAAC.Combat;
using SERAAC.Data;
using SERAAC.Meta;

namespace SERAAC.Trial
{
    // A party member's state for the duration of one trial (HP carries between rooms).
    public sealed class PartyMember
    {
        public CharacterDef Def;
        public int MaxHp, Damage, Speed;
        public int Hp;
        public bool Fallen => Hp <= 0;
    }

    // One Trial from entering to extraction or defeat.
    // Trial Loot (shards, found equipment) is at risk until Extract succeeds.
    public sealed class TrialRun
    {
        public AreaDef Area { get; private set; }
        public TrialDef Trial { get; private set; }
        public TrialMap Map { get; private set; }
        public Room Current { get; set; }
        public Random Rng { get; private set; }
        public List<PartyMember> Party { get; } = new(); // formation order, front first

        public int TrialShards { get; private set; }
        public List<string> FoundEquipment { get; } = new();
        public List<string> Consumables { get; } = new();
        public bool ObjectiveCompleted { get; private set; }
        public int RoomsCleared { get; private set; }

        // Characters met in this trial who join the Sanctum only if the party extracts.
        public List<string> PendingRecruits { get; } = new();

        // One-off things that happened this trial (e.g. "angel_blessed"), for dialogue conditions.
        public HashSet<string> Flags { get; } = new();

        public bool PartyWiped => Party.All(m => m.Fallen);

        public static TrialRun Start(Profile profile, AreaDef area, TrialDef trial, int seed)
        {
            var run = new TrialRun { Area = area, Trial = trial, Rng = new Random(seed) };
            foreach (var id in profile.PartyOrder)
            {
                var def = CharacterDb.Get(id);
                var s = Progression.Final(def, profile.Progress(id));
                run.Party.Add(new PartyMember { Def = def, MaxHp = s.Hp, Hp = s.Hp, Damage = s.Damage, Speed = s.Speed });
            }
            // At most one NPC per trial, chosen from whoever can currently appear.
            var npc = NpcDb.PickFor(new DialogueContext { Profile = profile, Run = run }, run.Rng);
            run.Map = TrialMap.Generate(trial, run.Rng, withNpcRoom: npc != null);
            foreach (var room in run.Map.Rooms.Values.Where(r => r.Type == RoomType.Npc)) room.Npc = npc;
            run.Current = run.Map.Entrance;
            run.Consumables.Add("tonic"); // a small starting kit so the item system is visible early
            profile.TrialsStarted++;
            ProfileStore.Save(profile);
            return run;
        }

        // ------------------------------------------------------------------ encounters

        // Global difficulty knobs, tuned against Relle-level party kits (see the self-test balance report).
        public const float EnemyHpMultiplier = 1.5f;
        public const float EnemyDamageMultiplier = 1.3f;

        public float EnemyScale(Room room) =>
            1f + 0.06f * Math.Max(0, room.Depth - 1) + 0.25f * (Trial.Tier - 1) + (room.Type == RoomType.Elite ? 0.1f : 0f);

        public List<EnemyDef> EncounterFor(Room room)
        {
            var list = new List<EnemyDef>();
            EnemyDef Front() => Area.FrontPool[Rng.Next(Area.FrontPool.Count)];
            EnemyDef Back() => Area.BackPool[Rng.Next(Area.BackPool.Count)];

            switch (room.Type)
            {
                case RoomType.Objective:
                    list.Add(Front()); list.Add(Front()); list.Add(Area.Boss);
                    break;
                case RoomType.Elite:
                    list.Add(Area.Elite); list.Add(Front()); list.Add(Back());
                    break;
                default:
                    int count = room.Depth <= 1 ? 3 : 4;
                    if (Trial.Tier >= 3) count = Math.Max(3, count);
                    for (int i = 0; i < count; i++) list.Add(i < 2 ? Front() : Back());
                    break;
            }
            return list;
        }

        public List<Combatant> BuildEnemyCombatants(Room room)
        {
            float scale = EnemyScale(room);
            return EncounterFor(room).Select(e =>
            {
                var c = Combatant.FromEnemy(e, scale);
                c.MaxHp = c.Hp = (int)Math.Round(c.MaxHp * EnemyHpMultiplier);
                c.Damage = Math.Max(1, (int)Math.Round(c.Damage * EnemyDamageMultiplier));
                return c;
            }).ToList();
        }

        // Alive members fill the front slots in the current party order.
        public List<Combatant> BuildPartyCombatants() =>
            Party.Where(m => !m.Fallen).Select(Combatant.FromMember).ToList();

        public void ApplyBattleResult(Battle battle)
        {
            foreach (var c in battle.AllPartyUnits)
                c.Member.Hp = Math.Max(0, c.Hp);

            // Keep the formation the player ended the fight in; fallen members go to the back.
            var newOrder = battle.Party.Where(c => c != null).OrderBy(c => c.Slot).Select(c => c.Member).ToList();
            newOrder.AddRange(Party.Where(m => !newOrder.Contains(m)));
            Party.Clear();
            Party.AddRange(newOrder);
        }

        // ------------------------------------------------------------------ room completion & loot

        public void OnCombatWon(Room room, Profile profile)
        {
            room.Cleared = true;
            RoomsCleared++;
            if (room.Type == RoomType.Objective && !ObjectiveCompleted)
            {
                ObjectiveCompleted = true;
                // Area Progression is saved immediately, independent of extraction.
                if (!profile.CompletedObjectives.Contains(Area.ObjectiveId))
                {
                    profile.CompletedObjectives.Add(Area.ObjectiveId);
                    ProfileStore.Save(profile);
                }
            }
            SpawnLoot(room);
        }

        public int ShardUnit(Room room) => (int)Math.Round((5 + 2 * room.Depth) * Trial.LootMultiplier);

        public void SpawnLoot(Room room)
        {
            int unit = ShardUnit(room);
            int Roll(float mult) => Math.Max(1, (int)Math.Round(unit * mult * (0.8 + Rng.NextDouble() * 0.4)));

            var drops = new List<LootPickup>();
            switch (room.Type)
            {
                case RoomType.Normal:
                    for (int i = 0, n = 2 + Rng.Next(2); i < n; i++) drops.Add(Shards(Roll(1f)));
                    if (Rng.NextDouble() < 0.2) drops.Add(Rng.NextDouble() < 0.65 ? ConsumableDrop() : EquipmentDrop());
                    break;
                case RoomType.Elite:
                    for (int i = 0; i < 3; i++) drops.Add(Shards(Roll(1.6f)));
                    drops.Add(EquipmentDrop());
                    break;
                case RoomType.Objective:
                    for (int i = 0; i < 4; i++) drops.Add(Shards(Roll(2.2f)));
                    drops.Add(EquipmentDrop());
                    break;
                case RoomType.Treasure:
                    for (int i = 0; i < 2; i++) drops.Add(Shards(Roll(1.3f)));
                    drops.Add(Rng.NextDouble() < 0.5 ? ConsumableDrop() : EquipmentDrop());
                    break;
            }
            PlaceDrops(room, drops);
        }

        public void PlaceDrops(Room room, List<LootPickup> drops)
        {
            var spots = room.Layout.LootSpots.OrderBy(_ => Rng.Next()).ToList();
            var feature = room.Layout.Feature;
            for (int i = 0; i < drops.Count; i++)
            {
                Vector2 pos;
                if (i < spots.Count) pos = new Vector2(spots[i].X + 0.5f, spots[i].Y + 0.5f);
                else pos = new Vector2(10.5f + (float)(Rng.NextDouble() * 4 - 2), 5.5f + (float)(Rng.NextDouble() * 2 - 1));
                if (room.Type == RoomType.Treasure && feature != Point.Zero)
                {
                    // Chest loot bursts out around the chest instead.
                    double ang = i * Math.PI * 2 / drops.Count;
                    pos = new Vector2(feature.X + 0.5f + (float)Math.Cos(ang) * 1.3f, feature.Y + 0.5f + (float)Math.Sin(ang) * 1.3f);
                    pos = ClampToFloor(room, pos, new Vector2(feature.X + 0.5f, feature.Y + 1.5f));
                }
                drops[i].Tile = pos;
                room.Pickups.Add(drops[i]);
            }
        }

        private static Vector2 ClampToFloor(Room room, Vector2 pos, Vector2 fallback) =>
            room.Layout.Solid((int)pos.X, (int)pos.Y) ? fallback : pos;

        private static LootPickup Shards(int amount) => new() { Kind = LootKind.Shards, Amount = amount };

        public LootPickup ConsumableDrop() =>
            new() { Kind = LootKind.Consumable, ItemId = ItemDb.Consumables[Rng.Next(ItemDb.Consumables.Count)].Id };

        public LootPickup EquipmentDrop()
        {
            var pool = ItemDb.Equipment.Where(e => e.MinTier <= Trial.Tier).ToList();
            return new() { Kind = LootKind.Equipment, ItemId = pool[Rng.Next(pool.Count)].Id };
        }

        public string Collect(LootPickup p)
        {
            p.Collected = true;
            switch (p.Kind)
            {
                case LootKind.Shards:
                    TrialShards += p.Amount;
                    return $"+{p.Amount} shards";
                case LootKind.Consumable:
                    Consumables.Add(p.ItemId);
                    return ItemDb.GetConsumable(p.ItemId).Name;
                default:
                    FoundEquipment.Add(p.ItemId);
                    return ItemDb.GetEquipment(p.ItemId).Name + " (equipment)";
            }
        }

        public void AddShards(int amount) => TrialShards += amount;
        public void AddFoundEquipment(string id) => FoundEquipment.Add(id);

        public void RecruitOnExtract(string charId)
        {
            if (!PendingRecruits.Contains(charId)) PendingRecruits.Add(charId);
        }

        // ------------------------------------------------------------------ items & healing

        public bool CanUse(ConsumableDef c) => c.Effect switch
        {
            ConsumableEffect.HealParty => Party.Any(m => !m.Fallen && m.Hp < m.MaxHp),
            ConsumableEffect.ReviveFallen => Party.Any(m => m.Fallen),
            _ => false,
        };

        public bool UseConsumable(string id)
        {
            var c = ItemDb.GetConsumable(id);
            if (c == null || !Consumables.Contains(id) || !CanUse(c)) return false;
            if (c.Effect == ConsumableEffect.HealParty) HealParty(c.Amount, reviveFallen: false);
            else foreach (var m in Party.Where(m => m.Fallen)) m.Hp = Math.Max(1, (int)(m.MaxHp * c.Amount));
            Consumables.Remove(id);
            return true;
        }

        public void HealParty(float fraction, bool reviveFallen)
        {
            foreach (var m in Party)
            {
                if (m.Fallen && !reviveFallen) continue;
                m.Hp = Math.Min(m.MaxHp, m.Hp + Math.Max(1, (int)Math.Round(m.MaxHp * fraction)));
            }
        }

        // ------------------------------------------------------------------ ending the trial

        // Extract: Trial Loot becomes Secured Loot, found equipment goes to the stash.
        public void Extract(Profile profile)
        {
            profile.SecuredShards += TrialShards;
            profile.Stash.AddRange(FoundEquipment);
            foreach (var id in PendingRecruits)
                if (!profile.IsUnlocked(id)) profile.UnlockedCharacters.Add(id);
            profile.Extractions++;
            ProfileStore.Save(profile);
        }

        // Defeat (or abandoning): everything unsecured is lost. Upgrades/equipment/area unlocks stay.
        public void Fail(Profile profile)
        {
            profile.Defeats++;
            ProfileStore.Save(profile);
        }
    }
}
