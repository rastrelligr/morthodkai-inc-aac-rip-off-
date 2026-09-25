using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SERAAC.Core;
using SERAAC.Data;
using SERAAC.Meta;
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

        public override void OnEnter()
        {
            _areaIndex = Math.Max(0, WorldDb.Areas.FindLastIndex(a => Profile.IsAreaUnlocked(a)));
        }

        private void Toast(string s)
        {
            _toast = s;
            _toastTime = 2.5f;
        }

        // ------------------------------------------------------------------ update

        public override void Update(GameTime gameTime)
        {
            _toastTime -= (float)gameTime.ElapsedGameTime.TotalSeconds;

            if (!_picking)
            {
                if (Input.Pressed(Keys.Tab) || Input.Pressed(Keys.E) || Input.Pressed(Keys.PageDown)) _tab = (Tab)(((int)_tab + 1) % TabCount);
                if (Input.Pressed(Keys.Q) || Input.Pressed(Keys.PageUp)) _tab = (Tab)(((int)_tab + TabCount - 1) % TabCount);
                for (int i = 0; i < TabCount; i++)
                    if (Input.Pressed(Keys.D1 + i)) _tab = (Tab)i;

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
            if (Profile.PartyOrder.Count == 0)
            {
                Toast("Your party is empty. Pick members in the PARTY tab.");
                return;
            }
            var run = TrialRun.Start(Profile, area, trial, Environment.TickCount);
            Screens.Replace(new ExplorationScreen(run));
        }

        private void UpdateParty()
        {
            var all = CharacterDb.All;
            if (Input.Up) _charIndex = (_charIndex + all.Count - 1) % all.Count;
            if (Input.DownKey) _charIndex = (_charIndex + 1) % all.Count;
            var id = all[_charIndex].Id;
            var order = Profile.PartyOrder;

            if (Input.Confirm && Locked(all[_charIndex]))
            {
                Toast("Locked. " + all[_charIndex].UnlockHint);
                return;
            }
            if (Input.Confirm)
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
                if (Input.Right) to = idx - 1;
                if (Input.Left) to = idx + 1;
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
            if (Input.Confirm && Locked(all[_charIndex])) Toast("Locked. " + all[_charIndex].UnlockHint);
            else if (Input.Confirm)
            {
                var stat = (UpgradeStat)_statIndex;
                var prog = Profile.Progress(all[_charIndex].Id);
                int lvl = prog.Level(stat);
                if (lvl >= Progression.MaxLevel) Toast("Already at max level.");
                else if (!Progression.TryBuyUpgrade(Profile, all[_charIndex].Id, stat))
                    Toast($"Need {Progression.UpgradeCost(lvl)} secured shards.");
                else Toast($"{all[_charIndex].Name}: {(stat == UpgradeStat.Hp ? "Health" : stat.ToString())} upgraded!");
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
                if (Input.Cancel) _picking = false;
                else if (Input.Confirm)
                {
                    Progression.Equip(Profile, all[_charIndex].Id, (EquipSlot)_slotIndex, opts[_pickIndex]);
                    _picking = false;
                }
                return;
            }
            if (Input.Up) _charIndex = (_charIndex + all.Count - 1) % all.Count;
            if (Input.DownKey) _charIndex = (_charIndex + 1) % all.Count;
            if (Input.Left || Input.Right) _slotIndex = 1 - _slotIndex;
            if (Input.Confirm && Locked(all[_charIndex])) Toast("Locked. " + all[_charIndex].UnlockHint);
            else if (Input.Confirm)
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

            Core.Draw.Text(sb, Assets.Header, "THE SANCTUM", new Vector2(40, 22), Palette.Accent);
            Core.Draw.Text(sb, Assets.Small, "Prepare before entering a Trial", new Vector2(40, 50), Palette.TextDim);
            Core.Draw.TextRight(sb, Assets.Header, $"{Profile.SecuredShards} SHARDS", new Vector2(Game1.Width - 40, 22), Palette.Accent);
            Core.Draw.TextRight(sb, Assets.Small, "secured loot", new Vector2(Game1.Width - 40, 50), Palette.TextDim);

            for (int i = 0; i < TabCount; i++)
            {
                var r = new Rectangle(40 + i * 200, 80, 190, 38);
                bool sel = (int)_tab == i;
                Core.Draw.Rect(sb, r, sel ? Palette.PanelLight : Palette.Panel);
                if (sel) Core.Draw.Rect(sb, new Rectangle(r.X, r.Bottom - 3, r.Width, 3), Palette.Accent);
                Core.Draw.TextCentered(sb, Assets.Body, $"{i + 1}  {TabNames[i]}", r.Center.ToVector2(), sel ? Palette.Accent : Palette.TextDim);
            }

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
                Tab.Trials => "Up/Down select   Left/Right column   Enter embark",
                Tab.Party => "Up/Down select   Enter add/remove   Left/Right move in formation",
                Tab.Upgrades => "Up/Down character   Left/Right stat   Enter buy",
                Tab.Journal => "Quests and allies",
                _ => _picking ? "Up/Down choose   Enter equip   Esc close" : "Up/Down character   Left/Right slot   Enter change",
            };
            Core.Draw.Text(sb, Assets.Small, hint + "      Tab/Q/E switch tab   Esc main menu   F5 +100 shards, F6 unlock all (debug)",
                new Vector2(40, Game1.Height - 28), Palette.TextDim);

            if (_toastTime > 0 && _toast != null)
            {
                var size = Assets.Body.MeasureString(_toast);
                var r = new Rectangle((int)(Game1.Width / 2 - size.X / 2 - 16), 640, (int)size.X + 32, 34);
                Core.Draw.Panel(sb, r, Palette.PanelLight, Palette.Accent);
                Core.Draw.TextCentered(sb, Assets.Body, _toast, r.Center.ToVector2(), Palette.Text);
            }
        }

        private void DrawTrials(SpriteBatch sb, double t)
        {
            var areas = WorldDb.Areas;
            var left = new Rectangle(40, 134, 400, 490);
            Core.Draw.Panel(sb, left, border: _column == 0 ? Palette.Accent : Palette.Border);
            Core.Draw.Text(sb, Assets.Body, "AREAS", new Vector2(left.X + 16, left.Y + 12), Palette.TextDim);

            for (int i = 0; i < areas.Count; i++)
            {
                var a = areas[i];
                bool unlocked = Profile.IsAreaUnlocked(a);
                bool done = Profile.CompletedObjectives.Contains(a.ObjectiveId);
                var r = new Rectangle(left.X + 12, left.Y + 44 + i * 110, left.Width - 24, 100);
                bool sel = i == _areaIndex;
                Core.Draw.Rect(sb, r, sel ? Palette.PanelLight : Palette.Panel);
                Core.Draw.Rect(sb, new Rectangle(r.X, r.Y, 6, r.Height), unlocked ? a.Accent : Palette.TextDisabled);
                Core.Draw.Text(sb, Assets.Header, a.Name, new Vector2(r.X + 18, r.Y + 8), unlocked ? (sel ? Palette.Accent : Palette.Text) : Palette.TextDisabled);
                if (!unlocked)
                    Core.Draw.Text(sb, Assets.Small, "LOCKED  -  complete the previous Main Objective", new Vector2(r.X + 18, r.Y + 40), Palette.TextDisabled);
                else
                {
                    Core.Draw.Wrapped(sb, Assets.Small, a.Description, new Vector2(r.X + 18, r.Y + 38), r.Width - 30, Palette.TextDim);
                    Core.Draw.Text(sb, Assets.Small, (done ? "[DONE] " : "[    ] ") + "Main Objective: " + a.ObjectiveName,
                        new Vector2(r.X + 18, r.Y + 76), done ? Palette.Heal : Palette.Text);
                }
            }

            var area = areas[_areaIndex];
            var right = new Rectangle(460, 134, 780, 490);
            Core.Draw.Panel(sb, right, border: _column == 1 ? Palette.Accent : Palette.Border);
            Core.Draw.Text(sb, Assets.Body, "TRIALS IN " + area.Name.ToUpper(), new Vector2(right.X + 16, right.Y + 12), Palette.TextDim);
            bool areaOpen = Profile.IsAreaUnlocked(area);

            for (int i = 0; i < area.Trials.Count; i++)
            {
                var tr = area.Trials[i];
                var r = new Rectangle(right.X + 12, right.Y + 44 + i * 150, right.Width - 24, 140);
                bool sel = _column == 1 && i == _trialIndex;
                Core.Draw.Rect(sb, r, sel ? Palette.PanelLight : Palette.Panel);
                if (sel) Core.Draw.Border(sb, r, Palette.Accent);
                var nameColor = areaOpen ? (sel ? Palette.Accent : Palette.Text) : Palette.TextDisabled;
                Core.Draw.Text(sb, Assets.Header, tr.Name, new Vector2(r.X + 16, r.Y + 10), nameColor);
                Core.Draw.Wrapped(sb, Assets.Body, tr.Description, new Vector2(r.X + 16, r.Y + 42), r.Width - 32, Palette.TextDim);

                string danger = new string('#', tr.Tier) + new string('-', 4 - tr.Tier);
                Core.Draw.Text(sb, Assets.Small,
                    $"Rooms {tr.RoomCount}    Danger [{danger}]    Loot x{tr.LootMultiplier:0.0#}    Extraction points {tr.ExtractionPoints}",
                    new Vector2(r.X + 16, r.Y + 90), Palette.Text);
                if (tr.HasMainObjective)
                    Core.Draw.Text(sb, Assets.Small, "* Contains the Main Objective: " + area.ObjectiveName,
                        new Vector2(r.X + 16, r.Y + 112), Palette.Accent);
            }

            // Party preview so the player knows who's going in.
            Core.Draw.Text(sb, Assets.Small, "PARTY (front -> back):", new Vector2(right.X + 16, right.Bottom - 70), Palette.TextDim);
            float x = right.X + 16;
            foreach (var id in Profile.PartyOrder)
            {
                var c = CharacterDb.Get(id);
                var s = Progression.Final(c, Profile.Progress(id));
                string label = $"{c.Name} ({c.Role})  HP {s.Hp} DMG {s.Damage} SPD {s.Speed}";
                Core.Draw.Text(sb, Assets.Small, label, new Vector2(x, right.Bottom - 46), Palette.Text);
                x += Assets.Small.MeasureString(label).X + 24;
            }
            if (Profile.PartyOrder.Count < CharacterDb.PartySize)
                Core.Draw.Text(sb, Assets.Small, $"Party has {Profile.PartyOrder.Count}/4 members.", new Vector2(right.X + 16, right.Bottom - 24), Palette.Unsecured);
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
                Core.Draw.Rect(sb, r, sel ? Palette.PanelLight : Palette.Panel);
                if (sel) Core.Draw.Border(sb, r, Palette.Accent);
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
            Core.Draw.Text(sb, Assets.Body, "FORMATION", new Vector2(right.X + 16, right.Y + 12), Palette.TextDim);
            Core.Draw.TextRight(sb, Assets.Small, "enemies are this way  >>", new Vector2(right.Right - 16, right.Y + 14), Palette.TextDim);

            // Back 2 on the left ... Front 1 on the right (facing the enemy).
            for (int slot = 0; slot < 4; slot++)
            {
                int col = 3 - slot;
                var cell = new Rectangle(right.X + 30 + col * 175, right.Y + 50, 160, 200);
                Core.Draw.Rect(sb, cell, new Color(22, 18, 26));
                Core.Draw.TextCentered(sb, Assets.Small, SlotName(slot), new Vector2(cell.Center.X, cell.Bottom - 14), Palette.TextDim);
                if (slot < order.Count)
                {
                    var c = CharacterDb.Get(order[slot]);
                    bool sel = CharacterDb.All[_charIndex].Id == c.Id;
                    if (sel) Core.Draw.Border(sb, cell, Palette.Accent);
                    Figures.Unit(sb, c.ArtId, c.Role.Substring(0, 1), new Rectangle(cell.Center.X - 40, cell.Y + 20, 80, 130), c.Color, false, t);
                    Core.Draw.TextCentered(sb, Assets.Small, c.Name, new Vector2(cell.Center.X, cell.Y + 164), sel ? Palette.Accent : Palette.Text);
                }
            }

            // Skills of the highlighted character.
            var ch = CharacterDb.All[_charIndex];
            float y = right.Y + 262;
            if (Locked(ch))
            {
                Core.Draw.Text(sb, Assets.Body, "Locked", new Vector2(right.X + 16, y), Palette.TextDisabled);
                Core.Draw.Wrapped(sb, Assets.Small, ch.UnlockHint ?? "", new Vector2(right.X + 16, y + 24), right.Width - 32, Palette.TextDim);
                return;
            }
            Core.Draw.Text(sb, Assets.Body, $"{ch.Name}, {ch.Role}" + (ch.MaxSp > 0 ? $"   (SP {ch.MaxSp}, +{Combat.Battle.SpPerTurn} per turn)" : ""),
                new Vector2(right.X + 16, y), Palette.Accent);
            Core.Draw.Wrapped(sb, Assets.Small, ch.Blurb, new Vector2(right.X + 16, y + 24), right.Width - 32, Palette.TextDim);
            y += 48;
            foreach (var a in ch.Skills)
            {
                SkillRow(sb, a, new Vector2(right.X + 16, y), right.Width - 32);
                y += 36;
            }
            foreach (var p in ch.Passives)
            {
                Core.Draw.Text(sb, Assets.Small, $"PASSIVE  {p.Name}: {p.Description}", new Vector2(right.X + 16, y), Palette.Accent);
                y += 18;
            }
        }

        public static void SkillRow(SpriteBatch sb, ActionDef a, Vector2 pos, float width)
        {
            if (a.SpCost > 0) Core.Draw.Text(sb, Assets.Small, $"{a.SpCost} SP", new Vector2(pos.X + 110, pos.Y + 2), Palette.Guard);
            Core.Draw.Text(sb, Assets.Body, a.Name, pos, Palette.Text);
            Core.Draw.Text(sb, Assets.Small, "use", new Vector2(pos.X + 150, pos.Y + 2), Palette.TextDim);
            Figures.RankPips(sb, a.UsableFrom, new Vector2(pos.X + 178, pos.Y + 2), false, Palette.Accent);
            if (a.Side == TargetSide.Enemy)
            {
                Core.Draw.Text(sb, Assets.Small, "hits", new Vector2(pos.X + 238, pos.Y + 2), Palette.TextDim);
                Figures.RankPips(sb, a.TargetRanks, new Vector2(pos.X + 270, pos.Y + 2), true, Palette.Danger);
            }
            Core.Draw.Text(sb, Assets.Small, a.Description, new Vector2(pos.X + 150, pos.Y + 18), Palette.TextDim);
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
            Core.Draw.Text(sb, Assets.Header, $"UPGRADE {c.Name.ToUpper()}", new Vector2(right.X + 16, right.Y + 12), Palette.Accent);
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
                Core.Draw.Rect(sb, r, sel ? Palette.PanelLight : new Color(22, 18, 26));
                if (sel) Core.Draw.Border(sb, r, Palette.Accent);
                Core.Draw.TextCentered(sb, Assets.Header, names[i], new Vector2(r.Center.X, r.Y + 26), Palette.Text);
                Core.Draw.TextCentered(sb, Assets.Title, values[i].ToString(), new Vector2(r.Center.X, r.Y + 90), Palette.Accent);
                if (!max)
                    Core.Draw.TextCentered(sb, Assets.Body, $"next: +{Progression.PerLevel(stat)}", new Vector2(r.Center.X, r.Y + 136), Palette.Heal);
                for (int p = 0; p < Progression.MaxLevel; p++)
                    Core.Draw.Diamond(sb, new Vector2(r.Center.X - 48 + p * 24, r.Y + 170), 12, p < lvl ? Palette.Accent : new Color(60, 54, 66));
                string costText = max ? "MAX LEVEL" : $"{cost} shards";
                Core.Draw.TextCentered(sb, Assets.Header, costText, new Vector2(r.Center.X, r.Y + 220),
                    max ? Palette.TextDim : afford ? Palette.Accent : Palette.Danger);
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
            Core.Draw.Text(sb, Assets.Header, $"{ch.Name.ToUpper()}'S EQUIPMENT", new Vector2(right.X + 16, right.Y + 12), Palette.Accent);

            for (int i = 0; i < 2; i++)
            {
                var slot = (EquipSlot)i;
                var item = ItemDb.GetEquipment(slot == EquipSlot.Weapon ? prog.Weapon : prog.Charm);
                var r = new Rectangle(right.X + 16 + i * 360, right.Y + 56, 346, 120);
                bool sel = i == _slotIndex;
                Core.Draw.Rect(sb, r, sel ? Palette.PanelLight : new Color(22, 18, 26));
                if (sel) Core.Draw.Border(sb, r, Palette.Accent);
                Core.Draw.Text(sb, Assets.Small, slot.ToString().ToUpper(), new Vector2(r.X + 12, r.Y + 10), Palette.TextDim);
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

        // Quests taken from NPCs, and how to unlock the characters you don't have yet.
        private void DrawJournal(SpriteBatch sb, double t)
        {
            var left = new Rectangle(40, 134, 640, 490);
            Core.Draw.Panel(sb, left);
            Core.Draw.Text(sb, Assets.Body, "QUESTS", new Vector2(left.X + 16, left.Y + 12), Palette.TextDim);
            float y = left.Y + 44;
            foreach (var q in QuestDb.All)
            {
                var r = new Rectangle(left.X + 12, (int)y, left.Width - 24, 104);
                Core.Draw.Rect(sb, r, Palette.PanelLight * 0.5f);
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
            Core.Draw.Text(sb, Assets.Body, "ALLIES", new Vector2(right.X + 16, right.Y + 12), Palette.TextDim);
            y = right.Y + 44;
            foreach (var c in CharacterDb.All.Where(c => !c.StartsUnlocked))
            {
                bool have = Profile.IsUnlocked(c.Id);
                var r = new Rectangle(right.X + 12, (int)y, right.Width - 24, 130);
                Core.Draw.Rect(sb, r, Palette.PanelLight * 0.5f);
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
            Core.Draw.Rect(sb, new Rectangle(0, 0, Game1.Width, Game1.Height), Color.Black * 0.6f);
            var r = new Rectangle(Game1.Width / 2 - 300, 170, 600, 80 + opts.Count * 40);
            Core.Draw.Panel(sb, r, border: Palette.Accent);
            Core.Draw.Text(sb, Assets.Header, $"Choose {slot} for {ch.Name}", new Vector2(r.X + 20, r.Y + 16), Palette.Accent);
            for (int i = 0; i < opts.Count; i++)
            {
                var e = ItemDb.GetEquipment(opts[i]);
                var row = new Rectangle(r.X + 12, r.Y + 56 + i * 40, r.Width - 24, 36);
                bool sel = i == _pickIndex;
                if (sel) Core.Draw.Rect(sb, row, Palette.PanelLight);
                string label = e == null ? (current == null ? "(leave empty)" : "(unequip)") : e.Name;
                if (e != null) Icons.Draw(sb, e.Id, new Rectangle(row.X + 8, row.Y + 2, 32, 32));
                Core.Draw.Text(sb, Assets.Body, label, new Vector2(row.X + 48, row.Y + 8), sel ? Palette.Accent : Palette.Text);
                if (e != null) Core.Draw.TextRight(sb, Assets.Small, e.StatText(), new Vector2(row.Right - 10, row.Y + 10), Palette.Heal);
            }
        }
    }
}
