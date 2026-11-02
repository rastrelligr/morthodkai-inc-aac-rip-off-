using System;
using System.Collections.Generic;
using System.Linq;
using SERAAC.Combat;
using SERAAC.Core;
using SERAAC.Data;
using SERAAC.Meta;
using SERAAC.Screens;
using SERAAC.Trial;
using SERAAC.UI;
using MapObject = SERAAC.World.TiledMap.MapObject;

namespace SERAAC.Story
{
    // The prologue, chapter by chapter (the playtest build):
    //   dorm     Naomi wakes Relle up (VN), then Relle walks around her room (Art/Maps/RelleDorm.tmx)
    //   spar     time skip to the Staging Room's combat simulator; the simulated spar teaches combat (Combat/CombatTutorial.cs)
    //   airlift  the Airlift platform (Art/Maps/RooftopAirlift.tmx); talking to Naomi starts the briefing
    //   hub      the Sanctum, with Naomi explaining each tab; funds to spend; only the Fallen Academy opens
    //   academy  the Fallen Academy as a whole trial; beating the Wyvern ends the playtest
    //   done
    // Progress is saved in Profile.Story, so Continue on the title screen resumes the current chapter.
    public static class StoryFlow
    {
        public const string Dorm = "dorm", Spar = "spar", Airlift = "airlift", Hub = "hub", Academy = "academy", Done = "done";

        private static Profile Profile => Game1.Instance.Profile;
        private static ScreenManager Screens => Game1.Instance.Screens;

        public static bool InPrologue => Profile.Story is Dorm or Spar or Airlift or Hub;
        // The Sanctum only lets the squad deploy to the Fallen Academy until the Wyvern is down.
        public static bool AcademyOnly => Profile.Story is Hub or Academy;

        private static void SetStage(string stage)
        {
            Profile.Story = stage;
            ProfileStore.Save(Profile);
        }

        // Title screen > New Game.
        public static void Begin()
        {
            SetStage(Dorm);
            Screens.Replace(DormScreen());
        }

        // Title screen > Continue: the chapter the save is in. Saves from before the prologue existed go to the hub.
        public static void Resume()
        {
            string stage = Profile.Story;
            if (string.IsNullOrEmpty(stage)) stage = Profile.TrialsStarted > 0 ? Done : Dorm;
            switch (stage)
            {
                case Dorm: Begin(); break;
                case Spar: Screens.Replace(SparScreen()); break;
                case Airlift: Screens.Replace(AirliftScreen()); break;
                case Hub: Screens.Replace(new HubScreen { Tutorial = true }); break;
                default: Screens.Replace(new HubScreen()); break;
            }
        }

        // ------------------------------------------------------------------ 1. the dorm

        private const string BladeFlag = "dorm_blade";

        private static OverworldScreen DormScreen()
        {
            bool hasBlade = false;
            var scene = new MapScene
            {
                MapFile = "RelleDorm.tmx",
                Scale = 2f, ActorScale = 3f,
                Backdrop = "black",
                AmbienceOver = "dorm",
                Music = "dialogue",
                Footsteps = "step_tile",
                WalkTiles = 3.6f,
            };
            scene.OnArrive = s =>
            {
                s.Locked = true;
                s.Say(StoryScripts.WakeUp(), () =>
                {
                    s.Locked = false;
                    s.SetHint(StoryScripts.DormHint);
                });
            };
            scene.OnInteract = (s, o) =>
            {
                if (o.Name == "door")
                {
                    if (!hasBlade) { s.Say(StoryScripts.NoBlade()); return true; }
                    bool leave = false;
                    s.Say(StoryScripts.LeaveRoom(() => leave = true), () =>
                    {
                        if (leave) Screens.Replace(SparScreen());
                    });
                    return true;
                }
                bool again = s.Seen.Contains(o.Name + "#2");
                s.Seen.Add(o.Name + "#2");
                var lines = StoryScripts.Inspect(o.Name, again, hasBlade);
                if (o.Name == "desk" && !hasBlade)
                {
                    hasBlade = true;
                    s.Say(lines, () => s.SetHint("Leave your room (the door mat, bottom right)"));
                    return true;
                }
                s.Say(lines);
                return true;
            };
            return new OverworldScreen(scene);
        }

        // ------------------------------------------------------------------ 2. the sparring match

        // The story screen with the time skip, over a black frame; then the fight.
        private static Screen SparScreen()
        {
            SetStage(Spar);
            Audio.Music("dialogue");
            return new BlankScreen(() => Screens.PushInstant(new StoryScreen(StoryScripts.SparIntro(), () => Screens.Replace(SparFight()))));
        }

        private static CombatScreen SparFight()
        {
            var (area, trial) = WorldDb.Trial("city_outskirt");
            var run = TrialRun.Sandbox(Profile, area, trial, new[] { "relle" }, Environment.TickCount);
            run.Consumables.Clear();
            var naomi = Combatant.FromEnemy(StoryScripts.Naomi, 1f);
            // A random City Outskirts background, like a normal fight there.
            var stage = Stage.BackgroundDb.Pick(area.Id, trial.Id, "normal", Array.Empty<string>(), new Random())?.Image;
            return new CombatScreen(run, new List<Combatant> { naomi }, battle =>
            {
                bool won = battle.Outcome == BattleOutcome.Victory;
                Audio.Music("dialogue");
                Screens.PushInstant(new StoryScreen(won ? StoryScripts.SparWon() : StoryScripts.SparLost(), () =>
                {
                    SetStage(Airlift);
                    Screens.Replace(AirliftScreen());
                }));
            })
            {
                Tutorial = StoryScripts.SparTutorial(),
                StageOverride = stage,
            };
        }

        // ------------------------------------------------------------------ 3. the Airlift platform

        private static OverworldScreen AirliftScreen()
        {
            var scene = new MapScene
            {
                MapFile = "RooftopAirlift.tmx",
                Scale = 2f, ActorScale = 2f,
                Backdrop = "sky",
                AmbienceUnder = "airlift_sky",
                AmbienceOver = "airlift_over",
                Title = "Airlift Platform",
                Subtitle = "Aetheris Academy, upper deck",
                Music = "airlift",
                WalkTiles = 6f,
                Tint = new Microsoft.Xna.Framework.Color(40, 60, 120) * 0.12f,
                DropShadow = new Microsoft.Xna.Framework.Vector2(70, 110),
            };
            scene.OnArrive = s =>
            {
                s.Locked = true;
                s.Say(StoryScripts.AirliftArrive(), () =>
                {
                    s.Locked = false;
                    s.SetHint(StoryScripts.AirliftHint);
                });
            };
            scene.OnInteract = (s, o) =>
            {
                if (o.Class == "npc" && o.Name == "Naomi")
                {
                    bool ready = false;
                    s.Say(StoryScripts.NaomiAirlift(() => ready = true), () =>
                    {
                        if (!ready) return;
                        s.Locked = true;
                        s.Say(StoryScripts.NaomiBriefing(), () =>
                        {
                            SetStage(Hub);
                            Screens.Replace(new HubScreen { Tutorial = true });
                        });
                    });
                    return true;
                }
                s.Say(StoryScripts.AirliftInspect(o.Name));
                return true;
            };
            return new OverworldScreen(scene);
        }

        // ------------------------------------------------------------------ 4. the Sanctum

        // Called by the hub tutorial when Naomi hands over the operation funds (once).
        public static void GiveFunds(int amount)
        {
            if (Profile.StoryFlags.Contains("funds")) return;
            Profile.StoryFlags.Add("funds");
            Profile.SecuredShards += amount;
            ProfileStore.Save(Profile);
        }

        public static void FinishHubTutorial() => SetStage(Academy);

        // ------------------------------------------------------------------ 5. the end

        // The exploration screen calls this when a fight completes a Main objective (the Wyvern).
        public static bool OnMainObjective()
        {
            if (Profile.Story != Academy) return false;
            SetStage(Done);
            Screens.PushInstant(new StoryScreen(StoryScripts.WyvernDown(), () => Screens.Replace(new EndScreen()), black: true));
            return true;
        }
    }

    // An empty black screen that runs something on its first frame (a story scene to play over it).
    public sealed class BlankScreen : Screen
    {
        private Action _first;
        public BlankScreen(Action first) { _first = first; }

        public override void Update(Microsoft.Xna.Framework.GameTime gameTime)
        {
            var f = _first;
            _first = null;
            f?.Invoke();
        }

        public override void Draw(Microsoft.Xna.Framework.Graphics.SpriteBatch sb, Microsoft.Xna.Framework.GameTime gameTime) =>
            Core.Draw.Rect(sb, new Microsoft.Xna.Framework.Rectangle(0, 0, Game1.Width, Game1.Height), Microsoft.Xna.Framework.Color.Black);
    }
}
