using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System;
using SERAAC.Data.Party;

namespace SERAAC.UI
{
    public class BattleUI
    {
        private SpriteFont _header;
        private SpriteFont _body;
        private Texture2D _white;
        private Texture2D _portraitTex;
        private Texture2D _weaponTex;
        private int _portraitFrameSize = 100;

        public BattleUI(SpriteFont header, SpriteFont body, Texture2D white, Texture2D portrait = null, Texture2D weapon = null, int portraitFrameSize = 100)
        {
            _header = header;
            _body = body;
            _white = white;
            _portraitTex = portrait;
            _weaponTex = weapon;
            _portraitFrameSize = portraitFrameSize;
        }

        // bondStartY: vertical position where bond rows should start (prevents overlap with mini-map)
        public void DrawBattle(SpriteBatch sb, int viewportWidth, int viewportHeight, Party party, List<SERAAC.Data.Enemies.Enemy> enemies, int selectedEnemyIndex, int selX, int selY, int playerFacing = 0, int bondStartY = 0)
        {
            int leftW = viewportWidth / 2;
            // draw bond rows on left side
            var font = _body ?? _header;
            if (font == null) return;

            int rowH = 56;
            int startY = bondStartY > 0 ? bondStartY : 100;
            int x = 40;
            for (int i = 0; i < party.Bonds.Count; i++)
            {
                var b = party.Bonds[i];
                var rowRect = new Rectangle(x - 8, startY + i * (rowH + 8) - 8, leftW - 80, rowH + 8);
                sb.Draw(_white, rowRect, Color.Black * 0.6f);

                // portrait (use sprite sheet frame if available)
                var pRect = new Rectangle(x, startY + i * (rowH + 8), _portraitFrameSize, _portraitFrameSize);
                if (_portraitTex != null)
                {
                    var frame = Math.Max(0, Math.Min(3, playerFacing));
                    var src = new Rectangle(frame * _portraitFrameSize, 0, _portraitFrameSize, _portraitFrameSize);
                    sb.Draw(_portraitTex, pRect, src, Color.White);
                }
                else
                {
                    sb.Draw(_white, pRect, Color.Gray * 0.9f);
                }

                // Name
                sb.DrawString(font, b.Wielder?.Name ?? "-", new Vector2(x + 56, startY + i * (rowH + 8)), Color.White);

                // HP bar
                var hpX = x + 200;
                var hpY = startY + i * (rowH + 8) + 8;
                var hpW = 160;
                var hpRectBg = new Rectangle(hpX, hpY, hpW, 12);
                sb.Draw(_white, hpRectBg, Color.DarkRed * 0.9f);
                float hpPct = 0f;
                if (b.Wielder != null && b.Wielder.MaxHp > 0) hpPct = (float)b.Wielder.Hp / b.Wielder.MaxHp;
                var hpRect = new Rectangle(hpX, hpY, (int)(hpW * hpPct), 12);
                sb.Draw(_white, hpRect, Color.Red);
                sb.DrawString(font, $"{b.Wielder?.Hp}/{b.Wielder?.MaxHp}", new Vector2(hpX + hpW + 8, hpY - 2), Color.White);

                // Sync bar
                var syncX = hpX;
                var syncY = hpY + 18;
                var syncW = hpW;
                sb.Draw(_white, new Rectangle(syncX, syncY, syncW, 8), Color.DarkGray * 0.9f);
                var syncPct = b.SyncMeter / 100f;
                sb.Draw(_white, new Rectangle(syncX, syncY, (int)(syncW * syncPct), 8), Color.LightBlue);
            }

            // draw enemies on right half as a simple list with selection
            int rightX = leftW + 40;
            int enemyY = 575;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                var er = new Rectangle(rightX, enemyY + i * 72, 160, 64);
                var col = i == selectedEnemyIndex ? Color.Orange : Color.DarkSlateGray;
                sb.Draw(_white, er, col * 0.9f);
                sb.DrawString(font, e.Name ?? "Enemy", new Vector2(er.X + 8, er.Y + 8), Color.White);
                // hp bar
                var ehpX = er.X + 8;
                var ehpY = er.Y + 32;
                var ehpW = 120;
                sb.Draw(_white, new Rectangle(ehpX, ehpY, ehpW, 8), Color.DarkRed * 0.9f);
                var epct = e.MaxHp > 0 ? (float)e.Hp / e.MaxHp : 0f;
                sb.Draw(_white, new Rectangle(ehpX, ehpY, (int)(ehpW * epct), 8), Color.Red);
            }

            // draw weapon placeholder on right side (scaled down)
            if (_weaponTex != null)
            {
                var wt = _weaponTex.Width;
                var ht = _weaponTex.Height;
                // scale to fit into a 200x200 box while preserving aspect
                float maxSize = 200f;
                float scale = Math.Min(maxSize / wt, maxSize / ht);
                var dest = new Rectangle(leftW + (viewportWidth - leftW) / 2 - (int)(wt * scale / 2), startY + 220, (int)(wt * scale), (int)(ht * scale));
                sb.Draw(_weaponTex, dest, Color.White);
            }
        }
    }
}
