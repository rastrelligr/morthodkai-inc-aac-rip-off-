using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SERAAC.Combat;
using SERAAC.Core;
using SERAAC.Data;
using SERAAC.Meta;
using SERAAC.Trial;
using SERAAC.UI;

namespace SERAAC.Screens
{
    // Top-down room exploration inside a Trial: walk between connected rooms, trigger
    // encounters, pick up loot, use special rooms, and decide when to Extract.
    public sealed class ExplorationScreen : Screen
    {
        public const int TileSize = 48;
        private static readonly Point RoomOrigin = new(24, 108);
        private const float WalkSpeed = 230f;
        private const float HalfBox = 13f;
        private const int SidebarX = 1048;

        private enum Modal { None, Pause, Items, Map, Extract, Event, Message, Dialogue }

        private readonly TrialRun _run;
        private Profile Profile => Game1.Instance.Profile;
        private Room Room => _run.Current;

        private Vector2 _pos;                                 // pixels, room-local
        private readonly List<Vector2> _trail = new();        // followers walk this path
        private float _walkAnim;

        private List<Combatant> _pendingEnemies;              // shown in the room right before combat
        private float _encounterTimer;
        private bool _pendingIsEventFight;

        private float _roomFade;                              // quick fade when changing rooms
        private Room _nextRoom;
        private Dir _nextEntry;
        private bool _debugWarpToFeature;

        private Modal _modal;
        private int _modalIndex;
        private string _messageTitle, _messageBody;

        private string _banner;
        private float _bannerTime;
        private readonly List<(string text, Vector2 pos, float age, Color color)> _floaters = new();

        public ExplorationScreen(TrialRun run)
        {
            _run = run;
        }

        public override void OnEnter()
        {
            PlaceAt(RoomLayout.EntryTile(Dir.South), Dir.South);
            ShowBanner($"{_run.Trial.Name}", 2.5f);
        }

        // Followers start lined up behind the leader, toward the door the party came through.
        private void PlaceAt(Point tile, Dir cameFrom)
        {
            _pos = new Vector2(tile.X * TileSize + TileSize / 2f, tile.Y * TileSize + TileSize / 2f);
            _trail.Clear();
            var back = RoomLayout.Step(cameFrom).ToVector2();
            for (int i = 0; i < 40; i++) _trail.Add(_pos + back * Math.Min(i, 30) * (TileSize * 1.05f / 30f));
        }

        private void ShowBanner(string text, float seconds = 2.2f)
        {
            _banner = text;
            _bannerTime = seconds;
        }

        private void Float(string text, Vector2 roomPos, Color c) => _floaters.Add((text, roomPos, 0f, c));

        // ------------------------------------------------------------------ update

        public override void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _bannerTime -= dt;
            for (int i = _floaters.Count - 1; i >= 0; i--)
            {
                var f = _floaters[i];
                f.age += dt;
                if (f.age > 1.6f) _floaters.RemoveAt(i); else _floaters[i] = f;
            }
            foreach (var p in Room.Pickups) p.Age += dt;

            if (UpdateRoomFade(dt)) return;

            if (_pendingEnemies != null)
            {
                _encounterTimer -= dt;
                if (_encounterTimer <= 0) StartCombat();
                return;
            }

            if (_modal == Modal.Dialogue)
            {
                UpdateDialogue(dt);
                return;
            }
            if (_modal != Modal.None)
            {
                UpdateModal();
                return;
            }

            if (Input.Pressed(Keys.Escape)) { OpenModal(Modal.Pause); return; }
            if (Input.Pressed(Keys.I) || Input.Pressed(Keys.Tab)) { OpenModal(Modal.Items); return; }
            if (Input.Pressed(Keys.M)) { OpenModal(Modal.Map); return; }
            if (Input.Pressed(Keys.F9)) { DebugJump(); return; }
            if (Input.Pressed(Keys.F10)) { DebugWarpTo(RoomType.Extraction); return; }
            if (Input.Pressed(Keys.F8)) { DebugWarpTo(RoomType.Npc); return; }

            Walk(dt);
            CollectPickups();
            if (Input.Interact && NearFeature()) Interact();
        }

        private bool UpdateRoomFade(float dt)
        {
            if (_nextRoom != null)
            {
                _roomFade += dt * 7f;
                if (_roomFade >= 1f)
                {
                    _roomFade = 1f;
                    ArriveIn(_nextRoom, _nextEntry);
                    _nextRoom = null;
                }
                return true;
            }
            if (_roomFade > 0) _roomFade = Math.Max(0, _roomFade - dt * 7f);
            return false;
        }

        private void Walk(float dt)
        {
            var move = Input.Move;
            if (move == Vector2.Zero) return;
            move.Normalize();
            var delta = move * WalkSpeed * dt;

            var p = _pos;
            if (!Blocked(p.X + delta.X, p.Y)) p.X += delta.X;
            if (!Blocked(p.X, p.Y + delta.Y)) p.Y += delta.Y;
            if (p == _pos) return;
            _pos = p;
            _walkAnim += dt;

            if (Vector2.Distance(_trail[0], _pos) > 4f)
            {
                _trail.Insert(0, _pos);
                if (_trail.Count > 60) _trail.RemoveAt(_trail.Count - 1);
            }

            // Stepping onto a door tile in the outer ring moves to the linked room.
            int tx = (int)(_pos.X / TileSize), ty = (int)(_pos.Y / TileSize);
            Dir? exit = tx <= 0 ? Dir.West : tx >= RoomLayout.Cols - 1 ? Dir.East : ty <= 0 ? Dir.North : ty >= RoomLayout.Rows - 1 ? Dir.South : null;
            if (exit.HasValue && Room.Links.TryGetValue(exit.Value, out var next))
            {
                _nextRoom = next;
                _nextEntry = RoomLayout.Opposite(exit.Value);
            }
        }

        // [debug] F10 / F8: warp next to the feature of an Extraction Point / NPC room.
        private void DebugWarpTo(RoomType type)
        {
            var ex = _run.Map.Rooms.Values.FirstOrDefault(r => r.Type == type);
            if (ex == null) { ShowBanner($"[debug] no {type} room in this trial", 1.5f); return; }
            ex.Visited = true;
            _debugWarpToFeature = true;
            _nextRoom = ex;
            _nextEntry = ex.Links.Keys.First();
        }

        // [debug] F9: go through a door, preferring rooms not visited yet.
        private void DebugJump()
        {
            var link = Room.Links.OrderBy(l => l.Value.Visited ? 1 : 0).First();
            _nextRoom = link.Value;
            _nextEntry = RoomLayout.Opposite(link.Key);
        }

        private bool Blocked(float x, float y)
        {
            foreach (var (cx, cy) in new[] { (x - HalfBox, y - HalfBox), (x + HalfBox, y - HalfBox), (x - HalfBox, y + HalfBox), (x + HalfBox, y + HalfBox) })
            {
                int tx = (int)Math.Floor(cx / TileSize), ty = (int)Math.Floor(cy / TileSize);
                if (Room.Layout.Solid(tx, ty)) return true;
                if (Room.HasFeature && tx == Room.Layout.Feature.X && ty == Room.Layout.Feature.Y) return true;
            }
            return false;
        }

        private void ArriveIn(Room room, Dir entry)
        {
            _run.Current = room;
            bool firstVisit = !room.Visited;
            room.Visited = true;
            PlaceAt(RoomLayout.EntryTile(entry), entry);
            if (_debugWarpToFeature)
            {
                _debugWarpToFeature = false;
                var f = room.Layout.Feature;
                PlaceAt(room.Layout.Solid(f.X, f.Y + 1) ? new Point(f.X, f.Y - 1) : new Point(f.X, f.Y + 1), Dir.South);
            }

            if (room.IsCombatRoom && !room.Cleared)
            {
                _pendingEnemies = _run.BuildEnemyCombatants(room);
                _pendingIsEventFight = false;
                _encounterTimer = 1.0f;
                ShowBanner(room.Type switch
                {
                    RoomType.Elite => "An elite blocks the way!",
                    RoomType.Objective => _run.Area.ObjectiveName + "!",
                    _ => "Ambush!",
                }, 1.0f);
            }
            else if (firstVisit)
            {
                ShowBanner(room.DisplayName, 1.6f);
            }
        }

        private void StartCombat()
        {
            var enemies = _pendingEnemies;
            _pendingEnemies = null;
            var room = Room;
            bool eventFight = _pendingIsEventFight;
            Screens.Push(new CombatScreen(_run, enemies, battle => OnCombatFinished(room, battle, eventFight)));
        }

        private void OnCombatFinished(Room room, Battle battle, bool eventFight)
        {
            _run.ApplyBattleResult(battle);
            if (battle.Outcome != BattleOutcome.Victory)
            {
                _run.Fail(Profile);
                Screens.Replace(new ResultScreen(_run, extracted: false));
                return;
            }

            bool wasObjective = room.Type == RoomType.Objective && !_run.ObjectiveCompleted;
            if (eventFight)
            {
                _run.PlaceDrops(room, new List<LootPickup> { _run.EquipmentDrop() });
            }
            else
            {
                _run.OnCombatWon(room, Profile);
            }
            var questsDone = Quests.OnVictory(Profile, room, battle);
            Screens.Pop();

            if (wasObjective)
            {
                var next = WorldDb.Areas.FirstOrDefault(a => a.UnlockedBy == _run.Area.ObjectiveId);
                ShowBanner("MAIN OBJECTIVE COMPLETE" + (next != null ? $"  -  {next.Name} unlocked" : ""), 4f);
            }
            else if (questsDone.Count > 0)
                ShowBanner($"Quest complete: {string.Join(", ", questsDone)}  -  find the quest giver", 3f);
            else ShowBanner("Room cleared  -  collect the loot", 1.8f);
        }

        private void CollectPickups()
        {
            foreach (var p in Room.Pickups)
            {
                if (p.Collected || p.Age < 0.35f) continue;
                var world = p.Tile * TileSize;
                if (Vector2.Distance(world, _pos) < TileSize * 0.6f)
                {
                    string msg = _run.Collect(p);
                    Float(msg, world, p.Kind == LootKind.Shards ? Palette.Unsecured : p.Kind == LootKind.Equipment ? Palette.Guard : Palette.Heal);
                }
            }
        }

        private bool NearFeature()
        {
            if (!Room.HasFeature) return false;
            var f = Room.Layout.Feature;
            var center = new Vector2(f.X * TileSize + TileSize / 2f, f.Y * TileSize + TileSize / 2f);
            return Vector2.Distance(center, _pos) < TileSize * 1.45f;
        }

        private string FeaturePrompt() => Room.Type switch
        {
            RoomType.Entrance => "[E] Read the sign",
            RoomType.Treasure => Room.FeatureUsed ? null : "[E] Open the cache",
            RoomType.Rest => Room.FeatureUsed ? null : "[E] Pray at the shrine",
            RoomType.Event => Room.FeatureUsed ? null : "[E] Investigate",
            RoomType.Extraction => "[E] Extract",
            RoomType.Npc when Room.Npc != null => $"[E] Talk to {Room.Npc.Name}",
            _ => null,
        };

        private void Interact()
        {
            var fc = new Vector2(Room.Layout.Feature.X + 0.5f, Room.Layout.Feature.Y + 0.5f) * TileSize;
            switch (Room.Type)
            {
                case RoomType.Entrance:
                    ShowMessage("Sealed Entrance", "The way you came in has closed behind you.\n\nFind an Extraction Point (X on the map) to leave with your loot.");
                    break;
                case RoomType.Treasure when !Room.FeatureUsed:
                    Room.FeatureUsed = true;
                    Room.Cleared = true;
                    _run.SpawnLoot(Room);
                    Float("The cache bursts open!", fc, Palette.Accent);
                    break;
                case RoomType.Rest when !Room.FeatureUsed:
                    Room.FeatureUsed = true;
                    Room.Cleared = true;
                    _run.HealParty(0.4f, reviveFallen: true);
                    Float("The party recovers 40% HP", fc, Palette.Heal);
                    break;
                case RoomType.Event when !Room.FeatureUsed:
                    OpenModal(Modal.Event);
                    break;
                case RoomType.Extraction:
                    OpenModal(Modal.Extract);
                    break;
                case RoomType.Npc when Room.Npc != null:
                    StartDialogue(Room.Npc);
                    break;
            }
        }

        // ------------------------------------------------------------------ dialogue

        private NpcDef _talkingTo;
        private DialogueNode _node;
        private float _typed;                 // characters revealed so far (typewriter)
        private const float TypeSpeed = 60f;  // characters per second

        private DialogueContext DialogueCtx => new()
        {
            Profile = Profile,
            Run = _run,
            Notify = msg => ShowBanner(msg, 3f),
        };

        private void StartDialogue(NpcDef npc)
        {
            _talkingTo = npc;
            Room.FeatureUsed = true;
            OpenModal(Modal.Dialogue);
            GoToNode(npc.Entry(DialogueCtx));
        }

        private void GoToNode(string id)
        {
            if (id == "end" || !_talkingTo.Nodes.TryGetValue(id, out var node))
            {
                _modal = Modal.None;
                _talkingTo = null;
                return;
            }
            _node = node;
            _typed = 0;
            _modalIndex = 0;
            node.OnEnter?.Invoke(DialogueCtx);
        }

        private List<DialogueChoice> VisibleChoices() =>
            _node.Choices.Where(c => c.If == null || c.If(DialogueCtx)).ToList();

        private void UpdateDialogue(float dt)
        {
            string text = _node.TextFor(DialogueCtx);
            _typed += dt * TypeSpeed * (Input.FastForward ? 3 : 1);
            if (_typed < text.Length)
            {
                if (Input.Confirm) _typed = text.Length;
                return;
            }
            var choices = VisibleChoices();
            if (choices.Count == 0)
            {
                if (Input.Confirm || Input.Cancel) GoToNode("end");
                return;
            }
            if (Input.Up) _modalIndex = (_modalIndex + choices.Count - 1) % choices.Count;
            if (Input.DownKey) _modalIndex = (_modalIndex + 1) % choices.Count;
            if (Input.Confirm) GoToNode(choices[_modalIndex].Next);
        }

        private void ShowMessage(string title, string body)
        {
            _messageTitle = title;
            _messageBody = body;
            OpenModal(Modal.Message);
        }

        private void OpenModal(Modal m)
        {
            _modal = m;
            _modalIndex = 0;
        }

        // ------------------------------------------------------------------ modals

        private int ModalOptionCount => _modal switch
        {
            Modal.Pause => 3,
            Modal.Extract => 2,
            Modal.Event => Room.Event.Options.Count,
            Modal.Items => Math.Max(1, _run.Consumables.Distinct().Count()),
            _ => 1,
        };

        private void UpdateModal()
        {
            int n = ModalOptionCount;
            if (Input.Up) _modalIndex = (_modalIndex + n - 1) % n;
            if (Input.DownKey) _modalIndex = (_modalIndex + 1) % n;

            bool close = Input.Cancel || (_modal == Modal.Map && Input.Pressed(Keys.M)) ||
                         (_modal == Modal.Items && (Input.Pressed(Keys.I) || Input.Pressed(Keys.Tab)));
            if (close && _modal != Modal.Event)
            {
                _modal = Modal.None;
                return;
            }
            if (!Input.Confirm) return;

            switch (_modal)
            {
                case Modal.Pause:
                    if (_modalIndex == 0) _modal = Modal.None;
                    else if (_modalIndex == 1) ShowMessage("How to Play", MainMenuScreen.HelpText);
                    else
                    {
                        // Abandoning counts as a failed trial: unsecured loot is lost.
                        _run.Fail(Profile);
                        Screens.Replace(new ResultScreen(_run, extracted: false, abandoned: true));
                    }
                    break;

                case Modal.Extract:
                    if (_modalIndex == 0)
                    {
                        _run.Extract(Profile);
                        Screens.Replace(new ResultScreen(_run, extracted: true));
                    }
                    else _modal = Modal.None;
                    break;

                case Modal.Event:
                    ResolveEvent(Room.Event.Options[_modalIndex]);
                    break;

                case Modal.Items:
                    var ids = _run.Consumables.Distinct().ToList();
                    if (ids.Count == 0) { _modal = Modal.None; break; }
                    var item = ItemDb.GetConsumable(ids[_modalIndex]);
                    if (_run.UseConsumable(item.Id))
                    {
                        Float($"Used {item.Name}", _pos, Palette.Heal);
                        _modal = Modal.None;
                    }
                    else Float("It would have no effect", _pos, Palette.TextDim);
                    break;

                default:
                    _modal = Modal.None;
                    break;
            }
        }

        private void ResolveEvent(EventOption opt)
        {
            Room.FeatureUsed = true;
            Room.Cleared = true;
            _modal = Modal.None;
            var fc = new Vector2(Room.Layout.Feature.X + 0.5f, Room.Layout.Feature.Y + 0.5f) * TileSize;
            switch (opt.Outcome)
            {
                case EventOutcome.HurtPartyForShards:
                    foreach (var m in _run.Party.Where(m => !m.Fallen))
                        m.Hp = Math.Max(1, m.Hp - (int)Math.Ceiling(m.Hp * opt.Amount));
                    int shards = (int)Math.Round(opt.Shards * _run.Trial.LootMultiplier);
                    _run.AddShards(shards);
                    Float($"Blood given. +{shards} shards", fc, Palette.Unsecured);
                    break;
                case EventOutcome.GainConsumable:
                    _run.PlaceDrops(Room, new List<LootPickup> { _run.ConsumableDrop() });
                    Float("You find something useful", fc, Palette.Heal);
                    break;
                case EventOutcome.GainEquipment:
                    _run.PlaceDrops(Room, new List<LootPickup> { _run.EquipmentDrop() });
                    break;
                case EventOutcome.HealParty:
                    _run.HealParty(opt.Amount, reviveFallen: false);
                    Float("The party rests", fc, Palette.Heal);
                    break;
                case EventOutcome.Gamble:
                    if (_run.Rng.NextDouble() < 0.5)
                    {
                        _run.PlaceDrops(Room, new List<LootPickup> { _run.EquipmentDrop() });
                        Float("Something glints in the flesh", fc, Palette.Guard);
                    }
                    else
                    {
                        var fake = new Room { Depth = Room.Depth + 1, Type = RoomType.Normal };
                        _pendingEnemies = _run.BuildEnemyCombatants(fake);
                        _pendingIsEventFight = true;
                        _encounterTimer = 1.0f;
                        ShowBanner("It was not empty!", 1.0f);
                    }
                    break;
                default:
                    Float("You leave it be", fc, Palette.TextDim);
                    break;
            }
        }

        // ------------------------------------------------------------------ draw

        private Vector2 ToScreen(Vector2 roomPos) => new(RoomOrigin.X + roomPos.X, RoomOrigin.Y + roomPos.Y);

        public override void Draw(SpriteBatch sb, GameTime gameTime)
        {
            double t = gameTime.TotalGameTime.TotalSeconds;
            DrawTopBar(sb);
            DrawRoom(sb, t);
            DrawSidebar(sb, t);

            if (_roomFade > 0)
                Core.Draw.Rect(sb, new Rectangle(RoomOrigin.X, RoomOrigin.Y, RoomLayout.Cols * TileSize, RoomLayout.Rows * TileSize), Color.Black * _roomFade);

            if (_bannerTime > 0 && _banner != null)
            {
                float a = MathHelper.Clamp(_bannerTime * 2f, 0, 1);
                var center = new Vector2(RoomOrigin.X + RoomLayout.Cols * TileSize / 2f, RoomOrigin.Y + 60);
                var size = Assets.Header.MeasureString(_banner);
                Core.Draw.Rect(sb, center.X - size.X / 2 - 20, center.Y - 22, size.X + 40, 44, Color.Black * 0.7f * a);
                Core.Draw.TextCentered(sb, Assets.Header, _banner, center, Palette.Accent * a);
            }

            DrawModal(sb, t);
        }

        private void DrawTopBar(SpriteBatch sb)
        {
            Core.Draw.Text(sb, Assets.Header, _run.Trial.Name.ToUpper(), new Vector2(24, 14), Palette.Accent);
            Core.Draw.Text(sb, Assets.Small, $"{_run.Area.Name}   -   {Room.DisplayName}   -   depth {Room.Depth}", new Vector2(24, 44), Palette.TextDim);

            var box = new Rectangle(560, 12, 700, 80);
            Core.Draw.Panel(sb, box, new Color(40, 22, 18), Palette.Unsecured);
            Core.Draw.Text(sb, Assets.Small, "TRIAL LOOT  (unsecured - lost if the party falls)", new Vector2(box.X + 14, box.Y + 8), Palette.Unsecured);
            Core.Draw.Text(sb, Assets.Header, $"{_run.TrialShards} shards", new Vector2(box.X + 14, box.Y + 32), Palette.Text);
            Core.Draw.Text(sb, Assets.Body, $"{_run.FoundEquipment.Count} equipment   {_run.Consumables.Count} items",
                new Vector2(box.X + 250, box.Y + 38), Palette.Text);
            Core.Draw.TextRight(sb, Assets.Small, $"secured: {Profile.SecuredShards}", new Vector2(box.Right - 14, box.Y + 8), Palette.Accent);
            if (_run.ObjectiveCompleted)
                Core.Draw.TextRight(sb, Assets.Small, "Main Objective complete", new Vector2(box.Right - 14, box.Y + 54), Palette.Heal);

            Core.Draw.Text(sb, Assets.Small, "WASD move   E interact   I items   M map   Esc menu", new Vector2(24, 74), Palette.TextDim);
        }

        private void DrawRoom(SpriteBatch sb, double t)
        {
            var area = _run.Area;
            var layout = Room.Layout;
            string floorArt = "tile_floor_" + area.Id, wallArt = "tile_wall_" + area.Id, pillarArt = "prop_pillar_" + area.Id;

            for (int x = 0; x < RoomLayout.Cols; x++)
            {
                for (int y = 0; y < RoomLayout.Rows; y++)
                {
                    var r = new Rectangle(RoomOrigin.X + x * TileSize, RoomOrigin.Y + y * TileSize, TileSize, TileSize);
                    var tile = layout.Tiles[x, y];
                    if (tile == Tile.Wall)
                    {
                        if (!Core.Draw.Art(sb, wallArt, r, t))
                        {
                            Core.Draw.Rect(sb, r, area.Wall);
                            if (y + 1 < RoomLayout.Rows && layout.Tiles[x, y + 1] != Tile.Wall)
                                Core.Draw.Rect(sb, new Rectangle(r.X, r.Bottom - 8, r.Width, 8), Draw_Darken(area.Floor, 0.5f));
                        }
                        continue;
                    }

                    if (!Core.Draw.Art(sb, floorArt, r, t))
                    {
                        Core.Draw.Rect(sb, r, (x + y) % 2 == 0 ? area.Floor : area.FloorAlt);
                        if (tile == Tile.Decor)
                        {
                            Core.Draw.Rect(sb, new Rectangle(r.X + 10, r.Y + 14, 8, 6), Draw_Darken(area.Floor, 0.35f));
                            Core.Draw.Rect(sb, new Rectangle(r.X + 26, r.Y + 28, 12, 7), Draw_Darken(area.Floor, 0.35f));
                        }
                    }

                    // Door tiles glow faintly so exits read clearly.
                    bool ring = x == 0 || y == 0 || x == RoomLayout.Cols - 1 || y == RoomLayout.Rows - 1;
                    if (ring) Core.Draw.Rect(sb, r, area.Accent * (0.18f + 0.08f * (float)Math.Sin(t * 3)));

                    if (tile == Tile.Pillar && !Core.Draw.Art(sb, pillarArt, r, t))
                    {
                        Core.Draw.Rect(sb, new Rectangle(r.X + 6, r.Bottom - 6, r.Width - 8, 6), Color.Black * 0.35f);
                        Core.Draw.Rect(sb, new Rectangle(r.X + 4, r.Y + 8, r.Width - 8, r.Height - 12), Color.Lerp(area.Wall, area.Floor, 0.35f));
                        Core.Draw.Rect(sb, new Rectangle(r.X + 4, r.Y + 2, r.Width - 8, 10), Color.Lerp(area.Floor, Color.White, 0.22f));
                    }
                }
            }

            DrawMinimapHintOnDoors(sb);
            if (Room.HasFeature) DrawFeature(sb, t);

            foreach (var p in Room.Pickups.Where(p => !p.Collected)) DrawPickup(sb, p, t);

            if (_pendingEnemies != null)
            {
                for (int i = 0; i < _pendingEnemies.Count; i++)
                {
                    var e = _pendingEnemies[i];
                    var c = ToScreen(new Vector2((13.5f + i * 1.4f) * TileSize, (5.9f + (i % 2) * 0.6f) * TileSize));
                    int w = (int)(34 * e.Scale), h = (int)(52 * e.Scale);
                    Figures.Unit(sb, e.ArtId, null, new Rectangle((int)c.X - w / 2, (int)c.Y - h, w, h), e.Color, true, t * 3,
                        fallbackSprite: EnemyDb.MockupSprite);
                }
            }

            DrawParty(sb, t);

            foreach (var f in _floaters)
            {
                float a = MathHelper.Clamp(1.6f - f.age, 0, 1);
                Core.Draw.TextCentered(sb, Assets.Body, f.text, ToScreen(f.pos) + new Vector2(0, -30 - f.age * 30), f.color * a);
            }

            if (_modal == Modal.None && _pendingEnemies == null && NearFeature())
            {
                string prompt = FeaturePrompt();
                if (prompt != null)
                {
                    var fp = ToScreen(new Vector2(Room.Layout.Feature.X + 0.5f, Room.Layout.Feature.Y) * TileSize);
                    var size = Assets.Body.MeasureString(prompt);
                    Core.Draw.Rect(sb, fp.X - size.X / 2 - 8, fp.Y - 34, size.X + 16, 28, Color.Black * 0.75f);
                    Core.Draw.TextCentered(sb, Assets.Body, prompt, fp + new Vector2(0, -20), Palette.Accent);
                }
            }
        }

        private static Color Draw_Darken(Color c, float amount) =>
            amount >= 0 ? Color.Lerp(c, Color.Black, amount) : Color.Lerp(c, Color.White, -amount * 0.3f);

        // Small arrow + label on each open door telling what's known beyond it.
        private void DrawMinimapHintOnDoors(SpriteBatch sb)
        {
            foreach (var (dir, next) in Room.Links)
            {
                var tiles = RoomLayout.Doors[dir];
                var mid = tiles.Aggregate(Vector2.Zero, (acc, p) => acc + new Vector2(p.X + 0.5f, p.Y + 0.5f)) / tiles.Length;
                var sp = ToScreen(mid * TileSize);
                string label = next.IsSpecial && Known(next) ? next.MapLabel : next.Visited ? "" : "?";
                if (label == "") continue;
                Core.Draw.TextCentered(sb, Assets.Small, label, sp, next.Type == RoomType.Extraction ? Palette.Accent : Palette.Text);
            }
        }

        private void DrawFeature(SpriteBatch sb, double t)
        {
            var f = Room.Layout.Feature;
            var r = new Rectangle(RoomOrigin.X + f.X * TileSize, RoomOrigin.Y + f.Y * TileSize, TileSize, TileSize);
            float pulse = 0.5f + 0.5f * (float)Math.Sin(t * 3);

            if (Room.Type == RoomType.Npc && Room.Npc != null)
            {
                // NPCs stand on the feature tile and turn to face the party.
                bool faceLeft = ToScreen(_pos).X < r.Center.X;
                var npc = Room.Npc;
                Figures.Unit(sb, npc.SpriteId, npc.Name.Substring(0, 1), new Rectangle(r.Center.X - 18, r.Bottom - 60, 36, 58),
                    npc.Color, faceLeft, t, pose: npc.Pose);
                if (!Room.FeatureUsed)
                    Core.Draw.TextCentered(sb, Assets.Header, "!", new Vector2(r.Center.X, r.Y - 30 + (float)Math.Sin(t * 5) * 3), Palette.Accent);
                return;
            }
            string art = Room.Type switch
            {
                RoomType.Treasure => Room.FeatureUsed ? "feature_chest_open" : "feature_chest",
                RoomType.Rest => "feature_shrine",
                RoomType.Event => "feature_event",
                RoomType.Extraction => "feature_portal",
                RoomType.Objective => "feature_objective",
                RoomType.Entrance => "feature_entrance",
                _ => null,
            };
            if (Core.Draw.Art(sb, art, new Rectangle(r.X - 8, r.Y - 16, r.Width + 16, r.Height + 16), t)) return;

            switch (Room.Type)
            {
                case RoomType.Treasure:
                    Core.Draw.Rect(sb, new Rectangle(r.X + 6, r.Y + 16, 36, 26), new Color(110, 70, 40));
                    Core.Draw.Rect(sb, new Rectangle(r.X + 6, r.Y + (Room.FeatureUsed ? 4 : 10), 36, 8), new Color(140, 90, 50));
                    Core.Draw.Rect(sb, new Rectangle(r.X + 21, r.Y + 22, 6, 8), Palette.Accent);
                    break;
                case RoomType.Rest:
                    Core.Draw.Rect(sb, new Rectangle(r.X + 14, r.Y - 10, 20, 52), new Color(200, 210, 230) * (Room.FeatureUsed ? 0.4f : 1f));
                    if (!Room.FeatureUsed) Core.Draw.Rect(sb, new Rectangle(r.X + 4, r.Y - 20, 40, 70), Palette.Heal * (0.12f * pulse));
                    break;
                case RoomType.Event:
                    Core.Draw.Rect(sb, new Rectangle(r.X + 8, r.Y + 8, 32, 36), new Color(50, 30, 40));
                    Core.Draw.Rect(sb, new Rectangle(r.X + 20, r.Y + 16, 8, 8), new Color(220, 40, 50) * (Room.FeatureUsed ? 0.3f : pulse));
                    break;
                case RoomType.Extraction:
                    for (int i = 0; i < 4; i++)
                    {
                        int inset = i * 5 + (int)(pulse * 3);
                        Core.Draw.Border(sb, new Rectangle(r.X - 6 + inset, r.Y - 12 + inset, r.Width + 12 - inset * 2, r.Height + 18 - inset * 2), Palette.Accent * (1f - i * 0.2f), 2);
                    }
                    break;
                case RoomType.Objective:
                    var heart = Room.Cleared ? new Color(70, 40, 50) : new Color(170, 30, 60);
                    int grow = Room.Cleared ? 0 : (int)(pulse * 6);
                    Core.Draw.Rect(sb, new Rectangle(r.X + 6 - grow, r.Y + 6 - grow, 36 + grow * 2, 36 + grow * 2), heart);
                    break;
                case RoomType.Entrance:
                    Core.Draw.Rect(sb, new Rectangle(r.X + 4, r.Y + 4, 40, 40), new Color(20, 16, 22));
                    Core.Draw.Rect(sb, new Rectangle(r.X + 8, r.Y + 8, 32, 6), new Color(90, 80, 70));
                    Core.Draw.Rect(sb, new Rectangle(r.X + 8, r.Y + 20, 32, 6), new Color(70, 60, 55));
                    Core.Draw.Rect(sb, new Rectangle(r.X + 8, r.Y + 32, 32, 6), new Color(50, 44, 40));
                    break;
            }
        }

        private void DrawPickup(SpriteBatch sb, LootPickup p, double t)
        {
            var c = ToScreen(p.Tile * TileSize);
            float pop = MathHelper.Clamp(p.Age / 0.35f, 0, 1);
            float bob = (float)Math.Sin(t * 4 + p.Tile.X) * 3f;
            float size = (p.Kind == LootKind.Equipment ? 20 : 14) * pop;
            string art = p.Kind switch { LootKind.Shards => "loot_shards", LootKind.Consumable => "loot_consumable", _ => "loot_equipment" };
            if (Core.Draw.Art(sb, art, new Rectangle((int)(c.X - 16 * pop), (int)(c.Y - 16 * pop + bob), (int)(32 * pop), (int)(32 * pop)), t)) return;

            var col = p.Kind switch { LootKind.Shards => Palette.Unsecured, LootKind.Consumable => Palette.Heal, _ => Palette.Guard };
            Core.Draw.Diamond(sb, new Vector2(c.X, c.Y + bob), size + 8, col * 0.25f);
            if (p.Kind == LootKind.Shards) Core.Draw.Diamond(sb, new Vector2(c.X, c.Y + bob), size, col);
            else
            {
                // Items show their icon, with a glow in the loot colour.
                Core.Draw.Diamond(sb, new Vector2(c.X, c.Y + bob), size + 18 * pop, col * (0.18f + 0.1f * (float)Math.Sin(t * 4)));
                int s = (int)(36 * pop);
                Icons.Draw(sb, p.ItemId, new Rectangle((int)c.X - s / 2, (int)(c.Y + bob) - s / 2, s, s));
            }
            Core.Draw.Rect(sb, new Rectangle((int)c.X - 6, (int)(c.Y + 14), 12, 4), Color.Black * 0.3f);
        }

        private void DrawParty(SpriteBatch sb, double t)
        {
            // Leader first in the formation, the rest follow along the trail.
            var alive = _run.Party.Where(m => !m.Fallen).ToList();
            var drawList = new List<(PartyMember m, Vector2 pos)>();
            for (int i = 0; i < alive.Count; i++)
            {
                int idx = Math.Min(_trail.Count - 1, i * 9);
                drawList.Add((alive[i], i == 0 ? _pos : _trail[idx]));
            }
            // Sort by depth; when stacked, the leader draws on top.
            foreach (var (m, pos) in drawList.Select((d, i) => (d, i)).OrderBy(x => x.d.pos.Y).ThenByDescending(x => x.i).Select(x => x.d))
            {
                var s = ToScreen(pos);
                bool moving = Input.Move != Vector2.Zero && _modal == Modal.None;
                float step = moving ? (float)Math.Abs(Math.Sin(_walkAnim * 12)) * 3 : 0;
                Figures.Unit(sb, m.Def.ArtId, m.Def.Role.Substring(0, 1),
                    new Rectangle((int)s.X - 15, (int)(s.Y - 44 - step), 30, 50), m.Def.Color, false, t, bob: false, pose: "walk");
            }
        }

        private void DrawSidebar(SpriteBatch sb, double t)
        {
            var panel = new Rectangle(SidebarX, 108, 216, 576);
            Core.Draw.Panel(sb, panel);
            Core.Draw.Text(sb, Assets.Small, "PARTY", new Vector2(panel.X + 10, panel.Y + 8), Palette.TextDim);
            for (int i = 0; i < _run.Party.Count; i++)
            {
                var m = _run.Party[i];
                var r = new Rectangle(panel.X + 8, panel.Y + 28 + i * 58, panel.Width - 16, 52);
                Core.Draw.Rect(sb, r, Palette.PanelLight * 0.6f);
                Figures.Portrait(sb, m.Def, new Rectangle(r.X + 4, r.Y + 4, 30, 44), t, m.Fallen ? 0.3f : 1f);
                Core.Draw.Text(sb, Assets.Small, m.Def.Name, new Vector2(r.X + 40, r.Y + 4), m.Fallen ? Palette.TextDisabled : Palette.Text);
                if (m.Fallen) Core.Draw.Text(sb, Assets.Small, "FALLEN", new Vector2(r.X + 40, r.Y + 26), Palette.Danger);
                else Figures.HpBar(sb, new Rectangle(r.X + 40, r.Y + 26, r.Width - 48, 16), m.Hp, m.MaxHp, false);
            }

            var mapArea = new Rectangle(panel.X + 8, panel.Y + 270, panel.Width - 16, 296);
            Core.Draw.Text(sb, Assets.Small, "MAP", new Vector2(mapArea.X + 2, mapArea.Y - 6), Palette.TextDim);
            DrawMap(sb, new Rectangle(mapArea.X, mapArea.Y + 14, mapArea.Width, mapArea.Height - 60), 22);
            Core.Draw.Text(sb, Assets.Small, "X extract  * objective\n! elite  $ cache  + shrine\n? event  @ someone", new Vector2(mapArea.X + 2, mapArea.Bottom - 50), Palette.TextDim);
        }

        private bool Known(Room r) =>
            r.Visited || r.Links.Values.Any(n => n.Visited) || r.Type == RoomType.Extraction || r.Type == RoomType.Objective;

        // Extraction points and the objective are revealed from the start; the route to them is not.
        private void DrawMap(SpriteBatch sb, Rectangle area, int cell)
        {
            var rooms = _run.Map.Rooms.Values.ToList();
            int minX = rooms.Min(r => r.Grid.X), maxX = rooms.Max(r => r.Grid.X);
            int minY = rooms.Min(r => r.Grid.Y), maxY = rooms.Max(r => r.Grid.Y);
            int gap = cell / 2;
            int w = (maxX - minX + 1) * (cell + gap), h = (maxY - minY + 1) * (cell + gap);
            var origin = new Vector2(area.Center.X - w / 2f, area.Center.Y - h / 2f);
            Vector2 Pos(Room r) => origin + new Vector2((r.Grid.X - minX) * (cell + gap) + cell / 2f, (r.Grid.Y - minY) * (cell + gap) + cell / 2f);

            foreach (var r in rooms.Where(r => r.Visited))
                foreach (var n in r.Links.Values)
                    Core.Draw.Line(sb, Pos(r), Pos(n), Palette.Border, 3);

            foreach (var r in rooms.Where(Known))
            {
                var c = Pos(r);
                var rect = new Rectangle((int)(c.X - cell / 2f), (int)(c.Y - cell / 2f), cell, cell);
                var fill = r == Room ? Palette.Accent : r.Visited ? (r.Cleared || !r.IsCombatRoom ? Palette.PanelLight : new Color(90, 40, 40)) : new Color(30, 26, 34);
                Core.Draw.Rect(sb, rect, fill);
                Core.Draw.Border(sb, rect, r.Type == RoomType.Extraction ? Palette.Accent : Palette.Border, 1);
                bool showType = r.Visited || r.IsSpecial;
                if (showType && r.MapLabel != "")
                    Core.Draw.TextCentered(sb, Assets.Small, r.MapLabel, c, r == Room ? Color.Black : r.Type == RoomType.Extraction ? Palette.Accent : Palette.Text, shadow: false);
            }
        }

        private void DrawModal(SpriteBatch sb, double t)
        {
            if (_modal == Modal.None) return;
            if (_modal == Modal.Dialogue)
            {
                DrawDialogue(sb, t);
                return;
            }
            Core.Draw.Rect(sb, new Rectangle(0, 0, Game1.Width, Game1.Height), Color.Black * 0.65f);

            switch (_modal)
            {
                case Modal.Pause:
                    OptionsBox(sb, "PAUSED", null, new[] { "Resume", "How to Play", "Abandon Trial (lose all unsecured loot)" });
                    break;

                case Modal.Map:
                {
                    var r = new Rectangle(240, 90, 800, 560);
                    Core.Draw.Panel(sb, r);
                    Core.Draw.TextCentered(sb, Assets.Header, "TRIAL MAP", new Vector2(r.Center.X, r.Y + 28), Palette.Accent);
                    DrawMap(sb, new Rectangle(r.X + 20, r.Y + 60, r.Width - 40, r.Height - 120), 48);
                    Core.Draw.TextCentered(sb, Assets.Small, "X extraction   * main objective   ! elite   $ cache   + shrine   ? event   S entrance      M / Esc close",
                        new Vector2(r.Center.X, r.Bottom - 30), Palette.TextDim);
                    break;
                }

                case Modal.Extract:
                {
                    string body = $"Leave the Trial now and secure everything you carry:\n\n" +
                                  $"   {_run.TrialShards} shards   (secured total becomes {Profile.SecuredShards + _run.TrialShards})\n" +
                                  $"   {_run.FoundEquipment.Count} equipment\n\n" +
                                  "Or keep exploring for more, and risk losing it all.";
                    OptionsBox(sb, "EXTRACTION POINT", body, new[] { "Extract", "Keep exploring" });
                    break;
                }

                case Modal.Event:
                {
                    var ev = Room.Event;
                    OptionsBox(sb, ev.Title.ToUpper(), ev.Text, ev.Options.Select(o => $"{o.Label}  -  {o.Hint}").ToArray());
                    break;
                }

                case Modal.Items:
                {
                    var ids = _run.Consumables.Distinct().ToList();
                    if (ids.Count == 0)
                        OptionsBox(sb, "ITEMS", "You carry no consumables. They drop in Trials and last until the Trial ends.", new[] { "Close" });
                    else
                        OptionsBox(sb, "ITEMS", "Consumables last until this Trial ends. Enter to use.",
                            ids.Select(id =>
                            {
                                var c = ItemDb.GetConsumable(id);
                                return $"{c.Name} x{_run.Consumables.Count(x => x == id)}  -  {c.Description}";
                            }).ToArray(), ids.ToArray());
                    break;
                }

                case Modal.Message:
                    OptionsBox(sb, _messageTitle, _messageBody, new[] { "OK" });
                    break;
            }
        }

        // Visual-novel style box: big portrait on the left, typewriter text, choices underneath.
        private void DrawDialogue(SpriteBatch sb, double t)
        {
            if (_talkingTo == null || _node == null) return;
            var npc = _talkingTo;
            Core.Draw.Rect(sb, new Rectangle(0, 0, Game1.Width, Game1.Height), Color.Black * 0.35f);

            var box = new Rectangle(60, 470, Game1.Width - 120, 230);
            var portrait = new Rectangle(box.X + 10, box.Y - 190, 230, 410);
            var set = Figures.ResolveSprite(npc.SpriteId, null, npc.Color);
            Core.Draw.Panel(sb, box, border: Palette.Accent);
            if (set != null) Sprites.DrawFit(sb, set, npc.Pose, portrait, Color.White);
            else Figures.Unit(sb, null, npc.Name.Substring(0, 1), new Rectangle(portrait.X + 60, portrait.Y + 150, 110, 180), npc.Color, false, t, bob: false);

            int textX = box.X + 270;
            var nameSize = Assets.Header.MeasureString(npc.Name);
            Core.Draw.Rect(sb, textX - 10, box.Y - 22, nameSize.X + 20, 38, Palette.PanelLight);
            Core.Draw.Text(sb, Assets.Header, npc.Name, new Vector2(textX, box.Y - 16), Palette.Accent);

            string full = _node.TextFor(DialogueCtx);
            string shown = full.Substring(0, Math.Min(full.Length, (int)_typed));
            Core.Draw.Wrapped(sb, Assets.Body, shown, new Vector2(textX, box.Y + 26), box.Right - textX - 30, Palette.Text);

            if (_typed < full.Length) return;
            var choices = VisibleChoices();
            if (choices.Count == 0)
            {
                Core.Draw.TextRight(sb, Assets.Small, "Enter - continue", new Vector2(box.Right - 20, box.Bottom - 26), Palette.TextDim);
                return;
            }
            for (int i = 0; i < choices.Count; i++)
            {
                bool sel = i == _modalIndex;
                var row = new Rectangle(textX - 8, box.Y + 96 + i * 30, 520, 28);
                if (sel) Core.Draw.Rect(sb, row, Palette.PanelLight);
                Core.Draw.Text(sb, Assets.Body, (sel ? "> " : "  ") + choices[i].Label, new Vector2(row.X + 8, row.Y + 5), sel ? Palette.Accent : Palette.Text);
            }
        }

        private void OptionsBox(SpriteBatch sb, string title, string body, string[] options, string[] icons = null)
        {
            float bodyH = 0;
            const int width = 760;
            if (body != null)
            {
                // Measure by wrapping off-screen once (cheap at this size).
                bodyH = MeasureWrapped(Assets.Body, body, width - 60);
            }
            int h = (int)(90 + bodyH + options.Length * 42 + 20);
            var r = new Rectangle(Game1.Width / 2 - width / 2, Game1.Height / 2 - h / 2, width, h);
            Core.Draw.Panel(sb, r, border: Palette.Accent);
            Core.Draw.TextCentered(sb, Assets.Header, title, new Vector2(r.Center.X, r.Y + 32), Palette.Accent);
            float y = r.Y + 64;
            if (body != null)
            {
                Core.Draw.Wrapped(sb, Assets.Body, body, new Vector2(r.X + 30, y), width - 60, Palette.Text);
                y += bodyH + 16;
            }
            for (int i = 0; i < options.Length; i++)
            {
                var row = new Rectangle(r.X + 20, (int)y + i * 42, width - 40, 36);
                bool sel = i == _modalIndex;
                if (sel) Core.Draw.Rect(sb, row, Palette.PanelLight);
                int textX = row.X + 10;
                if (icons != null && i < icons.Length)
                {
                    Icons.Draw(sb, icons[i], new Rectangle(row.X + 22, row.Y + 2, 32, 32));
                    textX += 50;
                }
                Core.Draw.Text(sb, Assets.Body, (sel ? "> " : "  ") + options[i], new Vector2(textX, row.Y + 8), sel ? Palette.Accent : Palette.Text);
            }
        }

        private static float MeasureWrapped(UiFont f, string s, float maxWidth)
        {
            int lines = 0;
            foreach (var paragraph in s.Split('\n'))
            {
                string line = "";
                lines++;
                foreach (var word in paragraph.Split(' '))
                {
                    string test = line.Length == 0 ? word : line + " " + word;
                    if (f.MeasureString(test).X > maxWidth && line.Length > 0) { lines++; line = word; }
                    else line = test;
                }
            }
            return lines * f.LineSpacing;
        }
    }
}
