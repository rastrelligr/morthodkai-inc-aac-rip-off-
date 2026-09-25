using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SERAAC.Combat;
using SERAAC.Core;
using SERAAC.Entities;
using SERAAC.Input;
using SERAAC.Rendering3D;
using SERAAC.World;

namespace SERAAC.Screens
{
    // Player 1 (Vessel, keyboard) walks the dungeon grid in real 3D; Player 2 (Weapon,
    // mouse) free-looks independently of the Vessel's facing while exploring. Stepping
    // near a patrolling enemy starts a CombatScreen encounter.
    public class ExplorationScreen : Screen
    {
        private const int MapCols = 14;
        private const int MapRows = 10;
        private const int DetectRange = 4;
        private const double PatrolInterval = 1.1;
        private static readonly (int dx, int dy)[] FacingDelta = { (0, 1), (1, 0), (0, -1), (-1, 0) };

        private readonly VesselInput _vesselInput = new VesselInput();
        private readonly WeaponInput _weaponInput = new WeaponInput();
        private readonly FirstPersonCamera _camera = new FirstPersonCamera();
        private readonly DungeonRenderer3D _renderer;
        private readonly List<Billboard> _billboards = new List<Billboard>();

        private DungeonMesh _mesh;

        // Render-side smoothing; the logical grid state lives on GameContext.
        private float _renderX, _renderZ, _renderYaw;
        private bool _moving;
        private double _moveTimer;
        private const double MoveDuration = 0.22;
        private float _fromX, _fromZ, _toX, _toZ;
        private bool _turning;
        private double _turnTimer;
        private const double TurnDuration = 0.16;
        private float _fromYaw, _toYaw;

        private float _aimYaw, _aimPitch;
        private const float MouseSensitivity = 0.0035f;
        private const float MaxAimYaw = MathHelper.PiOver4;
        private const float MaxAimPitch = 0.5f;

        private double _patrolTimer;
        private string _message = "Explore the dungeon. P1 moves, P2 looks around.";

        public ExplorationScreen(Game1 game, GameContext context, SharedAssets assets) : base(game, context, assets)
        {
            _renderer = new DungeonRenderer3D(game.GraphicsDevice);
        }

        public override void OnEnter()
        {
            Game.IsMouseVisible = false;

            if (Context.Dungeon == null)
                GenerateFloor();

            _mesh = DungeonMeshBuilder.Build(Game.GraphicsDevice, Context.Dungeon);
            SnapRenderStateToLogical();
            _message = $"Floor {Context.FloorNumber}: explore the dungeon.";
        }

        public override void OnExit()
        {
            Game.IsMouseVisible = true;
        }

        private void GenerateFloor()
        {
            Context.Dungeon = new DungeonMap(MapCols, MapRows, Context.Rng);
            var (sx, sy) = Context.Dungeon.FindStartTile();
            Context.VesselTileX = sx;
            Context.VesselTileY = sy;
            Context.VesselFacing = 0;

            Context.DungeonEnemies.Clear();
            int enemyCount = Context.Rng.Next(3, 6);
            string[] names = { "Goblin", "Slime", "Wraith", "Hollow Knight" };

            for (int i = 0; i < enemyCount; i++)
            {
                var spawn = FindFreeFloorTile(4);
                if (spawn.x < 0) continue;

                Context.DungeonEnemies.Add(new DungeonEnemy
                {
                    Data = new Enemy
                    {
                        Name = names[Context.Rng.Next(names.Length)],
                        Hp = 18 + Context.FloorNumber * 4,
                        MaxHp = 18 + Context.FloorNumber * 4,
                        AttackDamage = 3 + Context.FloorNumber,
                        WeakCell = Context.Rng.Next(CombatGridCellCount())
                    },
                    X = spawn.x,
                    Y = spawn.y
                });
            }
        }

        private static int CombatGridCellCount() => CombatGrid.CellCount;

        private (int x, int y) FindFreeFloorTile(int minDistanceFromVessel)
        {
            for (int i = 0; i < 200; i++)
            {
                int x = Context.Rng.Next(Context.Dungeon.Width);
                int y = Context.Rng.Next(Context.Dungeon.Height);
                if (!Context.Dungeon.IsFloor(x, y)) continue;
                if (Math.Abs(x - Context.VesselTileX) + Math.Abs(y - Context.VesselTileY) < minDistanceFromVessel) continue;
                if (IsEnemyAt(x, y)) continue;
                return (x, y);
            }
            return (-1, -1);
        }

        private bool IsEnemyAt(int x, int y)
        {
            foreach (var e in Context.DungeonEnemies)
                if (e.X == x && e.Y == y) return true;
            return false;
        }

        private void SnapRenderStateToLogical()
        {
            _renderX = Context.VesselTileX;
            _renderZ = Context.VesselTileY;
            _renderYaw = Context.VesselFacing * MathHelper.PiOver2;
            _moving = false;
            _turning = false;
        }

        public override void Update(GameTime gameTime)
        {
            _vesselInput.Update();
            _weaponInput.Update();

            var center = new Point(Game.GraphicsDevice.Viewport.Width / 2, Game.GraphicsDevice.Viewport.Height / 2);
            UpdateWeaponAim();
            _weaponInput.Recenter(center);

            if (!_moving && !_turning)
                HandleVesselInput();

            UpdateMovementAnimation(gameTime);
            UpdatePatrol(gameTime);
            UpdateCamera();
            CheckForEncounter();
        }

        private void UpdateWeaponAim()
        {
            _aimYaw = MathHelper.Clamp(_aimYaw + _weaponInput.Delta.X * MouseSensitivity, -MaxAimYaw, MaxAimYaw);
            _aimPitch = MathHelper.Clamp(_aimPitch - _weaponInput.Delta.Y * MouseSensitivity, -MaxAimPitch, MaxAimPitch);
        }

        private void HandleVesselInput()
        {
            if (_vesselInput.Up) TryMoveForward(1);
            else if (_vesselInput.Down) TryMoveForward(-1);
            else if (_vesselInput.Left) StartTurn(-1);
            else if (_vesselInput.Right) StartTurn(1);
        }

        private void TryMoveForward(int direction)
        {
            var (dx, dy) = FacingDelta[Context.VesselFacing];
            int nx = Context.VesselTileX + dx * direction;
            int ny = Context.VesselTileY + dy * direction;

            if (!Context.Dungeon.IsFloor(nx, ny) || IsEnemyAt(nx, ny))
            {
                _message = "A wall blocks the way.";
                return;
            }

            _fromX = _renderX; _fromZ = _renderZ;
            _toX = nx; _toZ = ny;
            Context.VesselTileX = nx;
            Context.VesselTileY = ny;
            _moving = true;
            _moveTimer = 0;
        }

        private void StartTurn(int direction)
        {
            Context.VesselFacing = (Context.VesselFacing + direction + 4) % 4;
            _fromYaw = _renderYaw;
            _toYaw = _fromYaw + direction * MathHelper.PiOver2;
            _turning = true;
            _turnTimer = 0;
        }

        private void UpdateMovementAnimation(GameTime gameTime)
        {
            if (_moving)
            {
                _moveTimer += gameTime.ElapsedGameTime.TotalSeconds;
                float t = (float)Math.Min(1.0, _moveTimer / MoveDuration);
                _renderX = MathHelper.Lerp(_fromX, _toX, t);
                _renderZ = MathHelper.Lerp(_fromZ, _toZ, t);
                if (t >= 1f) _moving = false;
            }

            if (_turning)
            {
                _turnTimer += gameTime.ElapsedGameTime.TotalSeconds;
                float t = (float)Math.Min(1.0, _turnTimer / TurnDuration);
                _renderYaw = MathHelper.Lerp(_fromYaw, _toYaw, t);
                if (t >= 1f) { _turning = false; _renderYaw = _toYaw; }
            }
        }

        private void UpdatePatrol(GameTime gameTime)
        {
            _patrolTimer += gameTime.ElapsedGameTime.TotalSeconds;
            if (_patrolTimer < PatrolInterval) return;
            _patrolTimer = 0;

            var options = new List<(int x, int y)>();
            foreach (var enemy in Context.DungeonEnemies)
            {
                options.Clear();
                AddPatrolOption(enemy.X + 1, enemy.Y, options);
                AddPatrolOption(enemy.X - 1, enemy.Y, options);
                AddPatrolOption(enemy.X, enemy.Y + 1, options);
                AddPatrolOption(enemy.X, enemy.Y - 1, options);

                if (options.Count > 0)
                {
                    var (x, y) = options[Context.Rng.Next(options.Count)];
                    enemy.X = x;
                    enemy.Y = y;
                }
            }
        }

        private void AddPatrolOption(int x, int y, List<(int x, int y)> options)
        {
            if (!Context.Dungeon.IsFloor(x, y)) return;
            if (x == Context.VesselTileX && y == Context.VesselTileY) return;
            if (IsEnemyAt(x, y)) return;
            options.Add((x, y));
        }

        private void UpdateCamera()
        {
            _camera.Position = new Vector3(
                (_renderX + 0.5f) * DungeonMeshBuilder.TileSize,
                DungeonMeshBuilder.WallHeight * 0.42f,
                (_renderZ + 0.5f) * DungeonMeshBuilder.TileSize);
            _camera.Yaw = _renderYaw + _aimYaw;
            _camera.Pitch = _aimPitch;
        }

        private void CheckForEncounter()
        {
            var nearby = new List<DungeonEnemy>();
            foreach (var enemy in Context.DungeonEnemies)
            {
                int dist = Math.Abs(enemy.X - Context.VesselTileX) + Math.Abs(enemy.Y - Context.VesselTileY);
                if (dist <= DetectRange) nearby.Add(enemy);
            }

            if (nearby.Count == 0) return;

            if (nearby.Count > 3) nearby.RemoveRange(3, nearby.Count - 3);

            Context.PendingEncounter = new List<Enemy>();
            foreach (var enemy in nearby)
            {
                Context.PendingEncounter.Add(enemy.Data);
                Context.DungeonEnemies.Remove(enemy);
            }

            Game.ChangeScreen(new CombatScreen(Game, Context, Assets));
        }

        public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            var device = Game.GraphicsDevice;
            float aspect = device.Viewport.AspectRatio;

            _billboards.Clear();
            foreach (var enemy in Context.DungeonEnemies)
            {
                _billboards.Add(new Billboard
                {
                    Center = new Vector3((enemy.X + 0.5f) * DungeonMeshBuilder.TileSize, 0,
                        (enemy.Y + 0.5f) * DungeonMeshBuilder.TileSize),
                    Width = 1.1f,
                    Height = 1.6f,
                    Tint = Color.IndianRed
                });
            }

            _renderer.DrawDungeon(device, _mesh, _camera, aspect);
            _renderer.DrawEnemies(device, _camera, aspect, _billboards);

            DrawHud(spriteBatch, device);
        }

        private void DrawHud(SpriteBatch spriteBatch, GraphicsDevice device)
        {
            var font = Assets.BodyFont;
            var vessel = Context.Party.Primary.Vessel;

            spriteBatch.Begin();

            // Aim reticle: shows where the Weapon (mouse) is currently pointed.
            int cx = device.Viewport.Width / 2;
            int cy = device.Viewport.Height / 2;
            spriteBatch.Draw(Assets.Pixel, new Rectangle(cx - 8, cy - 1, 16, 2), Color.White);
            spriteBatch.Draw(Assets.Pixel, new Rectangle(cx - 1, cy - 8, 2, 16), Color.White);

            DrawMinimap(spriteBatch, device);

            if (font != null)
            {
                spriteBatch.DrawString(font, $"Floor {Context.FloorNumber}/{GameContext.TotalFloors}   Vessel HP {vessel.Hp}/{vessel.MaxHp}",
                    new Vector2(20, 20), Color.White);
                spriteBatch.DrawString(font, _message, new Vector2(20, 46), Color.LightGoldenrodYellow);
                spriteBatch.DrawString(font, "P1: W/S move, A/D turn      P2: mouse to look",
                    new Vector2(20, device.Viewport.Height - 34), Color.LightGray);
            }

            spriteBatch.End();
        }

        private void DrawMinimap(SpriteBatch spriteBatch, GraphicsDevice device)
        {
            int tile = 6;
            int originX = device.Viewport.Width - Context.Dungeon.Width * tile - 20;
            int originY = 20;

            for (int x = 0; x < Context.Dungeon.Width; x++)
            {
                for (int y = 0; y < Context.Dungeon.Height; y++)
                {
                    var rect = new Rectangle(originX + x * tile, originY + y * tile, tile - 1, tile - 1);
                    var color = Context.Dungeon.IsFloor(x, y) ? new Color(80, 80, 90) : new Color(20, 20, 24);
                    spriteBatch.Draw(Assets.Pixel, rect, color);
                }
            }

            foreach (var enemy in Context.DungeonEnemies)
            {
                var rect = new Rectangle(originX + enemy.X * tile, originY + enemy.Y * tile, tile - 1, tile - 1);
                spriteBatch.Draw(Assets.Pixel, rect, Color.IndianRed);
            }

            var vesselRect = new Rectangle(originX + Context.VesselTileX * tile, originY + Context.VesselTileY * tile, tile - 1, tile - 1);
            spriteBatch.Draw(Assets.Pixel, vesselRect, Color.CornflowerBlue);
        }
    }
}
