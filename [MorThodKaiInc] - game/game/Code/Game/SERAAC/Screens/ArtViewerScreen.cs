using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SERAAC.Core;
using SERAAC.Data;
using SERAAC.UI;

namespace SERAAC.Screens
{
    // Dev tool for artists: shows every pose of every sheet standing on a shared ground line,
    // so boxes/foot points in Art/Sheets/*.json can be checked without touching code.
    public sealed class ArtViewerScreen : Screen
    {
        private readonly List<string> _pages = new();
        private int _page;
        private const string MonsterPage = "(monster recolours)";
        private const string IconPage = "(item icons)";

        public override void OnEnter()
        {
            _pages.AddRange(Sprites.SheetIds.OrderBy(s => s));
            _pages.Add(MonsterPage);
            _pages.Add(IconPage);
        }

        public override void Update(GameTime gameTime)
        {
            if (Input.Left) _page = (_page + _pages.Count - 1) % _pages.Count;
            if (Input.Right) _page = (_page + 1) % _pages.Count;
            if (Input.Cancel) Screens.Replace(new MainMenuScreen());
        }

        public override void Draw(SpriteBatch sb, GameTime gameTime)
        {
            double t = gameTime.TotalGameTime.TotalSeconds;
            string page = _pages.Count > 0 ? _pages[_page] : "(no sheets found in Art/Sheets)";
            Core.Draw.Text(sb, Assets.Header, "ART VIEWER  -  " + page, new Vector2(30, 20), Palette.Accent);
            Core.Draw.Text(sb, Assets.Small, "Left/Right page   Esc back      Red line = ground, cyan dot = foot point. Every pose is drawn at the same scale.",
                new Vector2(30, 52), Palette.TextDim);
            if (_pages.Count == 0) return;

            const float ground = 560;
            Core.Draw.Rect(sb, 0, ground, Game1.Width, 2, Palette.Danger * 0.7f);

            if (page == MonsterPage)
            {
                var enemies = EnemyDb.All;
                float x = 70;
                foreach (var e in enemies)
                {
                    float h = 110 * Math.Min(e.Scale, 1.4f);
                    Figures.Unit(sb, e.ArtId, null, new Rectangle((int)(x - h * 0.35f), (int)(ground - h), (int)(h * 0.7f), (int)h), e.Color, true, t,
                        fallbackSprite: EnemyDb.MockupSprite);
                    Core.Draw.TextCentered(sb, Assets.Small, e.Name, new Vector2(x, ground + 20 + (enemies.IndexOf(e) % 2) * 16), Palette.Text);
                    x += 114;
                }
                return;
            }

            if (page == IconPage)
            {
                int i = 0;
                foreach (var id in Icons.AllIds)
                {
                    var r = new Rectangle(60 + (i % 8) * 145, 120 + (i / 8) * 150, 72, 72);
                    Core.Draw.Rect(sb, r, Palette.Panel);
                    Icons.Draw(sb, id, r);
                    Core.Draw.Text(sb, Assets.Small, id, new Vector2(r.X, r.Bottom + 6), Palette.Text);
                    i++;
                }
                return;
            }

            var set = Sprites.Get(page);
            if (set == null)
            {
                Core.Draw.Text(sb, Assets.Body, "Failed to load. Check the console output for the reason.", new Vector2(30, 100), Palette.Danger);
                return;
            }

            float px = 60;
            float widthPerRefPixel = set.Frames.Values.Sum(f => f.W / set.ReferenceHeight);
            float refH = Math.Min(220, (Game1.Width - 120 - 30 * set.Frames.Count) / widthPerRefPixel);
            foreach (var (name, frame) in set.Frames)
            {
                float w = frame.W * (refH / set.ReferenceHeight);
                float footX = px + frame.Pivot.X * (refH / set.ReferenceHeight);
                Sprites.Draw(sb, set, name, new Vector2(footX, ground), refH, Color.White, false);
                Core.Draw.Rect(sb, footX - 3, ground - 3, 6, 6, Color.Cyan);
                Core.Draw.TextCentered(sb, Assets.Small, name + (name == set.Reference ? " (ref)" : ""), new Vector2(footX, ground + 24), Palette.Text);
                Core.Draw.TextCentered(sb, Assets.Small, $"{frame.W}x{frame.H}", new Vector2(footX, ground + 42), Palette.TextDim);
                px += Math.Max(w, 60) + 30;
            }
        }
    }
}
