using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SERAAC.Combat;
using SERAAC.Core;
using SERAAC.Entities;
using SERAAC.Input;

namespace SERAAC.Screens
{
    // Split-screen turn resolution: the left panel is the Vessel's 3x3 grid (Player 1,
    // keyboard - Move or Defend), the right panel is the Weapon's targeting (Player 2,
    // mouse - aim and Attack). Both must act before the round resolves and the enemies reply.
    public class CombatScreen : Screen
    {
        private enum Phase { Selecting, AttackChallenge, Resolving, EnemyTurn, Won, Lost }

        private readonly VesselInput _vesselInput = new VesselInput();
        private readonly WeaponInput _weaponInput = new WeaponInput();
        private readonly AttackMinigame _minigame = new AttackMinigame();
        private readonly SoulBond _bond;
        private readonly List<Enemy> _enemies;

        private Phase _phase = Phase.Selecting;
        private int _vesselCell = CombatGrid.CenterCell;
        private bool _vesselDone;
        private bool _weaponDone;
        private bool _vesselDefended;
        private int _selectedEnemy;
        private int _hoveredEnemy = -1;
        private string _message = "Choose your actions: P1 move/defend, P2 aim and attack.";

        public CombatScreen(Game1 game, GameContext context, SharedAssets assets) : base(game, context, assets)
        {
            _bond = context.Party.Primary;
            _enemies = context.PendingEncounter ?? new List<Enemy>();
            context.PendingEncounter = null;
            _bond.Vessel.IsGuarding = false;
        }

        public override void OnEnter() => Game.IsMouseVisible = true;

        public override void Update(GameTime gameTime)
        {
            _vesselInput.Update();
            _weaponInput.Update();

            switch (_phase)
            {
                case Phase.Selecting:
                    UpdateSelecting();
                    break;
                case Phase.AttackChallenge:
                    UpdateAttackChallenge(gameTime);
                    break;
                case Phase.EnemyTurn:
                    RunEnemyTurn();
                    break;
                case Phase.Won:
                case Phase.Lost:
                    UpdateResultWait();
                    break;
            }
        }

        private void UpdateSelecting()
        {
            HandleVesselSelection();
            HandleWeaponSelection();

            if (_vesselDone && _weaponDone)
            {
                _phase = Phase.Resolving;
                Resolve();
            }
        }

        private void HandleVesselSelection()
        {
            if (_vesselDone) return;

            int dRow = 0, dCol = 0;
            bool moved = false;
            if (_vesselInput.Up) { dRow = -1; moved = true; }
            else if (_vesselInput.Down) { dRow = 1; moved = true; }
            else if (_vesselInput.Left) { dCol = -1; moved = true; }
            else if (_vesselInput.Right) { dCol = 1; moved = true; }

            if (moved && CombatGrid.TryStep(_vesselCell, dRow, dCol, out int newCell))
            {
                _vesselCell = newCell;
                _vesselDone = true;
                _message = "Vessel repositioned.";
                return;
            }

            if (_vesselInput.Confirm)
            {
                _bond.Vessel.IsGuarding = true;
                _vesselDefended = true;
                _vesselDone = true;
                _message = "Vessel braces to Defend.";
            }
        }

        private void HandleWeaponSelection()
        {
            if (_weaponDone) return;

            _hoveredEnemy = HitTestEnemyCard(_weaponInput.Position);
            if (_hoveredEnemy >= 0) _selectedEnemy = _hoveredEnemy;

            if (_weaponInput.Attack && _selectedEnemy >= 0 && _selectedEnemy < _enemies.Count && _enemies[_selectedEnemy].IsAlive)
            {
                _phase = Phase.AttackChallenge;
                _minigame.Start();
                _message = "Both players: confirm the strike! P1 = Space, P2 = Left Click.";
            }
        }

        private void UpdateAttackChallenge(GameTime gameTime)
        {
            _minigame.Update(gameTime, _vesselInput, _weaponInput);
            if (!_minigame.IsComplete()) return;

            bool success = _minigame.Evaluate();
            var target = _enemies[_selectedEnemy];

            if (success)
            {
                bool weakPoint = target.WeakCell == _vesselCell;
                int damage = _bond.Weapon.BaseDamage + (weakPoint ? _bond.Weapon.WeakPointBonus : 0);
                target.TakeDamage(damage);
                _bond.AddSync(15f);
                _message = weakPoint
                    ? $"Weak point exposed! {target.Name} takes {damage} damage!"
                    : $"{target.Name} takes {damage} damage.";

                if (!target.IsAlive) _message = $"{target.Name} is defeated!";
            }
            else
            {
                _message = "The strike wasn't synced - the attack fails.";
            }

            _weaponDone = true;
            _phase = Phase.Selecting;

            if (_vesselDone && _weaponDone)
            {
                _phase = Phase.Resolving;
                Resolve();
            }
        }

        private void Resolve()
        {
            if (AllEnemiesDefeated())
            {
                _phase = Phase.Won;
                _message = "Encounter cleared!";
                return;
            }

            _phase = Phase.EnemyTurn;
        }

        private void RunEnemyTurn()
        {
            foreach (var enemy in _enemies)
            {
                if (!enemy.IsAlive) continue;
                if (Context.Rng.NextDouble() > enemy.HitChance) continue;

                _bond.Vessel.TakeDamage(enemy.AttackDamage);
                _message = $"{enemy.Name} hits the Vessel for {enemy.AttackDamage}.";

                if (!_bond.Vessel.IsAlive)
                {
                    _phase = Phase.Lost;
                    _message = "The Vessel has fallen...";
                    return;
                }
            }

            _bond.Vessel.IsGuarding = false;
            _vesselDefended = false;
            _vesselDone = false;
            _weaponDone = false;
            _phase = Phase.Selecting;
            _message = "Choose your actions: P1 move/defend, P2 aim and attack.";
        }

        private void UpdateResultWait()
        {
            if (!_vesselInput.Confirm) return;

            if (_phase == Phase.Won)
            {
                if (Context.DungeonEnemies.Count == 0)
                {
                    if (Context.FloorNumber >= GameContext.TotalFloors)
                    {
                        Game.ChangeScreen(new ResultScreen(Game, Context, Assets, true));
                        return;
                    }

                    Context.FloorNumber++;
                    Context.Dungeon = null;
                }

                Game.ChangeScreen(new ExplorationScreen(Game, Context, Assets));
            }
            else
            {
                Game.ChangeScreen(new ResultScreen(Game, Context, Assets, false));
            }
        }

        private bool AllEnemiesDefeated()
        {
            foreach (var e in _enemies)
                if (e.IsAlive) return false;
            return true;
        }

        // ---------------------------------------------------------------- drawing ----

        private Rectangle _leftPanel, _rightPanel;
        private readonly List<Rectangle> _enemyCardRects = new List<Rectangle>();

        private int HitTestEnemyCard(Point p)
        {
            for (int i = 0; i < _enemyCardRects.Count; i++)
                if (i < _enemies.Count && _enemies[i].IsAlive && _enemyCardRects[i].Contains(p))
                    return i;
            return -1;
        }

        public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            var device = Game.GraphicsDevice;
            int w = device.Viewport.Width;
            int h = device.Viewport.Height;
            _leftPanel = new Rectangle(0, 0, w / 2, h);
            _rightPanel = new Rectangle(w / 2, 0, w - w / 2, h);

            spriteBatch.Begin();
            spriteBatch.Draw(Assets.Pixel, _leftPanel, new Color(30, 34, 45));
            spriteBatch.Draw(Assets.Pixel, _rightPanel, new Color(45, 30, 30));

            DrawVesselPanel(spriteBatch);
            DrawWeaponPanel(spriteBatch);
            DrawMessageBar(spriteBatch, w, h);

            if (_phase == Phase.Won || _phase == Phase.Lost)
                DrawResultOverlay(spriteBatch, w, h);

            spriteBatch.End();
        }

        private void DrawVesselPanel(SpriteBatch spriteBatch)
        {
            var font = Assets.BodyFont;
            int pad = 30;
            int gridSize = Math.Min(_leftPanel.Width, _leftPanel.Height) - pad * 2 - 80;
            int cell = gridSize / CombatGrid.Size;
            int gx = _leftPanel.X + (_leftPanel.Width - cell * CombatGrid.Size) / 2;
            int gy = _leftPanel.Y + 110;

            for (int i = 0; i < CombatGrid.CellCount; i++)
            {
                var (row, col) = CombatGrid.ToRowCol(i);
                var rect = new Rectangle(gx + col * cell, gy + row * cell, cell - 4, cell - 4);
                bool occupied = i == _vesselCell;
                spriteBatch.Draw(Assets.Pixel, rect, occupied ? new Color(90, 160, 220) : new Color(60, 64, 76));
            }

            if (font == null) return;

            spriteBatch.DrawString(font, "VESSEL (P1)", new Vector2(gx, _leftPanel.Y + 24), Color.CornflowerBlue);
            spriteBatch.DrawString(font, $"HP {_bond.Vessel.Hp}/{_bond.Vessel.MaxHp}", new Vector2(gx, _leftPanel.Y + 50),
                _bond.Vessel.Hp <= _bond.Vessel.MaxHp / 3 ? Color.OrangeRed : Color.LightGreen);

            string status = _vesselDone ? (_vesselDefended ? "Defending" : "Ready") : "W/A/S/D to move, Space to Defend";
            spriteBatch.DrawString(font, status, new Vector2(gx, gy + gridSize + 20), Color.White);
        }

        private void DrawWeaponPanel(SpriteBatch spriteBatch)
        {
            var font = Assets.BodyFont;
            int pad = 30;
            int cardH = 64;
            int x = _rightPanel.X + pad;
            int y = _rightPanel.Y + 110;
            int width = _rightPanel.Width - pad * 2;

            _enemyCardRects.Clear();

            if (font != null)
            {
                spriteBatch.DrawString(font, "WEAPON (P2)", new Vector2(x, _rightPanel.Y + 24), Color.IndianRed);
                spriteBatch.DrawString(font, "Hover an enemy, Left Click to attack",
                    new Vector2(x, _rightPanel.Y + 50), Color.White);
            }

            for (int i = 0; i < _enemies.Count; i++)
            {
                var enemy = _enemies[i];
                var rect = new Rectangle(x, y + i * (cardH + 10), width, cardH);
                _enemyCardRects.Add(rect);

                if (!enemy.IsAlive)
                {
                    spriteBatch.Draw(Assets.Pixel, rect, new Color(20, 20, 20));
                    continue;
                }

                bool selected = i == _selectedEnemy;
                bool weak = enemy.WeakCell == _vesselCell;
                spriteBatch.Draw(Assets.Pixel, rect, selected ? new Color(150, 60, 60) : new Color(70, 40, 40));

                if (font != null)
                {
                    spriteBatch.DrawString(font, $"{enemy.Name}  HP {enemy.Hp}/{enemy.MaxHp}",
                        new Vector2(rect.X + 10, rect.Y + 8), Color.White);
                    spriteBatch.DrawString(font, weak ? "Weak point exposed!" : "Vessel not in position",
                        new Vector2(rect.X + 10, rect.Y + 32), weak ? Color.Gold : Color.LightGray);
                }
            }

            if (_phase == Phase.AttackChallenge && font != null)
            {
                int barY = y + _enemies.Count * (cardH + 10) + 20;
                var barRect = new Rectangle(x, barY, width, 30);
                spriteBatch.Draw(Assets.Pixel, barRect, new Color(20, 20, 20));
                float pct = (float)(_minigame.TimeRemaining / _minigame.Duration);
                spriteBatch.Draw(Assets.Pixel, new Rectangle(barRect.X, barRect.Y, (int)(barRect.Width * pct), barRect.Height), Color.Gold);
                string readyText = $"P1 Space: {(_minigame.VesselReady ? "OK" : "...")}   P2 Click: {(_minigame.WeaponReady ? "OK" : "...")}";
                spriteBatch.DrawString(font, readyText, new Vector2(x, barY + 36), Color.White);
            }
        }

        private void DrawMessageBar(SpriteBatch spriteBatch, int w, int h)
        {
            var font = Assets.BodyFont;
            if (font == null) return;
            var rect = new Rectangle(0, h - 44, w, 44);
            spriteBatch.Draw(Assets.Pixel, rect, Color.Black * 0.7f);
            spriteBatch.DrawString(font, _message, new Vector2(16, h - 34), Color.Yellow);
        }

        private void DrawResultOverlay(SpriteBatch spriteBatch, int w, int h)
        {
            spriteBatch.Draw(Assets.Pixel, new Rectangle(0, 0, w, h), Color.Black * 0.6f);
            var font = Assets.HeaderFont ?? Assets.BodyFont;
            if (font == null) return;

            string text = _phase == Phase.Won ? "ENCOUNTER CLEARED" : "THE VESSEL HAS FALLEN";
            var size = font.MeasureString(text);
            spriteBatch.DrawString(font, text, new Vector2(w / 2f - size.X / 2f, h / 2f - 30),
                _phase == Phase.Won ? Color.LightGreen : Color.OrangeRed);

            string prompt = "Press Space to continue";
            var promptSize = (Assets.BodyFont ?? font).MeasureString(prompt);
            spriteBatch.DrawString(Assets.BodyFont ?? font, prompt, new Vector2(w / 2f - promptSize.X / 2f, h / 2f + 30), Color.White);
        }
    }
}
