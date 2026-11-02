using System;
using System.Collections.Generic;

namespace DemandHighlight
{
    // An item's grid shape: which cells it covers.
    public struct Shape
    {
        public int W, H;          // cells
        public bool[] Cells;      // row 0 is the top row, as GridShape.GetLocal reads
        public long Hash;

        public static readonly Shape Full = new Shape { W = 1, H = 1, Cells = new[] { true }, Hash = 1 };

        // From cell values (row-major, top row first); a fully covered rectangle is the shared Full shape.
        public static Shape FromCells(int w, int h, Func<int, int, bool> covered)
        {
            if (w <= 0 || h <= 0 || w > 64 || h > 64) return Full;
            var cells = new bool[w * h];
            long hash = 17;
            bool full = true;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool on = covered(x, y);
                    cells[y * w + x] = on;
                    full &= on;
                    hash = hash * 1099511628211L ^ (on ? 2 : 1);
                }
            if (full) return Full;
            hash = hash * 31 + w; hash = hash * 31 + h;
            return new Shape { W = w, H = h, Cells = cells, Hash = hash };
        }
    }

    // The outline image of one look, as an alpha value per pixel, on plain data only (System types, no Unity), so
    // tests/OutlineTest.cs can check the geometry outside the game. Sprites puts the result into a texture.
    //
    // Pixel rows go bottom-up (as in a texture); shape rows go top-down. Distances are chamfer (3-4) distances, in
    // thirds of a pixel.
    //   wanted      solid line and a faint fill
    //   stolen      diagonal "caution tape" line and a faint fill
    //   contraband  heavy line, a thin inner line and a stronger fill
    //   contents    corner brackets only, at the outer corners of the cell outline
    public static class Outline
    {
        const int Inf = 1 << 20;

        public struct Point { public float X, Y; public Point(float x, float y) { X = x; Y = y; } }

        // reach: how far a corner bracket runs along each edge, in pixels; 0 picks a default from the size.
        public static byte[] Alpha(Shape shape, int w, int h, Style style, int t, int r, bool fill, float reach)
        {
            int n = w * h;
            var inside = Mask(shape, w, h);
            bool[] solid = inside;
            var dist = new int[n];
            if (r > 0)
            {
                // Round the outer corners: shrink by r, then grow back by r.
                Distance(inside, w, h, true, dist);
                var core = new bool[n];
                for (int i = 0; i < n; i++) core[i] = dist[i] > 3 * r;
                Distance(core, w, h, false, dist);
                var rounded = new bool[n];
                for (int i = 0; i < n; i++) rounded[i] = inside[i] && dist[i] <= 3 * r + 1;
                solid = rounded;
            }
            Distance(solid, w, h, true, dist);

            byte fillA = (byte)(!fill ? 0 : style.Mark == Mark.Contraband ? 44 : 30);
            List<Point> corners = null;
            if (style.Contents)
            {
                corners = Corners(shape, w, h);
                if (reach <= 0f) reach = Math.Max(3 * t + 3, Math.Min((float)w / shape.W, (float)h / shape.H) * 0.3f);
            }
            int band = 3 * t;
            int stripe = 2 * t + 2;
            var alpha = new byte[n];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (!solid[i]) continue;
                    int d = dist[i];
                    byte a;
                    if (style.Contents)
                        a = d <= band && NearCorner(corners, x + 0.5f, y + 0.5f, reach) ? (byte)255 : (byte)0;
                    else if (style.Mark == Mark.Stolen)
                        a = d <= band ? (((x + y) / stripe) % 2 == 0 ? (byte)255 : (byte)80) : fillA;
                    else if (style.Mark == Mark.Contraband)
                        a = d <= band + 3 || (d > 3 * (2 * t + 2) && d <= 3 * (2 * t + 3)) ? (byte)255 : fillA;
                    else
                        a = d <= band ? (byte)255 : fillA;
                    alpha[i] = a;
                }
            return alpha;
        }

        // Which pixels the shape covers, scaled to w by h.
        public static bool[] Mask(Shape shape, int w, int h)
        {
            var inside = new bool[w * h];
            for (int y = 0; y < h; y++)
            {
                int cy = Math.Min(shape.H - 1, (h - 1 - y) * shape.H / h);
                for (int x = 0; x < w; x++)
                {
                    int cx = Math.Min(shape.W - 1, x * shape.W / w);
                    inside[y * w + x] = shape.Cells[cy * shape.W + cx];
                }
            }
            return inside;
        }

        // towardOutside: distance from each set pixel to the nearest unset one, with everything beyond the edge
        // unset. Otherwise: distance from each pixel to the nearest set one, with nothing set beyond the edge.
        public static void Distance(bool[] set, int w, int h, bool towardOutside, int[] dist)
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

        // Outer (convex) corners of the cell outline, in pixels.
        public static List<Point> Corners(Shape shape, int w, int h)
        {
            var list = new List<Point>();
            for (int j = 0; j <= shape.H; j++)
                for (int i = 0; i <= shape.W; i++)
                {
                    int count = Cell(shape, i - 1, j - 1) + Cell(shape, i, j - 1) + Cell(shape, i - 1, j) + Cell(shape, i, j);
                    if (count == 1) list.Add(new Point((float)i * w / shape.W, h - (float)j * h / shape.H));
                }
            return list;
        }

        static int Cell(Shape shape, int x, int y)
        {
            return x >= 0 && y >= 0 && x < shape.W && y < shape.H && shape.Cells[y * shape.W + x] ? 1 : 0;
        }

        static bool NearCorner(List<Point> corners, float x, float y, float reach)
        {
            for (int k = 0; k < corners.Count; k++)
                if (Math.Abs(corners[k].X - x) <= reach && Math.Abs(corners[k].Y - y) <= reach) return true;
            return false;
        }
    }
}
