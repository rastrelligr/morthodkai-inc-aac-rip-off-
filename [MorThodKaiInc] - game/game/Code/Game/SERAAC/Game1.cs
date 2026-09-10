using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using SERAAC.Data.Party;
using SERAAC.Data.Characters;
using SERAAC.UI;
using SERAAC.Minigame;
using SERAAC.Data.Enemies;

namespace SERAAC
{
    public class Game1 : Game
    {
        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;
        private SpriteFont _fontHeader;
        private SpriteFont _fontBody;
        private Party _party;
        private KeyboardState _prevKeyboard;
        private Vector2 _textPos = new Vector2(16, 16);
        // UI / gameplay prototype fields
        private Texture2D _whiteTex;
        private Texture2D _playerTex;
        private Texture2D _grid;
        private Texture2D _weaponTex;
        private int _tileSize = 100; // size for each sprite frame
        private int _mapCols = 16;
        private int _mapRows = 9;
        private int _selX = 1;
        private int _selY = 1;
        private int _playerFacing = 0; // 0=down,1=left,2=right,3=up
        // Smooth movement / interpolation
        private float _playerPosX;
        private float _playerPosY;
        private bool _isMoving = false;
        private int _moveFromX, _moveFromY, _moveToX, _moveToY;
        private double _moveTimer = 0.0;
        private double _moveDuration = 0.18; // seconds per tile
        private string[] _actions = new[] { "Attack", "Item", "Guard" };
        private int _actionIndex = 0;
        private string[] enemyNames = new[] { "Goblin", "Fih", "Slime" };
        private int enemyCount = 0;

        // Minigame and UI managers
        private SyncMinigame _minigame;
        private BattleUI _battleUI;
        private double _minigameDuration = 5.0;
        private float _syncOnSuccess = 25f;
        private List<Enemy> _enemies = new List<Enemy>();
        private int _selectedEnemyIndex = 0;
        private Dungeon.DungeonMap _dungeon;

        // Runtime-only enemy positions. These do not modify the Enemy data model.
        private class RuntimeEnemy
        {
            public Enemy Data;
            public int X;
            public int Y;
        }

        private readonly List<RuntimeEnemy> _runtimeEnemies = new List<RuntimeEnemy>();
        private readonly Random _rng = new Random();
        private double _enemyPatrolTimer;
        private const double EnemyPatrolInterval = 1.0;
        private string _combatMessage = "Explore the dungeon.";

        // Combat / AI
        private enum GameState { Playing, Won, Lost }
        private GameState _gameState = GameState.Playing;
        private const int PlayerAttackRange = 3;
        private const int EnemyDetectRange = 5;
        private const double EnemyThinkInterval = 0.65;
        private const int EnemyAttackDamage = 4;
        private const double EnemyHitChance = 0.65;
        private double _enemyThinkTimer = 0.0;
        private readonly List<int> _targetableEnemyIndices = new List<int>();

        private enum TurnOwner
        {
            Player,
            Enemy
        }

        private TurnOwner _turnOwner = TurnOwner.Player;
        private bool _combatStarted = false;
        private bool _playerTurnActionTaken = false;


        public Game1()
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;

            // Configure borderless fullscreen with a 16:9 backbuffer that best fits the display.
            try
            {
                var dm = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
                int dw = dm.Width;
                int dh = dm.Height;
                const double targetRatio = 16.0 / 9.0;
                double displayRatio = (double)dw / dh;

                int targetW, targetH;
                if (displayRatio > targetRatio)
                {
                    // display is wider than 16:9 -> fit height
                    targetH = dh;
                    targetW = (int)(dh * targetRatio);
                }
                else
                {
                    // display is taller (or equal) -> fit width
                    targetW = dw;
                    targetH = (int)(dw / targetRatio);
                }

                _graphics.PreferredBackBufferWidth = targetW;
                _graphics.PreferredBackBufferHeight = targetH;
                // Use borderless windowed fullscreen (no mode switch)
                _graphics.HardwareModeSwitch = false;
                _graphics.IsFullScreen = true;
                // Request a borderless window
                Window.IsBorderless = true;
                _graphics.ApplyChanges();
            }
            catch
            {
                // If display query fails, fall back to defaults silently
            }
        }

        protected override void Initialize()
        {
            // Create a minimal party and soulbond for the prototype
            Random rnd = new Random();
            _party = new Party();
            for (int i = 0; i < 1; i++)
            {
                var wielder = new Wielder { CharId = $"w-{i}", Name = $"Hero{i + 1}", Hp = 30, MaxHp = 30 };
                var weapon = new Weapon { WeaponId = $"wp-{i}", Name = "Rusty Blade", WeaponType = "Sword" };
                var bond = new SoulBond { BondId = $"b-{i}", Wielder = wielder, Weapon = weapon, SyncMeter = 0f };
                _party.Bonds.Add(bond);
            }
            enemyCount = rnd.Next(3, 5); // random number of enemies for this run


            for (int i = 0; i < enemyCount; i++)
            {
                string name = enemyNames[rnd.Next(enemyNames.Length)];
                var enemy = new Enemy
                {
                    EnemyId = $"e-{i + 1}",
                    Name = name,
                    Hp = 20,
                    MaxHp = 20
                };
                _enemies.Add(enemy);
            }

            // Create dungeon map and generate rooms + corridors
            _dungeon = new Dungeon.DungeonMap(_mapCols, _mapRows);
            _dungeon.GenerateRooms(6, 3, 6);

            // Place player at center floor if possible (use first room center if available)
            int px = _mapCols / 2;
            int py = _mapRows / 2;
            if (_dungeon.Rooms.Count > 0)
            {
                var r0 = _dungeon.Rooms[0];
                px = r0.CenterX; py = r0.CenterY;
            }
            if (!_dungeon.IsFloor(px, py))
            {
                // find nearest floor
                for (int r = 1; r < Math.Max(_mapCols, _mapRows); r++)
                {
                    bool found = false;
                    for (int dx = -r; dx <= r && !found; dx++)
                        for (int dy = -r; dy <= r && !found; dy++)
                        {
                            int nx = px + dx, ny = py + dy;
                            if (nx >= 0 && ny >= 0 && nx < _mapCols && ny < _mapRows && _dungeon.IsFloor(nx, ny))
                            {
                                px = nx; py = ny; found = true;
                            }
                        }
                    if (found) break;
                }
            }
            _selX = px; _selY = py;
            _playerPosX = _selX;
            _playerPosY = _selY;

            SpawnRuntimeEnemies();

            _prevKeyboard = Keyboard.GetState();
            _minigame = new SyncMinigame();

            base.Initialize();
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);
            // Try to load available fonts from Content. Prefer Strive (header) and BB (body).
            try { _fontHeader = Content.Load<SpriteFont>("Strive"); } catch { _fontHeader = null; }
            try { _fontBody = Content.Load<SpriteFont>("BB"); } catch { _fontBody = null; }
            // Fallbacks
            if (_fontHeader == null)
            {
                try { _fontHeader = Content.Load<SpriteFont>("DefaultFont"); } catch { }
            }
            if (_fontBody == null)
            {
                try { _fontBody = Content.Load<SpriteFont>("DefaultFont"); } catch { }
            }
            // create a 1x1 white texture for drawing shapes
            _whiteTex = new Texture2D(GraphicsDevice, 1, 1);
            _whiteTex.SetData(new[] { Color.White });
            // load placeholder textures if available
            try { _playerTex = Content.Load<Texture2D>("placeholder_player"); } catch { _playerTex = null; }
            try { _weaponTex = Content.Load<Texture2D>("placeholder_weapon"); } catch { _weaponTex = null; }
            try { _grid = Content.Load<Texture2D>("placeholder_sprsheet_grid1"); } catch { _grid = null; }
            // instantiate BattleUI with loaded fonts and textures
            _battleUI = new BattleUI(_fontHeader, _fontBody, _whiteTex, _playerTex, _weaponTex, 100);
        }

        protected override void Update(GameTime gameTime)
        {
            var ks = Keyboard.GetState();

            if (ks.IsKeyDown(Keys.Escape)) { Exit(); return; }

            if (_gameState != GameState.Playing)
            {
                if (ks.IsKeyDown(Keys.R) && !_prevKeyboard.IsKeyDown(Keys.R)) RestartBattle();
                _prevKeyboard = ks;
                base.Update(gameTime);
                return;
            }

            // The minigame resolves the player's attack, then passes the turn to the enemies.
            if (_minigame != null && _minigame.Active)
            {
                _minigame.Update(gameTime, ks, _prevKeyboard);

                if (_minigame.IsComplete())
                {
                    bool success = _minigame.Evaluate();
                    var first = _party?.Bonds.Count > 0 ? _party.Bonds[0] : null;
                    if (first != null) first.SyncMeter = Math.Max(0f, Math.Min(100f, first.SyncMeter + (success ? _syncOnSuccess : -5f)));

                    if (success)
                    {
                        var target = GetSelectedTarget();
                        if (target != null)
                        {
                            int dmg = 10;
                            string name = target.Data.Name;
                            target.Data.Hp -= dmg;
                            _combatMessage = $"Both players finished! Hit {name} for {dmg} damage!";
                            if (target.Data.Hp <= 0)
                            {
                                // Keep defeated enemies in _enemies so BattleUI always has a valid list/index.
                                // Their HP=0 makes them non-targetable and non-active.
                                string id = target.Data.EnemyId;
                                _runtimeEnemies.RemoveAll(e => e.Data != null && e.Data.EnemyId == id);
                                _selectedEnemyIndex = FindFirstLivingEnemyIndex();
                                _combatMessage = $"{name} defeated!";
                            }
                        }
                    }
                    else _combatMessage = "Typing failed. The attack misses.";

                    RefreshTargetableEnemies();
                    EnsureSelectedTargetInRange();
                    CheckWinLose();
                    if (_gameState == GameState.Playing) BeginEnemyTurn();
                }

                _prevKeyboard = ks;
                base.Update(gameTime);
                return;
            }

            if (!_combatStarted)
            {
                HandlePlayerMovement(ks);

                _enemyPatrolTimer += gameTime.ElapsedGameTime.TotalSeconds;

                if (_enemyPatrolTimer >= EnemyThinkInterval)
                {
                    _enemyPatrolTimer = 0;
                    PatrolEnemies();
                }

                if (AnyEnemyDetectsPlayer())
                {
                    StartTurnBasedCombat();
                }
            }
            else if (_turnOwner == TurnOwner.Player)
            {
                RefreshTargetableEnemies();

                // Target/action selection is free and does not consume the turn.
                if (ks.IsKeyDown(Keys.Down) && !_prevKeyboard.IsKeyDown(Keys.Down)) SelectNextTarget(1);
                if (ks.IsKeyDown(Keys.Up) && !_prevKeyboard.IsKeyDown(Keys.Up)) SelectNextTarget(-1);
                EnsureSelectedTargetInRange();

                if (ks.IsKeyDown(Keys.Right) && !_prevKeyboard.IsKeyDown(Keys.Right)) _actionIndex = (_actionIndex + 1) % _actions.Length;
                if (ks.IsKeyDown(Keys.Left) && !_prevKeyboard.IsKeyDown(Keys.Left)) _actionIndex = (_actionIndex - 1 + _actions.Length) % _actions.Length;

                if (!_playerTurnActionTaken) HandlePlayerMovement(ks);

                if (ks.IsKeyDown(Keys.RightShift) && !_prevKeyboard.IsKeyDown(Keys.RightShift) && !_isMoving && !_playerTurnActionTaken)
                {
                    string action = _actions[_actionIndex];
                    if (action == "Attack")
                    {
                        if (TryStartAttack()) _playerTurnActionTaken = true;
                    }
                    else
                    {
                        _combatMessage = $"{action} used. Enemy turn.";
                        _playerTurnActionTaken = true;
                        BeginEnemyTurn();
                    }
                }

                if (_playerTurnActionTaken && !_isMoving && (_minigame == null || !_minigame.Active)) BeginEnemyTurn();
            }
            else
            {
                ExecuteEnemyTurn();
                if (_gameState == GameState.Playing) BeginPlayerTurn();
            }

            if (_isMoving)
            {
                _moveTimer += gameTime.ElapsedGameTime.TotalSeconds;
                double t = Math.Min(1.0, _moveTimer / _moveDuration);
                _playerPosX = (float)(_moveFromX + (_moveToX - _moveFromX) * t);
                _playerPosY = (float)(_moveFromY + (_moveToY - _moveFromY) * t);
                if (t >= 1.0)
                {
                    _isMoving = false;
                    _selX = _moveToX; _selY = _moveToY;
                    _playerPosX = _selX; _playerPosY = _selY;
                    if (_combatStarted && _turnOwner == TurnOwner.Player && _playerTurnActionTaken) BeginEnemyTurn();
                }
            }

            CheckWinLose();
            _prevKeyboard = ks;
            base.Update(gameTime);
        }

        private void HandlePlayerMovement(KeyboardState ks)
        {
            if (_isMoving) return;
            int nx = _selX, ny = _selY;
            bool pressed = false;
            if (ks.IsKeyDown(Keys.W) && !_prevKeyboard.IsKeyDown(Keys.W)) { ny = Math.Max(0, _selY - 1); _playerFacing = 3; pressed = true; }
            else if (ks.IsKeyDown(Keys.S) && !_prevKeyboard.IsKeyDown(Keys.S)) { ny = Math.Min(_mapRows - 1, _selY + 1); _playerFacing = 0; pressed = true; }
            else if (ks.IsKeyDown(Keys.A) && !_prevKeyboard.IsKeyDown(Keys.A)) { nx = Math.Max(0, _selX - 1); _playerFacing = 1; pressed = true; }
            else if (ks.IsKeyDown(Keys.D) && !_prevKeyboard.IsKeyDown(Keys.D)) { nx = Math.Min(_mapCols - 1, _selX + 1); _playerFacing = 2; pressed = true; }
            if (!pressed || (nx == _selX && ny == _selY)) return;
            if (!_dungeon.IsFloor(nx, ny) || IsEnemyAt(nx, ny)) return;
            _isMoving = true; _moveFromX = _selX; _moveFromY = _selY; _moveToX = nx; _moveToY = ny; _moveTimer = 0.0;
            if (_combatStarted && _turnOwner == TurnOwner.Player) _playerTurnActionTaken = true;
        }

        private bool TryStartAttack()
        {
            RefreshTargetableEnemies();
            var target = GetSelectedTarget();
            if (target == null) { _combatMessage = $"No target in attack range ({PlayerAttackRange} tiles)."; return false; }
            _combatMessage = $"Attacking {target.Data.Name}...";
            _minigame.Start(_minigameDuration);
            return true;
        }

        private bool AnyEnemyDetectsPlayer()
        {
            foreach (var e in _runtimeEnemies)
                if (e.Data != null && e.Data.Hp > 0 && Math.Abs(e.X - _selX) + Math.Abs(e.Y - _selY) <= EnemyDetectRange) return true;
            return false;
        }

        private void StartTurnBasedCombat()
        {
            _combatStarted = true; _turnOwner = TurnOwner.Player; _playerTurnActionTaken = false;
            RefreshTargetableEnemies(); EnsureSelectedTargetInRange(); _combatMessage = "Enemy detected! Your turn.";
        }

        private void BeginPlayerTurn()
        {
            if (_gameState != GameState.Playing) return;
            _turnOwner = TurnOwner.Player; _playerTurnActionTaken = false;
            RefreshTargetableEnemies(); EnsureSelectedTargetInRange(); _combatMessage = "Your turn.";
        }

        private void BeginEnemyTurn()
        {
            if (_gameState != GameState.Playing) return;
            _turnOwner = TurnOwner.Enemy; _playerTurnActionTaken = false; _combatMessage = "Enemy turn...";
        }

        private void ExecuteEnemyTurn()
        {
            if (_gameState != GameState.Playing) return;
            foreach (var enemy in _runtimeEnemies)
            {
                if (enemy.Data == null || enemy.Data.Hp <= 0) continue;
                int distance = Math.Abs(enemy.X - _selX) + Math.Abs(enemy.Y - _selY);
                if (distance <= 1)
                {
                    if (_rng.NextDouble() <= EnemyHitChance)
                    {
                        DamagePlayer(enemy.Data.Name, EnemyAttackDamage);
                        if (_gameState != GameState.Playing) return;
                    }
                    else
                    {
                        _combatMessage = $"{enemy.Data.Name} attacked but missed!";
                    }
                }
                else if (distance <= EnemyDetectRange)
                {
                    Point next = FindChaseStep(enemy);
                    if (next.X >= 0) { enemy.X = next.X; enemy.Y = next.Y; }
                }
            }
            CheckWinLose();
            if (_gameState == GameState.Playing) _combatMessage = "Enemies acted.";
        }

        private Point FindChaseStep(RuntimeEnemy enemy)
        {
            int dx = _selX - enemy.X, dy = _selY - enemy.Y;
            var candidates = new List<Point>();
            if (Math.Abs(dx) >= Math.Abs(dy)) { if (dx != 0) candidates.Add(new Point(enemy.X + Math.Sign(dx), enemy.Y)); if (dy != 0) candidates.Add(new Point(enemy.X, enemy.Y + Math.Sign(dy))); }
            else { if (dy != 0) candidates.Add(new Point(enemy.X, enemy.Y + Math.Sign(dy))); if (dx != 0) candidates.Add(new Point(enemy.X + Math.Sign(dx), enemy.Y)); }
            foreach (var p in candidates) if (CanEnemyMoveTo(p.X, p.Y, enemy)) return p;
            return new Point(-1, -1);
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.Black);

            int w = GraphicsDevice.Viewport.Width;
            int h = GraphicsDevice.Viewport.Height;

            // =========================================================
            // LAYOUT
            // =========================================================

            int padding = 20;
            int headerH = 50;

            int leftW = w / 2;
            int rightX = leftW;

            int leftInnerW = leftW - padding * 2;
            int rightInnerW = w - leftW - padding * 2;

            // Background panels
            _spriteBatch.Begin();

            _spriteBatch.Draw(
                _whiteTex,
                new Rectangle(0, 0, leftW, h),
                Color.SlateGray*0.4f);

            _spriteBatch.Draw(
                _whiteTex,
                new Rectangle(rightX, 0, w - leftW, h),
                Color.DimGray * 0.6f);

            _spriteBatch.End();


            // =========================================================
            // LEFT PANEL
            // =========================================================

            int leftY = headerH;

            // ---------- MINI-MAP ----------

            int miniTile = Math.Min(
                leftInnerW / _mapCols,
                (h - leftY - padding * 2) / _mapRows);

            int miniW = miniTile * _mapCols;
            int miniH = miniTile * _mapRows;

            int miniX = (leftW - miniW) / 2;
            int miniY = leftY;

            // ---------- BATTLE UI ----------

            int battleY = miniY + miniH + 16;

            // Reserve space for battle UI
            int battleH = h - battleY - padding;

            _spriteBatch.Begin();

            // Draw mini-map
            for (int my = 0; my < _mapRows; my++)
            {
                for (int mx = 0; mx < _mapCols; mx++)
                {
                    var rect = new Rectangle(
                        miniX + mx * miniTile,
                        miniY + my * miniTile,
                        miniTile - 1,
                        miniTile - 1);

                    Rectangle col = _dungeon.IsFloor(mx, my)? new Rectangle(_tileSize, 0, _tileSize, _tileSize): new Rectangle(_tileSize*2, 0, _tileSize, _tileSize);

                    _spriteBatch.Draw(_grid, rect, col,Color.White);
                }
            }

            // Enemies
            foreach (var enemy in _runtimeEnemies)
            {
                if (enemy.Data == null || enemy.Data.Hp <= 0) continue;

                bool selected = _enemies.Count > 0 &&_selectedEnemyIndex >= 0 &&
                                _selectedEnemyIndex < _enemies.Count &&
                                _enemies[_selectedEnemyIndex].EnemyId == enemy.Data.EnemyId;

                var enemysrc = new Rectangle(0, 0, _tileSize, _tileSize);
                var er = new Rectangle(
                    miniX + enemy.X * miniTile + 2,
                    miniY + enemy.Y * miniTile + 2,
                    Math.Max(4, miniTile - 5),
                    Math.Max(4, miniTile - 5));

                _spriteBatch.Draw(_grid, er, enemysrc, selected ? Color.RoyalBlue : Color.LightBlue);
            }

            // Player
            int playerX = miniX + (int)Math.Round(_playerPosX * miniTile);
            int playerY = miniY + (int)Math.Round(_playerPosY * miniTile);
            var playerRect = new Rectangle(
                playerX + 2,
                playerY + 2,
                Math.Max(4, miniTile - 4),
                Math.Max(4, miniTile - 4));

            if (_playerTex != null && _playerTex.Width >= (_playerFacing + 1) * _tileSize && _playerTex.Height >= _tileSize)
            {
                var playerSrc = new Rectangle(_playerFacing * _tileSize, 0, _tileSize, _tileSize);
                _spriteBatch.Draw(_playerTex, playerRect, playerSrc, Color.White);
            }
            else
            {
                _spriteBatch.Draw(_whiteTex, playerRect, Color.CornflowerBlue);
            }

            // Battle UI below mini-map
            _battleUI?.DrawBattle(
                _spriteBatch,
                leftW,
                h,
                _party,
                _enemies,
                _selectedEnemyIndex,
                _selX,
                _selY,
                _playerFacing,
                battleY);

            _spriteBatch.End();


            // =========================================================
            // RIGHT PANEL
            // =========================================================

            var font = _fontBody ?? _fontHeader;

            if (font != null)
            {
                _spriteBatch.Begin();

                var titleFont = _fontHeader ?? font;

                _spriteBatch.DrawString(
                    titleFont,
                    "Battle",
                    new Vector2(rightX + padding, 20),
                    Color.Yellow);

                // Right-panel bounds.
                int panelX = rightX + padding;
                int panelY = headerH;
                int panelW = rightInnerW;

                // ---------- FIRST-PERSON DUNGEON VIEW ----------
                int povX = panelX;
                int povY = panelY + 20;
                int povW = panelW;
                int povH = Math.Min((int)(h * 0.56f), panelW);
                povH = Math.Max(160, povH);

                var povRect = new Rectangle(povX, povY, povW, povH);
                DrawFirstPersonDungeon(povRect, titleFont);

                // ---------- ACTIONS ----------

                int actionY = povRect.Bottom + 16;

                int actionW = Math.Max(50, (panelW - 16) / Math.Max(1, _actions.Length));
                int actionH = 40;

                for (int i = 0; i < _actions.Length; i++)
                {
                    int ax = panelX + i * (actionW + 8);

                    var arect = new Rectangle(
                        ax,
                        actionY,
                        actionW,
                        actionH);

                    Color acol = i == _actionIndex
                        ? Color.Orange
                        : Color.Gray;

                    _spriteBatch.Draw(
                        _whiteTex,
                        arect,
                        acol * 0.9f);

                    var text = _actions[i];

                    _spriteBatch.DrawString(
                        font,
                        text,
                        new Vector2(ax + 8, actionY + 8),
                        Color.Black);
                }

                // ---------- HUD ----------

                int hudY = actionY + actionH + 16;

                var hudRect = new Rectangle(
                    panelX,
                    hudY,
                    panelW,
                    92);

                _spriteBatch.Draw(
                    _whiteTex,
                    hudRect,
                    Color.Black * 0.6f);

                var player = GetPlayerWielder();
                var target = GetSelectedTarget();

                string playerHpText = player != null
                    ? $"Player HP: {player.Hp}/{player.MaxHp}"
                    : "Player HP: --";

                string targetText = target != null
                    ? $"Target: {target.Data.Name}  HP {target.Data.Hp}/{target.Data.MaxHp}"
                    : "Target: None (Up/Down selects enemies in range)";

                _spriteBatch.DrawString(
                    font,
                    playerHpText,
                    new Vector2(hudRect.X + 8, hudRect.Y + 6),
                    player != null && player.Hp <= player.MaxHp / 3
                        ? Color.Red
                        : Color.LightGreen);

                _spriteBatch.DrawString(
                    font,
                    targetText,
                    new Vector2(hudRect.X + 8, hudRect.Y + 26),
                    target != null ? Color.Orange : Color.LightGray);

                _spriteBatch.DrawString(
                    font,
                    _combatMessage,
                    new Vector2(hudRect.X + 8, hudRect.Y + 46),
                    Color.Yellow);

                _spriteBatch.DrawString(
                    font,
                    "Attack: P1 types letters + P2 types numbers",
                    new Vector2(hudRect.X + 8, hudRect.Y + 66),
                    Color.White);

                _spriteBatch.End();
            }


            // =========================================================
            // WIN / LOSE OVERLAY
            // =========================================================
            if (_gameState != GameState.Playing)
            {
                _spriteBatch.Begin();
                _spriteBatch.Draw(_whiteTex, new Rectangle(0, 0, w, h), Color.Black * 0.72f);

                var resultFont = _fontHeader ?? _fontBody;
                if (resultFont != null)
                {
                    string result = _gameState == GameState.Won ? "DUNGEON CLEARED" : "YOU DIED";
                    string sub = _gameState == GameState.Won
                        ? "All enemies have been defeated."
                        : "The enemies defeated you.";

                    Vector2 resultSize = resultFont.MeasureString(result);
                    Vector2 subSize = resultFont.MeasureString(sub);

                    _spriteBatch.DrawString(
                        resultFont,
                        result,
                        new Vector2(w / 2f - resultSize.X / 2f, h / 2f - 50),
                        _gameState == GameState.Won ? Color.LightGreen : Color.Red);

                    _spriteBatch.DrawString(
                        resultFont,
                        sub,
                        new Vector2(w / 2f - subSize.X / 2f, h / 2f),
                        Color.White);

                    string restart = "Press R to restart";
                    Vector2 restartSize = resultFont.MeasureString(restart);
                    _spriteBatch.DrawString(
                        resultFont,
                        restart,
                        new Vector2(w / 2f - restartSize.X / 2f, h / 2f + 45),
                        Color.Yellow);
                }

                _spriteBatch.End();
            }

            // =========================================================
            // MINIGAME OVERLAY
            // =========================================================

            if (_minigame != null && _minigame.Active)
            {
                _spriteBatch.Begin();

                _spriteBatch.Draw(_whiteTex,new Rectangle(0, 0, w, h),Color.Black * 0.6f);

                int boxW = Math.Min(280, w - 40);
                int boxH = Math.Min(240, h - 40);

                int boxX = (w / 2) - boxW / 2;//rightX + (w - leftW) / 2 - boxW / 2;
                int boxY = (h / 2) - boxH / 2;//h / 2 - boxH / 2;

                var box = new Rectangle(boxX, boxY, boxW, boxH);

                _spriteBatch.Draw(_whiteTex,box,Color.DarkRed * 0.9f);

                var fm = _fontHeader ?? _fontBody;

                if (fm != null)
                {
                    _spriteBatch.DrawString(
                        fm,
                        "Co-op Minigame",
                        new Vector2(box.X + 12, box.Y + 8),
                        Color.White);

                    _spriteBatch.DrawString(
                        fm,
                        $"Time: {_minigame.Timer:F1}s",
                        new Vector2(box.X + 12, box.Y + 40),
                        Color.Yellow);

                    _spriteBatch.DrawString(
                        fm,
                        _minigame.P1Pressed
                            ? "P1: Pressed"
                            : "P1: Press Space",
                        new Vector2(box.X + 12, box.Y + 80),
                        Color.White);

                    _spriteBatch.DrawString(
                        fm,
                        _minigame.P2Pressed
                            ? "P2: Pressed"
                            : "P2: Press RShift",
                        new Vector2(box.X + 12, box.Y + 112),
                        Color.White);
                }

                _spriteBatch.End();
            }

            base.Draw(gameTime);
        }

        private void DrawFirstPersonDungeon(Rectangle area, SpriteFont font)
        {
            if (_dungeon == null || area.Width <= 0 || area.Height <= 0) return;

            // Simple first-person dungeon-crawler placeholder.
            _spriteBatch.Draw(_whiteTex, area, Color.Black);

            int horizon = area.Y + area.Height * 45 / 100;

            // Ceiling
            _spriteBatch.Draw(
                _whiteTex,
                new Rectangle(area.X, area.Y, area.Width, Math.Max(1, horizon - area.Y)),
                Color.DarkSlateGray * 0.9f);

            // Floor
            _spriteBatch.Draw(
                _whiteTex,
                new Rectangle(area.X, horizon, area.Width, Math.Max(1, area.Bottom - horizon)),
                Color.SaddleBrown * 0.85f);

            // Current room as the far wall. This is intentionally just a color placeholder.
            var room = _dungeon.Rooms.Find(r =>
                _playerPosX >= r.X && _playerPosX < r.X + r.Width &&
                _playerPosY >= r.Y && _playerPosY < r.Y + r.Height);

            Color roomColor = room != null ? Color.DarkOliveGreen : Color.DarkSlateBlue;

            int wallW = area.Width * 62 / 100;
            int wallH = area.Height * 48 / 100;
            int wallX = area.Center.X - wallW / 2;
            int wallY = area.Y + area.Height * 12 / 100;

            _spriteBatch.Draw(
                _whiteTex,
                new Rectangle(wallX, wallY, wallW, wallH),
                roomColor);

            // Simple perspective lines.
            for (int i = 1; i <= 4; i++)
            {
                int y = (int)MathHelper.Lerp(horizon, area.Bottom, i / 4f);
                _spriteBatch.Draw(_whiteTex, new Rectangle(area.X, y, area.Width, 2), Color.Black * 0.2f);
            }

            // First enemy visible directly ahead.
            var enemy = FindEnemyInFront(out int distance);
            if (enemy != null)
            {
                float scale = 1f / Math.Max(1, distance);
                int enemyH = Math.Max(20, (int)(area.Height * 0.42f * scale));
                int enemyW = Math.Max(14, enemyH / 2);
                int enemyX = area.Center.X - enemyW / 2;
                int enemyY = horizon + area.Height / 10 - enemyH;
                var enemysrc = new Rectangle(0, 0, _tileSize, _tileSize);
                _spriteBatch.Draw(_grid,new Rectangle(enemyX, enemyY + enemyH / 3, enemyW, enemyH * 2 / 3), enemysrc, Color.White);
                /*_spriteBatch.Draw(_whiteTex,
                    new Rectangle(enemyX + enemyW / 4, enemyY, Math.Max(8, enemyW / 2), Math.Max(8, enemyW / 2)),
                    Color.IndianRed);*/

                if (font != null)
                {
                    var nameSize = font.MeasureString(enemy.Data.Name);
                    _spriteBatch.DrawString(font, enemy.Data.Name,
                        new Vector2(area.Center.X - nameSize.X / 2, Math.Max(area.Y, enemyY - 22)),
                        Color.White);
                }
            }

            // Draw weapon LAST: it stays in front of the room.
            DrawFirstPersonWeapon(area);
        }

        private void DrawFirstPersonWeapon(Rectangle area)
        {
            int weaponBottom = area.Bottom - area.Height / 14;

            if (_weaponTex != null)
            {
                float scale = Math.Min(1f, (area.Width * 0.42f) / Math.Max(1, _weaponTex.Width));
                int weaponW = Math.Max(1, (int)(_weaponTex.Width * scale));
                int weaponH = Math.Max(1, (int)(_weaponTex.Height * scale));
                int weaponX = area.Center.X - weaponW / 2 + area.Width / 10;
                int weaponY = weaponBottom - weaponH;

                _spriteBatch.Draw(_weaponTex, new Rectangle(weaponX, weaponY, weaponW, weaponH), Color.White);
            }
            else
            {
                // Placeholder gun drawn over the room.
                int weaponW = Math.Max(24, area.Width / 10);
                int weaponH = Math.Max(70, area.Height / 3);
                int weaponX = area.Center.X + area.Width / 10;
                int weaponY = weaponBottom - weaponH;

                _spriteBatch.Draw(_whiteTex, new Rectangle(weaponX, weaponY, weaponW, weaponH), Color.DimGray);
                _spriteBatch.Draw(_whiteTex, new Rectangle(weaponX - weaponW / 2, weaponY + weaponH / 2, weaponW * 2, weaponW / 2), Color.DarkGray);
            }
        }

        private RuntimeEnemy FindEnemyInFront(out int distance)
        {
            distance = 0;
            int dx = 0, dy = 0;

            switch (_playerFacing)
            {
                case 0: dy = 1; break;
                case 1: dx = -1; break;
                case 2: dx = 1; break;
                case 3: dy = -1; break;
            }

            int x = _selX;
            int y = _selY;

            for (int i = 1; i <= 6; i++)
            {
                x += dx;
                y += dy;

                if (x < 0 || y < 0 || x >= _mapCols || y >= _mapRows) break;
                if (!_dungeon.IsFloor(x, y)) break;

                var enemy = GetEnemyAt(x, y);
                if (enemy != null)
                {
                    distance = i;
                    return enemy;
                }
            }

            return null;
        }



        private RuntimeEnemy GetEnemyAt(int x, int y)
        {
            foreach (var e in _runtimeEnemies)
            {
                if (e.Data != null && e.Data.Hp > 0 && e.X == x && e.Y == y)
                    return e;
            }
            return null;
        }

        private RuntimeEnemy GetRuntimeEnemy(string id)
        {
            foreach (var e in _runtimeEnemies)
            {
                if (e.Data != null && e.Data.EnemyId == id)
                    return e;
            }
            return null;
        }

        private bool IsEnemyAt(int x, int y)
        {
            return GetEnemyAt(x, y) != null;
        }

        private void SpawnRuntimeEnemies()
        {
            _runtimeEnemies.Clear();
            if (_dungeon == null || _enemies == null || _enemies.Count == 0) return;

            foreach (var enemy in _enemies)
            {
                Point spawn = FindFreeFloor(3);
                if (spawn.X < 0) spawn = FindFreeFloor(0);
                if (spawn.X < 0) continue;

                _runtimeEnemies.Add(new RuntimeEnemy
                {
                    Data = enemy,
                    X = spawn.X,
                    Y = spawn.Y
                });
            }

            _combatMessage = _runtimeEnemies.Count > 0
                ? $"{_runtimeEnemies.Count} enemies are patrolling."
                : "No enemies could be placed.";
        }

        private Point FindFreeFloor(int minimumDistance)
        {
            for (int i = 0; i < 200; i++)
            {
                int x = _rng.Next(_mapCols);
                int y = _rng.Next(_mapRows);
                if (!_dungeon.IsFloor(x, y)) continue;
                if (Math.Abs(x - _selX) + Math.Abs(y - _selY) < minimumDistance) continue;
                if (IsEnemyAt(x, y)) continue;
                return new Point(x, y);
            }

            return new Point(-1, -1);
        }

        private void PatrolEnemies()
        {
            foreach (var enemy in _runtimeEnemies)
            {
                if (enemy.Data == null || enemy.Data.Hp <= 0) continue;

                var options = new List<Point>();
                AddPatrolOption(enemy.X + 1, enemy.Y, options);
                AddPatrolOption(enemy.X - 1, enemy.Y, options);
                AddPatrolOption(enemy.X, enemy.Y + 1, options);
                AddPatrolOption(enemy.X, enemy.Y - 1, options);

                if (options.Count > 0)
                {
                    Point next = options[_rng.Next(options.Count)];
                    enemy.X = next.X;
                    enemy.Y = next.Y;
                }
            }
        }

        private void AddPatrolOption(int x, int y, List<Point> options)
        {
            if (x < 0 || y < 0 || x >= _mapCols || y >= _mapRows) return;
            if (!_dungeon.IsFloor(x, y)) return;
            if (x == _selX && y == _selY) return;
            if (IsEnemyAt(x, y)) return;
            options.Add(new Point(x, y));
        }

        private Wielder GetPlayerWielder()
        {
            if (_party == null || _party.Bonds == null || _party.Bonds.Count == 0)
                return null;

            return _party.Bonds[0]?.Wielder;
        }

        private RuntimeEnemy GetSelectedTarget()
        {
            if (_selectedEnemyIndex < 0 || _selectedEnemyIndex >= _enemies.Count)
                return null;

            var data = _enemies[_selectedEnemyIndex];
            if (data == null || data.Hp <= 0)
                return null;

            var runtime = GetRuntimeEnemy(data.EnemyId);
            if (runtime == null)
                return null;

            return IsEnemyInAttackRange(runtime) ? runtime : null;
        }

        private bool IsEnemyInAttackRange(RuntimeEnemy enemy)
        {
            if (enemy == null || enemy.Data == null || enemy.Data.Hp <= 0)
                return false;

            int distance = Math.Abs(enemy.X - _selX) + Math.Abs(enemy.Y - _selY);
            return distance <= PlayerAttackRange;
        }

        private int FindFirstLivingEnemyIndex()
        {
            for (int i = 0; i < _enemies.Count; i++)
            {
                if (_enemies[i] != null && _enemies[i].Hp > 0)
                    return i;
            }

            // Keep BattleUI index safe even when every enemy is defeated.
            return _enemies.Count > 0 ? 0 : -1;
        }

        private void RefreshTargetableEnemies()
        {
            _targetableEnemyIndices.Clear();

            for (int i = 0; i < _enemies.Count; i++)
            {
                var data = _enemies[i];
                var runtime = data != null ? GetRuntimeEnemy(data.EnemyId) : null;

                if (runtime != null && IsEnemyInAttackRange(runtime))
                    _targetableEnemyIndices.Add(i);
            }
        }

        private void SelectNextTarget(int direction)
        {
            RefreshTargetableEnemies();

            if (_targetableEnemyIndices.Count == 0)
            {
                // Keep the index valid for the existing BattleUI.
                _selectedEnemyIndex = _enemies.Count > 0 ? 0 : -1;
                _combatMessage = $"No enemies within attack range ({PlayerAttackRange} tiles).";
                return;
            }

            int currentPosition = _targetableEnemyIndices.IndexOf(_selectedEnemyIndex);

            if (currentPosition < 0)
                currentPosition = direction > 0 ? 0 : _targetableEnemyIndices.Count - 1;
            else
                currentPosition = (currentPosition + direction + _targetableEnemyIndices.Count) % _targetableEnemyIndices.Count;

            _selectedEnemyIndex = _targetableEnemyIndices[currentPosition];
            _combatMessage = $"Targeting {_enemies[_selectedEnemyIndex].Name}.";
        }

        private void EnsureSelectedTargetInRange()
        {
            RefreshTargetableEnemies();

            if (_targetableEnemyIndices.Count == 0)
            {
                // Keep the index valid for BattleUI; there is simply no active target.
                _selectedEnemyIndex = _enemies.Count > 0 ? 0 : -1;
                return;
            }

            if (!_targetableEnemyIndices.Contains(_selectedEnemyIndex))
                _selectedEnemyIndex = _targetableEnemyIndices[0];
        }

        private void UpdateEnemyAI()
        {
            for (int i = 0; i < _runtimeEnemies.Count; i++)
            {
                var enemy = _runtimeEnemies[i];
                if (enemy.Data == null || enemy.Data.Hp <= 0)
                    continue;

                int distance = Math.Abs(enemy.X - _selX) + Math.Abs(enemy.Y - _selY);

                // Detect and chase player.
                if (distance <= EnemyDetectRange)
                {
                    if (distance <= 1)
                    {
                        DamagePlayer(enemy.Data.Name, EnemyAttackDamage);
                        if (_gameState != GameState.Playing)
                            return;
                        continue;
                    }

                    Point next = FindChaseStep(enemy);
                    if (next.X >= 0)
                    {
                        enemy.X = next.X;
                        enemy.Y = next.Y;
                    }
                }
                else
                {
                    // Outside detection range, patrol normally.
                    PatrolOneEnemy(enemy);
                }
            }

            EnsureSelectedTargetInRange();
            CheckWinLose();
        }



        private Point[] BuildChaseCandidates(int x, int y, int tx, int ty)
        {
            int dx = tx - x;
            int dy = ty - y;

            var result = new List<Point>(4);

            if (Math.Abs(dx) >= Math.Abs(dy))
            {
                if (dx != 0) result.Add(new Point(x + Math.Sign(dx), y));
                if (dy != 0) result.Add(new Point(x, y + Math.Sign(dy)));
                if (dy != 0) result.Add(new Point(x, y + Math.Sign(dy) * -1));
                if (dx != 0) result.Add(new Point(x + Math.Sign(dx) * -1, y));
            }
            else
            {
                if (dy != 0) result.Add(new Point(x, y + Math.Sign(dy)));
                if (dx != 0) result.Add(new Point(x + Math.Sign(dx), y));
                if (dx != 0) result.Add(new Point(x + Math.Sign(dx) * -1, y));
                if (dy != 0) result.Add(new Point(x, y + Math.Sign(dy) * -1));
            }

            return result.ToArray();
        }

        private bool CanEnemyMoveTo(int x, int y, RuntimeEnemy movingEnemy)
        {
            if (x < 0 || y < 0 || x >= _mapCols || y >= _mapRows)
                return false;

            if (!_dungeon.IsFloor(x, y))
                return false;

            if (x == _selX && y == _selY)
                return false;

            foreach (var other in _runtimeEnemies)
            {
                if (other == movingEnemy || other.Data == null || other.Data.Hp <= 0)
                    continue;

                if (other.X == x && other.Y == y)
                    return false;
            }

            return true;
        }

        private void PatrolOneEnemy(RuntimeEnemy enemy)
        {
            var options = new List<Point>();
            AddPatrolOptionForEnemy(enemy.X + 1, enemy.Y, enemy, options);
            AddPatrolOptionForEnemy(enemy.X - 1, enemy.Y, enemy, options);
            AddPatrolOptionForEnemy(enemy.X, enemy.Y + 1, enemy, options);
            AddPatrolOptionForEnemy(enemy.X, enemy.Y - 1, enemy, options);

            if (options.Count == 0)
                return;

            Point next = options[_rng.Next(options.Count)];
            enemy.X = next.X;
            enemy.Y = next.Y;
        }

        private void AddPatrolOptionForEnemy(int x, int y, RuntimeEnemy enemy, List<Point> options)
        {
            if (CanEnemyMoveTo(x, y, enemy))
                options.Add(new Point(x, y));
        }

        private void DamagePlayer(string enemyName, int damage)
        {
            var player = GetPlayerWielder();
            if (player == null || player.Hp <= 0)
                return;

            player.Hp = Math.Max(0, player.Hp - damage);
            _combatMessage = $"{enemyName} hit you for {damage} damage!";

            if (player.Hp <= 0)
            {
                _gameState = GameState.Lost;
                _combatMessage = "You were defeated.";
            }
        }

        private void CheckWinLose()
        {
            if (_gameState != GameState.Playing)
                return;

            var player = GetPlayerWielder();

            if (player != null && player.Hp <= 0)
            {
                _gameState = GameState.Lost;
                return;
            }

            bool anyAlive = false;
            foreach (var e in _enemies)
            {
                if (e != null && e.Hp > 0)
                {
                    anyAlive = true;
                    break;
                }
            }

            if (!anyAlive)
            {
                _gameState = GameState.Won;
                _combatMessage = "All enemies defeated!";
            }
        }

        private void RestartBattle()
        {
            Random rnd = new Random();
            var player = GetPlayerWielder();
            if (player != null)
            {
                player.Hp = player.MaxHp;
            }

            _enemies.Clear();
            enemyCount = rnd.Next(3, 5); // random number of enemies for this run


            for (int i = 0; i < enemyCount; i++)
            {
                string name = enemyNames[rnd.Next(enemyNames.Length)];
                var enemy = new Enemy
                {
                    EnemyId = $"e-{i + 1}",
                    Name = name,
                    Hp = 20,
                    MaxHp = 20
                };
                _enemies.Add(enemy);
            }


            // Create dungeon map and generate rooms + corridors
            _dungeon = new Dungeon.DungeonMap(_mapCols, _mapRows);
            _dungeon.GenerateRooms(6, 3, 6);

            // Place player at center floor if possible (use first room center if available)
            int px = _mapCols / 2;
            int py = _mapRows / 2;
            if (_dungeon.Rooms.Count > 0)
            {
                var r0 = _dungeon.Rooms[0];
                px = r0.CenterX; py = r0.CenterY;
            }
            if (!_dungeon.IsFloor(px, py))
            {
                // find nearest floor
                for (int r = 1; r < Math.Max(_mapCols, _mapRows); r++)
                {
                    bool found = false;
                    for (int dx = -r; dx <= r && !found; dx++)
                        for (int dy = -r; dy <= r && !found; dy++)
                        {
                            int nx = px + dx, ny = py + dy;
                            if (nx >= 0 && ny >= 0 && nx < _mapCols && ny < _mapRows && _dungeon.IsFloor(nx, ny))
                            {
                                px = nx; py = ny; found = true;
                            }
                        }
                    if (found) break;
                }
            }
            _selX = px; _selY = py;
            _playerPosX = _selX;
            _playerPosY = _selY;

            _runtimeEnemies.Clear();
            SpawnRuntimeEnemies();

            _selectedEnemyIndex = 0;
            _targetableEnemyIndices.Clear();
            _enemyThinkTimer = 0.0;
            _enemyPatrolTimer = 0.0;
            _combatMessage = "Battle restarted.";
            _gameState = GameState.Playing;
            EnsureSelectedTargetInRange();
        }

        // Minigame lifecycle handled by SyncMinigame class
    }
}
