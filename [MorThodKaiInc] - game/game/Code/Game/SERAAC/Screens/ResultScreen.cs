using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.Tweening;
using SERAAC.Core;
using SERAAC.Data;
using SERAAC.Trial;
using SERAAC.UI;

namespace SERAAC.Screens
{
    // End of a Trial: what was secured (extraction) or what was lost (defeat / abandon).
    public sealed class ResultScreen : Screen
    {
        private readonly TrialRun _run;
        private readonly bool _extracted;
        private readonly bool _abandoned;
        private float _age;
        public System.Action OnContinue;   // replaces going back to the hub (the playtest ending)

        public ResultScreen(TrialRun run, bool extracted, bool abandoned = false)
        {
            _run = run;
            _extracted = extracted;
            _abandoned = abandoned;
        }

        public override void OnEnter()
        {
            if (_extracted) Audio.Play("secured");
        }

        public override void Update(GameTime gameTime)
        {
            _age += (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_age > 0.8f && (Input.Confirm || Input.Cancel || UI.Ui.Clicked("continue")))
            {
                if (OnContinue != null) OnContinue();
                else Screens.Replace(Meta.Playtest.Active ? new PlaytestScreen() : new HubScreen());   // playtests return to their menu
            }
        }

        public override void Draw(SpriteBatch sb, GameTime gameTime)
        {
            var profile = Game1.Instance.Profile;
            double t = gameTime.TotalGameTime.TotalSeconds;
            var accent = _extracted ? Palette.Gold : Palette.Danger;
            string title = _extracted ? "EXTRACTED" : _abandoned ? "TRIAL ABANDONED" : "THE PARTY HAS FALLEN";
            Ui.Backdrop(sb, t, _extracted ? 0.6f : 0.25f);
            Core.Draw.Rect(sb, new Rectangle(0, 0, Game1.Width, Game1.Height), Palette.Ink * (_extracted ? 0.35f : 0.55f));

            // Eased reveal (MonoGame.Extended EasingFunctions): the title fades in, then each line slides in after the last.
            float titleIn = MathHelper.Clamp(Ease.Progress(_age, 0.6f, EasingFunctions.CubicOut), 0, 1);
            var titleCenter = new Vector2(Game1.Width / 2f, 110);
            if (_extracted)
                // A victory band, like a boss being slain: lines of gold light above and below the title.
                Ui.Banner(sb, title, titleCenter, Game1.Width, accent, titleIn, Assets.Title, 92, 12);
            else
            {
                // Game over: the title spaced wide over a crest, with a long divider under it.
                Core.Draw.SoftGlow(sb, titleCenter, new Vector2(700, 200), accent * 0.15f * titleIn);
                Core.Draw.Ornament(sb, titleCenter + new Vector2(0, -50), 22, Palette.Paper * 0.6f * titleIn);
                Core.Draw.SpacedCentered(sb, Assets.Title, title, titleCenter + new Vector2(0, (1 - titleIn) * 10), Palette.Paper * titleIn, 12);
                Core.Draw.Divider(sb, titleCenter + new Vector2(0, 34), 640 * titleIn, Palette.Paper * 0.55f);
                Core.Draw.Diamond(sb, titleCenter + new Vector2(0, 34), 7, Palette.Paper * 0.8f * titleIn);
            }
            Core.Draw.TextCentered(sb, Assets.Body, $"{_run.Area.Name}  -  {_run.Trial.Name}", new Vector2(Game1.Width / 2, 178), Palette.TextDim * titleIn, shadow: false);

            var box = new Rectangle(Game1.Width / 2 - 380, 216, 760, 364);
            Core.Draw.Panel(sb, box);
            Ui.SectionBar(sb, new Rectangle(box.X + 12, box.Y + 10, box.Width - 24, 24), _extracted ? "Secured" : "Lost", _extracted ? Palette.Rose : Palette.Danger);
            float y = box.Y + 46;
            int line = 0;
            void Line(string s, Color c)
            {
                float a = Ease.Progress(_age - 0.3f - line++ * 0.08f, 0.35f, EasingFunctions.CubicOut);
                RichText.Line(sb, Assets.Body, s, new Vector2(box.X + 40 - (1 - a) * 40, y), c, a);
                y += 30;
            }

            if (_extracted)
            {
                Line($"Shards secured:  +{_run.TrialShards}", Palette.Accent);
                Line($"Secured total:   {profile.SecuredShards}", Palette.Text);
                Line(_run.FoundEquipment.Count == 0 ? "Equipment secured: none" :
                    "Equipment secured: " + string.Join(", ", _run.FoundEquipment.Select(id => ItemDb.GetEquipment(id).Name)), Palette.Text);
                for (int i = 0; i < _run.FoundEquipment.Count; i++)
                    UI.Icons.Draw(sb, _run.FoundEquipment[i], new Rectangle(box.Right - 60 - i * 44, box.Y + 20, 36, 36));
                foreach (var id in _run.PendingRecruits)
                    Line($"{CharacterDb.Get(id).Name} joined the Sanctum!", Palette.Heal);
            }
            else
            {
                Line($"Shards lost:  {_run.TrialShards}", Palette.Danger);
                Line(_run.FoundEquipment.Count == 0 ? "Equipment lost: none" :
                    "Equipment lost: " + string.Join(", ", _run.FoundEquipment.Select(id => ItemDb.GetEquipment(id).Name)), Palette.Danger);
                foreach (var id in _run.PendingRecruits)
                    Line($"{CharacterDb.Get(id).Name} didn't make it out with you. Find her again.", Palette.Danger);
                Line("Upgrades, equipped gear, quests and unlocked areas are kept.", Palette.TextDim);
            }
            y += 10;
            Line($"Rooms cleared: {_run.RoomsCleared}    Rooms explored: {_run.Map.Rooms.Values.Count(r => r.Visited)} / {_run.Map.Rooms.Count}", Palette.Text);
            if (_run.Objective is { } o)
                Line(_run.ObjectiveCompleted
                        ? $"Objective complete: {o.Name}" + (o.Main ? $"  -  Main Objective '{_run.Area.ObjectiveName}' saved" : "")
                        : $"Objective not completed: {o.Name}" + (o.Count > 1 ? $"  ({_run.ObjectiveProgress} / {o.Count})" : ""),
                    _run.ObjectiveCompleted ? Palette.Heal : Palette.TextDim);
            y += 10;
            foreach (var m in _run.Party)
                Line($"   {m.Def.Name,-12} {(m.Fallen ? "fallen" : $"{m.Hp}/{m.MaxHp} HP")}", m.Fallen ? Palette.TextDisabled : Palette.Text);

            if (_age > 0.8f)
            {
                // One choice, drawn like a title-menu row: a line of light under it and the pointer beside it.
                const string label = "Return to the Sanctum";
                var center = new Vector2(Game1.Width / 2f, 612);
                var size = Assets.Body.MeasureString(label, 1.25f);
                Core.Draw.GlowLine(sb, center + new Vector2(0, 16), 520, Palette.Rose, 24);
                Core.Draw.TextSizedCentered(sb, Assets.Body, label, center, Palette.Paper, 1.25f);
                Core.Draw.Pointer(sb, new Vector2(center.X - size.X / 2 - 60, center.Y + 1), t);
                UI.Ui.Hit("continue", new Rectangle((int)(center.X - 260), (int)center.Y - 22, 520, 44));
                UI.Ui.Footer(sb, "continue", ("Enter", "Confirm"));
            }
        }
    }
}
