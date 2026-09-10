using System.Collections.Generic;
using Microsoft.Xna.Framework;
using MonoGame.Extended.Screens;

namespace SERAAC.Data.Dungeon
{
    // Top-level Screen for the roguelike run / map navigation.
    public class DungeonScreen : GameScreen
    {
        public int Seed { get; set; }
        public Party.Party Party { get; set; }
        public List<DungeonFloor> Floors { get; set; } = new List<DungeonFloor>();
        public int CurrentFloorIndex { get; set; }
        public string CurrentRoomId { get; set; }
        public List<string> RunHistory { get; set; } = new List<string>(); // log of roomIds visited (meta/recap)

        public DungeonScreen(Game game) : base(game) { }

        public override void Update(GameTime gameTime) { }
        public override void Draw(GameTime gameTime) { }
    }
}
