using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SERAAC.Combat;
using SERAAC.Core;
using SERAAC.Data;
using SERAAC.Meta;
using SERAAC.Trial;
using SERAAC.UI;

namespace SERAAC.Screens
{
    // Title > Playtest: pick one part of the game and jump straight into it (a single fight, a trial with a chosen
    // objective and starting room, or the hub with everything unlocked). Runs on a throwaway profile (Meta/Playtest)
    // so the tester's real save is never touched. Settings stay between tests.
    public sealed class PlaytestScreen : Screen
    {
        private enum Mode { Combat, Exploration, Hub }

        private sealed class Row
        {
            public string Label, Help;
            public Func<string> Value;
            public Action<int> Change;   // -1 / +1
        }

        private sealed class EncounterDef
        {
            public string Name, Help;
            public RoomType Room;
            public Func<List<EnemyDef>> Enemies;
            public bool Dummy;
        }

        private static readonly EncounterDef[] Encounters =
        {
            new() { Name = "Training dummy", Room = RoomType.Normal, Dummy = true, Enemies = () => new() { EnemyDb.Gnawer },
                Help = "One Gnawer with 999 HP that only Guards. Try every skill safely." },
            new() { Name = "3 Gnawers", Room = RoomType.Normal, Enemies = () => new() { EnemyDb.Gnawer, EnemyDb.Gnawer, EnemyDb.Gnawer },
                Help = "The first fight a player sees (Normal type)." },
            new() { Name = "Husk + 2 Gnawers", Room = RoomType.Normal, Enemies = () => new() { EnemyDb.Husk, EnemyDb.Gnawer, EnemyDb.Gnawer },
                Help = "A Brute hitting the front line." },
            new() { Name = "Wraith pack", Room = RoomType.Normal, Enemies = () => new() { EnemyDb.Gnawer, EnemyDb.Gnawer, EnemyDb.Wraith, EnemyDb.Wraith },
                Help = "Two Stalkers going after the back line." },
            new() { Name = "Mixed pack", Room = RoomType.Normal, Enemies = () => new() { EnemyDb.Husk, EnemyDb.Gnawer, EnemyDb.Wraith, EnemyDb.Gnawer },
                Help = "One of each: Brute, Normal, Stalker." },
            new() { Name = "Elite Lair", Room = RoomType.Elite, Enemies = () => new() { EnemyDb.Husk, EnemyDb.Gnawer, EnemyDb.Wraith },
                Help = "A Husk leads (+10% stats)." },
            new() { Name = "Wyvern (boss)", Room = RoomType.Objective, Enemies = () => new() { EnemyDb.Gnawer, EnemyDb.Gnawer, EnemyDb.Wyvern },
                Help = "The Fallen Academy's Main Objective fight." },
        };

        private static readonly (string name, string[] party, string help)[] Presets =
        {
            ("Default", new[] { "relle", "warden", "naomi", "priest" }, "Relle, Mara, Naomi, Elian: the party a new player starts with."),
            ("Three Strikers", new[] { "relle", "knight", "naomi", "priest" }, "Relle, Pillow, Naomi + Elian: no Warden."),
            ("Pillow + Mara front", new[] { "knight", "warden", "naomi", "priest" }, "Two tanky front-liners."),
            ("Custom", null, "Pick each position yourself below."),
        };

        private static readonly string[] StartIn = { "Entrance", "First enemy room", "Objective site", "Extraction room" };
        private static readonly string[] ItemKits = { "1 Tonic", "Tonic + Ember Salt", "3 Tonics + 3 Ember Salts", "No items" };

        // Settings (kept between tests).
        private static Mode _mode;
        private static int _encounter, _trial = 1, _depth = 3, _objective, _startIn = 1, _preset, _items;
        private static PlaytestStrength _strength;
        private static bool _fullSp;
        private static readonly string[] _slots = (string[])Presets[0].party.Clone();

        private int _row;
        private List<Row> _rows = new();
        private const int RowsX = 60, RowsY = 140, RowH = 34, RowW = 640;

        public override void OnEnter() => BuildRows();

        private static AreaDef Area => WorldDb.AbandonedCity;
        private static TrialDef TrialDef => Area.Trials[Math.Clamp(_trial, 0, Area.Trials.Count - 1)];

        private static int Wrap(int v, int n) => ((v % n) + n) % n;

        private static string CharName(string id) => id == null ? "(empty)" : CharacterDb.Get(id) is { } c ? $"{c.Name} ({c.Role})" : id;

        // Rows depend on the mode, so they're rebuilt whenever something changes.
        private void BuildRows()
        {
            var rows = new List<Row>
            {
                new() { Label = "Test", Value = () => _mode switch
                    {
                        Mode.Combat => "One fight",
                        Mode.Exploration => "Trial (explore, chase, objective)",
                        _ => "Hub sandbox (upgrades, gear, party)",
                    }, Change = d => _mode = (Mode)Wrap((int)_mode + d, 3),
                    Help = "One fight: straight into combat. Trial: a full run with the objective and start room you pick. Hub sandbox: the Sanctum with 9999 shards, every item and every character." },
            };
            if (_mode == Mode.Combat)
            {
                rows.Add(new() { Label = "Enemies", Value = () => Encounters[_encounter].Name, Change = d => _encounter = Wrap(_encounter + d, Encounters.Length),
                    Help = "" });
                rows.Add(new() { Label = "Trial (difficulty)", Value = () => $"{TrialDef.Name} (Tier {TrialDef.Tier})", Change = d => _trial = Wrap(_trial + d, Area.Trials.Count),
                    Help = "Higher tiers make enemies stronger (see the balance sheet)." });
                rows.Add(new() { Label = "Room depth", Value = () => _depth.ToString(), Change = d => _depth = Math.Clamp(_depth + d, 1, 8),
                    Help = "Rooms from the entrance: deeper rooms have stronger enemies." });
            }
            if (_mode == Mode.Exploration)
            {
                rows.Add(new() { Label = "Trial", Value = () => TrialDef.Name, Change = d => { _trial = Wrap(_trial + d, Area.Trials.Count); _objective = 0; },
                    Help = "City Outskirts is short (8 rooms); the Fallen Academy has the Wyvern." });
                rows.Add(new() { Label = "Objective", Value = () => _objective == 0 ? "Random (like a real run)" : TrialDef.Objectives[_objective - 1].Name,
                    Change = d => _objective = Wrap(_objective + d, TrialDef.Objectives.Count + 1),
                    Help = "Force one objective from this trial's pool to test it." });
                rows.Add(new() { Label = "Start in", Value = () => StartIn[_startIn], Change = d => _startIn = Wrap(_startIn + d, StartIn.Length),
                    Help = "Skip the walk: start right where you want to test (the enemies there still have to spot you)." });
            }
            if (_mode != Mode.Hub)
            {
                rows.Add(new() { Label = "Party", Value = () => Presets[_preset].name, Change = d => { _preset = Wrap(_preset + d, Presets.Length); ApplyPreset(); },
                    Help = "" });
                for (int i = 0; i < CharacterDb.PartySize; i++)
                {
                    int slot = i;
                    rows.Add(new() { Label = "   " + HubScreen.SlotName(slot), Value = () => CharName(_slots[slot]), Change = d => CycleSlot(slot, d),
                        Help = "Change any position (this switches the party to Custom). Front 1 is closest to the enemies." });
                }
                rows.Add(new() { Label = "Party strength", Value = () => _strength switch
                    {
                        PlaytestStrength.Fresh => "Fresh (no upgrades)",
                        PlaytestStrength.Veteran => "Veteran (upgrades 3/3/2 + gear)",
                        _ => "Maxed (upgrades 5/5/5 + gear)",
                    }, Change = d => _strength = (PlaytestStrength)Wrap((int)_strength + d, 3),
                    Help = "Fresh = a new player. Veteran = what the balance test calls mid-game." });
                rows.Add(new() { Label = "Start with full SP", Value = () => _fullSp ? "Yes" : "No", Change = _ => _fullSp = !_fullSp,
                    Help = "Yes = every fight starts at 5 SP, to try the big skills right away." });
                rows.Add(new() { Label = "Items", Value = () => ItemKits[_items], Change = d => _items = Wrap(_items + d, ItemKits.Length),
                    Help = "Consumables carried into the test." });
            }
            _rows = rows;
            _row = Math.Clamp(_row, 0, _rows.Count);   // _rows.Count = the START button
        }

        private void ApplyPreset()
        {
            var party = Presets[_preset].party;
            if (party != null) Array.Copy(party, _slots, CharacterDb.PartySize);
        }

        private void CycleSlot(int slot, int d)
        {
            var options = new List<string> { null };
            options.AddRange(CharacterDb.All.Select(c => c.Id));
            int i = options.IndexOf(_slots[slot]);
            // Skip characters already in another position.
            for (int n = 0; n < options.Count; n++)
            {
                i = Wrap(i + d, options.Count);
                if (options[i] == null || !_slots.Where((s, k) => k != slot).Contains(options[i])) break;
            }
            _slots[slot] = options[i];
            _preset = Presets.Length - 1;   // Custom
        }

        // ------------------------------------------------------------------ update

        public override void Update(GameTime gameTime)
        {
            if (Input.Cancel) { Screens.Replace(new MainMenuScreen()); return; }

            int count = _rows.Count + 1;
            if (Input.Up) _row = Wrap(_row - 1, count);
            if (Input.DownKey) _row = Wrap(_row + 1, count);
            int hovered = Ui.Hover("ptrow");
            if (hovered >= 0 && Input.MouseMoved) _row = hovered;

            int change = Input.Left ? -1 : Input.Right ? 1 : 0;
            int left = Ui.ClickedIndex("ptleft"), right = Ui.ClickedIndex("ptright");
            if (left >= 0) { _row = left; change = -1; }
            if (right >= 0) { _row = right; change = 1; }
            if (change == 0 && Input.Click && hovered >= 0) { _row = hovered; change = 1; }   // clicking a row steps it forward
            if (change != 0 && _row < _rows.Count)
            {
                _rows[_row].Change(change);
                BuildRows();
            }

            if (Ui.Clicked("ptstart") || Input.Confirm) Start();   // Enter starts from any row
            if (Ui.Clicked("ptback")) Screens.Replace(new MainMenuScreen());
        }

        // ------------------------------------------------------------------ starting a test

        private void Start()
        {
            var party = _slots.Where(s => s != null).ToList();
            if (_mode != Mode.Hub && party.Count == 0)
            {
                Playtest.LastResult = "Pick at least one party member.";
                return;
            }
            var profile = Playtest.MakeProfile(_mode == Mode.Hub ? Presets[0].party : party, _strength);
            Playtest.Begin(profile);

            if (_mode == Mode.Hub)
            {
                Screens.Replace(new HubScreen());
                return;
            }

            var enc = Encounters[_encounter];
            ObjectiveDef objective = _mode == Mode.Exploration
                ? (_objective == 0 ? null : TrialDef.Objectives[_objective - 1])
                : enc.Room == RoomType.Objective ? ObjectiveDb.SlayWyvern : null;
            var run = TrialRun.Start(profile, Area, TrialDef, Environment.TickCount, objective);
            run.FullSp = _fullSp;
            run.Consumables.Clear();
            run.Consumables.AddRange(_items switch
            {
                0 => new[] { "tonic" },
                1 => new[] { "tonic", "ember_salt" },
                2 => new[] { "tonic", "tonic", "tonic", "ember_salt", "ember_salt", "ember_salt" },
                _ => Array.Empty<string>(),
            });

            if (_mode == Mode.Exploration)
            {
                Screens.Replace(new ExplorationScreen(run, PickStartRoom(run)));
                return;
            }

            // One fight in a room of its own (not on the map).
            var room = new Room
            {
                Type = enc.Room, Depth = _depth,
                Layout = RoomLayout.Build(0, false, RoomLayout.AllDirs),
                Encounter = enc.Enemies(),
            };
            run.Current = room;
            var enemies = run.BuildEnemyCombatants(room);
            if (enc.Dummy)
                foreach (var e in enemies)
                {
                    e.Name = "Training Dummy";
                    e.MaxHp = e.Hp = 999;
                    e.Actions = new List<ActionDef> { CommonActions.Defend };
                }
            Screens.Push(new CombatScreen(run, enemies, battle =>
            {
                run.ApplyBattleResult(battle);
                Playtest.LastResult = Summary(battle, enc.Name);
                Screens.Pop();
            }));
        }

        private static Room PickStartRoom(TrialRun run)
        {
            var rooms = run.Map.Rooms.Values.OrderBy(r => r.Depth).ToList();
            return _startIn switch
            {
                1 => rooms.FirstOrDefault(r => r.IsCombatRoom),
                2 => rooms.FirstOrDefault(r => r.Type == RoomType.Objective) ?? rooms.FirstOrDefault(r => r.IsCombatRoom),
                3 => rooms.FirstOrDefault(r => r.Type == RoomType.Extraction),
                _ => null,
            };
        }

        private static string Summary(Battle b, string what)
        {
            string outcome = b.Outcome == BattleOutcome.Victory ? "WON" : "LOST";
            var hp = string.Join("   ", b.AllPartyUnits.OrderBy(c => c.Slot).Select(c => $"{c.Name} {Math.Max(0, c.Hp)}/{c.MaxHp}"));
            return $"{what}: {outcome} on turn {b.Turn}.\n{hp}";
        }

        // ------------------------------------------------------------------ draw

        public override void Draw(SpriteBatch sb, GameTime gameTime)
        {
            Ui.ScreenTitle(sb, "Playtest", new Vector2(RowsX, 22));
            Core.Draw.Text(sb, Assets.Body, "Jump straight to one part of the game. This uses a throwaway save: your real progress is not touched.",
                new Vector2(RowsX, 94), Palette.TextDim, shadow: false);

            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var r = new Rectangle(RowsX, RowsY + i * RowH, RowW, RowH - 4);
                bool sel = i == _row;
                if (sel) Ui.SelectedRow(sb, r);
                else Core.Draw.Slant(sb, r, 8, Ui.IsHover("ptrow", i) ? Palette.Hover : Palette.Panel);
                Ui.Hit("ptrow", i, r);
                Core.Draw.Text(sb, Assets.Body, row.Label, new Vector2(r.X + 18, r.Y + 5), sel ? Palette.Paper : Palette.TextDim, shadow: sel);
                var valueBox = new Rectangle(r.X + 250, r.Y + 3, r.Width - 262, r.Height - 6);
                Ui.Button(sb, "ptleft", i, new Rectangle(valueBox.X, valueBox.Y, 28, valueBox.Height), "<", font: Assets.Small);
                Ui.Button(sb, "ptright", i, new Rectangle(valueBox.Right - 28, valueBox.Y, 28, valueBox.Height), ">", font: Assets.Small);
                Core.Draw.TextCentered(sb, Assets.Body, row.Value(), new Vector2(valueBox.Center.X, valueBox.Center.Y), Palette.Paper, shadow: sel);
            }

            int startY = RowsY + _rows.Count * RowH + 10;
            Ui.Button(sb, "ptstart", 0, new Rectangle(RowsX, startY, 320, 48), "START TEST", selected: _row == _rows.Count, font: Assets.Header);
            Ui.Button(sb, "ptback", 0, new Rectangle(RowsX + 340, startY, 170, 48), "Back (Esc)");

            // Right: what the selected row means, and the last result.
            var info = new Rectangle(740, RowsY, 490, 250);
            Core.Draw.SlabPanel(sb, info, Palette.Rose);
            string help = _row < _rows.Count ? _rows[_row].Help : "Start the test with these settings.";
            if (_row < _rows.Count && _rows[_row].Label == "Enemies") help = Encounters[_encounter].Help;
            if (_row < _rows.Count && _rows[_row].Label == "Party") help = Presets[_preset].help;
            string heading = _row < _rows.Count ? _rows[_row].Label.Trim() : "Start";
            Core.Draw.Text(sb, Assets.Header, heading.ToUpperInvariant(), new Vector2(info.X + 20, info.Y + 12), Palette.Paper, shadow: false);
            RichText.Wrapped(sb, Assets.Body, help, new Vector2(info.X + 20, info.Y + 50), info.Width - 50, Palette.Text, lineGap: 2);
            Ui.HelpButton(sb, new Vector2(info.Right - 30, info.Y + 24), "Choose a row: Up/Down.\nChange it: Left/Right, or click the arrows.\nStart: Enter. Back: Esc. Everything can be clicked.");

            var last = new Rectangle(740, info.Bottom + 16, 490, 150);
            Core.Draw.SlabPanel(sb, last, Palette.Lilac300);
            Core.Draw.Text(sb, Assets.Header, "LAST TEST", new Vector2(last.X + 20, last.Y + 12), Palette.Paper, shadow: false);
            RichText.Wrapped(sb, Assets.Body, Playtest.LastResult ?? "Nothing yet.", new Vector2(last.X + 20, last.Y + 48), last.Width - 50, Palette.Text, lineGap: 2);
            Core.Draw.Text(sb, Assets.Small, "Write what you find in the balance sheet's Playtest tab.", new Vector2(last.X + 20, last.Bottom - 26), Palette.TextDim, shadow: false);
        }
    }
}
