// Minimal 3x5 bitmap font (digits, '-', '.', and the few letters the developer map needs),
// so the generator has no dependency on fonts, System.Drawing or the Unity UI.
using System.Collections.Generic;
using UnityEngine;

namespace IslandTools.Mapping.Editor
{
    static class IslandMapFont
    {
        // Each glyph: 5 rows of 3 bits, top row first, MSB = left pixel.
        static readonly Dictionary<char, int[]> G = new Dictionary<char, int[]>
        {
            {'0', new[]{7,5,5,5,7}}, {'1', new[]{2,6,2,2,7}}, {'2', new[]{7,1,7,4,7}}, {'3', new[]{7,1,7,1,7}},
            {'4', new[]{5,5,7,1,1}}, {'5', new[]{7,4,7,1,7}}, {'6', new[]{7,4,7,5,7}}, {'7', new[]{7,1,1,1,1}},
            {'8', new[]{7,5,7,5,7}}, {'9', new[]{7,5,7,1,7}}, {'-', new[]{0,0,7,0,0}}, {'.', new[]{0,0,0,0,2}},
            {'N', new[]{5,7,7,7,5}}, {'X', new[]{5,5,2,5,5}}, {'Z', new[]{7,1,2,4,7}}, {'m', new[]{0,0,7,7,5}},
            {'k', new[]{5,5,6,5,5}}, {' ', new[]{0,0,0,0,0}},
        };

        public static int Width(string s, int scale) => s.Length * 4 * scale - scale;
        public static int Height(int scale) => 5 * scale;

        public static void Draw(Color32[] px, int w, int h, int x, int y, string s, int scale, Color32 fg, bool halo, Color32 haloColor)
        {
            // (x,y) = top-left in image coordinates (y down).
            if (halo)
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    if (dx != 0 || dy != 0) DrawPass(px, w, h, x + dx, y + dy, s, scale, haloColor);
            DrawPass(px, w, h, x, y, s, scale, fg);
        }

        static void DrawPass(Color32[] px, int w, int h, int x, int y, string s, int scale, Color32 c)
        {
            int cx = x;
            foreach (char ch in s)
            {
                if (G.TryGetValue(ch, out var rows))
                    for (int r = 0; r < 5; r++)
                        for (int col = 0; col < 3; col++)
                            if ((rows[r] & (4 >> col)) != 0)
                                for (int sy = 0; sy < scale; sy++) for (int sx = 0; sx < scale; sx++)
                                {
                                    int ix = cx + col * scale + sx, iy = y + r * scale + sy;
                                    if (ix >= 0 && ix < w && iy >= 0 && iy < h) px[(h - 1 - iy) * w + ix] = c; // image y-down -> texture y-up
                                }
                cx += 4 * scale;
            }
        }
    }
}
