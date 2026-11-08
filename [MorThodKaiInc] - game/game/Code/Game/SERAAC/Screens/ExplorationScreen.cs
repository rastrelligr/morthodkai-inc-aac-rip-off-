using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended.Tweening;
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
        private const float FeatureReach = TileSize * 1.45f;  // how close counts as "at" a feature (use it, collect, reach)
        private const int SidebarX = 1048;

        private enum Modal { None, Pause, Items, Map, Extract, Event, Message, Party }

        private readonly TrialRun _run;
        private Profile Profile => Game1.Instance.Profile;
        private Room Room => _run.Current;

        private Vector2 _pos;                                 // pixels, room-local
        private readonly List<Vector2> _trail = new();        // followers walk this path
        private float _walkAnim;
        private float _stepTimer;   // footstep sounds while walking
        private bool _moving;

        // Click-to-move: waypoints in room pixels, and whether to use the feature on arrival.
        private List<Vector2> _path;
        private bool _interactOnArrive;

        private List<Combatant> _pendingEnemies;              // shown in the room right before combat
        private float _encounterTimer;
        private bool _pendingIsEventFight;
        private Room _pendingRoom;                            // whose enemies the pending fight is (a pursuing pack's home room)

        // Hold-to-walk: after a click in the room, keeping the button down steers toward the cursor.
        private const float HoldToSteer = 0.2f;
        private bool _holdSteer;
        private float _holdTime;

        // A pack that chased the party out of its room and followed it through the door (Pack.Follow).
        private Pack _pursuit;
        private Room _pursuitFrom;
        private int _pursuitDoors;

        private float _roomFade;                              // quick fade when changing rooms
        private Room _nextRoom;
        private Dir _nextEntry;
        private bool _debugWarpToFeature;

        private Modal _modal;
        private int _modalIndex;
        private string _messageTitle, _messageBody;

        private string _banner;
        private int _bannerId;

        // Eased UI (MonoGame.Extended Tweener, see Core/Tweens.cs): the banner, the objective panel's glow,
        // and modals popping in.
        private readonly Tweens _tweens = new();
        private readonly Anim _bannerAlpha = new(), _bannerSlide = new(), _flash = new(), _modalIn = new(1f);
        private readonly Random _rng = new();                 // cosmetic only (where packs stand, idle wander)
        private readonly List<(string text, Vector2 pos, float age, Color color)> _floaters = new();

        private readonly Room _startIn;
        private readonly Fx.Ambience _ambience = new("room_dust");   // playtest: walk straight into this room instead of starting at the entrance

        public ExplorationScreen(TrialRun run, Room startIn = null)
        {
            _run = run;
            _startIn = startIn;
        }

        // Arrow keys walk and clicks on the floor move the party: menu sounds only while a menu is open.
        public override bool MenuKeySounds => _modal != Modal.None;
        public override bool UiSoundsFor(string key) => _modal != Modal.None || key != "room";

        private bool _storyEnding;

        // The playtest's map explains itself as the player finds things (UI/Coach.cs), and the squad talks at a few
        // moments (Story/SquadTalk.cs). Nothing says which door to take. If the first attempt failed, what only needs
        // explaining once is left out, and scenes don't repeat.
        private CoachTips _tips;
        private List<(string flag, Func<bool> when, Func<List<VnLine>> lines)> _scenes;
        private const string CoachedFlag = "coach_trial", CoachedFightFlag = "coach_fight", CoachedBossFlag = "coach_boss";

        private Rectangle TileRect(Point t) => new(RoomOrigin.X + t.X * TileSize, RoomOrigin.Y + t.Y * TileSize, TileSize, TileSize);

        private Rectangle? AroundPack()
        {
            var m = Room.Pack?.Members.FirstOrDefault();
            if (m == null) return null;
            var c = ToScreen(m.Pos);
            return new Rectangle((int)c.X - 44, (int)c.Y - 60, 88, 88);
        }

        private LootPickup NextPickup => Room.Pickups.FirstOrDefault(p => !p.Collected);

        private Rectangle? AroundPickup()
        {
            if (NextPickup == null) return null;
            var c = ToScreen(NextPickup.Tile * TileSize);
            return new Rectangle((int)c.X - 26, (int)c.Y - 26, 52, 52);
        }

        private CoachStep FeatureTip(RoomType type, string text) => new()
        {
            Soft = true,
            Text = text,
            Target = () => TileRect(Room.Layout.Feature),
            When = () => Room.Type == type && !Room.FeatureUsed,
            Done = () => Room.Type != type || Room.FeatureUsed || NearFeature(),   // close up, the room's own prompt takes over
        };

        private CoachTips BuildTips()
        {
            bool first = !Profile.StoryFlags.Contains(CoachedFlag);
            Profile.StoryFlags.Add(CoachedFlag);
            ProfileStore.Save(Profile);

            var tips = new List<CoachStep>();
            if (first)
            {
                tips.Add(new() { Target = () => new Rectangle(548, 12, 344, 80), Text = "Your goal for this run.\nThe way out stays sealed until it's done." });
                tips.Add(new() { Target = () => Ui.Bounds("minimap"), When = () => Ui.Bounds("minimap") != null, Text = "* is the Wyvern, X is the way out.\nHow you get there is up to you." });
                tips.Add(new() { Target = AroundPack, When = () => Room.Pack != null && Room.HasEnemies, Text = "Infected hunt you once they spot you.\nTouch one and the fight starts. Or run." });
                tips.Add(new()
                {
                    Soft = true,
                    Text = "Walk over loot to take it.\nIt's only yours if you make it out.",
                    Target = AroundPickup,
                    When = () => NextPickup != null,
                    Done = () => NextPickup == null,
                });
                tips.Add(FeatureTip(RoomType.Rest, "A shrine. Click it to heal the squad.\nEach one works once."));
                tips.Add(FeatureTip(RoomType.Treasure, "A cache. Click it to open."));
            }
            tips.Add(new()
            {
                Soft = true,
                Text = "Extraction is open. Head for the X.",
                Target = () => Ui.Bounds("minimap"),
                When = () => _run.ObjectiveCompleted && Room.Type != RoomType.Extraction,
                Done = () => Room.Type == RoomType.Extraction,
            });
            tips.Add(new()
            {
                Soft = true,
                Text = "Click to leave with your loot,\nor keep looking around.",
                Target = () => TileRect(Room.Layout.Feature),
                When = () => Room.Type == RoomType.Extraction && _run.CanExtract,
                Done = () => _modal == Modal.Extract || Room.Type != RoomType.Extraction,
            });
            return new CoachTips(tips);
        }

        private bool PlayScene()
        {
            foreach (var (flag, when, lines) in _scenes ?? new())
            {
                if (Story.Squad.Flag(flag) || !when()) continue;
                Story.Squad.SetFlag(flag);
                Screens.PushInstant(new StoryScreen(lines()));
                return true;
            }
            return false;
        }

        // The first fight of the route teaches the basics; the boss teaches how to answer a wind-up.
        private CombatTutorial CoachingFor(List<Combatant> enemies)
        {
            if (!_run.ShortRoute) return null;
            bool Once(string flag)
            {
                if (Profile.StoryFlags.Contains(flag)) return false;
                Profile.StoryFlags.Add(flag);
                ProfileStore.Save(Profile);
                return true;
            }
            var tutorial = Once(CoachedFightFlag) ? Story.StoryScripts.FirstFight() : null;
            if (enemies.Any(e => e.Enemy == EnemyDb.Wyvern) && Once(CoachedBossFlag))
            {
                tutorial ??= new CombatTutorial { Banner = null, Objective = null };
                tutorial.OnWindUp.AddRange(Story.StoryScripts.WyvernCoaching().OnWindUp);
            }
            return tutorial;
        }

        public override void OnResume()
        {
            if (!_storyEnding) return;
            _storyEnding = false;
            Story.StoryFlow.OnMainObjective();
        }

        public override void OnEnter()
        {
            PlaceAt(RoomLayout.EntryTile(Dir.South), Dir.South);
            ShowBanner($"{_run.Trial.Name}", 2.5f);
            if (_run.ShortRoute)
            {
                _tips = BuildTips();
                _scenes = new()
                {
                    ("vn_arrive", () => true, Story.SquadTalk.TrialArrive),
                    ("vn_first_win", () => Room.Type == RoomType.Normal && Room.Cleared, Story.SquadTalk.FirstWin),
                    ("vn_shrine", () => Room.Type == RoomType.Rest && Room.FeatureUsed, Story.SquadTalk.ShrineTalk),
                    ("vn_boss", () => Room.Type == RoomType.Objective && !_run.ObjectiveCompleted, Story.SquadTalk.BossRoom),
                };
            }
            if (_startIn != null && _startIn != _run.Current && _startIn.Links.Count > 0)
            {
                // Enter through one of its doors, like walking in (its pack spawns away from that door).
                _nextRoom = _startIn;
                _nextEntry = _startIn.Links.Keys.First();
            }
        }

        // Followers start lined up behind the leader, toward the door the party came through.
        private void PlaceAt(Point tile, Dir cameFrom)
        {
            _pos = new Vector2(tile.X * TileSize + TileSize / 2f, tile.Y * TileSize + TileSize / 2f);
            _path = null;
            _interactOnArrive = false;
            _trail.Clear();
            var back = RoomLayout.Step(cameFrom).ToVector2();
            for (int i = 0; i < 40; i++) _trail.Add(_pos + back * Math.Min(i, 30) * (TileSize * 1.05f / 30f));
        }

        private void ShowBanner(string text, float seconds = 2.2f)
        {
            // Slides down into place and fades in, holds, then fades out. A newer banner replaces it.
            _banner = text;
            int id = ++_bannerId;
            _tweens.FromTo(_bannerSlide, 16f, 0f, 0.35f, EasingFunctions.BackOut);
            _tweens.FromTo(_bannerAlpha, 0f, 1f, 0.18f, EasingFunctions.CubicOut).OnEnd(_ =>
            {
                if (id == _bannerId) _tweens.To(_bannerAlpha, 0f, 0.4f, EasingFunctions.QuadraticIn, Math.Max(0f, seconds - 0.6f));
            });
        }

        // The objective panel glows, then the glow eases away.
        private void FlashObjective(float seconds) => _tweens.FromTo(_flash, 1f, 0f, seconds, EasingFunctions.SineIn);

        private void Float(string text, Vector2 roomPos, Color c) => _floaters.Add((text, roomPos, 0f, c));

        // ------------------------------------------------------------------ update

        public override void Update(GameTime gameTime)
        {
            Audio.Music("explore");   // no-op while it's already playing (resumes after a fight)
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _ambience.Update(dt, new Vector2(SidebarX, Game1.Height));
            _tweens.Update(dt);
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

            if (_modal == Modal.Party)
            {
                UpdatePartyView();
                return;
            }
            if (_modal != Modal.None)
            {
                UpdateModal();
                return;
            }

            if (_roomFade <= 0)   // a scene over a half-faded room would freeze the fade
            {
                if (PlayScene()) return;
                if (_tips?.Update() == true) return;
            }

            int member = Ui.ClickedIndex("pmember");
            if (Input.Pressed(Keys.P) || Ui.Clicked("btn", 3) || member >= 0) { OpenPartyView(Math.Max(0, member)); return; }
            if (Input.Pressed(Keys.Escape) || Ui.Clicked("btn", 2)) { OpenModal(Modal.Pause); return; }
            if (Input.Pressed(Keys.I) || Input.Pressed(Keys.Tab) || Ui.Clicked("btn", 0)) { OpenModal(Modal.Items); return; }
            if (Input.Pressed(Keys.M) || Ui.Clicked("btn", 1) || Ui.Clicked("minimap")) { OpenModal(Modal.Map); return; }
            if (Input.Click && Ui.IsHover("room"))
            {
                var at = Input.MousePos - RoomOrigin.ToVector2();
                ClickRoom(at);
                _holdSteer = !IsFeatureTile(TileAt(at));   // holding the button down then steers the party
                _holdTime = 0;
            }
            if (Input.LeftDown) _holdTime += dt; else _holdSteer = false;
            if (Input.Pressed(Keys.F9)) { DebugJump(); return; }
            if (Input.Pressed(Keys.F10)) { DebugWarpTo(RoomType.Extraction); return; }
            if (Input.Pressed(Keys.F8)) { DebugWarpTo(RoomType.Npc); return; }
            if (Input.Pressed(Keys.F7)) { DebugWarpTo(RoomType.Objective); return; }
            if (Input.Pressed(Keys.F6)) { DebugFight(); return; }

            Walk(dt);
            CollectPickups();
            if (UpdatePack(dt)) return;
            UpdateObjectivePoint();
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
            _moving = false;
            var move = Input.Move;
            // Holding the left button (after a short moment) steers the party straight at the cursor.
            if (move == Vector2.Zero && _holdSteer && _holdTime > HoldToSteer)
            {
                var cursor = Input.MousePos - RoomOrigin.ToVector2();
                if (Vector2.Distance(cursor, _pos) > 8f) { _path = null; move = cursor - _pos; }
            }
            if (move != Vector2.Zero) _path = null;   // keys always win over a click
            else if (_path != null)
            {
                var to = _path[0] - _pos;
                float step = WalkSpeed * dt;
                if (to.Length() <= Math.Max(3f, step))
                {
                    _path.RemoveAt(0);
                    if (_path.Count == 0)
                    {
                        _path = null;
                        if (_interactOnArrive && NearFeature()) Interact();
                        _interactOnArrive = false;
                        return;
                    }
                    to = _path[0] - _pos;
                }
                move = to;
            }
            if (move == Vector2.Zero) return;
            move.Normalize();
            var delta = move * WalkSpeed * dt;
            if (_path != null && delta.Length() > (_path[0] - _pos).Length()) delta = _path[0] - _pos;

            var p = _pos;
            if (!Blocked(p.X + delta.X, p.Y)) p.X += delta.X;
            if (!Blocked(p.X, p.Y + delta.Y)) p.Y += delta.Y;
            if (p == _pos)
            {
                _path = null;   // stuck on a corner: give up rather than push forever
                return;
            }
            _pos = p;
            _moving = true;
            _walkAnim += dt;
            _stepTimer -= dt;
            if (_stepTimer <= 0) { Audio.Play("step", 0.35f, (float)(_rng.NextDouble() * 0.2 - 0.1)); _stepTimer = 0.32f; }

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

        // ------------------------------------------------------------------ click to move

        private static Point TileAt(Vector2 roomPos) => new((int)Math.Floor(roomPos.X / TileSize), (int)Math.Floor(roomPos.Y / TileSize));
        private static Vector2 TileCenter(Point t) => new(t.X * TileSize + TileSize / 2f, t.Y * TileSize + TileSize / 2f);

        private bool IsFeatureTile(Point t) => Room.HasFeature && t == Room.Layout.Feature;
        private bool Walkable(Point t) => !Room.Layout.Solid(t.X, t.Y) && !IsFeatureTile(t);

        // Walk to a clicked spot. Clicking the room's feature (chest, shrine, NPC...) walks next to it and uses it.
        private void ClickRoom(Vector2 roomPos)
        {
            var tile = TileAt(roomPos);
            var from = TileAt(_pos);
            _interactOnArrive = false;
            if (IsFeatureTile(tile))
            {
                if (NearFeature()) { _path = null; Interact(); return; }
                _interactOnArrive = true;
                // Stand on whichever free side of the feature is closest to the party.
                var sides = new[] { new Point(tile.X, tile.Y + 1), new Point(tile.X, tile.Y - 1), new Point(tile.X - 1, tile.Y), new Point(tile.X + 1, tile.Y) };
                tile = sides.Where(Walkable).OrderBy(s => Vector2.Distance(TileCenter(s), _pos)).DefaultIfEmpty(from).First();
            }
            else if (!Walkable(tile)) return;

            var tiles = FindPath(from, tile);
            if (tiles == null) return;
            var points = tiles.Select(TileCenter).ToList();
            // Stop exactly where the player clicked inside a floor tile (not for doors or features).
            bool ring = tile.X == 0 || tile.Y == 0 || tile.X == RoomLayout.Cols - 1 || tile.Y == RoomLayout.Rows - 1;
            if (!_interactOnArrive && !ring && !Blocked(roomPos.X, roomPos.Y)) points[^1] = roomPos;
            _path = Smooth(points);
        }

        // Breadth-first search over walkable tiles (4 directions). Null when unreachable.
        private List<Point> FindPath(Point from, Point to)
        {
            if (from == to) return new List<Point> { to };
            var prev = new Dictionary<Point, Point> { [from] = from };
            var q = new Queue<Point>();
            q.Enqueue(from);
            while (q.Count > 0)
            {
                var p = q.Dequeue();
                if (p == to) break;
                foreach (var d in RoomLayout.AllDirs)
                {
                    var s = RoomLayout.Step(d);
                    var n = new Point(p.X + s.X, p.Y + s.Y);
                    if (prev.ContainsKey(n) || !Walkable(n)) continue;
                    prev[n] = p;
                    q.Enqueue(n);
                }
            }
            if (!prev.ContainsKey(to)) return null;
            var path = new List<Point>();
            for (var c = to; c != from; c = prev[c]) path.Add(c);
            path.Reverse();
            return path;
        }

        // Skips waypoints the party can walk to in a straight line, so paths cut corners naturally.
        private List<Vector2> Smooth(List<Vector2> points)
        {
            var result = new List<Vector2>();
            var at = _pos;
            int i = 0;
            while (i < points.Count)
            {
                int far = i;
                for (int j = points.Count - 1; j > i; j--)
                    if (ClearLine(at, points[j])) { far = j; break; }
                result.Add(points[far]);
                at = points[far];
                i = far + 1;
            }
            return result;
        }

        private bool ClearLine(Vector2 a, Vector2 b)
        {
            float len = Vector2.Distance(a, b);
            for (float d = 0; d <= len; d += 6f)
            {
                var p = Vector2.Lerp(a, b, len < 0.01f ? 1 : d / len);
                if (Blocked(p.X, p.Y)) return false;
            }
            return true;
        }

        // [debug] F10 / F8 / F7: warp next to the feature of an Extraction Point / NPC room / objective site.
        private void DebugWarpTo(RoomType type)
        {
            var ex = _run.Map.Rooms.Values.Where(r => r.Type == type).OrderBy(r => r.FeatureUsed || r.Cleared).FirstOrDefault();
            if (ex == null) { ShowBanner($"[debug] no {type} room in this trial", 1.5f); return; }
            ex.Visited = true;
            _debugWarpToFeature = true;
            _nextRoom = ex;
            _nextEntry = ex.Links.Keys.First();
        }

        // [debug] F6: fight right here, against this room's enemies or the nearest room's (to check combat,
        // backgrounds and effects without hunting a pack). Counts as an event fight: the rooms stay as they were.
        private void DebugFight()
        {
            var source = Room.HasEnemies ? Room
                : _run.Map.Rooms.Values.Where(r => r.HasEnemies).OrderBy(r => Math.Abs(r.Depth - Room.Depth)).FirstOrDefault();
            if (source == null) { ShowBanner("[debug] no enemies left in this trial", 1.5f); return; }
            _pendingEnemies = _run.BuildEnemyCombatants(source);
            _pendingIsEventFight = true;
            _encounterTimer = 0.3f;
            ShowBanner("[debug] fight!", 0.6f);
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
            // Who is chasing the party through this door: the pack of the room it just left, or a pack
            // already following it. They keep coming for up to Pack.MaxFollow doors.
            var prev = _run.Current;
            Room chaserHome = null;
            int doors = 0;
            if (_pursuit is { State: PackState.Chase } && _pursuitFrom is { HasEnemies: true })
                (chaserHome, doors) = (_pursuitFrom, _pursuitDoors + 1);
            else if (prev != null && prev != room && prev.Pack is { State: PackState.Chase, Guarding: false } && prev.HasEnemies)
                (chaserHome, doors) = (prev, 1);
            _pursuit = null;
            _pursuitFrom = null;
            _pursuitDoors = 0;

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

            // Enemies don't attack on entry: they wait somewhere in the room and hunt the party once they see it.
            room.Pack = null;
            var leader = room.Type == RoomType.Objective ? _run.Objective?.RoomLeader : null;
            bool chased = chaserHome != null && doors <= Pack.MaxFollow;
            if (chased && chaserHome == room)
            {
                // Led them back home: they come through the door right behind the party.
                room.Pack = Pack.Follow(room, room.Encounter, entry, _rng);
                ShowBanner("They're right behind you!", 1.6f);
            }
            else if (room.HasEnemies)
            {
                room.Pack = Pack.Spawn(room, RoomLayout.EntryTile(entry), guarding: leader != null, _rng);
                if (room.Alerted && leader == null) room.Pack.Alarm();
                ShowBanner(room.Type switch
                {
                    RoomType.Elite => "An elite prowls this room...",
                    _ when leader != null => $"{leader.Name} guards this place",
                    _ when room.Alerted => "They were waiting for you...",
                    _ => firstVisit ? "Something moves in the dark..." : "They are still here...",
                }, 1.8f);
            }
            if (chased && chaserHome != room)
            {
                _pursuit = Pack.Follow(room, chaserHome.Encounter, entry, _rng);
                _pursuitFrom = chaserHome;
                _pursuitDoors = doors;
                ShowBanner($"The {chaserHome.Encounter[0].Name} followed you!", 1.6f);
            }
            else if (chaserHome != null && !chased)
                ShowBanner("You lost them... for now.", 1.6f);
            else if (_run.ObjectivePointOpen(room))
                ShowBanner("Objective: " + _run.Objective.Thing, 1.8f);
            else if (firstVisit)
            {
                ShowBanner(room.DisplayName, 1.6f);
            }
        }

        // The room's pack hunts the party; touching one of them starts the fight. True when combat is starting.
        private bool UpdatePack(float dt)
        {
            if (_nextRoom != null) return false;
            // The room's own pack first, then a pack that followed the party in from another room.
            foreach (var (pack, home) in new[] { (Room.Pack, Room), (_pursuit, _pursuitFrom) })
            {
                if (pack == null || home == null || !home.HasEnemies) continue;
                var before = pack.State;
                pack.Update(dt, _pos);
                if (before != PackState.Alert && pack.State == PackState.Alert) Audio.Play("alert");
                if (pack.State == PackState.Chase) home.Alerted = true;
                var hit = pack.Touching(_pos);
                if (hit == null) continue;

                bool caught = pack.State == PackState.Chase;
                _path = null;
                _pendingEnemies = _run.BuildEnemyCombatants(home);
                _pendingRoom = home;
                _pendingIsEventFight = false;
                _encounterTimer = 0.7f;
                ShowBanner(caught ? $"Caught by the {hit.Def.Name}!" : $"You engage the {hit.Def.Name}!", 1.0f);
                return true;
            }
            return false;
        }

        // Collect Item / Reach Location complete just by getting next to the room's objective point.
        private void UpdateObjectivePoint()
        {
            if (!_run.ObjectivePointOpen(Room) || !NearFeature()) return;
            if (_run.Objective.Type == ObjectiveType.Interact) return;  // needs [E]
            UseObjectivePoint();
        }

        private void UseObjectivePoint()
        {
            var o = _run.Objective;
            var fc = new Vector2(Room.Layout.Feature.X + 0.5f, Room.Layout.Feature.Y + 0.5f) * TileSize;
            var result = _run.UseObjectivePoint(Room, Profile);
            Float(o.Type switch
            {
                ObjectiveType.CollectItem => $"Took the {o.Thing}",
                ObjectiveType.ReachLocation => $"Reached the {o.Thing}",
                _ => o.Prompt,
            }, fc, Palette.Accent);
            ObjectiveFeedback(result);
        }

        // Feedback for objective progress (design: the player must be told when it completes).
        private bool ObjectiveFeedback(ObjectiveUpdate update)
        {
            var o = _run.Objective;
            if (update == ObjectiveUpdate.Completed)
            {
                var next = o.Main ? WorldDb.Areas.FirstOrDefault(a => a.UnlockedBy == _run.Area.ObjectiveId) : null;
                ShowBanner("OBJECTIVE COMPLETE  -  extraction unlocked" + (next != null ? $"  -  {next.Name} unlocked" : ""), 4f);
                Audio.Play("objective");
                FlashObjective(3f);
                return true;
            }
            if (update == ObjectiveUpdate.Progress)
            {
                ShowBanner($"{o.Task}  {_run.ObjectiveProgress} / {o.Count}", 2.2f);
                FlashObjective(1.5f);
                return true;
            }
            return false;
        }

        private void StartCombat()
        {
            var enemies = _pendingEnemies;
            _pendingEnemies = null;
            var room = _pendingRoom ?? Room;
            _pendingRoom = null;
            bool eventFight = _pendingIsEventFight;
            Screens.Push(new CombatScreen(_run, enemies, battle => OnCombatFinished(room, battle, eventFight)) { Tutorial = CoachingFor(enemies) });
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

            var objective = _run.RecordKills(battle, Profile);
            if (eventFight)
            {
                _run.PlaceDrops(room, new List<LootPickup> { _run.EquipmentDrop() });
            }
            else
            {
                // A pack that followed the party is cleared from its own room, but drops its loot here.
                _run.OnCombatWon(room, lootIn: Room);
                room.Pack = null;
                if (room == _pursuitFrom) { _pursuit = null; _pursuitFrom = null; }
            }
            var questsDone = Quests.OnVictory(Profile, room, battle);
            Screens.Pop();
            // The prologue's last fight: the ending plays once the fight has closed (OnResume).
            if (objective == ObjectiveUpdate.Completed && _run.Objective?.Main == true && Profile.Story == Story.StoryFlow.Academy)
                _storyEnding = true;

            if (objective == ObjectiveUpdate.Completed) ObjectiveFeedback(objective);
            else if (questsDone.Count > 0)
                ShowBanner($"Quest complete: {string.Join(", ", questsDone)}  -  find the quest giver", 3f);
            else if (!ObjectiveFeedback(objective)) ShowBanner("Room cleared  -  collect the loot", 1.8f);
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
                    Audio.Play("pickup");
                    Float(msg, world, p.Kind == LootKind.Shards ? Palette.Unsecured : p.Kind == LootKind.Equipment ? Palette.Guard : Palette.Heal);
                }
            }
        }

        private bool NearFeature()
        {
            if (!Room.HasFeature) return false;
            var f = Room.Layout.Feature;
            var center = new Vector2(f.X * TileSize + TileSize / 2f, f.Y * TileSize + TileSize / 2f);
            return Vector2.Distance(center, _pos) < FeatureReach;
        }

        private string FeaturePrompt() => Room.Type switch
        {
            RoomType.Entrance => "[E] Read the sign",
            RoomType.Treasure => Room.FeatureUsed ? null : "[E] Open the cache",
            RoomType.Rest => Room.FeatureUsed ? null : "[E] Pray at the shrine",
            RoomType.Event => Room.FeatureUsed ? null : "[E] Investigate",
            RoomType.Extraction => _run.CanExtract ? "[E] Extract" : "[E] Extraction sealed",
            RoomType.Npc when Room.Npc != null => $"[E] Talk to {Room.Npc.Name}",
            RoomType.Objective when _run.ObjectivePointOpen(Room) && _run.Objective.Type == ObjectiveType.Interact => $"[E] {_run.Objective.Prompt}",
            _ => null,
        };

        private void Interact()
        {
            var fc = new Vector2(Room.Layout.Feature.X + 0.5f, Room.Layout.Feature.Y + 0.5f) * TileSize;
            switch (Room.Type)
            {
                case RoomType.Entrance:
                    ShowMessage("Sealed Entrance", "The way you came in has closed behind you.\n\nComplete the objective (top of the screen), then find an Extraction Point (X on the map) to leave with your loot.");
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
                case RoomType.Extraction when !_run.CanExtract:
                    ShowMessage("Extraction Sealed", "The way out will not open until this layer's objective is complete.\n\n" + ObjectiveStatusText());
                    break;
                case RoomType.Extraction:
                    OpenModal(Modal.Extract);
                    break;
                case RoomType.Npc when Room.Npc != null:
                    StartDialogue(Room.Npc);
                    break;
                case RoomType.Objective when _run.ObjectivePointOpen(Room) && _run.Objective.Type == ObjectiveType.Interact:
                    UseObjectivePoint();
                    break;
            }
        }

        private string ObjectiveStatusText()
        {
            var o = _run.Objective;
            if (o == null) return "No objective in this trial.";
            string progress = o.Count > 1 ? $"  ({_run.ObjectiveProgress} / {o.Count})" : "";
            string where = o.RoomCount > 0 ? "\n\nObjective sites are marked * on the map." : "";
            return $"Objective: {o.Name}\n{o.Task}{progress}{where}";
        }

        // ------------------------------------------------------------------ dialogue

        private DialogueContext DialogueCtx => new()
        {
            Profile = Profile,
            Run = _run,
            Notify = msg => ShowBanner(msg, 3f),
        };

        // NPC conversations play as a visual-novel scene over the room: the NPC's background (else the room's own,
        // like a fight here), typewriter text, choices, and the backlog / auto / skip controls.
        private void StartDialogue(NpcDef npc)
        {
            Room.FeatureUsed = true;
            var ctx = DialogueCtx;
            StoryScreen screen = null;
            VnLine Line(string id)
            {
                if (id == "end" || !npc.Nodes.TryGetValue(id, out var node)) return null;
                var line = new VnLine { Speaker = npc.Name, TextFn = () => node.TextFor(ctx), Background = node.Background };
                line.OnShow = () =>
                {
                    node.OnEnter?.Invoke(ctx);
                    // Choices are read after the node's effects, so they reflect them.
                    line.Choices = node.Choices.Where(c => c.If == null || c.If(ctx))
                        .Select(c => new VnChoice(c.Label, () =>
                        {
                            var next = Line(c.Next);
                            if (next != null) screen.Dialogue.Insert(new[] { next });
                        })).ToList();
                };
                return line;
            }
            var first = Line(npc.Entry(ctx));
            if (first == null) return;
            string bg = npc.Background ?? RoomBackground();
            screen = new StoryScreen(new[] { first }, null, bg == "none" ? null : bg);
            Screens.PushInstant(screen);
        }

        // This room's background: the one it already has, else one picked like a normal fight in this area.
        private string RoomBackground()
        {
            if (Room.StageId == null)
                Room.StageId = Stage.BackgroundDb.Pick(_run.Area.Id, _run.Trial.Id, "normal", Array.Empty<string>(), new Random())?.Image;
            return Room.StageId;
        }

        private void ShowMessage(string title, string body)
        {
            _messageTitle = title;
            _messageBody = body;
            OpenModal(Modal.Message);
        }

        private void OpenModal(Modal m)
        {
            // Pop in when a box opens over the room; going from one box to another (Pause -> How to Play) pops too.
            _tweens.FromTo(_modalIn, 0f, 1f, 0.22f, EasingFunctions.BackOut);
            _modal = m;
            _modalIndex = 0;
        }

        // ------------------------------------------------------------------ modals

        private int ModalOptionCount => _modal switch
        {
            Modal.Pause => 4,
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

            bool clicked = Ui.Pick("opt", ref _modalIndex);
            // A click outside the box closes it (the map closes on any click).
            bool clickedAway = Input.Click && !clicked && (_modal == Modal.Map || Ui.Hover("optbox") < 0);
            bool close = Input.Cancel || clickedAway || (_modal == Modal.Map && Input.Pressed(Keys.M)) ||
                         (_modal == Modal.Items && (Input.Pressed(Keys.I) || Input.Pressed(Keys.Tab)));
            if (close && _modal != Modal.Event)
            {
                _modal = Modal.None;
                return;
            }
            if (!Input.Confirm && !clicked) return;

            switch (_modal)
            {
                case Modal.Pause:
                    if (_modalIndex == 0) _modal = Modal.None;
                    else if (_modalIndex == 1) ShowMessage("How to Play", MainMenuScreen.HelpText);
                    else if (_modalIndex == 2) { _modal = Modal.None; Screens.PushInstant(new OptionsScreen()); }
                    else
                    {
                        // Abandoning counts as a failed trial: unsecured loot is lost.
                        _run.Fail(Profile);
                        Screens.Replace(new ResultScreen(_run, extracted: false, abandoned: true));
                    }
                    break;

                case Modal.Extract:
                    if (_modalIndex == 0 && _run.CanExtract)
                    {
                        _run.Extract(Profile);
                        Screens.Replace(new ResultScreen(_run, extracted: true) { OnContinue = Story.StoryFlow.OnExtracted() });
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
                        Audio.Play("item");
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
            _ambience.DrawFront(sb, new Rectangle(0, RoomOrigin.Y, SidebarX, Game1.Height - RoomOrigin.Y), Core.Draw.FrameTransform);
            DrawSidebar(sb, t);

            if (_roomFade > 0)
                Core.Draw.Rect(sb, new Rectangle(RoomOrigin.X, RoomOrigin.Y, RoomLayout.Cols * TileSize, RoomLayout.Rows * TileSize), Palette.Ink * _roomFade);

            if (_banner != null && _bannerAlpha.Value > 0.01f)
            {
                float a = MathHelper.Clamp(_bannerAlpha.Value, 0, 1);
                var center = new Vector2(RoomOrigin.X + RoomLayout.Cols * TileSize / 2f, RoomOrigin.Y + 60 - _bannerSlide.Value);
                Ui.Banner(sb, _banner, center, RoomLayout.Cols * TileSize, Palette.Rose, a, Assets.Header, 48f, 1.5f);
            }

            DrawModal(sb, t);
            if (Screens.Top == this && _modal == Modal.None && _roomFade <= 0 && _pendingEnemies == null) _tips?.Draw(sb);
        }

        private void DrawTopBar(SpriteBatch sb)
        {
            Core.Draw.Ornament(sb, new Vector2(30, 27), 14, Palette.Paper * 0.85f);
            Core.Draw.Text(sb, Assets.Header, _run.Trial.Name.ToUpper(), new Vector2(48, 14), Palette.Paper);
            Core.Draw.Text(sb, Assets.Small, $"{_run.Area.Name}   -   {Room.DisplayName}   -   depth {Room.Depth}", new Vector2(48, 44), Palette.TextDim);

            DrawObjectivePanel(sb, new Rectangle(548, 12, 344, 80));

            var box = new Rectangle(900, 12, 360, 80);
            Core.Draw.Panel(sb, box, Palette.EnemyTag, Palette.Unsecured);
            Core.Draw.Text(sb, Assets.Small, "TRIAL LOOT  (lost if the party falls)", new Vector2(box.X + 14, box.Y + 8), Palette.Unsecured);
            Core.Draw.Text(sb, Assets.Header, $"{_run.TrialShards} shards", new Vector2(box.X + 14, box.Y + 32), Palette.Text);
            Core.Draw.TextRight(sb, Assets.Small, $"{_run.FoundEquipment.Count} equipment   {_run.Consumables.Count} items",
                new Vector2(box.Right - 14, box.Y + 56), Palette.Text);
            Core.Draw.TextRight(sb, Assets.Small, $"secured: {Profile.SecuredShards}", new Vector2(box.Right - 14, box.Y + 32), Palette.Accent);

            // Clickable shortcuts (keys still work: P, I, M, Esc). Button ids: 0 items, 1 map, 2 menu, 3 party.
            var buttons = new[] { (3, "Party (P)"), (0, "Items (I)"), (1, "Map (M)"), (2, "Menu (Esc)") };
            for (int i = 0; i < buttons.Length; i++)
                Ui.Button(sb, "btn", buttons[i].Item1, new Rectangle(24 + i * 104, 66, 98, 28), buttons[i].Item2, font: Assets.Small);
            Ui.HelpButton(sb, new Vector2(458, 80), "Walk: click a spot, hold the button to steer, or WASD / arrows.\nUse chests, shrines and the Extraction Point: click them or E.\nParty P, Items I, Map M, Menu Esc.\nEnemies hunt you once they spot you; touching one starts the fight.");
        }

        // Shown from the moment the trial starts: what to do, progress, and whether extraction is open.
        private void DrawObjectivePanel(SpriteBatch sb, Rectangle box)
        {
            var o = _run.Objective;
            bool done = _run.ObjectiveCompleted;
            var edge = done ? Palette.Heal : Palette.Accent;
            Core.Draw.Panel(sb, box, Palette.Panel, edge);
            if (_flash.Value > 0.01f)
                Core.Draw.Border(sb, box, edge * MathHelper.Clamp(_flash.Value, 0, 1), 3);

            if (o == null)
            {
                Core.Draw.Text(sb, Assets.Small, "OBJECTIVE", new Vector2(box.X + 14, box.Y + 8), Palette.TextDim);
                Core.Draw.Text(sb, Assets.Body, "None  -  extract any time", new Vector2(box.X + 14, box.Y + 34), Palette.Text);
                return;
            }
            Icons.Draw(sb, "ui_objective", new Rectangle(box.X + 10, box.Y + 6, 18, 18));
            Core.Draw.Text(sb, Assets.Small, o.Main ? "OBJECTIVE  (main)" : "OBJECTIVE", new Vector2(box.X + 32, box.Y + 8), Palette.TextDim);
            Core.Draw.TextRight(sb, Assets.Small, done ? "COMPLETE" : o.Count > 1 ? $"{_run.ObjectiveProgress} / {o.Count}" : "",
                new Vector2(box.Right - 14, box.Y + 8), done ? Palette.Heal : Palette.Accent);
            Core.Draw.Text(sb, Assets.Body, o.Name, new Vector2(box.X + 14, box.Y + 30), done ? Palette.Heal : Palette.Text);
            Core.Draw.Text(sb, Assets.Small, done ? "Extraction open  -  extract, or keep looting" : o.Task + "  -  extraction sealed",
                new Vector2(box.X + 14, box.Y + 56), done ? Palette.Heal : Palette.TextDim);
        }

        private void DrawPack(SpriteBatch sb, Pack pack, double t)
        {
            foreach (var m in pack.Members.OrderBy(m => m.Pos.Y))
            {
                var c = ToScreen(m.Pos);
                float scale = m.Def.Scale;
                int w = (int)(34 * scale), h = (int)(50 * scale);
                float step = m.Moving ? (float)Math.Abs(Math.Sin(m.Anim * 11)) * 3 : 0;
                Figures.Unit(sb, m.Def.ExploreArt, null, new Rectangle((int)c.X - w / 2, (int)(c.Y + 8 - h - step), w, h), m.Def.Color,
                    m.FaceLeft, t * 3, bob: !m.Moving, pose: m.Moving ? "walk" : null, fallbackSprite: EnemyDb.MockupSprite);
            }
            // No Type badges here on purpose: the player learns what is chasing the party when the fight starts.
            if (pack.State == PackState.Alert || pack.State == PackState.Chase && pack.StateTime < 0.6f)
            {
                var lead = ToScreen(pack.Members[0].Pos);
                // The moment they notice the party: a red ring bursts out from the one who spotted the party.
                float burst = pack.State == PackState.Alert ? MathHelper.Clamp(pack.StateTime / Pack.NoticeDelay, 0, 1) : 1f;
                Core.Draw.Ellipse(sb, lead + new Vector2(0, 6), new Vector2(14 + burst * 46, 6 + burst * 18), Palette.Danger * (1f - burst * 0.85f), 3);
                Core.Draw.TextCentered(sb, Assets.Header, "!", lead + new Vector2(0, -58 * pack.Members[0].Def.Scale + (float)Math.Sin(t * 20) * 2), Palette.Danger);
            }
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
                        Core.Draw.Rect(sb, new Rectangle(r.X + 6, r.Bottom - 6, r.Width - 8, 6), Palette.Ink * 0.35f);
                        Core.Draw.Rect(sb, new Rectangle(r.X + 4, r.Y + 8, r.Width - 8, r.Height - 12), Color.Lerp(area.Wall, area.Floor, 0.35f));
                        Core.Draw.Rect(sb, new Rectangle(r.X + 4, r.Y + 2, r.Width - 8, 10), Color.Lerp(area.Floor, Color.White, 0.22f));
                    }
                }
            }

            DrawMinimapHintOnDoors(sb);
            var roomRect = new Rectangle(RoomOrigin.X, RoomOrigin.Y, RoomLayout.Cols * TileSize, RoomLayout.Rows * TileSize);
            Ui.Hit("room", roomRect);
            DrawCursor(sb, roomRect, t);
            if (Room.HasFeature) DrawFeature(sb, t);

            foreach (var p in Room.Pickups.Where(p => !p.Collected)) DrawPickup(sb, p, t);

            if (_pursuit != null && _pursuitFrom is { HasEnemies: true }) DrawPack(sb, _pursuit, t);
            if (Room.Pack != null && Room.HasEnemies) DrawPack(sb, Room.Pack, t);
            else if (_pendingEnemies != null && _pendingRoom == null)
            {
                // Fights with no pack (an event gone wrong) appear in a line right before combat.
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

            bool hoverFeature = _modal == Modal.None && Ui.IsHover("room") && IsFeatureTile(TileAt(Input.MousePos - RoomOrigin.ToVector2()));
            if (_modal == Modal.None && _pendingEnemies == null && (NearFeature() || hoverFeature))
            {
                string prompt = FeaturePrompt()?.Replace("[E]", NearFeature() ? "[E / click]" : "[click]");
                if (prompt != null)
                {
                    var fp = ToScreen(new Vector2(Room.Layout.Feature.X + 0.5f, Room.Layout.Feature.Y) * TileSize);
                    var size = Assets.Body.MeasureString(prompt);
                    Core.Draw.Rect(sb, fp.X - size.X / 2 - 8, fp.Y - 34, size.X + 16, 28, Palette.Ink * 0.75f);
                    Core.Draw.TextCentered(sb, Assets.Body, prompt, fp + new Vector2(0, -20), Palette.Accent);
                }
            }
        }

        // Tile outline under the mouse, plus the path the party will take.
        private void DrawCursor(SpriteBatch sb, Rectangle roomRect, double t)
        {
            if (_path != null)
            {
                var from = ToScreen(_pos);
                foreach (var p in _path)
                {
                    var to = ToScreen(p);
                    Core.Draw.Line(sb, from, to, Palette.Lilac300 * 0.35f, 2);
                    from = to;
                }
                Core.Draw.Diamond(sb, from, 10, Palette.Lilac300 * 0.8f);
            }
            if (_modal != Modal.None || _pendingEnemies != null || !Ui.IsHover("room")) return;
            var tile = TileAt(Input.MousePos - RoomOrigin.ToVector2());
            if (!IsFeatureTile(tile) && !Walkable(tile)) return;
            var r = new Rectangle(RoomOrigin.X + tile.X * TileSize, RoomOrigin.Y + tile.Y * TileSize, TileSize, TileSize);
            var col = IsFeatureTile(tile) ? Palette.Accent : Palette.Lilac300;
            Core.Draw.Rect(sb, r, col * 0.12f);
            Core.Draw.Border(sb, r, col * (0.5f + 0.2f * (float)Math.Sin(t * 5)), 2);
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
            // Objective rooms: art per objective type, e.g. feature_objective_collect / feature_objective_collect_done.
            var objType = _run.Objective?.Type ?? ObjectiveType.DefeatTarget;
            bool objDone = objType == ObjectiveType.DefeatTarget ? Room.Cleared : Room.FeatureUsed;
            string art = Room.Type switch
            {
                RoomType.Treasure => Room.FeatureUsed ? "feature_chest_open" : "feature_chest",
                RoomType.Rest => "feature_shrine",
                RoomType.Event => "feature_event",
                RoomType.Extraction => _run.CanExtract ? "feature_portal" : "feature_portal_sealed",
                RoomType.Objective when objType == ObjectiveType.DefeatTarget => "feature_objective",
                RoomType.Objective => "feature_objective_" + objType switch
                {
                    ObjectiveType.CollectItem => "collect",
                    ObjectiveType.Interact => "interact",
                    _ => "reach",
                } + (objDone ? "_done" : ""),
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
                    // Sealed (grey, still) until the objective is complete.
                    bool open = _run.CanExtract;
                    // An upright oval portal: open = rings swirl outward, sealed = still grey rings crossed out.
                    var pc = new Vector2(r.Center.X, r.Center.Y - 4);
                    for (int i = 0; i < 4; i++)
                    {
                        float ring = open ? (float)((t * 0.8 + i * 0.25) % 1.0) : i * 0.25f;
                        var rad = new Vector2(10 + 16 * ring, 16 + 22 * ring);
                        float fade = open ? 1f - ring * 0.8f : 0.8f - i * 0.15f;
                        Core.Draw.Ellipse(sb, pc, rad, (open ? Palette.Accent : Palette.TextDisabled) * fade, 2);
                    }
                    if (!open)
                    {
                        Core.Draw.Line(sb, pc + new Vector2(-20, -28), pc + new Vector2(20, 28), Palette.TextDisabled, 2);
                        Core.Draw.Line(sb, pc + new Vector2(20, -28), pc + new Vector2(-20, 28), Palette.TextDisabled, 2);
                    }
                    break;
                case RoomType.Objective when objType == ObjectiveType.DefeatTarget:
                    var heart = Room.Cleared ? new Color(70, 40, 50) : new Color(170, 30, 60);
                    int grow = Room.Cleared ? 0 : (int)(pulse * 6);
                    Core.Draw.Rect(sb, new Rectangle(r.X + 6 - grow, r.Y + 6 - grow, 36 + grow * 2, 36 + grow * 2), heart);
                    break;
                case RoomType.Objective:
                    // Placeholder objective point: a plinth with a glowing marker until it's been taken / used / reached.
                    if (!objDone) Core.Draw.Rect(sb, new Rectangle(r.X - 4, r.Y - 6, r.Width + 8, r.Height + 10), Palette.Accent * (0.12f + 0.12f * pulse));
                    // Collect / Reach trigger by proximity: the ring on the floor is that range (see NearFeature).
                    if (!objDone && objType != ObjectiveType.Interact)
                        Core.Draw.Circle(sb, r.Center.ToVector2(), FeatureReach, Palette.Accent * (0.35f + 0.25f * pulse), 2, 40);
                    Core.Draw.Rect(sb, new Rectangle(r.X + 8, r.Y + 22, 32, 22), new Color(70, 66, 80));
                    Core.Draw.Rect(sb, new Rectangle(r.X + 8, r.Y + 22, 32, 4), new Color(110, 104, 124));
                    var mark = new Vector2(r.Center.X, r.Y + 10 - (objDone ? 0 : pulse * 4));
                    if (objType == ObjectiveType.CollectItem)
                    {
                        if (!objDone) Core.Draw.Diamond(sb, mark, 14, Palette.Accent);
                    }
                    else if (objType == ObjectiveType.Interact)
                        Core.Draw.Rect(sb, new Rectangle(r.Center.X - 7, r.Y - 4, 14, 26), objDone ? Palette.TextDisabled : Palette.Accent);
                    else
                    {
                        Core.Draw.Rect(sb, new Rectangle(r.Center.X - 2, r.Y - 18, 4, 40), new Color(150, 140, 130));
                        Core.Draw.Rect(sb, new Rectangle(r.Center.X + 2, r.Y - 18, 16, 10), objDone ? Palette.Heal : Palette.Accent);
                    }
                    if (!objDone)
                        Core.Draw.TextCentered(sb, Assets.Header, "*", new Vector2(r.Center.X, r.Y - 34 + (float)Math.Sin(t * 4) * 3), Palette.Accent);
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
            Core.Draw.Rect(sb, new Rectangle((int)c.X - 6, (int)(c.Y + 14), 12, 4), Palette.Ink * 0.3f);
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
                bool moving = _moving && _modal == Modal.None;
                float step = moving ? (float)Math.Abs(Math.Sin(_walkAnim * 12)) * 3 : 0;
                // Their overworld sprite (Art/Overworld/<id>.png) when they have one, else the combat sheet.
                string ow = m.Def.OverworldArt ?? "Overworld/" + m.Def.Id;
                string art = Sprites.Get(ow) != null ? ow : m.Def.ArtId;
                Figures.Unit(sb, art, m.Def.Role.Substring(0, 1),
                    new Rectangle((int)s.X - 15, (int)(s.Y - 44 - step), 30, 50), m.Def.Color, false, t, bob: false, pose: "walk");
            }
        }

        private void DrawSidebar(SpriteBatch sb, double t)
        {
            var panel = new Rectangle(SidebarX, 108, 216, 576);
            Core.Draw.Panel(sb, panel);
            Ui.SectionBar(sb, new Rectangle(panel.X + 6, panel.Y + 6, panel.Width - 12, 20), "Party  (click to manage)");
            for (int i = 0; i < _run.Party.Count; i++)
            {
                var m = _run.Party[i];
                var r = new Rectangle(panel.X + 8, panel.Y + 28 + i * 58, panel.Width - 16, 52);
                Ui.Card(sb, r, false);
                if (_modal == Modal.None) Ui.HoverRow(sb, "pmember", i, r, false);
                Figures.Portrait(sb, m.Def, new Rectangle(r.X + 4, r.Y + 4, 30, 44), t, m.Fallen ? 0.3f : 1f);
                Core.Draw.Text(sb, Assets.Small, m.Def.Name, new Vector2(r.X + 40, r.Y + 4), m.Fallen ? Palette.TextDisabled : Palette.Text);
                if (m.Fallen) Core.Draw.Text(sb, Assets.Small, "FALLEN", new Vector2(r.X + 40, r.Y + 26), Palette.Danger);
                else Figures.HpBar(sb, new Rectangle(r.X + 40, r.Y + 26, r.Width - 48, 16), m.Hp, m.MaxHp, false);
            }

            var mapArea = new Rectangle(panel.X + 8, panel.Y + 270, panel.Width - 16, 296);
            Ui.SectionBar(sb, new Rectangle(mapArea.X - 4, mapArea.Y - 8, mapArea.Width + 8, 20), "Map  (click to enlarge)");
            var mini = new Rectangle(mapArea.X, mapArea.Y + 14, mapArea.Width, mapArea.Height - 60);
            if (Ui.IsHover("minimap")) Core.Draw.Rect(sb, mini, Palette.Hover * 0.6f);
            DrawMap(sb, mini, 22);
            Ui.Hit("minimap", mini);
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
                var fill = r == Room ? Palette.Accent : r.Visited ? (r.Cleared || !r.IsCombatRoom ? Palette.PanelLight : Palette.EnemyTag) : Palette.PanelDark;
                Core.Draw.Rect(sb, rect, fill);
                Core.Draw.Border(sb, rect, r.Type == RoomType.Extraction ? Palette.Accent : Palette.Border, 1);
                bool showType = r.Visited || r.IsSpecial;
                if (showType && r.MapLabel != "")
                    Core.Draw.TextCentered(sb, Assets.Small, r.MapLabel, c, r == Room ? Palette.Ink : r.Type == RoomType.Extraction ? Palette.Accent : Palette.Text, shadow: false);
            }
        }

        // ------------------------------------------------------------------ party view (P)
        // Check the party and change the formation between fights. The order here is the trial's party
        // order: the living fill the fight's slots front to back in this order (the fallen are skipped).

        private int _partySel;         // index into _run.Party
        private int _partyHeld = -1;   // member picked up to swap, or -1

        private static readonly Rectangle PartyPanel = new(150, 60, 980, 610);

        // Formation cells: Front 1 on the right, facing the enemies, like in combat.
        private static Rectangle PartyCell(int index) =>
            new(PartyPanel.X + 30 + (3 - index) * 236, PartyPanel.Y + 56, 220, 236);

        private void OpenPartyView(int select)
        {
            OpenModal(Modal.Party);
            _partySel = Math.Clamp(select, 0, Math.Max(0, _run.Party.Count - 1));
            _partyHeld = -1;
        }

        private void UpdatePartyView()
        {
            int n = _run.Party.Count;
            if (n == 0) { _modal = Modal.None; return; }

            // Left/Right move across the screen (Left = toward the back).
            if (Input.Left) _partySel = Math.Min(n - 1, _partySel + 1);
            if (Input.Right) _partySel = Math.Max(0, _partySel - 1);
            int hover = Ui.Hover("pcell");
            if (hover >= 0 && Input.MouseMoved) _partySel = hover;

            int clicked = Ui.ClickedIndex("pcell");
            if (clicked >= 0) { _partySel = clicked; PickOrDrop(); return; }
            if (Input.Confirm) { PickOrDrop(); return; }

            bool clickedAway = Input.Click && Ui.Hover("ppanel") < 0 && hover < 0;
            if (Ui.Clicked("pclose") || Input.Pressed(Keys.P) || clickedAway) { _modal = Modal.None; return; }
            if (Input.Cancel)
            {
                if (_partyHeld >= 0) _partyHeld = -1;   // put the held member back first
                else _modal = Modal.None;
            }
        }

        // First press picks a member up, the second puts them in the selected slot (swapping).
        private void PickOrDrop()
        {
            if (_partyHeld < 0) { _partyHeld = _partySel; return; }
            if (_partyHeld != _partySel)
            {
                var p = _run.Party;
                (p[_partyHeld], p[_partySel]) = (p[_partySel], p[_partyHeld]);
                ShowBanner($"{p[_partySel].Def.Name} moves to {FightSlotName(_partySel)}", 1.2f);
            }
            _partyHeld = -1;
        }

        // The slot this member will stand in at the next fight (the fallen are skipped).
        private string FightSlotName(int index)
        {
            var m = _run.Party[index];
            if (m.Fallen) return "FALLEN";
            int slot = _run.Party.Take(index).Count(x => !x.Fallen);
            return HubScreen.SlotName(slot);
        }

        private void DrawPartyView(SpriteBatch sb, double t)
        {
            var r = PartyPanel;
            Core.Draw.Panel(sb, r);
            Ui.Hit("ppanel", r);
            Core.Draw.Text(sb, Assets.Header, "PARTY", new Vector2(r.X + 24, r.Y + 14), Palette.Accent);
            Core.Draw.Text(sb, Assets.Small, "FORMATION  (enemies are to the right  >>)", new Vector2(r.X + 124, r.Y + 22), Palette.TextDim);
            Ui.Button(sb, "pclose", 0, new Rectangle(r.Right - 110, r.Y + 12, 92, 30), "Close (P)", font: Assets.Small);

            // ---- formation
            for (int i = 0; i < 4; i++)
            {
                var cell = PartyCell(i);
                if (i >= _run.Party.Count)
                {
                    Core.Draw.Rect(sb, cell, Palette.PanelDark * 0.6f);
                    Core.Draw.TextCentered(sb, Assets.Small, "(empty)", cell.Center.ToVector2(), Palette.TextDisabled);
                    continue;
                }
                var m = _run.Party[i];
                bool sel = i == _partySel, held = i == _partyHeld;
                bool dropTarget = _partyHeld >= 0 && !held && sel;
                if (held) cell.Y -= 10;   // lifted

                Core.Draw.Rect(sb, cell, held ? Palette.PartyTag : sel ? Palette.Hover : Palette.PanelDark);
                if (held || dropTarget || sel) Core.Draw.Border(sb, cell, held ? Palette.Violet : dropTarget ? Palette.Lilac300 : Palette.Selected, held || dropTarget ? 3 : 2);
                Figures.Unit(sb, m.Def.ArtId, m.Def.Role.Substring(0, 1), new Rectangle(cell.Center.X - 46, cell.Y + 18, 92, 140),
                    m.Def.Color, false, t, m.Fallen ? 0.35f : 1f);
                Core.Draw.TextCentered(sb, Assets.Body, m.Def.Name, new Vector2(cell.Center.X, cell.Y + 172), m.Fallen ? Palette.TextDisabled : sel ? Palette.Accent : Palette.Text);
                if (m.Fallen) Core.Draw.TextCentered(sb, Assets.Small, "FALLEN", new Vector2(cell.Center.X, cell.Y + 196), Palette.Danger);
                else Figures.HpBar(sb, new Rectangle(cell.X + 20, cell.Y + 188, cell.Width - 40, 14), m.Hp, m.MaxHp, false);
                Core.Draw.TextCentered(sb, Assets.Small, FightSlotName(i), new Vector2(cell.Center.X, cell.Bottom - 14), Palette.TextDim);
                if (held) Core.Draw.TextCentered(sb, Assets.Small, "MOVING", new Vector2(cell.Center.X, cell.Y + 8), Palette.Violet);
                Ui.Hit("pcell", i, cell);
            }

            // ---- details of the selected member
            if (_partySel < _run.Party.Count) DrawMemberDetails(sb, _run.Party[_partySel], new Rectangle(r.X + 24, r.Y + 308, r.Width - 48, r.Height - 348));

            string hint = _partyHeld >= 0
                ? $"Moving {_run.Party[_partyHeld].Def.Name}: click a slot (or Left/Right + Enter) to swap.  Esc / right click: cancel"
                : "Click a member to pick them up, then another slot to swap.  Keys: Left/Right select, Enter pick up / drop, Esc close";
            Core.Draw.Text(sb, Assets.Small, hint, new Vector2(r.X + 24, r.Bottom - 28), Palette.TextDim);
        }

        // Name [Class], HP / ATK / SPD / Ethergy, equipment, then skills and passives.
        private void DrawMemberDetails(SpriteBatch sb, PartyMember m, Rectangle r)
        {
            var def = m.Def;
            Core.Draw.Rect(sb, r, Palette.PanelDark);
            float x = r.X + 16, y = r.Y + 10;
            Core.Draw.Text(sb, Assets.Header, def.Name, new Vector2(x, y), Palette.Blush);
            Core.Draw.Text(sb, Assets.Small, $"[{def.Role}]", new Vector2(x + Assets.Header.MeasureString(def.Name).X + 10, y + 10), Palette.TextDim);
            y += 36;

            Icons.Draw(sb, "ui_hp", new Rectangle((int)x, (int)y, 18, 18));
            Core.Draw.Text(sb, Assets.Body, m.Fallen ? "FALLEN" : $"{m.Hp} / {m.MaxHp}", new Vector2(x + 24, y - 1), m.Fallen ? Palette.Danger : Palette.Paper);
            y += 26;
            Icons.Draw(sb, "ui_damage", new Rectangle((int)x, (int)y, 18, 18));
            Core.Draw.Text(sb, Assets.Body, $"ATK {m.Damage}", new Vector2(x + 24, y - 1), Palette.Paper);
            Icons.Draw(sb, "ui_speed", new Rectangle((int)x + 110, (int)y, 18, 18));
            Core.Draw.Text(sb, Assets.Body, $"SPD {m.Speed}", new Vector2(x + 134, y - 1), Palette.Paper);
            y += 26;
            Icons.Draw(sb, "ui_sp", new Rectangle((int)x, (int)y, 18, 18));
            Core.Draw.Text(sb, Assets.Body, $"ETHERGY max {def.MaxSp}", new Vector2(x + 24, y - 1), Palette.Lilac300);
            Core.Draw.Text(sb, Assets.Small, $"starts at {Combat.Battle.StartingSp}, +{Combat.Battle.SpPerTurn}/turn", new Vector2(x + 24, y + 20), Palette.TextDim);
            y += 46;

            var prog = Profile.Progress(def.Id);
            var weapon = ItemDb.GetEquipment(prog.Weapon);
            var charm = ItemDb.GetEquipment(prog.Charm);
            Core.Draw.Text(sb, Assets.Small, "WEAPON  " + (weapon != null ? $"{weapon.Name}  {weapon.StatText()}" : "-"), new Vector2(x, y), Palette.Text);
            Core.Draw.Text(sb, Assets.Small, "CHARM    " + (charm != null ? $"{charm.Name}  {charm.StatText()}" : "-"), new Vector2(x, y + 18), Palette.Text);

            // Right column: skills (positions they work from / hit) and passives.
            float sx = r.X + 330, sy = r.Y + 10;
            Core.Draw.Text(sb, Assets.Small, "SKILLS", new Vector2(sx, sy), Palette.TextDim);
            sy += 24;
            foreach (var a in def.Skills)
            {
                HubScreen.SkillRow(sb, a, new Vector2(sx, sy), r.Right - sx - 12);
                sy += 28;
            }
            foreach (var p in def.Passives)
            {
                sy += Core.Draw.Wrapped(sb, Assets.Small, $"PASSIVE  {p.Name}: {p.Description}", new Vector2(sx, sy), r.Right - sx - 16, Palette.Accent) + 2;
            }
        }

        private void DrawModal(SpriteBatch sb, double t)
        {
            if (_modal == Modal.None) return;
            Core.Draw.Rect(sb, new Rectangle(0, 0, Game1.Width, Game1.Height), Palette.Ink * 0.65f * MathHelper.Clamp(_modalIn, 0, 1));
            Ui.BlockAll();
            Core.Draw.BeginPop(sb, new Vector2(Game1.Width / 2f, Game1.Height / 2f), _modalIn);

            switch (_modal)
            {
                case Modal.Party:
                    DrawPartyView(sb, t);
                    break;

                case Modal.Pause:
                    OptionsBox(sb, "PAUSED", null, new[] { "Resume", "How to Play", "Options", "Abandon Trial (lose all unsecured loot)" });
                    break;

                case Modal.Map:
                {
                    var r = new Rectangle(240, 90, 800, 560);
                    Core.Draw.Panel(sb, r);
                    Core.Draw.TextCentered(sb, Assets.Header, "TRIAL MAP", new Vector2(r.Center.X, r.Y + 28), Palette.Accent);
                    DrawMap(sb, new Rectangle(r.X + 20, r.Y + 60, r.Width - 40, r.Height - 120), 48);
                    Core.Draw.TextCentered(sb, Assets.Small, "X extraction   * main objective   ! elite   $ cache   + shrine   ? event   S entrance      click / M / Esc close",
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
            Core.Draw.EndPop(sb);
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
            Core.Draw.Rect(sb, r, Color.Lerp(Palette.Ink, Palette.Grey900, 0.3f) * 0.97f);
            Core.Draw.GradientV(sb, new Rectangle(r.X, r.Y, r.Width, 90), Palette.Crimson * 0.45f);
            Core.Draw.Border(sb, r, Palette.LineBright, 1);
            Core.Draw.Border(sb, new Rectangle(r.X + 5, r.Y + 5, r.Width - 10, r.Height - 10), Palette.Line, 1);
            Ui.Hit("optbox", r);
            Core.Draw.SpacedCentered(sb, Assets.Header, title.ToUpperInvariant(), new Vector2(r.Center.X, r.Y + 30), Palette.Paper, 3);
            Core.Draw.Divider(sb, new Vector2(r.Center.X, r.Y + 52), width - 160, Palette.Paper * 0.5f);
            Core.Draw.Ornament(sb, new Vector2(r.Center.X, r.Y + 52), 12, Palette.Gold);
            float y = r.Y + 66;
            if (body != null)
            {
                Core.Draw.Wrapped(sb, Assets.Body, body, new Vector2(r.X + 30, y), width - 60, Palette.Text);
                y += bodyH + 16;
            }
            for (int i = 0; i < options.Length; i++)
            {
                var row = new Rectangle(r.X + 20, (int)y + i * 42, width - 40, 36);
                bool sel = i == _modalIndex;
                if (sel) Ui.SelectedRow(sb, row);
                Ui.HoverRow(sb, "opt", i, row, sel);
                int textX = row.X + 10;
                if (icons != null && i < icons.Length)
                {
                    Icons.Draw(sb, icons[i], new Rectangle(row.X + 22, row.Y + 2, 32, 32));
                    textX += 50;
                }
                Core.Draw.Text(sb, Assets.Body, options[i], new Vector2(textX + 12, row.Y + 8), sel ? Palette.Paper : Palette.Text);
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
