using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SERAAC.Core;

namespace SERAAC.UI
{
    // Body text with game terms highlighted: statuses, damage, healing, SP, positions... each in its keyword
    // colour (Palette.Kw*), so "Blight 2 dmg" or "Acts first" stands out in a description without any markup.
    // The rules below are matched in order; the first rule to claim a character colours it.
    public static class RichText
    {
        private static readonly (Regex re, Color color)[] Rules =
        {
            (new Regex(@"\bPay \d+ HP\b"), Palette.Danger),
            (new Regex(@"\bActs first\b|\b[Ii]nterrupt(s|ed)?( a wind-up)?\b|\bwind-ups?\b|\bwinds? up\b|\bRears? [Uu]p\b|\bnext turn\b"), Palette.KwPriority),
            (new Regex(@"\bBlight(ed|s)?\b( \(?\d+ (dmg|damage)\)?)?"), Palette.KwBlight),
            (new Regex(@"\bMark(ed|s)?\b"), Palette.KwMark),
            (new Regex(@"\bSlow(ed|s)?\b|[-+]\d+ Speed\b"), Palette.KwSlow),
            (new Regex(@"\+?\d+ Guard\b|\bGuard(ed|s)?\b|\bAegis Wall\b|\bAegis\b|\bProtected\b|\bintercept(s|ed)?\b|\bstrikes? back\b|\bRiposte\b"), Palette.KwGuard),
            (new Regex(@"\+?\d+ Thread\b|\bThread\b|\bWoven\b|\+?\d+ Feast\b|\bFeast\b|\bcrit(s|ical)?( hit)?\b|\bENRAGED?\b|\benrages?\b"), Palette.KwStack),
            (new Regex(@"\+\d+ HP\b|\bheal(s|ed|ing)?\b|\bHeal(s|ing)?\b|\blifesteal\b|\brestores?\b|\bcleanse(s|d)?\b"), Palette.KwHeal),
            (new Regex(@"(?<![\d.x])\d+(-\d+)? ?(dmg|DMG|damage)\b|\bdamage\b"), Palette.KwDamage),
            (new Regex(@"\+?\d+ SP\b|\bSP\b|\bEthergy\b"), Palette.KwSp),
            (new Regex(@"\b(Front|Back) [12]( / (Front|Back) [12])?\b|\b(front|back) (row|line)\b|\bfront\b"), Palette.KwPosition),
            (new Regex(@"\bpush(es|ed)?\b|\bknock(s|ed)? back\b|\bpull(s|ed)?\b|\bdrag(s|ged)?\b|\bswap(s|ped)?\b|\bscrambled?\b"), Palette.KwMove),
            (new Regex(@"[+\-x]?\d+(\.\d+)?%|\bx\d+(\.\d+)?\b|\b\d+ turns?\b"), Palette.KwNumber),
        };

        // Colour of every character of s (base colour where no rule matches).
        public static Color[] Colorize(string s, Color baseColor)
        {
            var colors = new Color[s.Length];
            var claimed = new bool[s.Length];
            for (int i = 0; i < s.Length; i++) colors[i] = baseColor;
            foreach (var (re, color) in Rules)
                foreach (Match m in re.Matches(s))
                {
                    bool free = true;
                    for (int i = m.Index; i < m.Index + m.Length; i++) free &= !claimed[i];
                    if (!free) continue;
                    for (int i = m.Index; i < m.Index + m.Length; i++) { colors[i] = color; claimed[i] = true; }
                }
            return colors;
        }

        // One line, no wrapping. Returns its width.
        public static float Line(SpriteBatch sb, UiFont f, string s, Vector2 pos, Color baseColor, float alpha = 1f, bool shadow = false)
        {
            var colors = Colorize(s, baseColor);
            return DrawRuns(sb, f, s, 0, s.Length, colors, pos, alpha, shadow);
        }

        // Word-wrapped into maxWidth, keyword-coloured. Returns the height used (no drawing when sb is null).
        public static float Wrapped(SpriteBatch sb, UiFont f, string s, Vector2 pos, float maxWidth, Color baseColor,
                                    float alpha = 1f, float lineGap = 0f, bool shadow = false)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            var colors = Colorize(s, baseColor);
            float lineH = f.LineSpacing + lineGap, y = pos.Y;
            int paraStart = 0;
            while (paraStart <= s.Length)
            {
                int paraEnd = s.IndexOf('\n', paraStart);
                if (paraEnd < 0) paraEnd = s.Length;

                // Words of this paragraph as [start, end) ranges into s, so colours stay aligned.
                var words = new List<(int start, int end)>();
                for (int k = paraStart; k < paraEnd;)
                {
                    while (k < paraEnd && s[k] == ' ') k++;
                    int w0 = k;
                    while (k < paraEnd && s[k] != ' ') k++;
                    if (k > w0) words.Add((w0, k));
                }

                // Greedy lines: as many whole words as fit (one over-long word still gets its own line).
                int wi = 0;
                do
                {
                    int first = wi, last = wi;
                    while (last + 1 < words.Count
                           && f.MeasureString(s.Substring(words[first].start, words[last + 1].end - words[first].start)).X <= maxWidth)
                        last++;
                    if (sb != null && words.Count > 0)
                        DrawRuns(sb, f, s, words[first].start, words[last].end, colors, new Vector2(pos.X, y), alpha, shadow);
                    y += lineH;
                    wi = last + 1;
                } while (wi < words.Count);

                paraStart = paraEnd + 1;
            }
            return y - pos.Y;
        }

        public static float Measure(UiFont f, string s, float maxWidth, float lineGap = 0f) => Wrapped(null, f, s, Vector2.Zero, maxWidth, Color.White, lineGap: lineGap);

        // Draws s[start..end) as runs of one colour each.
        private static float DrawRuns(SpriteBatch sb, UiFont f, string s, int start, int end, Color[] colors, Vector2 pos, float alpha, bool shadow)
        {
            float x = pos.X;
            int k = start;
            while (k < end)
            {
                int runEnd = k + 1;
                while (runEnd < end && colors[runEnd] == colors[k]) runEnd++;
                string run = s.Substring(k, runEnd - k);
                // Measure up to the end of the run from the line start, so kerning across runs stays right.
                Core.Draw.Text(sb, f, run, new Vector2(x, pos.Y), colors[k] * alpha, shadow);
                x = pos.X + f.MeasureString(s.Substring(start, runEnd - start)).X;
                k = runEnd;
            }
            return x - pos.X;
        }
    }

    // A floating explanation box (statuses, passives, enemy types...): title + keyword-coloured body,
    // kept on screen and placed above the anchor (or below it when there's no room).
    public static class Tooltip
    {
        public const int Width = 320;

        public static Rectangle Draw(SpriteBatch sb, Rectangle anchor, string title, string body, Color accent, string tag = null, int width = Width)
        {
            float bodyH = RichText.Measure(Assets.Small, body ?? "", width - 24, 2);
            int h = (int)(40 + bodyH + 10);
            int x = (int)MathHelper.Clamp(anchor.Center.X - width / 2f, 8, Game1.Width - width - 8);
            int y = anchor.Top - h - 8;
            if (y < 8) y = anchor.Bottom + 8;
            if (y + h > Game1.Height - 8) y = Game1.Height - 8 - h;
            var r = new Rectangle(x, y, width, h);
            Core.Draw.Slant(sb, new Rectangle(r.X + 4, r.Y + 4, r.Width, r.Height), 10, Palette.Ink * 0.5f);
            Core.Draw.Slant(sb, r, 10, Color.Lerp(Palette.Ink, Palette.Grey900, 0.6f) * 0.97f);
            Core.Draw.Slant(sb, new Rectangle(r.X + 6, r.Y, r.Width - 12, 4), 4, accent);
            Core.Draw.Text(sb, Assets.Header, title.ToUpperInvariant(), new Vector2(r.X + 16, r.Y + 8), accent, shadow: false);
            if (tag != null)
                Core.Draw.TextRight(sb, Assets.Small, tag.ToUpperInvariant(), new Vector2(r.Right - 18, r.Y + 13), Palette.TextDim, shadow: false);
            RichText.Wrapped(sb, Assets.Small, body ?? "", new Vector2(r.X + 14, r.Y + 38), width - 24, Palette.Text, lineGap: 2);
            return r;
        }
    }
}
