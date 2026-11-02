using System;
using System.Collections.Generic;
using UnityEngine;

namespace DemandHighlight
{
    // Builds the outline textures, cached by look: an item is drawn once per change, never per frame.
    // - Rectangular items (almost all of them) share one small nine-slice frame per look: fixed corners, edges and
    //   centre stretched (or tiled, for the striped look), so any size costs nothing to draw.
    // - Other shapes get a texture that follows their grid cells (L-shapes get an L outline), drawn 1:1 with the
    //   UI so lines stay crisp, cached by shape and size.
    //
    // Looks, white in the texture and tinted by the Image colour:
    //   wanted      solid line and a faint fill
    //   stolen      diagonal "caution tape" line and a faint fill
    //   contraband  heavy line, a thin inner line and a stronger fill
    //   contents    corner brackets only: something inside this container is wanted
    public static class Sprites
    {
        const int MaxSide = 512;
        const int CacheCap = 384;
        const int Inf = 1 << 20;

        public struct Shape
        {
            public int W, H;          // cells
            public bool[] Cells;      // row 0 is the top row, as GridShape.GetLocal reads
            public long Hash;
        }

        struct Key : IEquatable<Key>
        {
            public long Shape;
            public int W, H, Look, Thickness, Corner;
            public bool Fill;
            public bool Equals(Key o)
            {
                return Shape == o.Shape && W == o.W && H == o.H && Look == o.Look && Thickness == o.Thickness &&
                    Corner == o.Corner && Fill == o.Fill;
            }
            public override bool Equals(object o) { return o is Key && Equals((Key)o); }
            public override int GetHashCode()
            {
                unchecked
                {
                    int h = (int)Shape ^ (int)(Shape >> 32);
                    h = h * 31 + W; h = h * 31 + H; h = h * 31 + Look; h = h * 31 + Thickness;
                    h = h * 31 + Corner; return h * 2 + (Fill ? 1 : 0);
                }
            }
        }

        static readonly Dictionary<Key, Sprite> cache = new Dictionary<Key, Sprite>();
        public static int Generation { get; private set; }

        // Scratch buffers, reused between bakes.
        static bool[] inside, rounded, core;
        static int[] dist;
        static Color32[] pixels;

        public static readonly Shape Full = MakeFull();

        static Shape MakeFull()
        {
            return new Shape { W = 1, H = 1, Cells = new[] { true }, Hash = 1 };
        }

        public static Shape Read(GridShape shape)
        {
            int w = shape.width, h = shape.height;
            if (w <= 0 || h <= 0 || w > 64 || h > 64) return Full;
            var cells = new bool[w * h];
            long hash = 17;
            bool full = true;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool on = shape.GetLocal(x, y) != 0;
                    cells[y * w + x] = on;
                    full &= on;
                    hash = hash * 1099511628211L ^ (on ? 2 : 1);
                }
            if (full) return Full;
            hash = hash * 31 + w; hash = hash * 31 + h;
            return new Shape { W = w, H = h, Cells = cells, Hash = hash };
        }

        public static void Clear()
        {
            foreach (var s in cache.Values)
                if (s != null) { UnityEngine.Object.Destroy(s.texture); UnityEngine.Object.Destroy(s); }
            cache.Clear();
            Generation++;
        }

        public static Sprite Get(Shape shape, float width, float height, Style style, int thickness, int corner, bool fill)
        {
            int w = Mathf.Clamp(Mathf.RoundToInt(width), 2, MaxSide);
            int h = Mathf.Clamp(Mathf.RoundToInt(height), 2, MaxSide);
            var key = new Key { Shape = shape.Hash, W = w, H = h, Look = LookOf(style), Thickness = thickness,
                Corner = corner, Fill = fill && !style.Contents };
            Sprite sprite;
            if (cache.TryGetValue(key, out sprite) && sprite != null) return sprite;
            if (cache.Count >= CacheCap) Clear();
            sprite = Bake(shape, w, h, style, thickness, corner, key.Fill, 0f, Vector4.zero);
            cache[key] = sprite;
            return sprite;
        }

        // Width of the fixed border of a nine-slice frame: the line (the heavy one has an inner line too), the
        // rounded corner and the corner brackets all fit inside it.
        public static int FrameBorder(int thickness, int corner) { return 3 * thickness + 4 + corner; }

        // The nine-slice frame for rectangular items and tabs. Its centre is one stripe period long, so the striped
        // look tiles without a seam.
        public static Sprite Frame(Style style, int thickness, int corner, bool fill)
        {
            int border = FrameBorder(thickness, corner);
            int size = 2 * border + 2 * (2 * thickness + 2);
            var key = new Key { Shape = -1, W = size, H = size, Look = LookOf(style), Thickness = thickness,
                Corner = corner, Fill = fill && !style.Contents };
            Sprite sprite;
            if (cache.TryGetValue(key, out sprite) && sprite != null) return sprite;
            if (cache.Count >= CacheCap) Clear();
            sprite = Bake(Full, size, size, style, thickness, corner, key.Fill, 3 * thickness + 4,
                new Vector4(border, border, border, border));
            cache[key] = sprite;
            return sprite;
        }

        static int LookOf(Style style) { return style.Contents ? 0 : (int)style.Mark; }

        // reach: how far corner brackets run (0 = from the cell size). border: nine-slice border of the sprite.
        static Sprite Bake(Shape shape, int w, int h, Style style, int t, int r, bool fill, float reach, Vector4 border)
        {
            int n = w * h;
            if (inside == null || inside.Length < n)
            {
                inside = new bool[n]; rounded = new bool[n]; core = new bool[n];
                dist = new int[n]; pixels = new Color32[n];
            }

            // Texture rows go bottom-up; shape rows go top-down.
            for (int y = 0; y < h; y++)
            {
                int cy = Math.Min(shape.H - 1, (h - 1 - y) * shape.H / h);
                for (int x = 0; x < w; x++)
                {
                    int cx = Math.Min(shape.W - 1, x * shape.W / w);
                    inside[y * w + x] = shape.Cells[cy * shape.W + cx];
                }
            }

            // Round the outer corners: shrink by r, then grow back by r.
            bool[] solid = inside;
            if (r > 0)
            {
                Distance(inside, w, h, true);
                for (int i = 0; i < n; i++) core[i] = dist[i] > 3 * r;
                Distance(core, w, h, false);
                for (int i = 0; i < n; i++) rounded[i] = inside[i] && dist[i] <= 3 * r + 1;
                solid = rounded;
            }
            Distance(solid, w, h, true);

            byte fillA = (byte)(!fill ? 0 : style.Mark == Mark.Contraband ? 44 : 30);
            List<Vector2> corners = null;
            if (style.Contents)
            {
                corners = Corners(shape, w, h);
                if (reach <= 0f) reach = Mathf.Max(3 * t + 3, Mathf.Min((float)w / shape.W, (float)h / shape.H) * 0.3f);
            }
            int band = 3 * t;
            int stripe = 2 * t + 2;

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    byte a = 0;
                    if (solid[i])
                    {
                        int d = dist[i];
                        if (style.Contents)
                            a = d <= band && NearCorner(corners, x + 0.5f, y + 0.5f, reach) ? (byte)255 : (byte)0;
                        else if (style.Mark == Mark.Stolen)
                            a = d <= band ? (((x + y) / stripe) % 2 == 0 ? (byte)255 : (byte)80) : fillA;
                        else if (style.Mark == Mark.Contraband)
                            a = d <= band + 3 || (d > 3 * (2 * t + 2) && d <= 3 * (2 * t + 3)) ? (byte)255 : fillA;
                        else
                            a = d <= band ? (byte)255 : fillA;
                    }
                    pixels[i] = new Color32(255, 255, 255, a);
                }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.HideAndDontSave;
            if (pixels.Length == n) tex.SetPixels32(pixels);
            else { var exact = new Color32[n]; Array.Copy(pixels, exact, n); tex.SetPixels32(exact); }
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                border);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        // Chamfer (3-4) distance, in thirds of a pixel. towardOutside: distance from each set pixel to the nearest
        // unset one, with everything beyond the texture edge unset. Otherwise: distance from each pixel to the
        // nearest set one, with nothing set beyond the edge.
        static void Distance(bool[] set, int w, int h, bool towardOutside)
        {
            int edge = towardOutside ? 0 : Inf;
            for (int i = 0; i < w * h; i++) dist[i] = set[i] == towardOutside ? Inf : 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x, d = dist[i];
                    if (d == 0) continue;
                    d = Math.Min(d, (x > 0 ? dist[i - 1] : edge) + 3);
                    d = Math.Min(d, (y > 0 ? dist[i - w] : edge) + 3);
                    d = Math.Min(d, (x > 0 && y > 0 ? dist[i - w - 1] : edge) + 4);
                    d = Math.Min(d, (x < w - 1 && y > 0 ? dist[i - w + 1] : edge) + 4);
                    dist[i] = d;
                }
            for (int y = h - 1; y >= 0; y--)
                for (int x = w - 1; x >= 0; x--)
                {
                    int i = y * w + x, d = dist[i];
                    if (d == 0) continue;
                    d = Math.Min(d, (x < w - 1 ? dist[i + 1] : edge) + 3);
                    d = Math.Min(d, (y < h - 1 ? dist[i + w] : edge) + 3);
                    d = Math.Min(d, (x < w - 1 && y < h - 1 ? dist[i + w + 1] : edge) + 4);
                    d = Math.Min(d, (x > 0 && y < h - 1 ? dist[i + w - 1] : edge) + 4);
                    dist[i] = d;
                }
        }

        // Outer (convex) corners of the cell outline, in texture pixels.
        static List<Vector2> Corners(Shape shape, int w, int h)
        {
            var list = new List<Vector2>();
            for (int j = 0; j <= shape.H; j++)
                for (int i = 0; i <= shape.W; i++)
                {
                    int count = Cell(shape, i - 1, j - 1) + Cell(shape, i, j - 1) + Cell(shape, i - 1, j) + Cell(shape, i, j);
                    if (count == 1) list.Add(new Vector2((float)i * w / shape.W, h - (float)j * h / shape.H));
                }
            return list;
        }

        static int Cell(Shape shape, int x, int y)
        {
            return x >= 0 && y >= 0 && x < shape.W && y < shape.H && shape.Cells[y * shape.W + x] ? 1 : 0;
        }

        static bool NearCorner(List<Vector2> corners, float x, float y, float reach)
        {
            for (int k = 0; k < corners.Count; k++)
                if (Math.Abs(corners[k].x - x) <= reach && Math.Abs(corners[k].y - y) <= reach) return true;
            return false;
        }
    }
}
