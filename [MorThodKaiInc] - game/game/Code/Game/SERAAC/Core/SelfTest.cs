using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using SERAAC.Combat;
using SERAAC.Data;
using SERAAC.Meta;
using SERAAC.Trial;

namespace SERAAC.Core
{
    // Headless checks + balance numbers, no window. Run with:  dotnet run -- --selftest
    // Writes selftest.txt next to the exe and returns a non-zero exit code on failure.
    public static class SelfTest
    {
        private static readonly StringBuilder Log = new();
        private static int _failures;

        public static int Run()
        {
            ProfileStore.Enabled = false;
            // --balance-only skips the structural checks (quicker when tuning numbers).
            if (!Environment.GetCommandLineArgs().Contains("--balance-only"))
            {
                CheckLayouts();
                CheckMaps();
                CheckContent();
                CheckRelleMechanics();
            }
            BalanceReport();

            Say(_failures == 0 ? "\nALL CHECKS PASSED" : $"\n{_failures} CHECK(S) FAILED");
            var path = Path.Combine(AppContext.BaseDirectory, "selftest.txt");
            File.WriteAllText(path, Log.ToString());
            Console.WriteLine(Log.ToString());
            return _failures == 0 ? 0 : 1;
        }

        private static void Say(string s) => Log.AppendLine(s);

        private static void Fail(string s)
        {
            _failures++;
            Say("  FAIL: " + s);
        }

        // Every door, loot spot and the feature must be reachable in every template/mirror.
        private static void CheckLayouts()
        {
            Say("== Room layouts ==");
            for (int t = 0; t < RoomLayout.Templates.Length; t++)
            {
                foreach (var row in RoomLayout.Templates[t])
                    if (row.Length != RoomLayout.InnerCols) Fail($"template {t} row width {row.Length}");
                if (RoomLayout.Templates[t].Length != RoomLayout.InnerRows) Fail($"template {t} height");

                foreach (bool mirror in new[] { false, true })
                {
                    var layout = RoomLayout.Build(t, mirror, RoomLayout.AllDirs);
                    var reach = layout.Reachable(RoomLayout.EntryTile(Dir.North));
                    foreach (var d in RoomLayout.AllDirs)
                        if (!reach.Contains(RoomLayout.EntryTile(d))) Fail($"template {t}{(mirror ? "m" : "")}: door {d} unreachable");
                    foreach (var l in layout.LootSpots)
                        if (!reach.Contains(l)) Fail($"template {t}: loot spot {l} unreachable");
                    var f = layout.Feature;
                    bool featureReach = new[] { new Point(f.X + 1, f.Y), new Point(f.X - 1, f.Y), new Point(f.X, f.Y + 1), new Point(f.X, f.Y - 1) }
                        .Any(reach.Contains);
                    if (!featureReach) Fail($"template {t}: feature unreachable");
                }
                Say($"  template {t}: ok ({RoomLayout.Build(t, false, RoomLayout.AllDirs).LootSpots.Count} loot spots)");
            }
        }

        private static void CheckContent()
        {
            Say("\n== Content ==");
            foreach (var id in UI.Icons.MalformedIds()) Fail($"icon '{id}' is not 12x12");
            var iconIds = UI.Icons.AllIds.ToHashSet();
            foreach (var e in ItemDb.Equipment) if (!iconIds.Contains(e.Id)) Fail($"equipment '{e.Id}' has no icon");
            foreach (var c in ItemDb.Consumables) if (!iconIds.Contains(c.Id)) Fail($"consumable '{c.Id}' has no icon");

            foreach (var npc in NpcDb.All)
                foreach (var (nodeId, node) in npc.Nodes)
                    foreach (var choice in node.Choices)
                        if (choice.Next != "end" && !npc.Nodes.ContainsKey(choice.Next))
                            Fail($"{npc.Id}/{nodeId}: choice '{choice.Label}' goes to missing node '{choice.Next}'");
            foreach (var q in QuestDb.All)
                if (!NpcDb.All.Any(n => n.Name == q.Giver))
                    Say($"  WARN: quest '{q.Id}' can't be started yet: giver '{q.Giver}' is not in NpcDb.All");
            Say($"  {UI.Icons.AllIds.Count()} icons, {NpcDb.All.Count} NPCs, {QuestDb.All.Count} quests checked");
        }

        // Scripted checks of Relle's kit against the designer spec.
        private static void CheckRelleMechanics()
        {
            Say("\n== Relle mechanics ==");
            var rng = new Random(1);
            PartyMember Member(CharacterDef d) => new() { Def = d, MaxHp = 999, Hp = 999, Damage = d.BaseDamage, Speed = d.BaseSpeed };
            List<Combatant> Dummies(int n) => Enumerable.Range(0, n).Select(_ =>
            {
                var c = Combatant.FromEnemy(EnemyDb.Bloated, 1f);
                c.MaxHp = c.Hp = 99999;
                c.Actions = new List<ActionDef> { CommonActions.Defend };
                return c;
            }).ToList();

            // Feast: skills 1-3 stack, skill 4 crits and consumes.
            var relle = Combatant.FromMember(Member(CharacterDb.Relle));
            var b = new Battle(new[] { relle }, Dummies(2), rng);
            var pierce = CharacterDb.Relle.Skills.First(s => s.Id == "pierce");
            for (int i = 0; i < 3; i++)
            {
                relle.Plan = new PlannedAction { Action = pierce, Target = b.Enemies[0] };
                b.Execute(relle);
            }
            if (relle.Feast != 3) Fail($"Feast should be 3 after three skills, is {relle.Feast}");
            relle.Plan = new PlannedAction { Action = pierce, Target = b.Enemies[0] };
            var ev = b.Execute(relle);
            if (!ev.Any(e => e.Type == BattleEventType.Damage && e.Crit)) Fail("4th skill with 3 Feast did not crit");
            if (relle.Feast != 0) Fail($"Feast should reset to 0 after the Feast crit, is {relle.Feast}");
            if (!ev.Any(e => e.Type == BattleEventType.Heal && e.Target == relle) && relle.Hp < relle.MaxHp) Fail("Feast lifesteal did not heal");

            // SP gating + Blood Feast: 2 hits on every enemy, then a consistent shuffled formation.
            var feast = CharacterDb.Relle.Skills.First(s => s.Id == "blood_feast");
            relle.Sp = 1;
            if (b.CanUse(relle, feast)) Fail("Blood Feast usable with 1 SP");
            relle.Sp = 5;
            relle.Plan = new PlannedAction { Action = feast };
            ev = b.Execute(relle);
            int hits = ev.Count(e => e.Type == BattleEventType.Damage);
            if (hits != 4) Fail($"Blood Feast vs 2 enemies should land 4 hits, landed {hits}");
            if (relle.Sp != 3) Fail($"Blood Feast should cost 2 SP (5 -> 3), left {relle.Sp}");
            for (int s = 0; s < Battle.Slots; s++)
                if (b.Enemies[s] != null && b.Enemies[s].Slot != s) Fail("shuffle left a unit whose Slot doesn't match its position");
            if (b.Enemies.Count(e => e != null) != 2) Fail("shuffle lost or duplicated an enemy");

            // Undercut knocks its target 2 slots back.
            var elle = Combatant.FromMember(Member(CharacterDb.RelleInverted));
            elle.Sp = 5;
            var b2 = new Battle(new[] { elle }, Dummies(3), rng);
            var front = b2.Enemies[0];
            elle.Plan = new PlannedAction { Action = CharacterDb.RelleInverted.Skills.First(s => s.Id == "undercut"), Target = front };
            b2.Execute(elle);
            if (front.Slot != 2) Fail($"Undercut should knock Front 1 to slot 3 (index 2), it is at {front.Slot}");
            Say("  Feast stacking / crit / lifesteal, SP cost, multi-hit, shuffle, knockback: checked");
        }

        private static void CheckMaps()
        {
            Say("\n== Trial map generation (300 seeds each) ==");
            foreach (var area in WorldDb.Areas)
            {
                foreach (var trial in area.Trials)
                {
                    int shortMaps = 0, minExtractDepth = int.MaxValue;
                    double avgDepth = 0;
                    for (int seed = 0; seed < 300; seed++)
                    {
                        var map = TrialMap.Generate(trial, new Random(seed));
                        if (map.Rooms.Count < trial.RoomCount) shortMaps++;
                        if (map.Rooms.Values.Any(r => r.Depth < 0)) Fail($"{trial.Id} seed {seed}: unreachable room");
                        int ex = map.Rooms.Values.Count(r => r.Type == RoomType.Extraction);
                        if (ex != trial.ExtractionPoints) Fail($"{trial.Id} seed {seed}: {ex} extraction points");
                        if (trial.HasMainObjective && map.Rooms.Values.Count(r => r.Type == RoomType.Objective) != 1)
                            Fail($"{trial.Id} seed {seed}: objective room missing");
                        foreach (var r in map.Rooms.Values.Where(r => r.Type == RoomType.Extraction))
                            minExtractDepth = Math.Min(minExtractDepth, r.Depth);
                        foreach (var r in map.Rooms.Values)
                            foreach (var (d, n) in r.Links)
                                if (n.Links.GetValueOrDefault(RoomLayout.Opposite(d)) != r) Fail($"{trial.Id}: one-way link");
                        avgDepth += map.MaxDepth;
                    }
                    if (shortMaps > 0) Fail($"{trial.Id}: {shortMaps} maps smaller than {trial.RoomCount} rooms");
                    Say($"  {trial.Name,-20} rooms={trial.RoomCount,2}  avg max depth={avgDepth / 300:0.0}  closest extraction depth={minExtractDepth}");
                }
            }
        }

        // Auto-plays each trial clearing every combat room nearest-first, resting at shrines.
        // Run for a fresh party and a mid-game "veteran" (upgrades + gear) to see the curve.
        private static void BalanceReport()
        {
            var veteran = Profile.CreateNew();
            foreach (var c in CharacterDb.All)
            {
                var p = veteran.Progress(c.Id);
                p.HpLevel = 3; p.DamageLevel = 3; p.SpeedLevel = 2;
                p.Weapon = "bonded_fang"; p.Charm = "pale_locket";
            }





            foreach (var (label, profile) in new[] { ("fresh default party (Relle, Bram, Winter, Noa)", Profile.CreateNew()), ("veteran party (lv 3/3/2 + gear)", veteran) })
            {
                Say($"\n== Balance: {label}, AI-controlled, 400 full-clear runs per trial ==");
                foreach (var area in WorldDb.Areas)
                    foreach (var trial in area.Trials)
                        SimulateTrial(profile, area, trial);
            }
            Say("  (AI plays worse than a person; use these for relative tuning, not pass/fail)");

            // Roster parity: swap one member of the default party for someone who plays a similar
            // position, and compare on the second trial. Big gaps mean a character is out of line.
            Say("\n== Roster parity: Quarantine Block, fresh party, one member swapped ==");
            var quarantine = WorldDb.AbandonedCity.Trials[1];
            var swaps = new (string label, string[] order)[]
            {
                ("default  Relle / Bram / Winter / Noa", new[] { "relle", "warden", "hunter", "priest" }),
                ("Pillow  for Relle", new[] { "knight", "warden", "hunter", "priest" }),
                ("Mireille for Winter", new[] { "relle", "warden", "occultist", "priest" }),
                ("Elle    for Noa", new[] { "relle", "warden", "hunter", "relle_inv" }),
                ("Pillow  for Bram", new[] { "relle", "knight", "hunter", "priest" }),
            };
            foreach (var (label, order) in swaps)
            {
                var p = Profile.CreateNew();
                p.UnlockedCharacters = CharacterDb.All.Select(c => c.Id).ToList();
                p.PartyOrder = order.ToList();
                Say($"  [{label}]");
                SimulateTrial(p, WorldDb.AbandonedCity, quarantine);
            }
        }

        private static void SimulateTrial(Profile profile, AreaDef area, TrialDef trial)
        {
            const int runs = 400;
            int survived = 0, turns = 0, fights = 0, shards = 0;
            var byType = new Dictionary<RoomType, (int won, int total)>();
            for (int seed = 0; seed < runs; seed++)
            {
                var run = TrialRun.Start(profile, area, trial, seed);
                bool dead = false;
                foreach (var room in run.Map.Rooms.Values.OrderBy(r => r.Depth))
                {
                    if (room.Type == RoomType.Rest) run.HealParty(0.4f, reviveFallen: true);
                    if (room.Type == RoomType.Treasure) run.SpawnLoot(room);
                    if (!room.IsCombatRoom) continue;

                    var battle = new Battle(run.BuildPartyCombatants(), run.BuildEnemyCombatants(room), run.Rng);
                    int t = 0;
                    while (battle.Outcome == BattleOutcome.Ongoing && t++ < 60)
                    {
                        CombatAI.PlanAll(battle, battle.Living, run.Rng);
                        foreach (var c in battle.ResolveOrder())
                        {
                            battle.Execute(c);
                            if (battle.Outcome != BattleOutcome.Ongoing) break;
                        }
                        if (battle.Outcome == BattleOutcome.Ongoing) battle.EndTurn();
                    }
                    run.ApplyBattleResult(battle);
                    turns += t;
                    fights++;
                    bool won = battle.Outcome == BattleOutcome.Victory;
                    var rec = byType.GetValueOrDefault(room.Type);
                    byType[room.Type] = (rec.won + (won ? 1 : 0), rec.total + 1);
                    if (!won) { dead = true; break; }
                    run.OnCombatWon(room, new Profile());
                }
                foreach (var p in run.Map.Rooms.Values.SelectMany(r => r.Pickups).Where(p => !p.Collected)) run.Collect(p);
                if (!dead) { survived++; shards += run.TrialShards; }
            }
            Say($"  {trial.Name,-20} full-clear survival {survived * 100 / runs,3}%   avg turns/fight {(double)turns / Math.Max(1, fights):0.0}" +
                $"   avg shards on success {(survived > 0 ? shards / survived : 0)}");
            Say("      " + string.Join("   ", byType.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value.won * 100 / Math.Max(1, k.Value.total)}%")));
        }
    }
}
