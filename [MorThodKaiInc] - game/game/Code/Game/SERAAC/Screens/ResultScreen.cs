using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SERAAC.Core;
using SERAAC.Data;
using SERAAC.Trial;

namespace SERAAC.Screens
{
    // End of a Trial: what was secured (extraction) or what was lost (defeat / abandon).
    public sealed class ResultScreen : Screen
    {
        private readonly TrialRun _run;
        private readonly bool _extracted;
        private readonly bool _abandoned;
        private float _age;

        public ResultScreen(TrialRun run, bool extracted, bool abandoned = false)
        {
            _run = run;
            _extracted = extracted;
            _abandoned = abandoned;
        }

        public override void Update(GameTime gameTime)
        {
            _age += (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_age > 0.8f && (Input.Confirm || Input.Cancel))
                Screens.Replace(new HubScreen());
        }

        public override void Draw(SpriteBatch sb, GameTime gameTime)
        {
            var profile = Game1.Instance.Profile;
            var accent = _extracted ? Palette.Accent : Palette.Danger;
            string title = _extracted ? "EXTRACTED" : _abandoned ? "TRIAL ABANDONED" : "THE PARTY HAS FALLEN";
            Core.Draw.TextCentered(sb, Assets.Title, title, new Vector2(Game1.Width / 2, 120), accent);
            Core.Draw.TextCentered(sb, Assets.Body, $"{_run.Area.Name}  -  {_run.Trial.Name}", new Vector2(Game1.Width / 2, 170), Palette.TextDim);

            var box = new Rectangle(Game1.Width / 2 - 380, 220, 760, 340);
            Core.Draw.Panel(sb, box, border: accent);
            float y = box.Y + 24;
            void Line(string s, Color c)
            {
                Core.Draw.Text(sb, Assets.Body, s, new Vector2(box.X + 30, y), c);
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
            if (_run.Trial.HasMainObjective)
                Line(_run.ObjectiveCompleted ? $"Main Objective complete: {_run.Area.ObjectiveName}  (saved)" : "Main Objective not completed",
                    _run.ObjectiveCompleted ? Palette.Heal : Palette.TextDim);
            y += 10;
            foreach (var m in _run.Party)
                Line($"   {m.Def.Name,-12} {(m.Fallen ? "fallen" : $"{m.Hp}/{m.MaxHp} HP")}", m.Fallen ? Palette.TextDisabled : Palette.Text);

            if (_age > 0.8f)
                Core.Draw.TextCentered(sb, Assets.Body, "Enter - return to the Sanctum", new Vector2(Game1.Width / 2, 620), Palette.TextDim);
        }
    }
}
