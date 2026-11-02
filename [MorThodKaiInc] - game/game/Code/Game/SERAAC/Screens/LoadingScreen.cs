using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SERAAC.Core;
using SERAAC.Trial;
using SERAAC.UI;

namespace SERAAC.Screens
{
    // Between the Sanctum and a Trial: the trial's name over one of its backgrounds, the objective, a tip, and a
    // progress bar while the exploration music and the combat sounds load in the background. Then the trial starts.
    public sealed class LoadingScreen : Screen
    {
        private static readonly string[] Tips =
        {
            "Enemies roam each room. Touch one and the fight starts; slip past and it may follow you through a door or two.",
            "Shards you pick up are lost if the party falls. Reach an Extraction Point to keep them.",
            "Guard acts before everyone else. Use it when an enemy winds up a big attack.",
            "Every skill shows the positions it can be used from and the positions it can reach.",
            "Relle's Feast: every fourth skill she uses is a guaranteed critical hit.",
            "Focus gathers two extra Ethergy. Use it on a quiet turn to set up a big skill.",
            "Shrines heal the party and bring fallen members back.",
            "Mara's Shield Bash and Iron Rebuke interrupt a wind-up. So does dragging the enemy forward first.",
            "Press M to open the map. Extraction Points and objective sites are marked from the start.",
        };

        // Trials without fight backgrounds of their own yet.
        private static readonly System.Collections.Generic.Dictionary<string, string> Pictures = new()
        {
            ["city_academy"] = "Academy_Boss",
        };

        private readonly TrialRun _run;
        private readonly string _tip;
        private readonly string _bg;
        private float _t;
        private bool _leaving;

        private const float MinSeconds = 2.2f;

        public LoadingScreen(TrialRun run)
        {
            _run = run;
            var rng = new Random();
            _tip = Tips[rng.Next(Tips.Length)];
            // One of the trial's fight backgrounds, else its own picture for this screen (Art/Stages, layers found by name).
            _bg = Stage.BackgroundDb.Pick(run.Area.Id, run.Trial.Id, "normal", Array.Empty<string>(), rng)?.Image
                  ?? (Pictures.TryGetValue(run.Trial.Id, out var own) ? own : null);
        }

        public override bool MenuKeySounds => false;

        public override void OnEnter()
        {
            Audio.Music("explore");
            Audio.Preload("encounter", "dash", "cast", "move", "hit", "hit_magic", "crit", "tick", "heal", "status", "guard",
                "windup", "interrupted", "death", "stack", "feast", "riposte", "aegis", "victory", "defeat", "step", "pickup", "alert");
        }

        private float Progress => Math.Min(_t / MinSeconds, Audio.MusicLoading ? 0.9f : 1f);

        public override void Update(GameTime gameTime)
        {
            _t += (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_leaving || Progress < 1f) return;
            _leaving = true;
            Screens.Replace(new ExplorationScreen(_run));
        }

        public override void Draw(SpriteBatch sb, GameTime gameTime)
        {
            double time = gameTime.TotalGameTime.TotalSeconds;
            var screen = new Rectangle(0, 0, Game1.Width, Game1.Height);
            Core.Draw.Rect(sb, screen, Palette.Ink);

            // The trial's own scenery, dark and slowly drifting.
            var layers = VnDialogue.BackgroundArt(_bg);
            if (layers.Count > 0)
            {
                sb.End();
                sb.Begin(samplerState: SamplerState.LinearClamp, transformMatrix: Core.Draw.FrameTransform);
                foreach (var (tex, depth) in layers)
                {
                    float scale = Math.Max(Game1.Width / (float)tex.Width, Game1.Height / (float)tex.Height) * (1.08f + 0.03f * _t / 10f);
                    var size = new Vector2(tex.Width, tex.Height) * scale;
                    var pos = (new Vector2(Game1.Width, Game1.Height) - size) / 2f + new Vector2(-_t * 6f * depth, 0);
                    sb.Draw(tex, pos, null, Color.White * 0.45f, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
                }
                sb.End();
                sb.Begin(samplerState: SamplerState.PointClamp, transformMatrix: Core.Draw.FrameTransform);
            }
            Core.Draw.GradientV(sb, new Rectangle(0, 300, Game1.Width, 420), Palette.Ink, fromBottom: true);
            Ui.Embers(sb, time, 0.5f, 40);

            float a = MathHelper.Clamp(_t / 0.5f, 0, 1);
            Core.Draw.Text(sb, Assets.Small, _run.Area.Name.ToUpperInvariant(), new Vector2(80, 420), Palette.TextDim * a);
            Core.Draw.Spaced(sb, Assets.Title, _run.Trial.Name.ToUpperInvariant(), new Vector2(80, 440), Palette.Paper * a, 4);
            Core.Draw.GlowLine(sb, new Vector2(380, 500), 600, Palette.Rose * a);
            if (_run.Objective != null)
            {
                Icons.Draw(sb, "ui_objective", new Rectangle(80, 518, 20, 20));
                Core.Draw.Text(sb, Assets.Body, $"{_run.Objective.Name}:  {_run.Objective.Task}", new Vector2(110, 516), Palette.Blush * a);
            }
            Core.Draw.Text(sb, Assets.Small, "TIP", new Vector2(80, 572), Palette.Gold * a);
            Core.Draw.Wrapped(sb, Assets.Body, _tip, new Vector2(120, 568), 900, Palette.Text * a);

            // Progress bar and a turning crest, bottom right.
            var bar = new Rectangle(Game1.Width - 380, Game1.Height - 54, 300, 4);
            Core.Draw.Rect(sb, bar, Palette.Paper * 0.15f);
            Core.Draw.Rect(sb, new Rectangle(bar.X, bar.Y, (int)(bar.Width * Progress), bar.Height), Palette.Rose);
            Core.Draw.TextRight(sb, Assets.Small, Progress >= 1f ? "READY" : "LOADING", new Vector2(bar.Right, bar.Y - 24), Palette.TextDim);
            var crest = new Vector2(Game1.Width - 50, Game1.Height - 52);
            float spin = (float)(time * 2.5);
            for (int i = 0; i < 4; i++)
            {
                var p = crest + new Vector2(MathF.Cos(spin + i * MathHelper.PiOver2), MathF.Sin(spin + i * MathHelper.PiOver2)) * 12f;
                Core.Draw.Diamond(sb, p, 6, (i == 0 ? Palette.Rose : Palette.Paper * 0.5f));
            }
        }
    }
}
