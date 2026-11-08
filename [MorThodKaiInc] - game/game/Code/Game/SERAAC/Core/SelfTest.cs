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
            Mods.LoadContent();   // check the game the way it plays, mods included (--no-mods for the base game)
            // --balance-only skips the structural checks (quicker when tuning numbers).
            if (!Environment.GetCommandLineArgs().Contains("--balance-only"))
            {
                CheckMods();
                CheckLayouts();
                CheckMaps();
                CheckObjectivesAndEnemyTypes();
                CheckPacks();
                CheckContent();
                CheckRelleMechanics();
                CheckSpecialAbilities();
                CheckMaraAndElian();
                CheckStagesAndFx();
                CheckAudio();
            }
            // --no-balance skips the slow auto-played runs.
            if (!Environment.GetCommandLineArgs().Contains("--no-balance")) BalanceReport();

            Say(_failures == 0 ? "\nALL CHECKS PASSED" : $"\n{_failures} CHECK(S) FAILED");
            var path = Path.Combine(AppContext.BaseDirectory, "selftest.txt");
            File.WriteAllText(path, Log.ToString());
            Console.WriteLine(Log.ToString());
            return _failures == 0 ? 0 : 1;
        }

        private static void Say(string s) => Log.AppendLine(s);

        // Every sound id and music track in Core/Audio.cs points at a file that decodes.
        private static void CheckAudio()
        {
            Say("== Audio ==");
            int before = _failures;
            foreach (var p in Audio.Problems()) Fail(p);
            if (_failures == before) Say($"  {Audio.Files.Count} sound ids and {Audio.Tracks.Count} music ids, all files decode");
        }

        private static void Fail(string s)
        {
            _failures++;
            Say("  FAIL: " + s);
        }

        // Every mod in Mods/ loaded without errors (the rest of the checks then cover what they added).
        private static void CheckMods()
        {
            Say("== Mods ==");
            if (Mods.Disabled) { Say("  skipped (--no-mods)"); return; }
            if (Mods.All.Count == 0) { Say($"  none in {Mods.Root}"); return; }
            foreach (var mod in Mods.All)
            {
                Say($"  {mod.Title} {mod.Version} ({System.IO.Path.GetFileName(mod.Folder)}){(mod.Loaded ? "" : mod.Enabled ? " - not loaded" : " - off")}");
                foreach (var note in mod.Notes) Say("    " + note);
                foreach (var err in mod.Errors) Fail($"mod '{mod.Id}': {err}");
            }
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

        // Art/Stages, Art/Dialogue and Art/Fx: files parse, every background named anywhere exists, and
        // every effect the combat screen fires exists. Fights without art of their own yet are listed,
        // not failed (they use a normal background, or a plain gradient).
        private static void CheckStagesAndFx()
        {
            Say("== Combat backgrounds and effects ==");
            var bgFile = Stage.BackgroundDb.File;
            if (bgFile.Error != null) Fail("Art/Stages/backgrounds.json: " + bgFile.Error);
            Say($"  {Stage.BackgroundDb.All.Count} backgrounds listed");
            if (Stage.BackgroundDb.All.Count == 0) Fail("no backgrounds listed in Art/Stages/backgrounds.json");
            foreach (var missing in Stage.BackgroundDb.MissingImages())
                Fail($"background '{missing}' is listed but Art/Stages/{missing}.png does not exist");
            // Typos in area / trial ids would silently make a background never show.
            var areaIds = WorldDb.Areas.Select(a => a.Id).ToHashSet();
            var trialIds = WorldDb.Areas.SelectMany(a => a.Trials).Select(t => t.Id).ToHashSet();
            foreach (var b in Stage.BackgroundDb.All)
            {
                if (b.Area != null && !areaIds.Contains(b.Area)) Fail($"background '{b.Image}': unknown area '{b.Area}' (areas: {string.Join(", ", areaIds)})");
                foreach (var t in b.Trial)
                    if (!trialIds.Contains(t)) Fail($"background '{b.Image}': unknown trial '{t}' (trials: {string.Join(", ", trialIds)})");
            }

            // What each trial's fights can show. Elite / boss only where that trial can have them (see TrialMap).
            foreach (var area in WorldDb.Areas)
                foreach (var trial in area.Trials)
                {
                    Say($"  {area.Name} / {trial.Name} ({trial.Id})");
                    var kinds = new List<(string, EnemyDef)> { ("normal", null) };
                    if (trial.RoomCount >= 10) kinds.Add(("elite", area.Elite));
                    foreach (var leader in trial.Objectives.Select(o => o.RoomLeader).Where(l => l != null).Distinct())
                        kinds.Add((leader == area.Boss ? "boss" : "elite", leader));
                    foreach (var (kind, enemy) in kinds)
                    {
                        var ids = enemy != null ? new[] { enemy.Id } : Array.Empty<string>();
                        var options = Stage.BackgroundDb.Candidates(area.Id, trial.Id, kind, ids);
                        if (options.Count == 0) { Say($"    {kind,-7} -> (none yet: plain gradient)"); continue; }
                        string note = !options[0].Use.Contains(kind) ? $"   (no {kind} background yet, using a normal one)"
                                    : options.All(b => b.Trial.Count == 0) ? "   (the area's)" : "";
                        Say($"    {kind,-7} -> {string.Join(", ", options.Select(b => b.Image))}{note}");
                    }
                }

            // Dialogue backgrounds named on NPCs or dialogue nodes must exist in Art/Dialogue or Art/Stages.
            foreach (var npc in NpcDb.All)
                foreach (var (where, name) in new[] { ("", npc.Background) }.Concat(npc.Nodes.Select(n => ($" node '{n.Key}'", n.Value.Background))))
                {
                    if (string.IsNullOrEmpty(name) || name == "none") continue;
                    bool found = new[] { "Dialogue", "Stages" }.Any(dir => File.Exists(LiveJson.ArtPath(Path.Combine(dir, name + ".png"))))
                                 || Stage.BackgroundDb.LayerSuffixes.Any(s => File.Exists(LiveJson.ArtPath(Path.Combine("Stages", name + s + ".png"))));
                    if (!found) Fail($"{npc.Name}{where}: background '{name}' not found in Art/Dialogue or Art/Stages");
                }

            var presets = Fx.ParticleSystem.Presets;
            if (presets.Error != null) Fail("Art/Fx/particles.json: " + presets.Error);
            var needed = new List<string> { "slash", "impact", "crit", "cast", "cast_heal", "dash", "step", "heal", "cleanse",
                "death", "feast", "victory", "blight_tick", "status_guard", "status_taunt", "status_blight", "status_mark",
                "status_slow", "status_protected", "status_wall" };
            foreach (var a in CharacterDb.All.SelectMany(c => c.Skills).Concat(EnemyDb.All.SelectMany(e => e.Skills)))
            {
                if (a.HitFx != null) needed.Add(a.HitFx);
                if (a.CastFx != null) needed.Add(a.CastFx);
            }
            foreach (var id in needed.Distinct())
                if (!presets.Value.ContainsKey(id)) Fail($"particle preset '{id}' is used but missing from Art/Fx/particles.json");
            foreach (var (id, p) in presets.Value)
                foreach (var also in p.Also)
                    if (!presets.Value.ContainsKey(also)) Fail($"particle preset '{id}' refers to missing '{also}'");
            Say($"  {presets.Value.Count} particle presets");
            if (Stage.CombatCamera.Tuning.Error != null) Fail("Art/Stages/_camera.json: " + Stage.CombatCamera.Tuning.Error);
        }

        private static void CheckContent()
        {
            Say("\n== Content ==");
            foreach (var id in UI.Icons.MalformedIds()) Fail($"icon '{id}' is not 12x12");
            var iconIds = UI.Icons.AllIds.ToHashSet();
            bool HasIcon(string id) => iconIds.Contains(id) || File.Exists(LiveJson.ArtPath("icon_" + id + ".png"));
            foreach (var e in ItemDb.Equipment) if (!HasIcon(e.Id)) Fail($"equipment '{e.Id}' has no icon");
            foreach (var c in ItemDb.Consumables) if (!HasIcon(c.Id)) Fail($"consumable '{c.Id}' has no icon");

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

        // Scripted checks of Relle's kit against the character design document.
        // The Special Abilities: Guard is old; Advance (charge), Position and Item are checked here.
        // Scripted checks of Mara's and Elian's kits (New Playable Character Design).
        private static void CheckMaraAndElian()
        {
            Say("\n== Mara and Elian ==");
            var rng = new Random(2);
            PartyMember M(CharacterDef d) => new() { Def = d, MaxHp = 100, Hp = 100, Damage = d.BaseDamage, Speed = d.BaseSpeed };
            ActionDef Skill(CharacterDef d, string id) => d.Skills.First(s => s.Id == id);
            Combatant Foe()
            {
                var f = Combatant.FromEnemy(EnemyDb.Husk, 1f);
                f.MaxHp = f.Hp = 9999;
                return f;
            }

            // Ironbound Aegis: 2 Guard, then an attack on the ally next to her is redirected (and halved).
            var relle = Combatant.FromMember(M(CharacterDb.Relle));
            var mara = Combatant.FromMember(M(CharacterDb.Warden));
            var elian = Combatant.FromMember(M(CharacterDb.Priest));
            var foe = Foe();
            var b = new Battle(new[] { relle, mara, elian }, new[] { foe }, rng);
            mara.Sp = 5;
            mara.Plan = new PlannedAction { Action = Skill(CharacterDb.Warden, "shield_bash"), Target = foe };
            b.Execute(mara);
            if (mara.Aegis != 1) Fail($"Shield Bash should give 1 Guard, Mara has {mara.Aegis}");
            if (foe.Slot != 0 && b.Enemies[foe.Slot] != foe) Fail("Shield Bash knockback broke the formation");
            mara.Plan = new PlannedAction { Action = Skill(CharacterDb.Warden, "aegis_wall") };
            b.Execute(mara);
            if (mara.Aegis != 2) Fail($"Aegis Wall should bring Guard to 2, Mara has {mara.Aegis}");
            if (!relle.Has(StatusKind.Wall) || !elian.Has(StatusKind.Wall)) Fail("Aegis Wall should cover both allies next to Mara");
            if (b.CanUse(mara, CommonActions.Swap)) Fail("Mara should not be able to move while holding the Aegis Wall");
            int relleHp = relle.Hp, maraHp = mara.Hp;
            foe.Plan = new PlannedAction { Action = foe.Actions.First(a => a.Kind == ActionKind.Attack), Target = relle };
            b.Execute(foe);
            if (relle.Hp != relleHp || mara.Hp == maraHp) Fail("Aegis at 2 Guard did not redirect the attack on Relle to Mara");
            if (mara.Aegis != 0) Fail("Redirecting should use up all Guard");

            // Guard (the skill): attacks on the protected ally hit Mara until she acts again.
            mara.Plan = new PlannedAction { Action = Skill(CharacterDb.Warden, "guard_ally"), Target = elian };
            mara.Sp = 5;
            b.Execute(mara);
            int elianHp = elian.Hp;
            maraHp = mara.Hp;
            foe.Plan = new PlannedAction { Action = new ActionDef { Id = "t", Name = "Test", Kind = ActionKind.Attack, Power = 1f }, Target = elian };
            b.Execute(foe);
            if (elian.Hp != elianHp || mara.Hp == maraHp) Fail("Guard did not intercept the attack on Elian");

            // Bulwark: 20% less damage at the front (nothing else on her right now).
            var front = Battle.TargetMultiplier(foe, mara);
            if (Math.Abs(front - Battle.BulwarkMultiplier) > 0.001f) Fail($"Bulwark at Front 2 should be x{Battle.BulwarkMultiplier}, is x{front}");

            mara.Plan = new PlannedAction { Action = CommonActions.Defend };
            b.Execute(mara);
            if (elian.Has(StatusKind.Protected) || relle.Has(StatusKind.Wall)) Fail("Guard / Aegis Wall should end when Mara acts again");

            // Elian: Mend gives Thread and heals more on a badly hurt ally; Bind slows; Reweave pulls toward him.
            var mend = Skill(CharacterDb.Priest, "mend");
            int normal = Battle.EstimateHeal(elian, mend, relle);
            relle.Hp = 20;
            int sympathetic = Battle.EstimateHeal(elian, mend, relle);
            if (sympathetic <= normal) Fail($"Sympathetic Thread should boost healing under 50% HP ({normal} -> {sympathetic})");
            elian.Plan = new PlannedAction { Action = mend, Target = relle };
            b.Execute(elian);
            if (elian.Thread != 1) Fail($"Mend should give 1 Thread, Elian has {elian.Thread}");
            elian.Sp = 5;
            int speed = foe.EffectiveSpeed;
            elian.Plan = new PlannedAction { Action = Skill(CharacterDb.Priest, "bind"), Target = foe };
            b.Execute(elian);
            if (foe.EffectiveSpeed >= speed) Fail($"Bind should slow the target ({speed} -> {foe.EffectiveSpeed})");
            elian.Sp = 5;
            int relleSlot = relle.Slot;
            elian.Plan = new PlannedAction { Action = Skill(CharacterDb.Priest, "reweave"), Target = relle };
            b.Execute(elian);
            if (relle.Slot != relleSlot + 1) Fail($"Reweave should pull Relle 1 position toward Elian ({relleSlot} -> {relle.Slot})");
            Say("  Aegis Guard / intercept, Guard, Aegis Wall, Bulwark, Sympathetic Thread, Thread, Bind (Slow), Reweave: checked");

            // Iron Rebuke spends every Guard and hits harder for it; Mara strikes back after intercepting.
            var b2 = new Battle(new[] { Combatant.FromMember(M(CharacterDb.Warden)), Combatant.FromMember(M(CharacterDb.Relle)) }, new[] { Foe() }, rng);
            var m2 = b2.Party[0];
            var f2 = b2.Enemies[0];
            var rebuke = Skill(CharacterDb.Warden, "iron_rebuke");
            var (low, _) = Battle.EstimateDamage(m2, f2, rebuke);
            m2.Aegis = 2;
            var (high, _) = Battle.EstimateDamage(m2, f2, rebuke);
            if (high <= low) Fail($"Iron Rebuke preview should grow with Guard ({low} -> {high})");
            m2.Sp = 5;
            m2.Plan = new PlannedAction { Action = rebuke, Target = f2 };
            b2.Execute(m2);
            if (m2.Aegis != 0) Fail($"Iron Rebuke should spend all Guard, Mara has {m2.Aegis}");
            m2.Aegis = 2;
            int foeHp = f2.Hp;
            f2.Plan = new PlannedAction { Action = new ActionDef { Id = "t", Name = "Test", Kind = ActionKind.Attack, Power = 1f }, Target = b2.Party[1] };
            b2.Execute(f2);
            if (f2.Hp >= foeHp) Fail("Mara should strike back after the Aegis intercepts");

            // Pillow: Guarded after a skill, and ripostes while Guarded.
            var pillow = Combatant.FromMember(M(CharacterDb.Knight));
            var f3 = Foe();
            var b3 = new Battle(new[] { pillow }, new[] { f3 }, rng);
            pillow.Plan = new PlannedAction { Action = Skill(CharacterDb.Knight, "pixel_sword"), Target = f3 };
            b3.Execute(pillow);
            if (!pillow.Has(StatusKind.Guard)) Fail("Pillow should be Guarded after a skill");
            foeHp = f3.Hp;
            f3.Plan = new PlannedAction { Action = new ActionDef { Id = "t", Name = "Test", Kind = ActionKind.Attack, Power = 1f }, Target = pillow };
            b3.Execute(f3);
            if (f3.Hp >= foeHp) Fail("Pillow should riposte while Guarded");

            // Elian: Snare Line pulls 2 forward; at 3 Thread a Mend is Woven and heals everyone.
            var e4 = Combatant.FromMember(M(CharacterDb.Priest));
            var r4 = Combatant.FromMember(M(CharacterDb.Relle));
            var w4 = Combatant.FromMember(M(CharacterDb.Warden));
            var foes4 = new[] { Foe(), Foe(), Foe() };
            var b4 = new Battle(new[] { r4, w4, e4 }, foes4, rng);
            e4.Sp = 5;
            var back = foes4[2];
            e4.Plan = new PlannedAction { Action = Skill(CharacterDb.Priest, "snare_line"), Target = back };
            b4.Execute(e4);
            if (back.Slot != 0) Fail($"Snare Line should pull Back 1 to Front 1, it is at slot {back.Slot}");
            r4.Hp = 30; w4.Hp = 30;
            e4.Thread = Battle.ThreadMax;
            e4.Plan = new PlannedAction { Action = Skill(CharacterDb.Priest, "mend"), Target = r4 };
            b4.Execute(e4);
            if (r4.Hp <= 30 || w4.Hp <= 30) Fail("A Woven Mend should heal every ally");
            if (e4.Thread != 0) Fail($"Woven should use up all Thread, Elian has {e4.Thread}");
            Say("  Iron Rebuke, Mara's counter, Pillow's riposte, Snare Line, Woven: checked");
        }

        private static void CheckSpecialAbilities()
        {
            Say("\n== Special abilities ==");
            var rng = new Random(1);
            PartyMember Member(CharacterDef d) => new() { Def = d, MaxHp = 100, Hp = 100, Damage = d.BaseDamage, Speed = d.BaseSpeed };
            var foe = Combatant.FromEnemy(EnemyDb.Husk, 1f);
            var a = Combatant.FromMember(Member(CharacterDb.Relle));
            var bU = Combatant.FromMember(Member(CharacterDb.Warden));
            var cU = Combatant.FromMember(Member(CharacterDb.Priest));
            var b = new Battle(new[] { a, bU, cU }, new[] { foe }, rng) { Items = new List<string> { "tonic", "ember_salt" } };

            // Advance = gather energy.
            int sp = a.Sp;
            a.Plan = new PlannedAction { Action = CommonActions.Charge };
            b.Execute(a);
            if (a.Sp != Math.Min(a.MaxSp, sp + Battle.ChargeSp)) Fail($"Advance (charge) should give +{Battle.ChargeSp} SP: {sp} -> {a.Sp}");

            // Position: swap with an ally, and step into an empty slot.
            cU.Plan = new PlannedAction { Action = CommonActions.Swap, TargetSlot = 0, Target = a };
            b.Execute(cU);
            if (cU.Slot != 0 || a.Slot != 2 || b.Party[0] != cU || b.Party[2] != a) Fail("Position did not swap Elian (Back 1) with Relle (Front 1)");
            cU.Plan = new PlannedAction { Action = CommonActions.Swap, TargetSlot = 3 };
            b.Execute(cU);
            if (cU.Slot != 3 || b.Party[0] != null || b.Party[3] != cU) Fail("Position into an empty slot failed");

            // Item: Tonic heals the living, Ember Salt revives the fallen into a free slot, both are used up.
            bU.Hp = 50;
            bU.Plan = new PlannedAction { Action = CommonActions.UseItem, ItemId = "tonic" };
            b.Execute(bU);
            if (bU.Hp != 80) Fail($"Tonic should heal 30% (50 -> 80), got {bU.Hp}");
            if (b.Items.Contains("tonic")) Fail("Tonic was not used up");
            a.Hp = 0; b.Fallen.Add(a); b.Party[a.Slot] = null;
            bU.Plan = new PlannedAction { Action = CommonActions.UseItem, ItemId = "ember_salt" };
            var ev = b.Execute(bU);
            if (!a.Alive || !ev.Any(e => e.Type == BattleEventType.Revive && e.Target == a) || b.Party[a.Slot] != a || b.Fallen.Contains(a))
                Fail("Ember Salt did not revive Relle into the formation");
            bU.Plan = new PlannedAction { Action = CommonActions.UseItem, ItemId = "ember_salt" };
            if (!b.Execute(bU).Any(e => e.Type == BattleEventType.Fizzle)) Fail("Using an item that's gone should fizzle");
            Say("  Advance, Position and Item checked");
        }

        private static void CheckRelleMechanics()
        {
            Say("\n== Relle mechanics ==");
            var rng = new Random(1);
            PartyMember Member(CharacterDef d) => new() { Def = d, MaxHp = 999, Hp = 999, Damage = d.BaseDamage, Speed = d.BaseSpeed };
            List<Combatant> Dummies(int n) => Enumerable.Range(0, n).Select(_ =>
            {
                var c = Combatant.FromEnemy(EnemyDb.Gnawer, 1f);
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
            int hits = ev.Count(e => e.Type == BattleEventType.Damage && e.Target != relle);
            if (hits != 4) Fail($"Blood Feast vs 2 enemies should land 4 hits, landed {hits}");
            if (relle.Sp != 3) Fail($"Blood Feast should cost 2 SP (5 -> 3), left {relle.Sp}");
            for (int s = 0; s < Battle.Slots; s++)
                if (b.Enemies[s] != null && b.Enemies[s].Slot != s) Fail("shuffle left a unit whose Slot doesn't match its position");
            if (b.Enemies.Count(e => e != null) != 2) Fail("shuffle lost or duplicated an enemy");

            // Shield Bash knocks its target 1 slot back.
            var mara2 = Combatant.FromMember(Member(CharacterDb.Warden));
            mara2.Sp = 5;
            var b2 = new Battle(new[] { mara2 }, Dummies(3), rng);
            var front = b2.Enemies[0];
            mara2.Plan = new PlannedAction { Action = CharacterDb.Warden.Skills.First(s => s.Id == "shield_bash"), Target = front };
            b2.Execute(mara2);
            if (front.Slot != 1) Fail($"Shield Bash should knock Front 1 to slot 2 (index 1), it is at {front.Slot}");

            // Blood Feast's blood price: 3 HP, never fatal.
            relle.Hp = 2; relle.Sp = 5;
            relle.Plan = new PlannedAction { Action = feast };
            b.Execute(relle);
            if (relle.Hp < 1) Fail("Blood Feast's HP cost killed Relle");

            // Brutes and bosses are Steadfast: knockback doesn't move them.
            var husk = Combatant.FromEnemy(EnemyDb.Husk, 1f);
            husk.MaxHp = husk.Hp = 99999;
            var b5 = new Battle(new[] { Combatant.FromMember(Member(CharacterDb.Warden)) }, new[] { husk }.Concat(Dummies(2)), rng);
            var mara5 = b5.Party[0];
            mara5.Plan = new PlannedAction { Action = CharacterDb.Warden.Skills.First(s => s.Id == "shield_bash"), Target = husk };
            b5.Execute(mara5);
            if (husk.Slot != 0) Fail($"Shield Bash pushed a Steadfast Husk to slot {husk.Slot}");

            // Wind-up: Rear Up marks the Wyvern Charging; the AI then unleashes Skyfall Crush; Shield Bash interrupts it.
            var wyv = Combatant.FromEnemy(EnemyDb.Wyvern, 1f);
            var b6 = new Battle(new[] { Combatant.FromMember(Member(CharacterDb.Warden)), Combatant.FromMember(Member(CharacterDb.Priest)) }, new[] { wyv }, rng);
            var mara6 = b6.Party[0];
            wyv.Plan = new PlannedAction { Action = EnemyDb.Wyvern.Skills.First(s => s.Kind == ActionKind.WindUp) };
            b6.Execute(wyv);
            if (wyv.WindUpNext == null || !wyv.Has(StatusKind.Charging)) Fail("Rear Up did not start a wind-up");
            var next = CombatAI.Choose(b6, wyv, rng);
            if (next.Action != wyv.WindUpNext) Fail($"a winding-up Wyvern should unleash next turn, planned {next.Action.Name}");
            wyv.Plan = next;
            mara6.Plan = new PlannedAction { Action = CharacterDb.Warden.Skills.First(s => s.Id == "shield_bash"), Target = wyv };
            b6.Execute(mara6);
            if (wyv.WindUpNext != null || wyv.Plan != null || wyv.Has(StatusKind.Charging)) Fail("Shield Bash did not interrupt the wind-up");
            if (wyv.Slot != 0) Fail("the Steadfast Wyvern was knocked back");

            // Priority: Guard and Mara's shields resolve before faster units.
            var slowMara = b6.Party[0];
            slowMara.Plan = new PlannedAction { Action = CommonActions.Defend };
            b6.Party[1].Plan = new PlannedAction { Action = CharacterDb.Priest.Skills[0], Target = slowMara };
            wyv.Plan = new PlannedAction { Action = EnemyDb.Wyvern.Skills.First(s => s.Id == "tail_sweep") };
            if (b6.ResolveOrder()[0] != slowMara) Fail("Guard (priority) should resolve first");

            // Enrage at half HP: faster, cleansed.
            wyv.AddStatus(StatusKind.Slow, 2, 3);
            int speed = wyv.Speed;
            wyv.Hp = wyv.MaxHp / 2 + 1;
            mara6.Plan = new PlannedAction { Action = CharacterDb.Warden.Skills.First(s => s.Id == "shield_bash"), Target = wyv };
            b6.Execute(mara6);
            if (!wyv.Enraged || wyv.Speed != speed + Battle.EnrageSpeed || wyv.Has(StatusKind.Slow)) Fail("the Wyvern did not enrage at half HP");
            Say("  Feast stacking / crit / lifesteal, SP and HP cost, multi-hit, shuffle, knockback, Steadfast, wind-up / interrupt, priority, enrage: checked");
        }

        // Objective pools are well-formed and every pick can actually be completed, and every enemy has a Type
        // whose Target Preference shows in how the AI picks targets.
        private static void CheckObjectivesAndEnemyTypes()
        {
            Say("\n== Objectives and Enemy Types ==");
            foreach (var e in EnemyDb.All)
                if (e.TypeDef == null) Fail($"enemy '{e.Id}' has no Enemy Type");

            // Every enemy has its own sheet for combat and for exploration (Husk: two forms).
            // Mods' sheets count too; a mod enemy may go without (it draws the recoloured mock-up monster).
            var sheetIds = Mods.ArtRoots(LiveJson.ArtRoot).Select(r => Path.Combine(r, "Sheets")).Where(Directory.Exists)
                .SelectMany(d => Directory.GetFiles(d, "*.json"))
                .Select(f => System.Text.RegularExpressions.Regex.Match(File.ReadAllText(f), "\"id\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value)
                .ToHashSet();
            foreach (var e in EnemyDb.All)
                foreach (var id in new[] { e.ArtId, e.ExploreArt }.Distinct())
                {
                    if (sheetIds.Contains(id)) continue;
                    if (ModContent.Added.Contains(e)) Say($"  WARN: mod enemy '{e.Id}' has no sheet '{id}' (draws the recoloured mock-up)");
                    else Fail($"enemy '{e.Id}': no sheet with id '{id}' in Art/Sheets");
                }
            foreach (var t in EnemyTypeDb.All)
                if (t.SlotPreference.Length != Battle.Slots) Fail($"enemy type {t.Name}: SlotPreference needs {Battle.Slots} entries");

            foreach (var area in WorldDb.Areas)
                foreach (var trial in area.Trials)
                {
                    if (trial.Objectives.Count == 0) Fail($"{trial.Id}: empty Objective Pool");
                    foreach (var o in trial.Objectives)
                    {
                        if (o.Count < 1) Fail($"objective '{o.Id}': Count must be at least 1");
                        if (o.Type == ObjectiveType.DefeatTarget && o.Target == null && o.TargetType == EnemyType.None)
                            Fail($"objective '{o.Id}': Defeat Target needs a Target or a TargetType");
                        if (o.Type == ObjectiveType.DefeatTarget && o.Target == null &&
                            !area.FrontPool.Concat(area.BackPool).Any(o.Counts))
                            Fail($"objective '{o.Id}' in {trial.Id}: no {o.TargetType} in {area.Name}'s enemy pools");
                        if (o.Type != ObjectiveType.DefeatTarget && string.IsNullOrEmpty(o.Thing)) Fail($"objective '{o.Id}': needs a Thing");
                        if (o.Type == ObjectiveType.Interact && string.IsNullOrEmpty(o.Prompt)) Fail($"objective '{o.Id}': needs a Prompt");
                    }

                    // Every pick, on many seeds: the right sites exist, and enough targets are in the trial's fights.
                    var picked = new HashSet<string>();
                    for (int seed = 0; seed < 200; seed++)
                    {
                        var run = TrialRun.Start(new Profile(), area, trial, seed);
                        var o = run.Objective;
                        picked.Add(o.Id);
                        var sites = run.Map.Rooms.Values.Where(r => r.Type == RoomType.Objective).ToList();
                        if (sites.Count != o.RoomCount) Fail($"{trial.Id} seed {seed}: {sites.Count} sites for '{o.Id}'");
                        if (sites.Any(r => r.IsCombatRoom != o.RoomHasEnemies)) Fail($"{trial.Id} seed {seed}: objective site enemies don't match '{o.Id}'");
                        int targets = run.Map.Rooms.Values.Sum(r => r.Encounter.Count(o.Counts));
                        if (o.Type == ObjectiveType.DefeatTarget && targets < o.Count)
                            Fail($"{trial.Id} seed {seed}: only {targets} targets for '{o.Id}' (needs {o.Count})");
                        if (run.CanExtract) Fail($"{trial.Id} seed {seed}: extraction open before the objective");
                    }
                    if (picked.Count != trial.Objectives.Count) Fail($"{trial.Id}: only {picked.Count}/{trial.Objectives.Count} pool objectives ever picked");
                    Say($"  {trial.Name,-20} pool: {string.Join(", ", trial.Objectives.Select(o => $"{o.Name} [{o.Type}{(o.Count > 1 ? " x" + o.Count : "")}]"))}");
                }

            // Target Preference: a Brute should mostly hit the front, a Stalker mostly the back.
            var rng = new Random(3);
            foreach (var (enemy, front) in new[] { (EnemyDb.Husk, true), (EnemyDb.Gnawer, true), (EnemyDb.Wraith, false) })
            {
                PartyMember M(CharacterDef d) => new() { Def = d, MaxHp = 100, Hp = 100, Damage = d.BaseDamage, Speed = d.BaseSpeed };
                int frontHits = 0, total = 0;
                for (int i = 0; i < 400; i++)
                {
                    var party = new[] { CharacterDb.Relle, CharacterDb.Warden, CharacterDb.Naomi, CharacterDb.Priest }.Select(d => Combatant.FromMember(M(d)));
                    var foe = Combatant.FromEnemy(enemy, 1f);
                    var b = new Battle(party, new[] { foe }, rng);
                    var plan = CombatAI.Choose(b, foe, rng);
                    if (plan.Target == null || !plan.Target.IsParty) continue;
                    total++;
                    if (plan.Target.Slot < 2) frontHits++;
                }
                int pct = frontHits * 100 / Math.Max(1, total);
                Say($"  {enemy.Name,-10} ({enemy.Type}) targets the front {pct}% of the time");
                if (front ? pct < 70 : pct > 40) Fail($"{enemy.Name} ({enemy.Type}) ignores its Target Preference: front {pct}%");
            }
        }

        // The exploration chase: every pack starts away from the door, and catches a party standing still.
        private static void CheckPacks()
        {
            Say("\n== Exploration packs (chase) ==");
            var rng = new Random(5);
            int worstCatch = 0;
            for (int t = 0; t < RoomLayout.Templates.Length; t++)
                foreach (bool mirror in new[] { false, true })
                    foreach (var entry in RoomLayout.AllDirs)
                        foreach (var guarding in new[] { false, true })
                        {
                            var room = new Room
                            {
                                Type = guarding ? RoomType.Objective : RoomType.Normal,
                                Layout = RoomLayout.Build(t, mirror, RoomLayout.AllDirs),
                                Encounter = new List<EnemyDef> { EnemyDb.Gnawer, EnemyDb.Husk, EnemyDb.Wraith, EnemyDb.Gnawer },
                            };
                            var tile = RoomLayout.EntryTile(entry);
                            var party = new Vector2(tile.X * Pack.Tile + Pack.Tile / 2f, tile.Y * Pack.Tile + Pack.Tile / 2f);
                            var pack = Pack.Spawn(room, tile, guarding, rng);
                            if (pack.Members.Count != room.Encounter.Count) Fail($"template {t}: pack lost members");
                            if (!guarding && pack.Members.Any(m => Vector2.Distance(m.Pos, party) < Pack.Tile * 4))
                                Fail($"template {t}{(mirror ? "m" : "")} from {entry}: pack spawned next to the door");

                            // A party standing in the doorway (guardians: a couple of tiles from where they stand).
                            if (guarding)
                            {
                                var home = pack.Members[0].Home;
                                var near = room.Layout.Reachable(tile).Select(p => new Vector2(p.X * Pack.Tile + Pack.Tile / 2f, p.Y * Pack.Tile + Pack.Tile / 2f))
                                    .Where(p => Vector2.Distance(p, home) >= Pack.Tile * 1.5f).OrderBy(p => Vector2.Distance(p, home)).First();
                                party = near;
                            }
                            int frames = 0;
                            while (pack.Touching(party) == null && frames < 60 * 20)
                            {
                                pack.Update(1f / 60, party);
                                frames++;
                            }
                            if (pack.Touching(party) == null)
                                Fail($"template {t}{(mirror ? "m" : "")} from {entry}{(guarding ? " (guarding)" : "")}: pack never reached the party");
                            worstCatch = Math.Max(worstCatch, frames);
                        }
            Say($"  every template / door / guard mode: caught a still party, slowest {worstCatch / 60f:0.0}s");
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
                        var objective = trial.Objectives.Count > 0 ? trial.Objectives[seed % trial.Objectives.Count] : null;
                        var map = TrialMap.Generate(trial, new Random(seed), objectiveRooms: objective?.RoomCount ?? 0);
                        if (map.Rooms.Count < trial.RoomCount) shortMaps++;
                        if (map.Rooms.Values.Any(r => r.Depth < 0)) Fail($"{trial.Id} seed {seed}: unreachable room");
                        int ex = map.Rooms.Values.Count(r => r.Type == RoomType.Extraction);
                        if (ex != trial.ExtractionPoints) Fail($"{trial.Id} seed {seed}: {ex} extraction points");
                        int objRooms = map.Rooms.Values.Count(r => r.Type == RoomType.Objective);
                        if (objRooms != (objective?.RoomCount ?? 0))
                            Fail($"{trial.Id} seed {seed}: {objRooms} objective rooms for {objective?.Id} (needs {objective?.RoomCount ?? 0})");
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

            // Loot always lands on floor the party can walk to (regression check: a shard once spawned inside a wall),
            // and a pack following the party through a door appears in that doorway, not inside a wall.
            int drops = 0;
            foreach (var area in WorldDb.Areas)
                foreach (var trial in area.Trials)
                    for (int seed = 0; seed < 60; seed++)
                    {
                        var run = TrialRun.Start(Profile.CreateNew(), area, trial, seed);
                        foreach (var room in run.Map.Rooms.Values)
                        {
                            var layout = room.Layout;
                            var walk = layout.Reachable(RoomLayout.EntryTile(room.Links.Keys.First()));
                            run.SpawnLoot(room);
                            foreach (var p in room.Pickups)
                            {
                                drops++;
                                var t = new Point((int)Math.Floor(p.Tile.X), (int)Math.Floor(p.Tile.Y));
                                if (layout.Solid(t.X, t.Y) || !walk.Contains(t) || (room.HasFeature && t == layout.Feature))
                                    Fail($"{trial.Id} seed {seed}: {room.Type} loot at {t} is not on reachable floor");
                            }
                            if (room.Encounter.Count == 0) continue;
                            foreach (var d in room.Links.Keys)
                                foreach (var m in Pack.Follow(room, room.Encounter, d, run.Rng).Members)
                                {
                                    var t = new Point((int)(m.Pos.X / Pack.Tile), (int)(m.Pos.Y / Pack.Tile));
                                    if (layout.Solid(t.X, t.Y)) Fail($"{trial.Id} seed {seed}: a following pack spawned inside a wall at {t}");
                                }
                        }
                    }
            Say($"  {drops} loot drops on reachable floor; following packs spawn in doorways");
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





            foreach (var (label, profile) in new[] { ("fresh default party (Relle, Mara, Naomi, Elian)", Profile.CreateNew()), ("veteran party (lv 3/3/2 + gear)", veteran) })
            {
                Say($"\n== Balance: {label}, AI-controlled, 400 full-clear runs per trial ==");
                foreach (var area in WorldDb.Areas)
                    foreach (var trial in area.Trials)
                        SimulateTrial(profile, area, trial);
            }
            Say("  (AI plays worse than a person; use these for relative tuning, not pass/fail)");

            // The boss alone, at full HP (like Playtest > One fight > Wyvern): separates the boss from attrition.
            Say("\n== Wyvern fight at full HP, fresh default party (Playtest one fight), 400 fights per depth ==");
            foreach (var (depth, escorts) in new[] { (3, 2), (6, 2), (3, 1) })
            {
                var academy = WorldDb.AbandonedCity.Trials[1];
                int won = 0, turns = 0, windUps = 0, interrupts = 0, enraged = 0;
                for (int seed = 0; seed < 400; seed++)
                {
                    var run = TrialRun.Start(Profile.CreateNew(), WorldDb.AbandonedCity, academy, seed, ObjectiveDb.SlayWyvern);
                    var room = new Room { Type = RoomType.Objective, Depth = depth, Encounter = Enumerable.Repeat(EnemyDb.Gnawer, escorts).Append(EnemyDb.Wyvern).ToList() };
                    var battle = new Battle(run.BuildPartyCombatants(), run.BuildEnemyCombatants(room), run.Rng);
                    var boss = battle.Enemies.First(e => e?.Enemy == EnemyDb.Wyvern);
                    int t = 0;
                    while (battle.Outcome == BattleOutcome.Ongoing && t++ < 60)
                    {
                        CombatAI.PlanAll(battle, battle.Living, run.Rng);
                        foreach (var c in battle.ResolveOrder())
                        {
                            var ev = battle.Execute(c);
                            windUps += ev.Count(e => e.Status == StatusKind.Charging && e.Type == BattleEventType.Status);
                            interrupts += ev.Count(e => e.Text == "INTERRUPTED!");
                            if (battle.Outcome != BattleOutcome.Ongoing) break;
                        }
                        if (battle.Outcome == BattleOutcome.Ongoing) battle.EndTurn();
                    }
                    if (battle.Outcome == BattleOutcome.Victory) won++;
                    if (boss.Enraged) enraged++;
                    turns += t;
                }
                Say($"  depth {depth}, {escorts} escorts: won {won * 100 / 400}%   avg turns {turns / 400.0:0.0}   wind-ups/fight {windUps / 400.0:0.0}" +
                    $"   interrupted {(windUps > 0 ? interrupts * 100 / windUps : 0)}%   reached phase 2 {enraged * 100 / 400}%");
            }

            // Roster parity: swap one member of the default party for someone who plays a similar
            // position, and compare on the second trial. Big gaps mean a character is out of line.
            Say("\n== Roster parity: Fallen Academy, fresh party, one member swapped ==");
            var quarantine = WorldDb.AbandonedCity.Trials[1];
            var swaps = new (string label, string[] order)[]
            {
                ("default  Relle / Mara / Naomi / Elian", new[] { "relle", "warden", "naomi", "priest" }),
                ("Pillow  for Relle", new[] { "knight", "warden", "naomi", "priest" }),
                ("Pillow  for Naomi", new[] { "relle", "warden", "knight", "priest" }),
                ("Pillow  for Mara", new[] { "relle", "knight", "naomi", "priest" }),
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
                    if (run.ObjectivePointOpen(room)) run.UseObjectivePoint(room, new Profile());
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
                    run.RecordKills(battle, new Profile());
                    run.OnCombatWon(room);
                }
                foreach (var p in run.Map.Rooms.Values.SelectMany(r => r.Pickups).Where(p => !p.Collected)) run.Collect(p);
                if (!dead) { survived++; shards += run.TrialShards; }
                if (!dead && !run.CanExtract) Fail($"{trial.Id} seed {seed}: full clear but objective '{run.Objective.Id}' not complete ({run.ObjectiveProgress}/{run.Objective.Count})");
            }
            Say($"  {trial.Name,-20} full-clear survival {survived * 100 / runs,3}%   avg turns/fight {(double)turns / Math.Max(1, fights):0.0}" +
                $"   avg shards on success {(survived > 0 ? shards / survived : 0)}");
            Say("      " + string.Join("   ", byType.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value.won * 100 / Math.Max(1, k.Value.total)}%")));
        }
    }
}
