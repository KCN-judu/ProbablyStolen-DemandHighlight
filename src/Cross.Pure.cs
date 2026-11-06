using System;
using System.Collections.Generic;

namespace DemandHighlight
{
    // The Item Filter's "excluded" mark: a cross drawn the way the game draws its check mark, from that check's
    // pixels, so it sits in the same box and reads as part of the same pixel art (and follows a texture pack that
    // redraws the check).
    //
    // The game's check, in its own pixels (one art pixel is drawn as s by s screen pixels):
    //   ......oo.        o  outline, dark, around the mark on four sides (not at corners)
    //   .....o++o        +  highlight: a stroke pixel with nothing of the mark above it
    //   .oo..o##o        #  fill
    //   o++oo+#o.
    //   o##oo##o.        strokes two pixels wide
    //   .o#++#o..
    //   .o####o..
    //   ..o##o...
    //   ...oo....
    // and the cross drawn from it:
    //   .oo...oo.
    //   o++o.o++o
    //   .o#+o+#o.
    //   ..o#+#o..
    //   ...o#o...        where the strokes cross, the middle row is one pixel narrower: the cross pinches there
    //   ..o+#+o..
    //   .o+#o#+o.
    //   o+#o.o#+o
    //   .oo...oo.
    // From it the cross takes the art pixel size, the box the mark sits in, the three colours and the stroke width;
    // then it draws two diagonals in a square inside that box, lights their top faces, outlines them on four sides
    // and colours them red, brighter than the box behind, which is itself red.
    //
    // Pixels are 0xAARRGGBB, row by row from the bottom, as Unity's GetPixels32 lays them out. Plain data only, so
    // tests/CrossTest.cs runs it outside the game.
    public static class Cross
    {
        public sealed class Art
        {
            public int Scale;                    // screen pixels per art pixel
            public uint Fill, Highlight, Outline;
            public int Stroke;                   // art pixels across a stroke, in a row
            public int X0, Y0, X1, Y1;           // the box around the mark's opaque art pixels, inclusive
        }

        // What the check is made of; null when it has no opaque pixels.
        public static Art Study(uint[] px, int w, int h)
        {
            int s = Scale(px, w, h);
            int aw = w / s, ah = h / s;
            var a = Down(px, w, aw, ah, s);

            int x0 = aw, y0 = ah, x1 = -1, y1 = -1;
            var outline = new Dictionary<uint, int>();
            var inner = new Dictionary<uint, int>();
            for (int y = 0; y < ah; y++)
                for (int x = 0; x < aw; x++)
                {
                    if (!Opaque(a, aw, ah, x, y)) continue;
                    x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
                    Count(OnEdge(a, aw, ah, x, y) ? outline : inner, a[y * aw + x]);
                }
            if (x1 < 0) return null;

            var art = new Art { Scale = s, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1 };
            art.Outline = Most(outline.Count > 0 ? outline : inner);
            art.Fill = inner.Count > 0 ? Most(inner) : art.Outline;

            // The highlight: the commonest other colour on a stroke's top face (above it, rows count up, is outline).
            var tops = new Dictionary<uint, int>();
            for (int y = 0; y < ah; y++)
                for (int x = 0; x < aw; x++)
                {
                    if (!Opaque(a, aw, ah, x, y) || OnEdge(a, aw, ah, x, y)) continue;
                    var c = a[y * aw + x];
                    if (c == art.Fill) continue;
                    if (y + 1 < ah && a[(y + 1) * aw + x] == art.Outline) Count(tops, c);
                }
            art.Highlight = tops.Count > 0 ? Most(tops) : art.Fill;

            // The stroke: the usual run of mark pixels (fill or highlight) in a row.
            var runs = new List<int>();
            for (int y = 0; y < ah; y++)
            {
                int run = 0;
                for (int x = 0; x <= aw; x++)
                {
                    bool mark = x < aw && Opaque(a, aw, ah, x, y) && !OnEdge(a, aw, ah, x, y);
                    if (mark) run++;
                    else if (run > 0) { runs.Add(run); run = 0; }
                }
            }
            runs.Sort();
            art.Stroke = runs.Count > 0 ? Math.Max(1, runs[runs.Count / 2]) : 1;
            return art;
        }

        // The cross, as large as the check's image.
        public static uint[] Draw(Art art, int w, int h)
        {
            var px = new uint[w * h];
            if (art == null) return px;
            int s = art.Scale, aw = w / s, ah = h / s;
            uint fill = Red(art.Fill), light = art.Highlight == art.Fill ? fill : Lighter(fill), dark = art.Outline;

            // A square inside the mark's box, inside its outline, centred; the strokes as wide as the check's.
            int bw = art.X1 - art.X0 - 1, bh = art.Y1 - art.Y0 - 1;
            int side = Math.Max(1, Math.Min(bw, bh));
            int left = art.X0 + 1 + (bw - side) / 2, bottom = art.Y0 + 1 + (bh - side) / 2;
            int k = Math.Max(1, Math.Min(art.Stroke, side / 2));

            var mark = new bool[aw * ah];
            for (int i = 0; i < side; i++)
            {
                int y = bottom + side - 1 - i;
                // Where the strokes cross, on an odd square, the middle row is one pixel narrower than a stroke, so
                // the cross pinches at its waist instead of swelling into a block.
                if (side % 2 == 1 && i == side / 2 && side > 1)
                {
                    int narrow = Math.Max(1, k - 1);
                    for (int j = 0; j < narrow; j++) Set(mark, aw, ah, left + side / 2 - (narrow - 1) / 2 + j, y);
                    continue;
                }
                int start = side == 1 ? 0 : (int)Math.Floor((double)i * (side - k) / (side - 1) + 0.5);
                for (int j = 0; j < k; j++)
                {
                    Set(mark, aw, ah, left + start + j, y);
                    Set(mark, aw, ah, left + side - 1 - start - j, y);
                }
            }

            var a = new uint[aw * ah];
            for (int y = 0; y < ah; y++)
                for (int x = 0; x < aw; x++)
                {
                    if (mark[y * aw + x]) a[y * aw + x] = y + 1 < ah && mark[(y + 1) * aw + x] ? fill : light;
                    else if (Near(mark, aw, ah, x, y)) a[y * aw + x] = dark;
                }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int ax = x / s, ay = y / s;
                    if (ax < aw && ay < ah) px[y * w + x] = a[ay * aw + ax];
                }
            return px;
        }

        // The game's toggle as it ships (checkbox_checked_orange and checkbox_orange, 13 by 13), top row first:
        // r box, w frame, # fill, + highlight, . outline inside the box and nothing outside it. The cross offered to
        // texture packs is drawn from these, so it exists from the moment the mod starts, before the game has built
        // any window; what the window shows is drawn from the toggle it really has.
        public static readonly string[] GameCheckedBox =
        {
            ".rrrrrrrrrrr.",
            "rwwwwwwwwwwwr",
            "rwrrrrrr..rwr",
            "rwrrrrr.++.wr",
            "rwr..rr.##.wr",
            "rw.++..+#.rwr",
            "rw.##..##.rwr",
            "rwr.#++#.rrwr",
            "rwr.####.rrwr",
            "rwrr.##.rrrwr",
            "rwrrr..rrrrwr",
            "rwwwwwwwwwwwr",
            ".rrrrrrrrrrr.",
        };
        public const uint GameBox = 0xFF9B4A3Du, GameFrame = 0xFFC4C4B0u, GameFill = 0xFF7AB053u, GameLight = 0xFFA8BB7Fu, GameOutline = 0xFF453943u;

        // The two pictures above as pixels (rows from the bottom), the empty one with the check painted out.
        public static void GameBoxes(out uint[] checkedBox, out uint[] emptyBox, out int w, out int h)
        {
            h = GameCheckedBox.Length; w = GameCheckedBox[0].Length;
            checkedBox = new uint[w * h];
            emptyBox = new uint[w * h];
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                {
                    char ch = GameCheckedBox[r][c];
                    bool inside = r >= 2 && r <= h - 3 && c >= 2 && c <= w - 3;
                    uint v = ch == 'r' ? GameBox : ch == 'w' ? GameFrame : ch == '#' ? GameFill : ch == '+' ? GameLight
                        : ch == '.' && inside ? GameOutline : 0u;
                    checkedBox[(h - 1 - r) * w + c] = v;
                    emptyBox[(h - 1 - r) * w + c] = inside ? GameBox : v;
                }
        }

        public static uint[] FromCheck(uint[] check, int w, int h)
        {
            return Draw(Study(check, w, h), w, h);
        }

        // The game draws its toggle as two whole pictures of one size, the empty box and the box with the check in it
        // (checkbox and checkbox_checked). The check is what differs between them, its outline included; the cross is
        // drawn from that and laid over the empty box, giving the box with the cross, a third picture of that size.
        // Null when the two are not the same size or do not differ.
        public static uint[] FromBoxes(uint[] checkedBox, uint[] emptyBox, int w, int h)
        {
            if (checkedBox == null || emptyBox == null || checkedBox.Length != w * h || emptyBox.Length != w * h) return null;
            var mark = new uint[w * h];
            bool any = false;
            for (int i = 0; i < mark.Length; i++)
                if (checkedBox[i] != emptyBox[i]) { mark[i] = checkedBox[i] | 0xFF000000u; any = true; }
            if (!any) return null;
            var cross = Draw(Study(mark, w, h), w, h);
            var result = (uint[])emptyBox.Clone();
            for (int i = 0; i < result.Length; i++) if ((cross[i] >> 24) != 0) result[i] = cross[i];
            return result;
        }

        // The size of an art pixel: the largest number every run of one colour, across and down, is a multiple of.
        static int Scale(uint[] px, int w, int h)
        {
            int g = 0;
            for (int y = 0; y < h; y++)
            {
                int run = 1;
                for (int x = 1; x <= w; x++)
                {
                    if (x < w && px[y * w + x] == px[y * w + x - 1]) { run++; continue; }
                    g = Gcd(g, run); run = 1;
                }
            }
            for (int x = 0; x < w; x++)
            {
                int run = 1;
                for (int y = 1; y <= h; y++)
                {
                    if (y < h && px[y * w + x] == px[(y - 1) * w + x]) { run++; continue; }
                    g = Gcd(g, run); run = 1;
                }
            }
            return Math.Max(1, Math.Min(g, 8));
        }

        static int Gcd(int a, int b) { while (b != 0) { int t = a % b; a = b; b = t; } return a; }

        static uint[] Down(uint[] px, int w, int aw, int ah, int s)
        {
            var a = new uint[aw * ah];
            for (int y = 0; y < ah; y++)
                for (int x = 0; x < aw; x++) a[y * aw + x] = px[y * s * w + x * s];
            return a;
        }

        static bool Opaque(uint[] px, int w, int h, int x, int y)
        {
            return x >= 0 && y >= 0 && x < w && y < h && (px[y * w + x] >> 24) >= 128;
        }

        // An opaque pixel with transparency (or the image's end) on one of its four sides: the outline.
        static bool OnEdge(uint[] px, int w, int h, int x, int y)
        {
            return !Opaque(px, w, h, x - 1, y) || !Opaque(px, w, h, x + 1, y) || !Opaque(px, w, h, x, y - 1) || !Opaque(px, w, h, x, y + 1);
        }

        static bool Near(bool[] mark, int w, int h, int x, int y)
        {
            return At(mark, w, h, x - 1, y) || At(mark, w, h, x + 1, y) || At(mark, w, h, x, y - 1) || At(mark, w, h, x, y + 1);
        }

        static bool At(bool[] mark, int w, int h, int x, int y) { return x >= 0 && y >= 0 && x < w && y < h && mark[y * w + x]; }
        static void Set(bool[] mark, int w, int h, int x, int y) { if (x >= 0 && y >= 0 && x < w && y < h) mark[y * w + x] = true; }

        static void Count(Dictionary<uint, int> counts, uint c)
        {
            int n;
            counts.TryGetValue(c, out n);
            counts[c] = n + 1;
        }

        static uint Most(Dictionary<uint, int> counts)
        {
            uint best = 0; int most = -1;
            foreach (var pair in counts) if (pair.Value > most || pair.Value == most && pair.Key < best) { best = pair.Key; most = pair.Value; }
            return best;
        }

        // The cross's red: the check's fill turned red and lifted, saturation at least 0.6 and value a third higher,
        // so it stands out from the red box as the green check does.
        public static uint Red(uint c)
        {
            float h, s, v;
            ToHsv(c, out h, out s, out v);
            return FromHsv(c & 0xFF000000u, 0f, Math.Max(s, 0.6f), Math.Min(1f, v * 4f / 3f));
        }

        // The highlight: the fill mixed 45% toward white.
        public static uint Lighter(uint c)
        {
            uint r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
            Func<uint, uint> up = t => (uint)Math.Round(t + (255 - t) * 0.45);
            return (c & 0xFF000000u) | (up(r) << 16) | (up(g) << 8) | up(b);
        }

        static void ToHsv(uint c, out float h, out float s, out float v)
        {
            float r = ((c >> 16) & 255) / 255f, g = ((c >> 8) & 255) / 255f, b = (c & 255) / 255f;
            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            v = max;
            s = max <= 0 ? 0 : d / max;
            if (d <= 0) h = 0;
            else if (max == r) h = ((g - b) / d) / 6f;
            else if (max == g) h = (2 + (b - r) / d) / 6f;
            else h = (4 + (r - g) / d) / 6f;
            if (h < 0) h += 1;
        }

        static uint FromHsv(uint alpha, float h, float s, float v)
        {
            float h6 = (h - (float)Math.Floor(h)) * 6f;
            int sector = (int)Math.Floor(h6) % 6;
            float f = h6 - (float)Math.Floor(h6);
            float p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
            float r, g, b;
            switch (sector)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
            return alpha | (Byte(r) << 16) | (Byte(g) << 8) | Byte(b);
        }

        static uint Byte(float f) { return (uint)Math.Max(0, Math.Min(255, (int)Math.Round(f * 255f))); }
    }
}
