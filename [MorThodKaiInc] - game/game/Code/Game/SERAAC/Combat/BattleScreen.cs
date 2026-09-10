using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using MonoGame.Extended.Screens;
using SERAAC.Data.Enemies;
using SERAAC.Data.UI;
using SERAAC.Data.Minigames;
using SERAAC.Data.Characters;

namespace SERAAC.Data.Combat
{
    // A Screen (MonoGame.Extended.Screens) that owns one battle encounter.
    // Drop the base class if you're not using ScreenManager.
    public class BattleScreen : GameScreen
    {
        public Party.Party Party { get; set; }
        public List<Enemy> Enemies { get; set; } = new List<Enemy>();
        public Queue<TurnActor> TurnQueue { get; set; } = new Queue<TurnActor>();
        public Hud Hud { get; set; } = new Hud();
        public SyncedBattleTurn PendingSyncedTurn { get; set; }
        public Dictionary<MinigameType, MinigameConfig> MinigamePool { get; set; } = new Dictionary<MinigameType, MinigameConfig>();

        public BattleScreen(Game game) : base(game) { }

        public void BuildTurnOrder()
        {
            // TODO: enqueue TurnActors from Party.Bonds + Enemies (speed stat, etc.)
            throw new NotImplementedException();
        }

        public TurnActor AdvanceTurn()
        {
            // TODO: TurnQueue.Dequeue(), re-Enqueue if the round repeats
            throw new NotImplementedException();
        }

        public void ResolveSyncedTurn(SyncedBattleTurn turn)
        {
            // TODO: check left/right minigame results, apply sync bonus, damage, buffs
            throw new NotImplementedException();
        }

        public override void Update(GameTime gameTime)
        {
            // TODO: drive MinigameState timers, capture input, tick buffs
        }

        public override void Draw(GameTime gameTime)
        {
            // TODO: draw LeftPanel / RightPanel via SpriteBatch, then Hud on top
        }
    }
}
