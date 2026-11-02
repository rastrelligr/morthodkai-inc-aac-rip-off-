using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended.Tweening;
using SERAAC.Core;
using SERAAC.Fx;
using SERAAC.UI;
using SERAAC.World;
using MapObject = SERAAC.World.TiledMap.MapObject;

namespace SERAAC.Screens
{
    // What a walkable story map is: which Tiled file, how big it is drawn, what is behind / over it, and
    // what happens when Relle uses something. The story (Story/StoryFlow.cs) builds these.
    public sealed class MapScene
    {
        public string MapFile;                 // in Art/Maps
        public float Scale = 2f;               // screen pixels per map pixel
        public float ActorScale = 4f;          // screen pixels per sprite pixel (characters)
        public string Backdrop = "black";      // "black", or "sky" (deep blue under the clouds)
        public string AmbienceUnder;           // Art/Fx/ambience.json preset drawn under the map (the sky)
        public string AmbienceOver;            // preset drawn over the map
        public string PlayerSprite = "Overworld/relle";
        public string Spawn = "player_start";
        public float WalkTiles = 4.2f;         // tiles per second
        public string Title, Subtitle;         // banner on arrival
        public string Music;
        public string Footsteps = "step";      // sound id for Relle's steps (Core/Audio.cs)
        public Color? Tint;                    // a wash over the whole map (cold blue up on the Airlift)
        public Vector2 DropShadow;             // map pixels: the map's own shadow cast on the backdrop (0 = none)
        public Func<OverworldScreen, MapObject, bool> OnInteract;   // true = handled
        public Action<OverworldScreen> OnArrive;
    }

    // Top-down walking on a Tiled map (World/TiledMap.cs): WASD / arrows or click to walk, E / Enter / click
    // to use what is in front of Relle. Object layers drive it (see Art/Maps/README.md):
    //   class "collision" (or layer "Collision")  blocks the feet;   class "walkable" (or layer "Walkable")
    //   = the only floor (when any exists);   "interact" / "exit" / "npc"  = things to use;   "spawn" = start points.
    // The map reloads when the .tmx is saved, and F9 shows every shape.
    public sealed class OverworldScreen : Screen
    {
        private sealed class Actor
        {
            public string Name, Sprite;
            public Vector2 Pos;          // feet, map pixels
            public bool Flip;
            public float Walk;           // walk cycle time
            public bool Moving;
            public MapObject Source;     // npc object
        }

        public readonly MapScene Scene;
        private TiledMap _map;
        private DateTime _mapStamp;
        private double _nextCheck;
        private readonly Actor _player;
        private readonly List<Actor> _npcs = new();
        private Vector2 _cam;
        private float _time;
        private float _stepTimer;
        private bool _debug;

        private readonly Ambience _under, _over;
        private readonly Tweens _tweens = new();
        private readonly Anim _bannerIn = new(), _fadeIn = new();
        private string _hint;                  // objective line, top left
        private float _hintFlash;
        private string _toast;
        private float _toastTime;

        // Click to walk: a point, or an object to use on arrival.
        private Vector2? _walkTo;
        private MapObject _useOnArrive;
        private Actor _talkOnArrive;

        public readonly HashSet<string> Seen = new();   // objects already inspected (story uses it)
        public bool Locked;                              // the story is in control (no walking)

        public OverworldScreen(MapScene scene)
        {
            Scene = scene;
            _under = new Ambience(scene.AmbienceUnder);
            _over = new Ambience(scene.AmbienceOver);
            _player = new Actor { Name = "Relle", Sprite = scene.PlayerSprite };
            LoadMap();
            var spawn = _map.Named(scene.Spawn) ?? _map.OfClass("spawn").FirstOrDefault();
            _player.Pos = spawn != null ? new Vector2(spawn.X, spawn.Y) : new Vector2(_map.PixelWidth / 2f, _map.PixelHeight / 2f);
            _cam = _player.Pos;
        }

        private string MapPath => LiveJson.ArtPath(Path.Combine("Maps", Scene.MapFile));

        private void LoadMap()
        {
            _map = TiledMap.Load(MapPath);
            _mapStamp = File.GetLastWriteTimeUtc(MapPath);
            _npcs.Clear();
            foreach (var o in _map.OfClass("npc"))
                _npcs.Add(new Actor { Name = o.Name, Sprite = o.Prop("sprite", "Overworld/" + o.Name.ToLowerInvariant()), Pos = new Vector2(o.X, o.Y), Source = o });
        }

        public override bool MenuKeySounds => false;

        public override void OnEnter()
        {
            _tweens.FromTo(_fadeIn, 0f, 1f, 0.8f, EasingFunctions.QuadraticOut);
            if (Scene.Title != null) _tweens.FromTo(_bannerIn, 0f, 1f, 0.6f, EasingFunctions.ExponentialOut).OnEnd(_ =>
                _tweens.To(_bannerIn, 0f, 0.5f, EasingFunctions.QuadraticIn, 2.2f));
            if (Scene.Music != null) Audio.Music(Scene.Music);
            Scene.OnArrive?.Invoke(this);
        }

        // ------------------------------------------------------------------ story helpers

        // Plays a VN scene over the map; walking stops until it ends.
        public void Say(IEnumerable<VnLine> lines, Action onDone = null)
        {
            _walkTo = null;
            Screens.PushInstant(new StoryScreen(lines, onDone));
        }

        public void SetHint(string text)
        {
            if (text == _hint) return;
            _hint = text;
            _hintFlash = 1.5f;
        }

        public void Toast(string text)
        {
            _toast = text;
            _toastTime = 2.5f;
        }

        public Vector2 PlayerPos => _player.Pos;

        public void RemoveNpc(string name) => _npcs.RemoveAll(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));

        public void PlaceNpc(string name, string sprite, Vector2 pos)
        {
            RemoveNpc(name);
            _npcs.Add(new Actor { Name = name, Sprite = sprite, Pos = pos });
        }

        // ------------------------------------------------------------------ update

        public override void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            ReloadIfSaved();

            if (Input.Pressed(Keys.F9)) _debug = !_debug;
            if (Input.Pressed(Keys.Escape)) { Screens.PushInstant(new OptionsScreen()); return; }
            if (Locked) { _player.Moving = false; return; }

            var move = Input.Move;
            if (move != Vector2.Zero) { _walkTo = null; _useOnArrive = null; _talkOnArrive = null; }

            if (Input.Click && !Ui.IsHover("hud"))
            {
                var at = ScreenToMap(Input.MousePos);
                var npc = _npcs.FirstOrDefault(n => Vector2.Distance(n.Pos - new Vector2(0, SpriteHeight(n) * 0.4f), at) < SpriteHeight(n) * 0.55f);
                var obj = npc == null ? Usable().FirstOrDefault(o => o.Contains(at) || o.DistanceTo(at) < 6) : null;
                _talkOnArrive = npc;
                _useOnArrive = obj;
                _walkTo = npc?.Pos ?? (obj != null ? ClosestPoint(obj, _player.Pos) : at);
            }

            if (_walkTo.HasValue && move == Vector2.Zero)
            {
                var d = _walkTo.Value - _player.Pos;
                bool arrived = d.Length() < 3f
                               || (_useOnArrive != null && _useOnArrive.DistanceTo(_player.Pos) <= UseReach)
                               || (_talkOnArrive != null && Vector2.Distance(_talkOnArrive.Pos, _player.Pos) <= Reach);
                if (arrived)
                {
                    _walkTo = null;
                    if (_talkOnArrive != null) { var n = _talkOnArrive; _talkOnArrive = null; Talk(n); return; }
                    if (_useOnArrive != null) { var o = _useOnArrive; _useOnArrive = null; Use(o); return; }
                }
                else move = d;
            }

            Walk(move, dt);
            FollowCamera(dt);

            if (Input.Interact)
            {
                var (npc, obj) = Nearest();
                if (npc != null) Talk(npc);
                else if (obj != null) Use(obj);
            }
        }

        private void ReloadIfSaved()
        {
            if (_time < _nextCheck) return;
            _nextCheck = _time + 0.5;
            try
            {
                if (File.Exists(MapPath) && File.GetLastWriteTimeUtc(MapPath) != _mapStamp)
                {
                    var npcs = _npcs.ToList();
                    LoadMap();
                    // Keep story-placed NPCs that aren't on the map.
                    foreach (var n in npcs.Where(n => n.Source == null)) _npcs.Add(n);
                    Toast("Map reloaded");
                }
            }
            catch (Exception e) { Toast("Map failed to load: " + e.Message); }
        }

        private float TileSize => _map.TileWidth;
        private float Reach => TileSize * 2f;            // talking to someone
        private float UseReach => TileSize * 1.2f;       // using an object (distance from the feet to its shape)
        private float FeetRadius => Scene.ActorScale / Scene.Scale * 6f;

        private void Walk(Vector2 move, float dt)
        {
            _player.Moving = false;
            if (move == Vector2.Zero) return;
            move.Normalize();
            float speed = Scene.WalkTiles * TileSize;
            var step = move * speed * dt;
            var p = _player.Pos;
            // Each axis on its own, so the feet slide along walls.
            if (step.X != 0 && Free(new Vector2(p.X + step.X, p.Y))) p.X += step.X;
            if (step.Y != 0 && Free(new Vector2(p.X, p.Y + step.Y))) p.Y += step.Y;
            bool moved = (p - _player.Pos).LengthSquared() > 0.0001f;
            if (!moved && _walkTo.HasValue) _walkTo = null;   // clicked somewhere unreachable: stop
            _player.Moving = moved;
            if (moved)
            {
                _player.Walk += dt;
                _stepTimer -= dt;
                if (_stepTimer <= 0) { Audio.Play(Scene.Footsteps, 0.4f); _stepTimer = 0.3f; }
            }
            if (Math.Abs(move.X) > 0.2f) _player.Flip = move.X < 0;
            _player.Pos = p;
        }

        // The feet fit here: inside the map, on walkable floor (if the map has any), off every collision shape
        // and clear of the NPCs.
        private bool Free(Vector2 feet)
        {
            float r = FeetRadius;
            var probes = new[]
            {
                feet, feet + new Vector2(-r, 0), feet + new Vector2(r, 0), feet + new Vector2(0, -r * 0.5f), feet + new Vector2(0, r * 0.4f),
                feet + new Vector2(-r * 0.7f, -r * 0.4f), feet + new Vector2(r * 0.7f, -r * 0.4f),
            };
            var walkable = Walkable().ToList();
            var blocks = Collisions().ToList();
            foreach (var p in probes)
            {
                if (p.X < 0 || p.Y < 0 || p.X >= _map.PixelWidth || p.Y >= _map.PixelHeight) return false;
                if (walkable.Count > 0 && !walkable.Any(w => w.Contains(p))) return false;
                if (blocks.Any(b => b.Contains(p))) return false;
            }
            foreach (var n in _npcs)
                if (Vector2.Distance(n.Pos, feet) < r * 2.2f && Vector2.Distance(n.Pos, _player.Pos) >= r * 2.2f - 0.5f) return false;
            return true;
        }

        private IEnumerable<MapObject> Collisions() => _map.Objects.Where(o =>
            o.Class.Equals("collision", StringComparison.OrdinalIgnoreCase)
            || (o.Group.Equals("collision", StringComparison.OrdinalIgnoreCase) && o.Class.Length == 0));

        private IEnumerable<MapObject> Walkable() => _map.Objects.Where(o =>
            o.Class.Equals("walkable", StringComparison.OrdinalIgnoreCase)
            || (o.Group.Equals("walkable", StringComparison.OrdinalIgnoreCase) && o.Class.Length == 0));

        private IEnumerable<MapObject> Usable() => _map.Objects.Where(o =>
            o.Class.Equals("interact", StringComparison.OrdinalIgnoreCase) || o.Class.Equals("exit", StringComparison.OrdinalIgnoreCase)
            || (o.Group.Equals("interact", StringComparison.OrdinalIgnoreCase) && o.Class.Length == 0));

        private (Actor npc, MapObject obj) Nearest()
        {
            var npc = _npcs.Where(n => Vector2.Distance(n.Pos, _player.Pos) <= Reach)
                           .OrderBy(n => Vector2.Distance(n.Pos, _player.Pos)).FirstOrDefault();
            if (npc != null) return (npc, null);
            var obj = Usable().Where(o => o.DistanceTo(_player.Pos) <= UseReach)
                              .OrderBy(o => o.DistanceTo(_player.Pos)).FirstOrDefault();
            return (null, obj);
        }

        private static Vector2 ClosestPoint(MapObject o, Vector2 from)
        {
            var b = o.Bounds;
            return new Vector2(MathHelper.Clamp(from.X, b.X, b.X + b.Width), MathHelper.Clamp(from.Y, b.Y, b.Y + b.Height));
        }

        private void Use(MapObject o)
        {
            _player.Moving = false;
            Seen.Add(o.Name);
            if (Scene.OnInteract?.Invoke(this, o) == true) return;
            // Default: the object's "text" property, lines split by '|' ("Speaker: line" for a speaker).
            string text = o.Prop("text");
            if (text != null) Say(ParseLines(text));
        }

        private void Talk(Actor npc)
        {
            _player.Moving = false;
            _player.Flip = npc.Pos.X < _player.Pos.X;
            npc.Flip = _player.Pos.X < npc.Pos.X;
            var obj = npc.Source ?? new MapObject { Name = npc.Name, Class = "npc", X = npc.Pos.X, Y = npc.Pos.Y, IsPoint = true };
            Seen.Add(obj.Name);
            if (Scene.OnInteract?.Invoke(this, obj) == true) return;
            string text = obj.Prop("text");
            if (text != null) Say(ParseLines(text));
        }

        public static List<VnLine> ParseLines(string text) => text.Split('|').Select(s =>
        {
            int colon = s.IndexOf(':');
            return colon > 0 && colon < 20 ? VnLine.Say(s.Substring(0, colon).Trim(), s.Substring(colon + 1).Trim()) : VnLine.Narrate(s.Trim());
        }).ToList();

        // ------------------------------------------------------------------ camera

        private void FollowCamera(float dt)
        {
            var target = _player.Pos - new Vector2(0, SpriteHeight(_player) * 0.3f);
            _cam = Vector2.Lerp(_cam, target, 1 - (float)Math.Pow(0.002, dt));
        }

        // Camera clamped to the map (centred when the map is smaller than the screen).
        private Vector2 CamClamped()
        {
            float halfW = Game1.Width / 2f / Scene.Scale, halfH = Game1.Height / 2f / Scene.Scale;
            var c = _cam;
            c.X = _map.PixelWidth <= halfW * 2 ? _map.PixelWidth / 2f : MathHelper.Clamp(c.X, halfW, _map.PixelWidth - halfW);
            c.Y = _map.PixelHeight <= halfH * 2 ? _map.PixelHeight / 2f : MathHelper.Clamp(c.Y, halfH, _map.PixelHeight - halfH);
            return new Vector2((float)Math.Round(c.X * Scene.Scale) / Scene.Scale, (float)Math.Round(c.Y * Scene.Scale) / Scene.Scale);
        }

        private Matrix MapMatrix()
        {
            var c = CamClamped();
            return Matrix.CreateTranslation(-c.X, -c.Y, 0) * Matrix.CreateScale(Scene.Scale, Scene.Scale, 1)
                   * Matrix.CreateTranslation(Game1.Width / 2f, Game1.Height / 2f, 0);
        }

        private Vector2 ScreenToMap(Vector2 screen) => Vector2.Transform(screen, Matrix.Invert(MapMatrix()));
        private Vector2 MapToScreen(Vector2 map) => Vector2.Transform(map, MapMatrix());

        private float SpriteHeight(Actor a) => (Assets.Art(a.Sprite)?.Texture.Height ?? 42) * Scene.ActorScale / Scene.Scale;

        // ------------------------------------------------------------------ draw

        // Fades, banners, clouds and fog keep moving while a story scene covers the map (only the top screen
        // gets Update), so they run from Draw.
        private void Animate(float dt)
        {
            _time += dt;
            _tweens.Update(dt);
            _hintFlash = Math.Max(0, _hintFlash - dt);
            _toastTime -= dt;
            Ambience.File.Poll(_time);
            var view = new Vector2(Game1.Width, Game1.Height);
            _under.Update(dt, view);
            _over.Update(dt, view);
            if (Locked || Screens.Top != this) FollowCamera(dt);
        }

        public override void Draw(SpriteBatch sb, GameTime gameTime)
        {
            Animate((float)gameTime.ElapsedGameTime.TotalSeconds);
            var screen = new Rectangle(0, 0, Game1.Width, Game1.Height);
            DrawBackdrop(sb, screen);

            var m = MapMatrix();
            var c = CamClamped();
            var viewMap = new Rectangle((int)(c.X - Game1.Width / 2f / Scene.Scale), (int)(c.Y - Game1.Height / 2f / Scene.Scale),
                                        (int)(Game1.Width / Scene.Scale) + 1, (int)(Game1.Height / Scene.Scale) + 1);
            sb.End();
            sb.Begin(samplerState: SamplerState.PointClamp, transformMatrix: m * Core.Draw.FrameTransform);
            if (Scene.DropShadow != Vector2.Zero) DrawDropShadow(sb);
            foreach (var l in _map.Layers.Where(l => !l.Above)) _map.DrawLayer(sb, l, viewMap);

            // Characters, back to front.
            foreach (var a in _npcs.Append(_player).OrderBy(a => a.Pos.Y)) DrawActor(sb, a);

            foreach (var l in _map.Layers.Where(l => l.Above)) _map.DrawLayer(sb, l, viewMap);
            if (_debug) DrawDebug(sb);
            sb.End();
            sb.Begin(samplerState: SamplerState.PointClamp, transformMatrix: Core.Draw.FrameTransform);

            _over.DrawBack(sb, screen, Core.Draw.FrameTransform);
            _over.DrawFront(sb, screen, Core.Draw.FrameTransform);
            if (Scene.Tint is { } tint) Core.Draw.Rect(sb, screen, tint);
            DrawVignette(sb);
            DrawHud(sb);
            if (_fadeIn < 1f) Core.Draw.Rect(sb, screen, Palette.Ink * (1 - _fadeIn));
        }

        private void DrawBackdrop(SpriteBatch sb, Rectangle screen)
        {
            if (Scene.Backdrop == "sky")
            {
                // Deep blue far below, lighter toward the top: looking down past the platform into the clouds.
                var top = new Color(64, 66, 104);
                var bottom = new Color(22, 24, 46);
                for (int i = 0; i < 36; i++)
                    Core.Draw.Rect(sb, 0, i * 20, Game1.Width, 21, Color.Lerp(top, bottom, i / 35f));
                // The clouds drift with a little parallax against the camera.
                var c = CamClamped();
                var shift = new Point((int)(-(c.X * Scene.Scale) * 0.15f) % Game1.Width, (int)(-(c.Y * Scene.Scale) * 0.08f));
                var area = new Rectangle(screen.X + shift.X - Game1.Width / 2, screen.Y + shift.Y - 60, Game1.Width * 2, Game1.Height + 120);
                _under.DrawBack(sb, area, Core.Draw.FrameTransform);
                _under.DrawFront(sb, area, Core.Draw.FrameTransform);
            }
            else
            {
                Core.Draw.Rect(sb, screen, _map.BackgroundColor ?? Color.Black);
                _under.DrawBack(sb, screen, Core.Draw.FrameTransform);
            }
        }

        // The map's footprint (every non-empty tile of its first layer), darkened and offset onto the backdrop with
        // soft edges, so the platform floats above the clouds instead of sitting on them.
        private Rectangle? _footprint;

        private void DrawDropShadow(SpriteBatch sb)
        {
            if (_footprint == null)
            {
                var l = _map.Layers.FirstOrDefault();
                int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
                if (l != null)
                    for (int i = 0; i < l.Gids.Length; i++)
                    {
                        if (l.Gids[i] == 0) continue;
                        int x = i % l.Width, y = i / l.Width;
                        x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
                    }
                _footprint = x1 < 0 ? Rectangle.Empty
                    : new Rectangle(x0 * _map.TileWidth, y0 * _map.TileHeight, (x1 - x0 + 1) * _map.TileWidth, (y1 - y0 + 1) * _map.TileHeight);
            }
            var r = _footprint.Value;
            if (r.IsEmpty) return;
            r.Offset((int)Scene.DropShadow.X, (int)Scene.DropShadow.Y);
            for (int i = 0; i < 8; i++)
            {
                int g = i * 6;
                Core.Draw.Rect(sb, new Rectangle(r.X - g, r.Y - g, r.Width + g * 2, r.Height + g * 2), Color.Black * 0.07f);
            }
        }

        private void DrawActor(SpriteBatch sb, Actor a)
        {
            var art = Assets.Art(a.Sprite);
            float s = Scene.ActorScale / Scene.Scale;
            // Soft shadow under the feet.
            float sw = (art?.Texture.Width ?? 30) * s * 0.42f;
            DrawShadow(sb, a.Pos, sw);
            if (art == null)
            {
                Core.Draw.Rect(sb, a.Pos.X - 8 * s, a.Pos.Y - 40 * s, 16 * s, 40 * s, Palette.Rose);
                return;
            }
            // Walk: a little hop and sway. Idle: a slow breath.
            float bob = a.Moving ? -Math.Abs(MathF.Sin(a.Walk * 11f)) * 2.2f * s : MathF.Sin(_time * 2f + a.Pos.X) * 0.4f * s;
            float tilt = a.Moving ? MathF.Sin(a.Walk * 11f) * 0.05f : 0f;
            float squash = a.Moving ? 1f : 1f + MathF.Sin(_time * 2f + a.Pos.X) * 0.012f;
            var tex = art.Texture;
            sb.Draw(tex, new Vector2(a.Pos.X, a.Pos.Y + bob), art.Source(_time), Color.White, tilt,
                new Vector2(art.FrameWidth / 2f, tex.Height), new Vector2(s, s * squash), a.Flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0);
        }

        private static Texture2D _shadow;

        private static void DrawShadow(SpriteBatch sb, Vector2 feet, float halfWidth)
        {
            if (_shadow == null)
            {
                const int w = 32, h = 16;
                var data = new Color[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float dx = (x + 0.5f) / w * 2 - 1, dy = (y + 0.5f) / h * 2 - 1;
                        float a = MathHelper.Clamp(1 - (dx * dx + dy * dy), 0, 1);
                        data[y * w + x] = Color.Black * (a * 0.45f);
                    }
                _shadow = new Texture2D(sb.GraphicsDevice, w, h);
                _shadow.SetData(data);
            }
            sb.Draw(_shadow, new Rectangle((int)(feet.X - halfWidth), (int)(feet.Y - halfWidth * 0.22f), (int)(halfWidth * 2), (int)(halfWidth * 0.44f)), Color.White);
        }

        private void DrawDebug(SpriteBatch sb)
        {
            foreach (var o in _map.Objects)
            {
                var col = o.Class.ToLowerInvariant() switch
                {
                    "collision" => Color.Red,
                    "walkable" => Color.LimeGreen,
                    "exit" => Color.Orange,
                    "npc" or "spawn" => Color.Yellow,
                    _ => Color.DeepSkyBlue,
                };
                if (o.IsPoint) { Core.Draw.Circle(sb, new Vector2(o.X, o.Y), 4, col, 1); continue; }
                if (o.Polygon != null)
                {
                    for (int i = 0; i < o.Polygon.Count; i++) Core.Draw.Line(sb, o.Polygon[i], o.Polygon[(i + 1) % o.Polygon.Count], col, 1);
                    continue;
                }
                if (o.IsEllipse) { Core.Draw.Ellipse(sb, o.Center, new Vector2(o.Width / 2, o.Height / 2), col, 1); continue; }
                var b = o.Bounds;
                Core.Draw.Border(sb, new Rectangle((int)b.X, (int)b.Y, (int)b.Width, (int)b.Height), col * 0.9f, 1);
            }
            Core.Draw.Circle(sb, _player.Pos, FeetRadius, Color.White, 1);
        }

        private static Texture2D _vignette;

        private static void DrawVignette(SpriteBatch sb)
        {
            if (_vignette == null)
            {
                const int w = 64, h = 36;
                var data = new Color[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float dx = (x + 0.5f) / w * 2 - 1, dy = (y + 0.5f) / h * 2 - 1;
                        float d = MathHelper.Clamp((MathF.Sqrt(dx * dx + dy * dy) - 0.55f) / 0.8f, 0, 1);
                        data[y * w + x] = Color.Black * (d * d * 0.75f);
                    }
                _vignette = new Texture2D(sb.GraphicsDevice, w, h);
                _vignette.SetData(data);
            }
            sb.End();
            sb.Begin(samplerState: SamplerState.LinearClamp, transformMatrix: Core.Draw.FrameTransform);
            sb.Draw(_vignette, new Rectangle(0, 0, Game1.Width, Game1.Height), Color.White);
            sb.End();
            sb.Begin(samplerState: SamplerState.PointClamp, transformMatrix: Core.Draw.FrameTransform);
        }

        private void DrawHud(SpriteBatch sb)
        {
            // Arrival banner.
            if (Scene.Title != null && _bannerIn > 0.01f)
            {
                float a = _bannerIn;
                Core.Draw.SpacedCentered(sb, Assets.Title, Scene.Title.ToUpperInvariant(), new Vector2(Game1.Width / 2f, 120), Palette.Paper * a, 5);
                Core.Draw.Divider(sb, new Vector2(Game1.Width / 2f, 156), 420 * a, Palette.Paper * 0.5f * a);
                Core.Draw.Ornament(sb, new Vector2(Game1.Width / 2f, 156), 12, Palette.Gold * a);
                if (Scene.Subtitle != null)
                    Core.Draw.TextCentered(sb, Assets.Body, Scene.Subtitle, new Vector2(Game1.Width / 2f, 180), Palette.TextDim * a);
            }

            // Objective hint, top left.
            if (_hint != null)
            {
                var size = Assets.Body.MeasureString(_hint);
                var r = new Rectangle(24, 22, (int)size.X + 64, 40);
                Core.Draw.Slant(sb, r, 12, Palette.Ink * 0.7f);
                Core.Draw.Slant(sb, new Rectangle(r.X, r.Y, 6, r.Height), 4, _hintFlash > 0 && (int)(_hintFlash * 6) % 2 == 0 ? Palette.Gold : Palette.Rose);
                Icons.Draw(sb, "ui_objective", new Rectangle(r.X + 16, r.Y + 10, 20, 20));
                Core.Draw.Text(sb, Assets.Body, _hint, new Vector2(r.X + 44, r.Y + 9), Palette.Paper, shadow: false);
            }

            // What E would do right now, floating over the target.
            if (!Locked && Screens.Top == this)
            {
                var (npc, obj) = Nearest();
                string label = npc != null ? npc.Source?.Prop("label") ?? $"Talk to {npc.Name}" : obj?.Prop("label") ?? obj?.Name;
                if (label != null)
                {
                    var anchor = npc != null ? npc.Pos - new Vector2(0, SpriteHeight(npc) + 4) : new Vector2(obj.Center.X, obj.Bounds.Y);
                    var p = MapToScreen(anchor);
                    p.Y = Math.Max(p.Y, 70) - 18 + MathF.Sin(_time * 4f) * 2f;
                    string text = label;
                    var size = Assets.Body.MeasureString(text);
                    float capW = 30;
                    var r = new Rectangle((int)(p.X - (size.X + capW + 26) / 2), (int)p.Y - 16, (int)(size.X + capW + 26), 32);
                    Core.Draw.Rect(sb, r, Palette.Ink * 0.75f);
                    Core.Draw.Border(sb, r, Palette.LineBright, 1);
                    Ui.KeyCap(sb, "E", new Vector2(r.X + 20, r.Center.Y));
                    Core.Draw.Text(sb, Assets.Body, text, new Vector2(r.X + capW + 12, r.Y + 6), Palette.Paper, shadow: false);
                }
            }

            if (_toastTime > 0 && _toast != null)
                Core.Draw.TextCentered(sb, Assets.Small, _toast, new Vector2(Game1.Width / 2f, Game1.Height - 28), Palette.TextDim * Math.Min(1, _toastTime));

            Ui.HelpButton(sb, new Vector2(Game1.Width - 30, Game1.Height - 26),
                "WASD / arrows or click: walk\nE / Enter / click: use, talk\nF9: show collision and interaction shapes");
        }
    }
}
