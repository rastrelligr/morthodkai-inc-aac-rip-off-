using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SERAAC.Core;
using SERAAC.Data;
using SERAAC.Meta;
using SERAAC.Story;
using SERAAC.Trial;
using SERAAC.UI;

namespace SERAAC.Screens
{
    // Preparation between Trials: pick the party, spend Secured Loot on upgrades,
    // manage equipment, and choose an Area + Trial to enter.
    public sealed class HubScreen : Screen
    {
        private enum Tab { Trials, Party, Upgrades, Equipment, Journal }
        private const int TabCount = 5;
        private static readonly string[] TabNames = { "TRIALS", "PARTY", "UPGRADES", "EQUIPMENT", "JOURNAL" };
        private static readonly string[] TabTitles = { "Trials", "Party", "Upgrades", "Equipment", "Journal" };

        private Tab _tab = Tab.Trials;
        private int _areaIndex, _trialIndex, _column;  // Trials tab
        private int _charIndex;                         // Party / Upgrades / Equipment
        private int _statIndex;                         // Upgrades
        private int _slotIndex;                         // Equipment (0 weapon, 1 charm)
        private bool _picking;                          // Equipment picker open
        private int _pickIndex;
        private string _toast;
        private float _toastTime;

        private static Profile Profile => Game1.Instance.Profile;

        // Prologue: Naomi walks the player through each tab (Story/StoryScripts.cs), one step at a time.
        public bool Tutorial { get; init; }
        private int _tutStep = -1;
        private int _tutLevels;          // upgrade levels owned when the upgrade step began

        private sealed class TutorialStep
        {
            public Func<List<UI.VnLine>> Lines;
            public string Instruction;
            public Func<HubScreen, bool> Done;
            public Action OnStart;
        }

        private static int TotalLevels() => Profile.Characters.Values.Sum(c => c.HpLevel + c.DamageLevel + c.SpeedLevel);

        private static readonly TutorialStep[] Steps =
        {
            new() { Lines = StoryScripts.HubWelcome, Instruction = "Open the PARTY tab", Done = h => h._tab == Tab.Party },
            new() { Lines = StoryScripts.HubParty, Instruction = "Set up your squad, then open UPGRADES", Done = h => h._tab == Tab.Upgrades },
            new() { Lines = StoryScripts.HubUpgrades, Instruction = "Buy at least one upgrade", Done = h => TotalLevels() > h._tutLevels,
                    OnStart = () => StoryFlow.GiveFunds(200) },
            new() { Lines = StoryScripts.HubEquipmentGo, Instruction = "Open the EQUIPMENT tab", Done = h => h._tab == Tab.Equipment },
            new() { Lines = StoryScripts.HubEquipment, Instruction = "Open the TRIALS tab", Done = h => h._tab == Tab.Trials },
            new() { Lines = StoryScripts.HubTrials, Instruction = "Select the Fallen Academy, then Embark", Done = _ => false },
        };

        private bool TutorialOn => Tutorial && _tutStep >= 0 && _tutStep < Steps.Length;

        private void StartStep(int i)
        {
            _tutStep = i;
            if (i >= Steps.Length) return;
            var step = Steps[i];
            step.OnStart?.Invoke();
            _tutLevels = TotalLevels();
            Screens.PushInstant(new StoryScreen(step.Lines()));
        }

        private void UpdateTutorial()
        {
            if (!TutorialOn) return;
            if (Steps[_tutStep].Done(this)) StartStep(_tutStep + 1);
        }

        public override void OnEnter()
        {
            _areaIndex = Math.Max(0, WorldDb.Areas.FindLastIndex(a => Profile.IsAreaUnlocked(a)));
            if (StoryFlow.AcademyOnly)
            {
                // The prologue only deploys to the Fallen Academy: start with it selected.
                var (area, trial) = WorldDb.Trial("city_academy");
                if (area != null)
                {
                    _areaIndex = WorldDb.Areas.IndexOf(area);
                    _trialIndex = area.Trials.IndexOf(trial);
                }
            }
        }

        private void Toast(string s)
        {
            _toast = s;
            _toastTime = 2.5f;
        }

        // ------------------------------------------------------------------ update

        public override void Update(GameTime gameTime)
        {
            Audio.Music("hub");   // no-op while it's already playing
            _toastTime -= (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (Tutorial && _tutStep < 0) { StartStep(0); return; }
            UpdateTutorial();

            if (!_picking)
            {
                var tabWas = _tab;
                if (Input.Pressed(Keys.Tab) || Input.Pressed(Keys.E) || Input.Pressed(Keys.PageDown)) _tab = (Tab)(((int)_tab + 1) % TabCount);
                if (Input.Pressed(Keys.Q) || Input.Pressed(Keys.PageUp)) _tab = (Tab)(((int)_tab + TabCount - 1) % TabCount);
                for (int i = 0; i < TabCount; i++)
                    if (Input.Pressed(Keys.D1 + i)) _tab = (Tab)i;
                if (_tab != tabWas) Audio.Play("ui_move");
                int tabClick = Ui.ClickedIndex("tab");
                if (tabClick >= 0)
                {
                    _tab = (Tab)tabClick;
                    return;
                }
                if (Ui.Clicked("title"))
                {
                    Screens.Replace(new MainMenuScreen());
                    return;
                }
                // Clicking a character row selects it on every tab that has the list.
                int charClick = Ui.ClickedIndex("char");
                if (charClick >= 0) _charIndex = charClick;

                if (Input.Pressed(Keys.F5))
                {
                    Profile.SecuredShards += 100;
                    ProfileStore.Save(Profile);
                    Toast("[debug] +100 secured shards");
                }
                if (Input.Pressed(Keys.F6))
                {
                    foreach (var c in CharacterDb.All) if (!Profile.IsUnlocked(c.Id)) Profile.UnlockedCharacters.Add(c.Id);
                    ProfileStore.Save(Profile);
                    Toast("[debug] all characters unlocked");
                }
                if (Input.Pressed(Keys.Escape))
                {
                    Screens.Replace(new MainMenuScreen());
                    return;
                }
            }

            switch (_tab)
            {
                case Tab.Trials: UpdateTrials(); break;
                case Tab.Party: UpdateParty(); break;
                case Tab.Upgrades: UpdateUpgrades(); break;
                case Tab.Equipment: UpdateEquipment(); break;
            }
        }

        private void UpdateTrials()
        {
            var areas = WorldDb.Areas;
            if (Input.Left) _column = 0;
            if (Input.Right && Profile.IsAreaUnlocked(areas[_areaIndex])) _column = 1;

            // Mouse: click an area to open it, click a trial to select it, then Embark.
            int areaClick = Ui.ClickedIndex("area");
            if (areaClick >= 0)
            {
                _areaIndex = areaClick;
                _trialIndex = Math.Min(_trialIndex, areas[_areaIndex].Trials.Count - 1);
                if (Profile.IsAreaUnlocked(areas[_areaIndex])) _column = 1;
                else Toast("Locked: complete the previous area's Main Objective.");
                return;
            }
            int trialClick = Ui.ClickedIndex("trial");
            if (trialClick >= 0 && Profile.IsAreaUnlocked(areas[_areaIndex]))
            {
                _column = 1;
                _trialIndex = trialClick;
                return;
            }
            if (Ui.Clicked("embark"))
            {
                Embark(areas[_areaIndex], areas[_areaIndex].Trials[_trialIndex]);
                return;
            }

            if (_column == 0)
            {
                if (Input.Up) _areaIndex = (_areaIndex + areas.Count - 1) % areas.Count;
                if (Input.DownKey) _areaIndex = (_areaIndex + 1) % areas.Count;
                _trialIndex = Math.Min(_trialIndex, areas[_areaIndex].Trials.Count - 1);
                if (Input.Confirm)
                {
                    if (Profile.IsAreaUnlocked(areas[_areaIndex])) _column = 1;
                    else Toast("Locked: complete the previous area's Main Objective.");
                }
            }
            else
            {
                var trials = areas[_areaIndex].Trials;
                if (Input.Up) _trialIndex = (_trialIndex + trials.Count - 1) % trials.Count;
                if (Input.DownKey) _trialIndex = (_trialIndex + 1) % trials.Count;
                if (Input.Pressed(Keys.Enter) || Input.Pressed(Keys.Space) || Input.Pressed(Keys.Z)) Embark(areas[_areaIndex], trials[_trialIndex]);
            }
        }

        private void Embark(AreaDef area, TrialDef trial)
        {
            if (Tutorial && _tutStep < Steps.Length - 1)
            {
                Toast("Naomi: \"Finish getting ready first.\"");
                return;
            }
            if (StoryFlow.AcademyOnly && trial.Id != "city_academy")
            {
                Screens.PushInstant(new StoryScreen(StoryScripts.HubWrongTrial()));
                return;
            }
            if (Tutorial) StoryFlow.FinishHubTutorial();
            if (Profile.PartyOrder.Count == 0)
            {
                Toast("Your party is empty. Pick members in the PARTY tab.");
                return;
            }
            var run = TrialRun.Start(Profile, area, trial, Environment.TickCount);
            Screens.Replace(new LoadingScreen(run));
        }

        private void UpdateParty()
        {
            var all = CharacterDb.All;
            if (Input.Up) _charIndex = (_charIndex + all.Count - 1) % all.Count;
            if (Input.DownKey) _charIndex = (_charIndex + 1) % all.Count;
            var id = all[_charIndex].Id;
            var order = Profile.PartyOrder;

            // Mouse: clicking a filled formation slot selects who stands there.
            int cellClick = Ui.ClickedIndex("cell");
            if (cellClick >= 0 && cellClick < order.Count)
            {
                _charIndex = all.FindIndex(c => c.Id == order[cellClick]);
                return;
            }
            bool toggle = Input.Confirm || Ui.Clicked("toggle");
            if (toggle && Locked(all[_charIndex]))
            {
                Toast("Locked. " + all[_charIndex].UnlockHint);
                return;
            }
            if (toggle)
            {
                if (order.Contains(id)) order.Remove(id);
                else if (order.Count < CharacterDb.PartySize) order.Add(id);
                else Toast("Party is full (4). Remove someone first.");
                ProfileStore.Save(Profile);
            }

            // Left = move toward the back, Right = move toward the front (matches the formation picture).
            int idx = order.IndexOf(id);
            if (idx >= 0)
            {
                int to = idx;
                if (Input.Right || Ui.Clicked("forward")) to = idx - 1;
                if (Input.Left || Ui.Clicked("backward")) to = idx + 1;
                if (to != idx && to >= 0 && to < order.Count)
                {
                    (order[idx], order[to]) = (order[to], order[idx]);
                    ProfileStore.Save(Profile);
                }
            }
        }

        private void UpdateUpgrades()
        {
            var all = CharacterDb.All;
            if (Input.Up) _charIndex = (_charIndex + all.Count - 1) % all.Count;
            if (Input.DownKey) _charIndex = (_charIndex + 1) % all.Count;
            if (Input.Left) _statIndex = (_statIndex + 2) % 3;
            if (Input.Right) _statIndex = (_statIndex + 1) % 3;
            Ui.Pick("stat", ref _statIndex);
            int buy = Ui.ClickedIndex("buy");
            if (buy >= 0) _statIndex = buy;
            bool confirm = Input.Confirm || buy >= 0;
            if (confirm && Locked(all[_charIndex])) Toast("Locked. " + all[_charIndex].UnlockHint);
            else if (confirm)
            {
                var stat = (UpgradeStat)_statIndex;
                var prog = Profile.Progress(all[_charIndex].Id);
                int lvl = prog.Level(stat);
                if (lvl >= Progression.MaxLevel) Toast("Already at max level.");
                else if (!Progression.TryBuyUpgrade(Profile, all[_charIndex].Id, stat))
                    Toast($"Need {Progression.UpgradeCost(lvl)} secured shards.");
                else
                {
                    Audio.Play("upgrade");
                    Toast($"{all[_charIndex].Name}: {(stat == UpgradeStat.Hp ? "Health" : stat.ToString())} upgraded!");
                }
            }
        }

        private List<string> PickerOptions()
        {
            var slot = (EquipSlot)_slotIndex;
            var list = new List<string> { null }; // null = remove
            list.AddRange(Profile.Stash.Where(id => ItemDb.GetEquipment(id)?.Slot == slot).Distinct());
            return list;
        }

        private void UpdateEquipment()
        {
            var all = CharacterDb.All;
            if (_picking)
            {
                var opts = PickerOptions();
                if (Input.Up) _pickIndex = (_pickIndex + opts.Count - 1) % opts.Count;
                if (Input.DownKey) _pickIndex = (_pickIndex + 1) % opts.Count;
                bool picked = Ui.Pick("pick", ref _pickIndex);
                // A click outside the picker closes it.
                bool clickedAway = Input.Click && !picked && Ui.Hover("pickbox") < 0;
                if (Input.Cancel || clickedAway) _picking = false;
                else if (Input.Confirm || picked)
                {
                    Progression.Equip(Profile, all[_charIndex].Id, (EquipSlot)_slotIndex, opts[_pickIndex]);
                    Audio.Play("upgrade");
                    _picking = false;
                }
                return;
            }
            if (Input.Up) _charIndex = (_charIndex + all.Count - 1) % all.Count;
            if (Input.DownKey) _charIndex = (_charIndex + 1) % all.Count;
            if (Input.Left || Input.Right) _slotIndex = 1 - _slotIndex;
            bool slotClicked = Ui.Pick("slot", ref _slotIndex);
            bool open = Input.Confirm || slotClicked;
            if (open && Locked(all[_charIndex])) Toast("Locked. " + all[_charIndex].UnlockHint);
            else if (open)
            {
                _picking = true;
                _pickIndex = 0;
            }
        }

        private static bool Locked(CharacterDef c) => !Profile.IsUnlocked(c.Id);

        // ------------------------------------------------------------------ draw

        public override void Draw(SpriteBatch sb, GameTime gameTime)
        {
            double t = gameTime.TotalGameTime.TotalSeconds;
            Core.Draw.Art(sb, "bg_hub", new Rectangle(0, 0, Game1.Width, Game1.Height), t);

            if (!Core.Draw.Art(sb, "bg_hub", new Rectangle(0, 0, Game1.Width, Game1.Height), t)) Ui.Backdrop(sb, t, 0.45f);

            // Top: the menu bar (Q / E cycle it), then the screen title and the secured shards under it.
            Ui.TabBar(sb, "tab", TabTitles, (int)_tab, new Rectangle(0, 0, Game1.Width, 56));
            Ui.ScreenTitle(sb, "The Sanctum", new Vector2(40, 68));
            float titleW = Assets.Title.MeasureString("THE SANCTUM").X;
            Core.Draw.Text(sb, Assets.Small, "Prepare before entering a Trial", new Vector2(40 + 26 + titleW + 16, 96), Palette.TextDim, shadow: false);
            Core.Draw.TextRight(sb, Assets.Small, "Secured Loot", new Vector2(Game1.Width - 40, 74), Palette.TextDim, shadow: false);
            Core.Draw.TextRight(sb, Assets.Header, $"{Profile.SecuredShards} SHARDS", new Vector2(Game1.Width - 40, 90), Palette.Accent);
            Core.Draw.Diamond(sb, new Vector2(Game1.Width - 52 - Assets.Header.MeasureString($"{Profile.SecuredShards} SHARDS").X, 104), 14, Palette.Accent);

            switch (_tab)
            {
                case Tab.Trials: DrawTrials(sb, t); break;
                case Tab.Party: DrawParty(sb, t); break;
                case Tab.Upgrades: DrawUpgrades(sb, t); break;
                case Tab.Equipment: DrawEquipment(sb, t); break;
                case Tab.Journal: DrawJournal(sb, t); break;
            }

            string hint = _tab switch
            {
                Tab.Trials => "Click an area, then a trial, then Embark.\nKeys: Up/Down, Left/Right, Enter.",
                Tab.Party => "Click a character, then Add / Remove. The arrows move them in the formation.\nKeys: Enter, Left/Right.",
                Tab.Upgrades => "Click a character, then Buy on a stat.\nKeys: Up/Down, Left/Right, Enter.",
                Tab.Journal => "Quests and allies.",
                _ => "Click a character, then a slot to change it. Click outside the list to close it.",
            };
            Ui.Footer(sb, "title", ("Esc", "Title"), ("Enter", "Confirm"));
            Ui.HelpButton(sb, new Vector2(40, Game1.Height - 20), hint + "\nSwitch tab: Tab, Q / E or 1-5.\nDebug: F5 +100 shards, F6 unlock all.");

            if (TutorialOn) DrawCoach(sb, t);

            if (_toastTime > 0 && _toast != null)
            {
                var size = Assets.Body.MeasureString(_toast);
                var r = new Rectangle((int)(Game1.Width / 2 - size.X / 2 - 16), 640, (int)size.X + 32, 34);
                Core.Draw.Panel(sb, r, Palette.EnemyTag, Palette.Rose);
                Core.Draw.TextCentered(sb, Assets.Body, _toast, r.Center.ToVector2(), Palette.Text);
            }
        }

        // Tutorial: Naomi's face and what to do next, bottom left, pulsing.
        private void DrawCoach(SpriteBatch sb, double t)
        {
            string text = Steps[_tutStep].Instruction;
            var size = Assets.Body.MeasureString(text);
            int w = (int)size.X + 110;
            var r = new Rectangle(Game1.Width / 2 - w / 2, Game1.Height - 94, w, 58);
            float pulse = 0.7f + 0.3f * (float)Math.Sin(t * 4);
            Core.Draw.Slant(sb, r, 14, Palette.Ink * 0.88f);
            Core.Draw.Slant(sb, new Rectangle(r.X + 10, r.Bottom - 3, r.Width - 20, 3), 3, Palette.Lilac300 * pulse);
            var face = Assets.Art("Dialogue/Portrait/Portrait_Naomi");
            var faceBox = new Rectangle(r.X + 14, r.Y + 5, 48, 48);
            if (face != null)
            {
                var tex = face.Texture;
                int side = (int)(tex.Width * 0.42f);
                var src = new Rectangle((tex.Width - side) / 2, (int)(tex.Height * 0.08f), side, side);
                sb.End();
                sb.Begin(samplerState: SamplerState.LinearClamp, transformMatrix: Core.Draw.FrameTransform);
                sb.Draw(tex, faceBox, src, Color.White);
                sb.End();
                sb.Begin(samplerState: SamplerState.PointClamp, transformMatrix: Core.Draw.FrameTransform);
            }
            Core.Draw.Border(sb, faceBox, Palette.Lilac300, 1);
            Core.Draw.Text(sb, Assets.Small, "NAOMI", new Vector2(r.X + 76, r.Y + 6), Palette.Lilac300, shadow: false);
            Core.Draw.Text(sb, Assets.Body, text, new Vector2(r.X + 76, r.Y + 24), Palette.Paper, shadow: false);
        }

        private void DrawTrials(SpriteBatch sb, double t)
        {
            var areas = WorldDb.Areas;
            var left = new Rectangle(40, 134, 400, 490);
            Core.Draw.Panel(sb, left, border: _column == 0 ? Palette.Selected : Palette.Border);
            Ui.SectionBar(sb, new Rectangle(left.X + 12, left.Y + 10, left.Width - 24, 24), "Areas");

            // Mods can add areas and trials: past what fits, the list scrolls to keep the selection in view.
            var (areaFirst, areaShown) = Window(areas.Count, _areaIndex, 132, left.Height - 52);
            for (int i = areaFirst; i < areaFirst + areaShown; i++)
            {
                var a = areas[i];
                bool unlocked = Profile.IsAreaUnlocked(a);
                bool done = Profile.CompletedObjectives.Contains(a.ObjectiveId);
                var r = new Rectangle(left.X + 12, left.Y + 44 + (i - areaFirst) * 132, left.Width - 24, 124);
                bool sel = i == _areaIndex;
                Ui.Card(sb, r, sel);
                Ui.HoverRow(sb, "area", i, r, sel);
                Core.Draw.Rect(sb, new Rectangle(r.X, r.Y, 6, r.Height), unlocked ? a.Accent : Palette.TextDisabled);
                Core.Draw.Text(sb, Assets.Header, a.Name, new Vector2(r.X + 18, r.Y + 8), unlocked ? (sel ? Palette.Accent : Palette.Text) : Palette.TextDisabled);
                if (!unlocked)
                    Core.Draw.Text(sb, Assets.Small, "LOCKED  -  complete the previous Main Objective", new Vector2(r.X + 18, r.Y + 40), Palette.TextDisabled);
                else
                {
                    RichText.Wrapped(sb, Assets.Small, a.Description, new Vector2(r.X + 22, r.Y + 40), r.Width - 36, Palette.TextDim);
                    Core.Draw.Text(sb, Assets.Small, (done ? "DONE  " : "") + "Main Objective: " + a.ObjectiveName,
                        new Vector2(r.X + 22, r.Y + 98), done ? Palette.Heal : Palette.Accent, shadow: false);
                }
            }

            var area = areas[_areaIndex];
            var right = new Rectangle(460, 134, 780, 490);
            Core.Draw.Panel(sb, right, border: _column == 1 ? Palette.Selected : Palette.Border);
            Ui.SectionBar(sb, new Rectangle(right.X + 12, right.Y + 10, right.Width - 24, 24), "Trials in " + area.Name);
            bool areaOpen = Profile.IsAreaUnlocked(area);

            // Two trials fit as full cards; more get compact cards (no description), then scroll.
            const int listH = 366;   // between the header and the party preview
            int step = area.Trials.Count <= 2 ? 150 : Math.Max(100, listH / area.Trials.Count);
            bool compact = step < 150;
            var (trialFirst, trialShown) = Window(area.Trials.Count, _trialIndex, step, listH);
            for (int i = trialFirst; i < trialFirst + trialShown; i++)
            {
                var tr = area.Trials[i];
                var r = new Rectangle(right.X + 12, right.Y + 44 + (i - trialFirst) * step, right.Width - 24, step - 10);
                int dy = compact ? -44 : 0;   // compact cards move the numbers up into the description's place
                bool sel = _column == 1 && i == _trialIndex;
                Ui.Card(sb, r, sel);
                if (areaOpen) Ui.HoverRow(sb, "trial", i, r, sel);
                if (sel && areaOpen)
                    Ui.Button(sb, "embark", 0, new Rectangle(r.Right - 170, r.Y + 10, 156, 40), "EMBARK", font: Assets.Header);
                var nameColor = areaOpen ? (sel ? Palette.Accent : Palette.Text) : Palette.TextDisabled;
                Core.Draw.Text(sb, Assets.Header, tr.Name.ToUpperInvariant(), new Vector2(r.X + 22, r.Y + 10), nameColor);
                if (!compact) RichText.Wrapped(sb, Assets.Body, tr.Description, new Vector2(r.X + 22, r.Y + 44), r.Width - 220, Palette.TextDim);

                // Danger as four slanted pips, then the trial's numbers.
                Core.Draw.Label(sb, "Danger", new Vector2(r.X + 22, r.Y + 90 + dy), Palette.TextDim);
                for (int d = 0; d < 4; d++)
                    Core.Draw.Slant(sb, new Rectangle(r.X + 96 + d * 16, r.Y + 93 + dy, 13, 12), 4, d < tr.Tier ? Palette.Danger : Palette.Grey900);
                Core.Draw.Text(sb, Assets.Small,
                    $"Rooms {tr.RoomCount}    Loot x{tr.LootMultiplier:0.0#}    Extraction points {tr.ExtractionPoints}",
                    new Vector2(r.X + 176, r.Y + 90 + dy), Palette.Text, shadow: false);
                // The Objective Pool: one of these is rolled each time the trial starts.
                string pool = string.Join(" / ", tr.Objectives.Select(o => o.Name));
                if (tr.HasMainObjective)
                    Core.Draw.Text(sb, Assets.Small, Fit($"Main Objective: {area.ObjectiveName}", Assets.Small, r.Width - 40),
                        new Vector2(r.X + 22, r.Y + 112 + dy), Palette.Accent, shadow: false);
                else if (tr.Objectives.Count > 0)
                    Core.Draw.Text(sb, Assets.Small, Fit("Objective, one of: " + pool, Assets.Small, r.Width - 40), new Vector2(r.X + 22, r.Y + 112 + dy), Palette.Text, shadow: false);
            }
            if (trialFirst > 0) Core.Draw.TextRight(sb, Assets.Small, "more above", new Vector2(right.Right - 16, right.Y + 14), Palette.TextDim, shadow: false);
            if (trialFirst + trialShown < area.Trials.Count)
                Core.Draw.TextRight(sb, Assets.Small, "more below", new Vector2(right.Right - 16, right.Y + 44 + trialShown * step - 6), Palette.TextDim, shadow: false);

            // Party preview so the player knows who's going in (front first).
            Core.Draw.Label(sb, "Party, front first", new Vector2(right.X + 16, right.Bottom - 74), Palette.TextDim);
            float x = right.X + 16;
            foreach (var id in Profile.PartyOrder)
            {
                var c = CharacterDb.Get(id);
                var s = Progression.Final(c, Profile.Progress(id));
                var chip = new Rectangle((int)x, right.Bottom - 52, 186, 40);
                Core.Draw.Rect(sb, chip, Palette.Ink * 0.5f);
                Core.Draw.Border(sb, chip, Palette.Line, 1);
                Figures.Portrait(sb, c, new Rectangle(chip.X + 12, chip.Y + 3, 30, 34), t);
                Core.Draw.Text(sb, Assets.Body, c.Name, new Vector2(chip.X + 48, chip.Y + 1), Palette.Paper, shadow: false);
                Core.Draw.Text(sb, Assets.Small, $"HP {s.Hp}  DMG {s.Damage}  SPD {s.Speed}", new Vector2(chip.X + 48, chip.Y + 20), Palette.TextDim, shadow: false);
                x += chip.Width + 6;
            }
            if (Profile.PartyOrder.Count < CharacterDb.PartySize)
                Core.Draw.Text(sb, Assets.Small, $"Party has {Profile.PartyOrder.Count}/4 members.", new Vector2(right.X + 200, right.Bottom - 74), Palette.Unsecured, shadow: false);
        }

        private void DrawCharacterList(SpriteBatch sb, double t, Func<CharacterDef, string> extra)
        {
            var left = new Rectangle(40, 134, 440, 490);
            Core.Draw.Panel(sb, left);
            int rowH = (left.Height - 16) / CharacterDb.All.Count;
            for (int i = 0; i < CharacterDb.All.Count; i++)
            {
                var c = CharacterDb.All[i];
                var r = new Rectangle(left.X + 10, left.Y + 8 + i * rowH, left.Width - 20, rowH - 4);
                bool sel = i == _charIndex;
                bool locked = Locked(c);
                Ui.Card(sb, r, sel);
                if (!_picking) Ui.HoverRow(sb, "char", i, r, sel);
                Figures.Portrait(sb, c, new Rectangle(r.X + 8, r.Y + 4, 40, r.Height - 8), t, locked ? 0.2f : 1f);
                if (locked)
                {
                    Core.Draw.Text(sb, Assets.Header, "LOCKED", new Vector2(r.X + 62, r.Y + 6), Palette.TextDisabled);
                    Core.Draw.Text(sb, Assets.Small, "??? - see the Journal tab", new Vector2(r.X + 62, r.Y + 34), Palette.TextDisabled);
                    continue;
                }
                Core.Draw.Text(sb, Assets.Header, c.Name, new Vector2(r.X + 62, r.Y + 4), sel ? Palette.Accent : Palette.Text);
                var s = Progression.Final(c, Profile.Progress(c.Id));
                Core.Draw.Text(sb, Assets.Small, $"{c.Role}    HP {s.Hp}   DMG {s.Damage}   SPD {s.Speed}", new Vector2(r.X + 62, r.Y + 36), Palette.TextDim);
                string ex = extra(c);
                if (ex != null) Core.Draw.TextRight(sb, Assets.Small, ex, new Vector2(r.Right - 12, r.Y + 8), Palette.Accent);
            }
        }

        private static void DrawLockedInfo(SpriteBatch sb, CharacterDef c, Rectangle right)
        {
            Core.Draw.Panel(sb, right);
            Core.Draw.Text(sb, Assets.Header, "LOCKED CHARACTER", new Vector2(right.X + 16, right.Y + 12), Palette.TextDisabled);
            Core.Draw.Wrapped(sb, Assets.Body, c.UnlockHint ?? "Not available yet.", new Vector2(right.X + 16, right.Y + 56), right.Width - 32, Palette.Text);
        }

        private void DrawParty(SpriteBatch sb, double t)
        {
            var order = Profile.PartyOrder;
            DrawCharacterList(sb, t, c =>
            {
                int i = order.IndexOf(c.Id);
                return i < 0 ? null : "IN PARTY - " + SlotName(i);
            });

            var right = new Rectangle(500, 134, 740, 490);
            Core.Draw.Panel(sb, right);
            Ui.SectionBar(sb, new Rectangle(right.X + 12, right.Y + 13, right.Width - 270, 24), "Formation  -  enemies are to the right");

            // Back 2 on the left ... Front 1 on the right (facing the enemy).
            for (int slot = 0; slot < 4; slot++)
            {
                int col = 3 - slot;
                var cell = new Rectangle(right.X + 30 + col * 175, right.Y + 46, 160, 160);
                Core.Draw.Rect(sb, cell, Palette.PanelDark);
                Core.Draw.Label(sb, SlotName(slot), new Vector2(cell.X + 10, cell.Bottom - 20), Palette.TextDim);
                if (slot < order.Count)
                {
                    var c = CharacterDb.Get(order[slot]);
                    bool sel = CharacterDb.All[_charIndex].Id == c.Id;
                    Ui.HoverRow(sb, "cell", slot, cell, sel);
                    if (sel) Core.Draw.Rect(sb, new Rectangle(cell.X, cell.Bottom - 3, cell.Width, 3), Palette.Rose);
                    Figures.Unit(sb, c.ArtId, c.Role.Substring(0, 1), new Rectangle(cell.Center.X - 34, cell.Y + 14, 68, 112), c.Color, false, t);
                    Core.Draw.TextRight(sb, Assets.Small, c.Name.ToUpperInvariant(), new Vector2(cell.Right - 10, cell.Bottom - 20), sel ? Palette.Accent : Palette.Text, shadow: false);
                    // Move arrows on the selected member: left = toward the back, right = toward the front.
                    if (sel)
                    {
                        if (slot < order.Count - 1) Ui.Button(sb, "backward", 0, new Rectangle(cell.X + 4, cell.Y + 4, 30, 30), "<", font: Assets.Body);
                        if (slot > 0) Ui.Button(sb, "forward", 0, new Rectangle(cell.Right - 34, cell.Y + 4, 30, 30), ">", font: Assets.Body);
                    }
                }
            }

            var chosen = CharacterDb.All[_charIndex];
            bool inParty = order.Contains(chosen.Id);
            if (!Locked(chosen))
                Ui.Button(sb, "toggle", 0, new Rectangle(right.Right - 236, right.Y + 8, 220, 34),
                    inParty ? $"Remove {chosen.Name}" : $"Add {chosen.Name} to party", enabled: inParty || order.Count < CharacterDb.PartySize, font: Assets.Small);

            // The highlighted character: name, blurb, skills (one row each, hover for the full text), passives.
            var ch = CharacterDb.All[_charIndex];
            float y = right.Y + 216;
            float w = right.Width - 32;
            if (Locked(ch))
            {
                Core.Draw.Text(sb, Assets.Header, "LOCKED", new Vector2(right.X + 16, y), Palette.TextDisabled, shadow: false);
                RichText.Wrapped(sb, Assets.Body, ch.UnlockHint ?? "", new Vector2(right.X + 16, y + 32), w, Palette.TextDim);
                return;
            }
            Core.Draw.Text(sb, Assets.Header, ch.Name, new Vector2(right.X + 16, y - 4), Palette.Paper);
            float nx = right.X + 26 + Assets.Header.MeasureString(ch.Name).X;
            Core.Draw.Text(sb, Assets.Small, ch.Role.ToUpperInvariant() + (ch.MaxSp > 0 ? $"   SP {ch.MaxSp}, +{Combat.Battle.SpPerTurn} per turn" : ""),
                new Vector2(nx, y + 6), Palette.TextDim, shadow: false);
            y += 30 + RichText.Wrapped(sb, Assets.Small, ch.Blurb, new Vector2(right.X + 16, y + 30), w, Palette.TextDim) + 6;
            int hovered = Ui.Hover("skillrow");
            for (int i = 0; i < ch.Skills.Count; i++)
            {
                var row = new Rectangle(right.X + 12, (int)y, (int)w + 8, 26);
                if (hovered == i) Core.Draw.Slant(sb, row, 8, Palette.Hover);
                SkillRow(sb, ch.Skills[i], new Vector2(right.X + 16, y + 2), w);
                Ui.Hit("skillrow", i, row);
                y += 28;
            }
            y += 4;
            foreach (var p in ch.Passives)
            {
                Core.Draw.Diamond(sb, new Vector2(right.X + 22, y + 9), 7, Palette.Coral);
                RichText.Line(sb, Assets.Small, Fit($"{p.Name}: {p.Description}", Assets.Small, w - 20), new Vector2(right.X + 32, y), Palette.TextDim);
                y += 19;
            }
            if (hovered >= 0 && hovered < ch.Skills.Count)
            {
                var a = ch.Skills[hovered];
                Tooltip.Draw(sb, new Rectangle(right.X + 200, right.Y + 230, 200, 1), a.Name, a.Description, Palette.Rose,
                    a.SpCost > 0 ? $"{a.SpCost} SP" : "free", 420);
            }
        }

        // One line per skill: name, SP cost, positions it's used from, positions it hits, and its description cut to fit.
        public static void SkillRow(SpriteBatch sb, ActionDef a, Vector2 pos, float width)
        {
            Core.Draw.Text(sb, Assets.Body, a.Name, new Vector2(pos.X, pos.Y - 2), Palette.Text, shadow: false);
            if (a.SpCost > 0) Core.Draw.Text(sb, Assets.Small, $"{a.SpCost} SP", new Vector2(pos.X + 120, pos.Y + 1), Palette.KwSp, shadow: false);
            Figures.RankPips(sb, a.UsableFrom, new Vector2(pos.X + 160, pos.Y + 2), false, Palette.Accent);
            if (a.Side == TargetSide.Enemy) Figures.RankPips(sb, a.TargetRanks, new Vector2(pos.X + 222, pos.Y + 2), true, Palette.Danger);
            RichText.Line(sb, Assets.Small, Fit(a.Description ?? "", Assets.Small, width - 292), new Vector2(pos.X + 290, pos.Y + 1), Palette.TextDim);
        }

        // Which rows of a list to draw: all of them if they fit, else a run that keeps `selected` in view.
        private static (int first, int count) Window(int total, int selected, int rowHeight, int height)
        {
            int shown = Math.Max(1, Math.Min(total, height / rowHeight));
            int first = Math.Clamp(selected - shown + 1, 0, Math.Max(0, total - shown));
            return (first, shown);
        }

        private static string Fit(string s, UiFont f, float width)
        {
            if (f.MeasureString(s).X <= width) return s;
            while (s.Length > 1 && f.MeasureString(s + "..").X > width) s = s.Substring(0, s.Length - 1);
            return s.TrimEnd() + "..";
        }

        public static string SlotName(int slot) => slot switch { 0 => "FRONT 1", 1 => "FRONT 2", 2 => "BACK 1", _ => "BACK 2" };

        private void DrawUpgrades(SpriteBatch sb, double t)
        {
            DrawCharacterList(sb, t, _ => null);
            var c = CharacterDb.All[_charIndex];
            var prog = Profile.Progress(c.Id);
            var right = new Rectangle(500, 134, 740, 490);
            if (Locked(c)) { DrawLockedInfo(sb, c, right); return; }
            Core.Draw.Panel(sb, right);
            Ui.SectionBar(sb, new Rectangle(right.X + 12, right.Y + 10, right.Width - 24, 24), $"Upgrade {c.Name}");
            Core.Draw.Text(sb, Assets.Small, "Upgrades are permanent and are never lost, even if a Trial fails.",
                new Vector2(right.X + 16, right.Y + 44), Palette.TextDim);

            var baseStats = Progression.Base(c, prog);
            string[] names = { "HEALTH", "DAMAGE", "SPEED" };
            int[] values = { baseStats.Hp, baseStats.Damage, baseStats.Speed };
            for (int i = 0; i < 3; i++)
            {
                var stat = (UpgradeStat)i;
                int lvl = prog.Level(stat);
                bool max = lvl >= Progression.MaxLevel;
                int cost = Progression.UpgradeCost(lvl);
                bool afford = Profile.SecuredShards >= cost;
                var r = new Rectangle(right.X + 16 + i * 240, right.Y + 80, 226, 260);
                bool sel = i == _statIndex;
                Ui.Card(sb, r, sel, Palette.PanelDark);
                Ui.Hit("stat", i, r);
                Core.Draw.TextCentered(sb, Assets.Header, names[i], new Vector2(r.Center.X, r.Y + 26), Palette.Text);
                Core.Draw.TextCentered(sb, Assets.Title, values[i].ToString(), new Vector2(r.Center.X, r.Y + 90), Palette.Accent);
                if (!max)
                    Core.Draw.TextCentered(sb, Assets.Body, $"next: +{Progression.PerLevel(stat)}", new Vector2(r.Center.X, r.Y + 136), Palette.Heal);
                for (int p = 0; p < Progression.MaxLevel; p++)
                    Core.Draw.Diamond(sb, new Vector2(r.Center.X - 48 + p * 24, r.Y + 170), 12, p < lvl ? Palette.Accent : Palette.Grey900);
                if (max)
                    Core.Draw.TextCentered(sb, Assets.Header, "MAX LEVEL", new Vector2(r.Center.X, r.Y + 220), Palette.TextDim);
                else
                {
                    var btn = Ui.Button(sb, "buy", i, new Rectangle(r.X + 16, r.Y + 198, r.Width - 32, 44), $"Buy  -  {cost} shards",
                        enabled: afford, selected: sel && afford, font: Assets.Body);
                    if (!afford) Core.Draw.TextCentered(sb, Assets.Small, "not enough shards", new Vector2(r.Center.X, btn.Bottom + 8), Palette.Danger);
                }
            }
            Core.Draw.Text(sb, Assets.Small, "Cost rises with each level. Equipment bonuses are added on top of these values.",
                new Vector2(right.X + 16, right.Y + 360), Palette.TextDim);
        }

        private void DrawEquipment(SpriteBatch sb, double t)
        {
            DrawCharacterList(sb, t, c =>
            {
                var p = Profile.Progress(c.Id);
                int n = (p.Weapon != null ? 1 : 0) + (p.Charm != null ? 1 : 0);
                return n > 0 ? $"{n} equipped" : null;
            });
            var ch = CharacterDb.All[_charIndex];
            var prog = Profile.Progress(ch.Id);
            var right = new Rectangle(500, 134, 740, 490);
            if (Locked(ch)) { DrawLockedInfo(sb, ch, right); return; }
            Core.Draw.Panel(sb, right);
            Ui.SectionBar(sb, new Rectangle(right.X + 12, right.Y + 10, right.Width - 24, 24), $"{ch.Name}'s Equipment");

            for (int i = 0; i < 2; i++)
            {
                var slot = (EquipSlot)i;
                var item = ItemDb.GetEquipment(slot == EquipSlot.Weapon ? prog.Weapon : prog.Charm);
                var r = new Rectangle(right.X + 16 + i * 360, right.Y + 56, 346, 120);
                bool sel = i == _slotIndex;
                Ui.Card(sb, r, sel, Palette.PanelDark);
                if (!_picking) Ui.HoverRow(sb, "slot", i, r, sel);
                Core.Draw.Text(sb, Assets.Small, slot.ToString().ToUpper(), new Vector2(r.X + 12, r.Y + 10), Palette.TextDim);
                Core.Draw.TextRight(sb, Assets.Small, "click to change", new Vector2(r.Right - 12, r.Y + 10), Palette.TextDim);
                if (item == null)
                    Core.Draw.Text(sb, Assets.Body, "(empty)", new Vector2(r.X + 12, r.Y + 36), Palette.TextDisabled);
                else
                {
                    Icons.Draw(sb, item.Id, new Rectangle(r.X + 10, r.Y + 30, 48, 48));
                    Core.Draw.Text(sb, Assets.Header, item.Name, new Vector2(r.X + 68, r.Y + 32), Palette.Text);
                    Core.Draw.Text(sb, Assets.Body, item.StatText(), new Vector2(r.X + 68, r.Y + 62), Palette.Heal);
                    Core.Draw.Text(sb, Assets.Small, item.Flavor, new Vector2(r.X + 12, r.Y + 94), Palette.TextDim);
                }
            }

            var s = Progression.Final(ch, prog);
            Core.Draw.Text(sb, Assets.Body, $"Total with gear:  HP {s.Hp}   DMG {s.Damage}   SPD {s.Speed}", new Vector2(right.X + 16, right.Y + 190), Palette.Text);

            Core.Draw.Text(sb, Assets.Body, "STASH (secured equipment)", new Vector2(right.X + 16, right.Y + 230), Palette.TextDim);
            var groups = Profile.Stash.GroupBy(x => x).ToList();
            if (groups.Count == 0)
                Core.Draw.Text(sb, Assets.Small, "Empty. Find equipment in Trials and extract to keep it.", new Vector2(right.X + 16, right.Y + 256), Palette.TextDisabled);
            for (int i = 0; i < groups.Count; i++)
            {
                var e = ItemDb.GetEquipment(groups[i].Key);
                if (e == null) continue;
                string line = $"{e.Name}{(groups[i].Count() > 1 ? " x" + groups[i].Count() : "")}  [{e.Slot}]  {e.StatText()}";
                var pos = new Vector2(right.X + 16 + (i % 2) * 360, right.Y + 258 + (i / 2) * 30);
                Icons.Draw(sb, e.Id, new Rectangle((int)pos.X, (int)pos.Y - 2, 24, 24));
                Core.Draw.Text(sb, Assets.Small, line, pos + new Vector2(30, 4), Palette.Text);
            }

            if (_picking) DrawPicker(sb, ch, prog);
        }

        // Quests taken from NPCs, and how to unlock the characters that are still locked.
        private void DrawJournal(SpriteBatch sb, double t)
        {
            var left = new Rectangle(40, 134, 640, 490);
            Core.Draw.Panel(sb, left);
            Ui.SectionBar(sb, new Rectangle(left.X + 12, left.Y + 10, left.Width - 24, 24), "Quests");
            float y = left.Y + 44;
            foreach (var q in QuestDb.All)
            {
                var r = new Rectangle(left.X + 12, (int)y, left.Width - 24, 104);
                Ui.Card(sb, r, false);
                if (Profile.Quest(q.Id) == null)
                {
                    Core.Draw.Text(sb, Assets.Header, "???", new Vector2(r.X + 14, r.Y + 10), Palette.TextDisabled);
                    Core.Draw.Text(sb, Assets.Small, "Someone in the Trials might have work for you.", new Vector2(r.X + 14, r.Y + 44), Palette.TextDisabled);
                }
                else
                {
                    bool claimed = Quests.IsClaimed(Profile, q), done = Quests.IsDone(Profile, q);
                    string status = claimed ? "COMPLETE" : done ? "DONE - find " + q.Giver : $"{Quests.Progress(Profile, q)} / {q.Target}";
                    Core.Draw.Text(sb, Assets.Header, q.Name, new Vector2(r.X + 14, r.Y + 8), claimed ? Palette.TextDim : Palette.Accent);
                    Core.Draw.TextRight(sb, Assets.Body, status, new Vector2(r.Right - 14, r.Y + 12), claimed ? Palette.TextDim : done ? Palette.Heal : Palette.Text);
                    Core.Draw.Text(sb, Assets.Small, $"From {q.Giver}.  {q.Description}", new Vector2(r.X + 14, r.Y + 44), Palette.Text);
                    Core.Draw.Text(sb, Assets.Small, "Reward: " + q.RewardText, new Vector2(r.X + 14, r.Y + 70), Palette.Heal);
                }
                y += 114;
            }

            var right = new Rectangle(700, 134, 540, 490);
            Core.Draw.Panel(sb, right);
            Ui.SectionBar(sb, new Rectangle(right.X + 12, right.Y + 10, right.Width - 24, 24), "Allies");
            y = right.Y + 44;
            foreach (var c in CharacterDb.All.Where(c => !c.StartsUnlocked))
            {
                bool have = Profile.IsUnlocked(c.Id);
                var r = new Rectangle(right.X + 12, (int)y, right.Width - 24, 130);
                Ui.Card(sb, r, false);
                Figures.Portrait(sb, c, new Rectangle(r.X + 8, r.Y + 8, 70, r.Height - 16), t, have ? 1f : 0.15f);
                Core.Draw.Text(sb, Assets.Header, have ? $"{c.Name} ({c.Role})" : "???", new Vector2(r.X + 92, r.Y + 10), have ? Palette.Accent : Palette.TextDisabled);
                Core.Draw.Wrapped(sb, Assets.Small, have ? "Joined the Sanctum." : c.UnlockHint, new Vector2(r.X + 92, r.Y + 44), r.Width - 104, have ? Palette.Heal : Palette.Text);
                y += 140;
            }
        }

        private void DrawPicker(SpriteBatch sb, CharacterDef ch, CharacterProgress prog)
        {
            var opts = PickerOptions();
            var slot = (EquipSlot)_slotIndex;
            var current = ItemDb.GetEquipment(slot == EquipSlot.Weapon ? prog.Weapon : prog.Charm);
            Core.Draw.Rect(sb, new Rectangle(0, 0, Game1.Width, Game1.Height), Palette.Ink * 0.6f);
            Ui.BlockAll();
            var r = new Rectangle(Game1.Width / 2 - 300, 170, 600, 80 + opts.Count * 40);
            Core.Draw.Panel(sb, r);
            Ui.Hit("pickbox", r);
            Core.Draw.Text(sb, Assets.Header, $"Choose {slot} for {ch.Name}", new Vector2(r.X + 20, r.Y + 16), Palette.Accent);
            for (int i = 0; i < opts.Count; i++)
            {
                var e = ItemDb.GetEquipment(opts[i]);
                var row = new Rectangle(r.X + 12, r.Y + 56 + i * 40, r.Width - 24, 36);
                bool sel = i == _pickIndex;
                if (sel) Core.Draw.Rect(sb, row, Palette.PanelLight);
                Ui.HoverRow(sb, "pick", i, row, sel);
                string label = e == null ? (current == null ? "(leave empty)" : "(unequip)") : e.Name;
                if (e != null) Icons.Draw(sb, e.Id, new Rectangle(row.X + 8, row.Y + 2, 32, 32));
                Core.Draw.Text(sb, Assets.Body, label, new Vector2(row.X + 48, row.Y + 8), sel ? Palette.Accent : Palette.Text);
                if (e != null) Core.Draw.TextRight(sb, Assets.Small, e.StatText(), new Vector2(row.Right - 10, row.Y + 10), Palette.Heal);
            }
        }
    }
}
