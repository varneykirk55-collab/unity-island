// Small CPU raster helpers for the map exporter: an RGBA canvas with anti-aliased lines/shapes and a text
// rasterizer that uses Unity's built-in dynamic font (no extra packages or font assets).
// Canvas coordinates use a TOP-LEFT origin; pixel (i, j) has its centre at (i + 0.5, j + 0.5) — same as MapProjection.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace IslandMap.Editor
{
    public sealed class MapCanvas
    {
        public readonly int W, H;
        public readonly Color32[] Px;

        public MapCanvas(int w, int h, Color fill)
        {
            W = w; H = h; Px = new Color32[w * h];
            Color32 f = fill;
            for (int i = 0; i < Px.Length; i++) Px[i] = f;
        }

        public void Blend(int x, int y, Color c, float a)
        {
            if ((uint)x >= (uint)W || (uint)y >= (uint)H) return;
            a *= c.a;
            if (a <= 0f) return;
            if (a > 1f) a = 1f;
            int i = y * W + x;
            var d = Px[i];
            Px[i] = new Color32((byte)(d.r + (c.r * 255f - d.r) * a + 0.5f), (byte)(d.g + (c.g * 255f - d.g) * a + 0.5f), (byte)(d.b + (c.b * 255f - d.b) * a + 0.5f), 255);
        }

        public void Paste(Color32[] src, int sw, int sh, int dx, int dy)
        {
            for (int y = 0; y < sh; y++)
            {
                int ty = dy + y;
                if ((uint)ty >= (uint)H) continue;
                for (int x = 0; x < sw; x++)
                {
                    int tx = dx + x;
                    if ((uint)tx < (uint)W) Px[ty * W + tx] = src[y * sw + x];
                }
            }
        }

        public void FillRect(float x0, float y0, float x1, float y1, Color c)
        {
            int ix0 = Mathf.Max(0, Mathf.RoundToInt(x0)), iy0 = Mathf.Max(0, Mathf.RoundToInt(y0));
            int ix1 = Mathf.Min(W, Mathf.RoundToInt(x1)), iy1 = Mathf.Min(H, Mathf.RoundToInt(y1));
            for (int y = iy0; y < iy1; y++) for (int x = ix0; x < ix1; x++) Blend(x, y, c, 1f);
        }

        /// <summary>Anti-aliased line segment with round caps.</summary>
        public void Line(Vector2 a, Vector2 b, Color c, float width)
        {
            float r = width * 0.5f;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - r - 1)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + r + 1));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - r - 1)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + r + 1));
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float t = len2 > 0 ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
                    float d = (p - (a + ab * t)).magnitude;
                    float cov = r + 0.5f - d;
                    if (cov > 0f) Blend(x, y, c, Mathf.Min(1f, cov));
                }
        }

        public void RectOutline(Vector2 min, Vector2 max, Color c, float width)
        {
            Line(new Vector2(min.x, min.y), new Vector2(max.x, min.y), c, width);
            Line(new Vector2(max.x, min.y), new Vector2(max.x, max.y), c, width);
            Line(new Vector2(max.x, max.y), new Vector2(min.x, max.y), c, width);
            Line(new Vector2(min.x, max.y), new Vector2(min.x, min.y), c, width);
        }

        public void Circle(Vector2 centre, float radius, Color c)
        {
            int x0 = Mathf.FloorToInt(centre.x - radius - 1), x1 = Mathf.CeilToInt(centre.x + radius + 1);
            int y0 = Mathf.FloorToInt(centre.y - radius - 1), y1 = Mathf.CeilToInt(centre.y + radius + 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float d = (new Vector2(x + 0.5f, y + 0.5f) - centre).magnitude;
                    float cov = radius + 0.5f - d;
                    if (cov > 0f) Blend(x, y, c, Mathf.Min(1f, cov));
                }
        }

        /// <summary>Filled polygon (even-odd), 4x4 supersampled edges.</summary>
        public void Polygon(IList<Vector2> pts, Color c)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in pts) { minX = Mathf.Min(minX, p.x); minY = Mathf.Min(minY, p.y); maxX = Mathf.Max(maxX, p.x); maxY = Mathf.Max(maxY, p.y); }
            for (int y = Mathf.FloorToInt(minY); y <= Mathf.CeilToInt(maxY); y++)
                for (int x = Mathf.FloorToInt(minX); x <= Mathf.CeilToInt(maxX); x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                            if (Inside(pts, new Vector2(x + (sx + 0.5f) / 4f, y + (sy + 0.5f) / 4f))) hits++;
                    if (hits > 0) Blend(x, y, c, hits / 16f);
                }
        }

        static bool Inside(IList<Vector2> p, Vector2 q)
        {
            bool inside = false;
            for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
                if ((p[i].y > q.y) != (p[j].y > q.y) && q.x < (p[j].x - p[i].x) * (q.y - p[i].y) / (p[j].y - p[i].y) + p[i].x)
                    inside = !inside;
            return inside;
        }

        public void SavePng(string path)
        {
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var flipped = new Color32[Px.Length];
            for (int y = 0; y < H; y++) Array.Copy(Px, y * W, flipped, (H - 1 - y) * W, W); // Texture2D row 0 = bottom
            tex.SetPixels32(flipped);
            tex.Apply(false);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }

    /// <summary>Rasterizes text with the built-in dynamic font (LegacyRuntime.ttf) at fixed pixel sizes.</summary>
    public sealed class MapFont
    {
        const string Chars = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~°×–·…";
        readonly Font _font;
        float[] _cov;
        int _aw, _ah;

        public MapFont(params int[] sizes)
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (!_font) throw new Exception("Built-in font LegacyRuntime.ttf not found");
            // Requesting glyphs can rebuild the atlas (invalidating earlier UVs); repeat until a pass causes no rebuild.
            for (int pass = 0; pass < 6; pass++)
            {
                bool rebuilt = false;
                void OnRebuilt(Font f) { if (f == _font) rebuilt = true; }
                Font.textureRebuilt += OnRebuilt;
                foreach (int s in sizes)
                {
                    _font.RequestCharactersInTexture(Chars, s, FontStyle.Normal);
                    _font.RequestCharactersInTexture(Chars, s, FontStyle.Bold);
                }
                Font.textureRebuilt -= OnRebuilt;
                if (!rebuilt && pass > 0) break;
            }
            var src = _font.material.mainTexture;
            _aw = src.width; _ah = src.height;
            var rt = RenderTexture.GetTemporary(_aw, _ah, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(_aw, _ah, TextureFormat.RGBA32, false, true);
            tex.ReadPixels(new Rect(0, 0, _aw, _ah), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = tex.GetPixels32();
            UnityEngine.Object.DestroyImmediate(tex);
            // Alpha8 atlases come back either in alpha or in rgb depending on the graphics API: pick the channel with signal.
            int aVar = 0, rVar = 0;
            foreach (var p in px) { if (p.a > 0 && p.a < 255) aVar++; if (p.r > 0 && p.r < 255) rVar++; }
            bool useAlpha = aVar >= rVar;
            _cov = new float[px.Length];
            for (int i = 0; i < px.Length; i++) _cov[i] = (useAlpha ? px[i].a : px[i].r) / 255f;
        }

        float Sample(Vector2 uv)
        {
            float fx = uv.x * _aw - 0.5f, fy = uv.y * _ah - 0.5f;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0, ty = fy - y0;
            float At(int x, int y) => (uint)x < (uint)_aw && (uint)y < (uint)_ah ? _cov[y * _aw + x] : 0f;
            return Mathf.Lerp(Mathf.Lerp(At(x0, y0), At(x0 + 1, y0), tx), Mathf.Lerp(At(x0, y0 + 1), At(x0 + 1, y0 + 1), tx), ty);
        }

        public float Measure(string s, int size, bool bold = false)
        {
            float w = 0;
            foreach (char ch in s)
                if (_font.GetCharacterInfo(ch, out var ci, size, bold ? FontStyle.Bold : FontStyle.Normal)) w += ci.advance;
                else if (_font.GetCharacterInfo(' ', out ci, size)) w += ci.advance;
            return w;
        }

        /// <summary>
        /// Draws text. pos is the anchor point; ax: 0 = left, 0.5 = centre, 1 = right; ay: 0 = top, 0.5 = middle,
        /// 1 = bottom of a box from cap height to baseline (descenders hang below). Returns the drawn box.
        /// </summary>
        public Rect Draw(MapCanvas c, string s, Vector2 pos, int size, Color col, float ax = 0, float ay = 0, bool bold = false, Color? halo = null, float haloRadius = 0)
        {
            var style = bold ? FontStyle.Bold : FontStyle.Normal;
            float capH = _font.GetCharacterInfo('H', out var hi, size, style) ? hi.maxY : size * 0.72f;
            int descent = Mathf.CeilToInt(size * 0.25f);
            int pad = Mathf.CeilToInt(haloRadius) + 2;
            float width = Measure(s, size, bold);
            int mw = Mathf.CeilToInt(width) + pad * 2;
            int top = Mathf.CeilToInt(size * 1.0f); // room above baseline
            int mh = top + descent + pad * 2;
            var mask = new float[mw * mh];
            float pen = pad;
            int baseRow = pad + top; // mask row of the baseline
            foreach (char ch in s)
            {
                if (!_font.GetCharacterInfo(ch, out var ci, size, style)) { if (_font.GetCharacterInfo(' ', out ci, size)) pen += ci.advance; continue; }
                int gw = ci.maxX - ci.minX, gh = ci.maxY - ci.minY;
                for (int gy = 0; gy < gh; gy++)
                    for (int gx = 0; gx < gw; gx++)
                    {
                        float u = (gx + 0.5f) / gw, v = (gy + 0.5f) / gh;
                        var uv = Vector2.Lerp(Vector2.Lerp(ci.uvBottomLeft, ci.uvBottomRight, u), Vector2.Lerp(ci.uvTopLeft, ci.uvTopRight, u), v);
                        float a = Sample(uv);
                        int mx = Mathf.RoundToInt(pen) + ci.minX + gx;
                        int my = baseRow - (ci.minY + gy) - 1;
                        if ((uint)mx < (uint)mw && (uint)my < (uint)mh) mask[my * mw + mx] = Mathf.Max(mask[my * mw + mx], a);
                    }
                pen += ci.advance;
            }

            // Position: box spans [baseline - capH, baseline] vertically and [0, width] horizontally.
            float originX = pos.x - width * ax - pad;
            float baselineY = pos.y - capH * ay + capH;
            int ox = Mathf.RoundToInt(originX), oy = Mathf.RoundToInt(baselineY) - baseRow;

            if (halo.HasValue && haloRadius > 0)
            {
                int r = Mathf.CeilToInt(haloRadius);
                var h = new float[mask.Length];
                for (int y = 0; y < mh; y++)
                    for (int x = 0; x < mw; x++)
                    {
                        float m = 0;
                        for (int dy = -r; dy <= r && m < 1f; dy++)
                            for (int dx = -r; dx <= r; dx++)
                            {
                                int sx = x + dx, sy = y + dy;
                                if ((uint)sx >= (uint)mw || (uint)sy >= (uint)mh) continue;
                                float fall = Mathf.Clamp01(haloRadius + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                                m = Mathf.Max(m, mask[sy * mw + sx] * fall);
                            }
                        h[y * mw + x] = m;
                    }
                for (int y = 0; y < mh; y++) for (int x = 0; x < mw; x++) if (h[y * mw + x] > 0) c.Blend(ox + x, oy + y, halo.Value, h[y * mw + x]);
            }
            for (int y = 0; y < mh; y++) for (int x = 0; x < mw; x++) if (mask[y * mw + x] > 0) c.Blend(ox + x, oy + y, col, mask[y * mw + x]);
            return new Rect(originX + pad, baselineY - capH, width, capH);
        }
    }
}
