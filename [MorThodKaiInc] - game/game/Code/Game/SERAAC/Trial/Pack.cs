using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using SERAAC.Data;

namespace SERAAC.Trial
{
    public enum PackState { Idle, Alert, Chase, Return }

    // One enemy of a pack, as it moves around the room while exploring.
    public sealed class Hunter
    {
        public EnemyDef Def;
        public Vector2 Pos, Home;          // room pixels
        public bool Moving, FaceLeft = true;
        public float Anim;

        internal Vector2? WanderTo;
        internal float WanderWait;
        internal List<Vector2> Path;
        internal float Repath;
    }

    // A room's uncleared enemies while the party explores it. Combat no longer starts on entry: the enemies
    // idle, notice the party when it comes into sight, shout ("!"), then hunt it down. Touching any of them
    // starts the room's fight. A chasing pack follows the party through doors (Follow) for up to MaxFollow
    // rooms; a room whose pack lost the party stays on guard (Alarm) when the party comes back.
    // How fast and how far they see comes from each enemy's Type (Data/EnemyTypeDb.cs).
    public sealed class Pack
    {
        public const int Tile = 48;                // same as ExplorationScreen.TileSize
        public const float FollowDelay = 1.1f;     // a pursuing pack bursts through the door this long after the party
        public const int MaxFollow = 2;            // doors a pack will follow the party through before losing the trail
        public const float EntryGrace = 0.8f;      // seconds after the party walks in before they can notice it
        public const float NoticeDelay = 0.55f;    // the "!" moment before they move
        public const float HearTime = 3.5f;        // seconds in the room before they come looking anyway (sight or not)
        public const float TouchDistance = 30f;
        public const float CloseNotice = 2.2f;     // tiles: noticed this close even without a clear line
        public const float GuardLeash = 1.6f;      // guardians give up past sight * this from home
        private const float HalfBox = 11f;
        private const float WanderSpeed = 55f, ReturnSpeed = 130f, GuardSpeedScale = 0.9f;

        public readonly List<Hunter> Members = new();
        public PackState State { get; private set; }
        public float StateTime { get; private set; }
        public bool Guarding { get; private set; }  // holds near the room's feature (objective guardians, bosses)

        private readonly Room _room;
        private readonly Random _rng;

        private Pack(Room room, Random rng)
        {
            _room = room;
            _rng = rng;
        }

        // Places the room's encounter away from the door the party came in by (or around the feature when guarding).
        public static Pack Spawn(Room room, Point entryTile, bool guarding, Random rng)
        {
            var pack = new Pack(room, rng) { Guarding = guarding };
            var layout = room.Layout;
            var open = layout.Reachable(entryTile)
                .Where(t => t.X > 0 && t.Y > 0 && t.X < RoomLayout.Cols - 1 && t.Y < RoomLayout.Rows - 1)
                .ToList();
            int Dist(Point a, Point b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

            Point center;
            if (guarding && room.HasFeature)
                center = open.Where(t => Dist(t, entryTile) >= 4).DefaultIfEmpty(entryTile)
                             .OrderBy(t => Dist(t, layout.Feature)).ThenBy(_ => rng.Next()).First();
            else
            {
                var far = open.Where(t => Dist(t, entryTile) >= 8).ToList();
                if (far.Count == 0) far = open.OrderByDescending(t => Dist(t, entryTile)).Take(6).ToList();
                center = far[rng.Next(far.Count)];
            }

            // Members stand on the open tiles nearest the centre.
            var spots = open.OrderBy(t => Vector2.Distance(t.ToVector2(), center.ToVector2())).ThenBy(_ => rng.Next()).ToList();
            for (int i = 0; i < room.Encounter.Count; i++)
            {
                var t = spots[Math.Min(i, spots.Count - 1)];
                var p = new Vector2(t.X * Tile + Tile / 2f, t.Y * Tile + Tile / 2f);
                pack.Members.Add(new Hunter { Def = room.Encounter[i], Pos = p, Home = p, WanderWait = (float)rng.NextDouble() * 2f });
            }
            return pack;
        }

        // A pack that chased the party out of another room: it stands in the doorway the party came through
        // and charges in after FollowDelay (+ the "!" moment). `encounter` is the source room's enemies.
        public static Pack Follow(Room into, IReadOnlyList<EnemyDef> encounter, Dir entry, Random rng)
        {
            var pack = new Pack(into, rng);
            var inside = RoomLayout.EntryTile(entry);
            var step = RoomLayout.Step(entry);
            var door = new Point(inside.X + step.X, inside.Y + step.Y);
            var side = new Point(step.Y, step.X);   // along the wall
            for (int i = 0; i < encounter.Count; i++)
            {
                int k = (i + 1) / 2 * (i % 2 == 0 ? 1 : -1);   // 0, -1, +1, -2...
                var t = new Point(door.X + side.X * k, door.Y + side.Y * k);
                if (into.Layout.Solid(t.X, t.Y)) t = door;
                var p = new Vector2(t.X * Tile + Tile / 2f, t.Y * Tile + Tile / 2f);
                pack.Members.Add(new Hunter { Def = encounter[i], Pos = p, Home = p });
            }
            pack.SetState(PackState.Alert, -FollowDelay);
            return pack;
        }

        // Coming back to a room whose pack already spotted the party: no grace, they shout at once.
        public void Alarm() => SetState(PackState.Alert, -0.25f);

        public Hunter Touching(Vector2 party) => Members.FirstOrDefault(m => Vector2.Distance(m.Pos, party) < TouchDistance);

        public void Update(float dt, Vector2 party)
        {
            StateTime += dt;
            foreach (var m in Members) m.Moving = false;

            switch (State)
            {
                case PackState.Idle:
                    foreach (var m in Members) Wander(m, dt);
                    if (StateTime > EntryGrace && (Notices(party) || (!Guarding && StateTime > HearTime))) SetState(PackState.Alert);
                    break;

                case PackState.Alert:
                    foreach (var m in Members) m.FaceLeft = party.X < m.Pos.X;
                    if (StateTime > NoticeDelay) SetState(PackState.Chase);
                    break;

                case PackState.Chase:
                    foreach (var m in Members)
                        MoveToward(m, party, Speed(m), dt);
                    if (Guarding && Vector2.Distance(party, Members[0].Home) > SightOf(Members[0]) * GuardLeash * Tile)
                        SetState(PackState.Return);
                    break;

                case PackState.Return:
                    bool home = true;
                    foreach (var m in Members)
                    {
                        if (Vector2.Distance(m.Pos, m.Home) < 4f) continue;
                        home = false;
                        MoveToward(m, m.Home, ReturnSpeed, dt);
                    }
                    if (home) SetState(PackState.Idle, EntryGrace);
                    else if (Notices(party)) SetState(PackState.Alert);
                    break;
            }
            Separate();
        }

        private void SetState(PackState s, float time = 0f)
        {
            State = s;
            StateTime = time;
            foreach (var m in Members) { m.Path = null; m.Repath = 0; }
        }

        private float Speed(Hunter m) => (m.Def.TypeDef?.ChaseSpeed ?? 180f) * (Guarding ? GuardSpeedScale : 1f);
        private static float SightOf(Hunter m) => m.Def.TypeDef?.SightTiles ?? 6f;

        // Someone sees the party: within the Type's sight with a clear line, or simply very close.
        private bool Notices(Vector2 party) => Members.Any(m =>
        {
            float d = Vector2.Distance(m.Pos, party);
            return d <= CloseNotice * Tile || (d <= SightOf(m) * Tile && ClearLine(m.Pos, party, box: false));
        });

        // Idle: amble around home a little so the room feels alive.
        private void Wander(Hunter m, float dt)
        {
            if (m.WanderTo == null)
            {
                m.WanderWait -= dt;
                if (m.WanderWait > 0) return;
                var to = m.Home + new Vector2((float)(_rng.NextDouble() * 2 - 1), (float)(_rng.NextDouble() * 2 - 1)) * Tile * 1.2f;
                m.WanderTo = ClearLine(m.Pos, to, box: true) ? to : m.Home;
            }
            if (Step(m, m.WanderTo.Value, WanderSpeed, dt) || Vector2.Distance(m.Pos, m.WanderTo.Value) < 3f)
            {
                m.WanderTo = null;
                m.WanderWait = 1.2f + (float)_rng.NextDouble() * 2.5f;
            }
        }

        // Straight at the target when the way is clear, otherwise along a tile path (refreshed often).
        private void MoveToward(Hunter m, Vector2 target, float speed, float dt)
        {
            if (ClearLine(m.Pos, target, box: true))
            {
                m.Path = null;
                Step(m, target, speed, dt);
                return;
            }
            m.Repath -= dt;
            if (m.Path == null || m.Path.Count == 0 || m.Repath <= 0)
            {
                m.Path = FindPath(TileAt(m.Pos), TileAt(target));
                m.Repath = 0.3f;
            }
            if (m.Path == null || m.Path.Count == 0) { Step(m, target, speed, dt); return; }
            if (Vector2.Distance(m.Pos, m.Path[0]) < 4f) m.Path.RemoveAt(0);
            if (m.Path.Count > 0) Step(m, m.Path[0], speed, dt);
        }

        // Moves toward a point, sliding along walls. Returns true when stuck.
        private bool Step(Hunter m, Vector2 to, float speed, float dt)
        {
            var dir = to - m.Pos;
            float len = dir.Length();
            if (len < 0.5f) return false;
            var delta = dir / len * Math.Min(len, speed * dt);
            var p = m.Pos;
            if (!Blocked(p.X + delta.X, p.Y)) p.X += delta.X;
            if (!Blocked(p.X, p.Y + delta.Y)) p.Y += delta.Y;
            if (p == m.Pos) return true;
            if (Math.Abs(delta.X) > 0.2f) m.FaceLeft = delta.X < 0;
            m.Pos = p;
            m.Moving = true;
            m.Anim += dt;
            return false;
        }

        // Keeps the pack from stacking into one sprite.
        private void Separate()
        {
            for (int i = 0; i < Members.Count; i++)
                for (int j = i + 1; j < Members.Count; j++)
                {
                    var a = Members[i]; var b = Members[j];
                    var d = b.Pos - a.Pos;
                    float len = d.Length();
                    if (len >= 26f) continue;
                    var push = (len < 0.01f ? new Vector2(1, 0) : d / len) * (26f - len) * 0.5f;
                    if (!Blocked(a.Pos.X - push.X, a.Pos.Y - push.Y)) a.Pos -= push;
                    if (!Blocked(b.Pos.X + push.X, b.Pos.Y + push.Y)) b.Pos += push;
                }
        }

        // ---------------------------------------------------------------- room geometry

        private static Point TileAt(Vector2 p) => new((int)Math.Floor(p.X / Tile), (int)Math.Floor(p.Y / Tile));

        private bool SolidTile(int tx, int ty) =>
            _room.Layout.Solid(tx, ty) || (_room.HasFeature && tx == _room.Layout.Feature.X && ty == _room.Layout.Feature.Y);

        private bool Blocked(float x, float y)
        {
            foreach (var (cx, cy) in new[] { (x - HalfBox, y - HalfBox), (x + HalfBox, y - HalfBox), (x - HalfBox, y + HalfBox), (x + HalfBox, y + HalfBox) })
                if (SolidTile((int)Math.Floor(cx / Tile), (int)Math.Floor(cy / Tile))) return true;
            return false;
        }

        // box: whether a body fits all the way (movement) or just the line of sight (pillars block both).
        private bool ClearLine(Vector2 a, Vector2 b, bool box)
        {
            float len = Vector2.Distance(a, b);
            for (float d = 0; d <= len; d += 8f)
            {
                var p = Vector2.Lerp(a, b, len < 0.01f ? 1 : d / len);
                if (box ? Blocked(p.X, p.Y) : SolidTile((int)Math.Floor(p.X / Tile), (int)Math.Floor(p.Y / Tile))) return false;
            }
            return true;
        }

        // Breadth-first over walkable tiles; returns tile-centre waypoints (null when unreachable).
        private List<Vector2> FindPath(Point from, Point to)
        {
            if (from == to) return new List<Vector2>();
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
                    if (prev.ContainsKey(n) || SolidTile(n.X, n.Y)) continue;
                    prev[n] = p;
                    q.Enqueue(n);
                }
            }
            if (!prev.ContainsKey(to)) return null;
            var path = new List<Vector2>();
            for (var c = to; c != from; c = prev[c]) path.Add(new Vector2(c.X * Tile + Tile / 2f, c.Y * Tile + Tile / 2f));
            path.Reverse();
            return path;
        }
    }
}
