// Checks the "excluded" cross drawn from the game's check mark (src/Cross.Pure.cs), outside the game, starting from
// the check as the game draws it, and prints what it drew: + highlight, # fill, o outline, . nothing.
//   csc -out:CrossTest.exe tests\CrossTest.cs src\*.Pure.cs && CrossTest.exe
using System;
using System.Collections.Generic;
using DemandHighlight;

static class CrossTest
{
    static int failed;

    static void Check(string what, bool ok)
    {
        if (!ok) failed++;
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + what);
    }

    // The game's colours, read from a screenshot of its toggle.
    const uint Fill = 0xFF7AB053u, Light = 0xFFA8BB7Fu, Dark = 0xFF453943u;

    // The game's check, one art pixel per character, top row first.
    static readonly string[] GameCheck =
    {
        "......oo.",
        ".....o++o",
        ".oo..o##o",
        "o++oo+#o.",
        "o##oo##o.",
        ".o#++#o..",
        ".o####o..",
        "..o##o...",
        "...oo....",
    };

    // What the cross must look like, drawn the same way.
    static readonly string[] Expected =
    {
        ".oo...oo.",
        "o++o.o++o",
        ".o#+o+#o.",
        "..o#+#o..",
        "...o#o...",
        "..o+#+o..",
        ".o+#o#+o.",
        "o+#o.o#+o",
        ".oo...oo.",
    };

    // Rows into Unity's layout (bottom row first), each art pixel scale by scale, in a transparent margin.
    static uint[] Pixels(string[] rows, int scale, int margin, out int w, out int h)
    {
        int ah = rows.Length + 2 * margin, aw = rows[0].Length + 2 * margin;
        w = aw * scale; h = ah * scale;
        var px = new uint[w * h];
        for (int r = 0; r < rows.Length; r++)
            for (int c = 0; c < rows[r].Length; c++)
            {
                char ch = rows[r][c];
                uint v = ch == '#' ? Fill : ch == '+' ? Light : ch == 'o' ? Dark : 0u;
                int ax = c + margin, ay = ah - 1 - (r + margin);
                for (int dy = 0; dy < scale; dy++)
                    for (int dx = 0; dx < scale; dx++) px[(ay * scale + dy) * w + ax * scale + dx] = v;
            }
        return px;
    }

    // An image back into rows, top first, one character per art pixel.
    static string[] Rows(uint[] px, int w, int h, int scale, uint fill, uint light, uint dark)
    {
        int aw = w / scale, ah = h / scale;
        var rows = new string[ah];
        for (int r = 0; r < ah; r++)
        {
            var line = new char[aw];
            for (int c = 0; c < aw; c++)
            {
                var v = px[((ah - 1 - r) * scale) * w + c * scale];
                line[c] = v == 0 ? '.' : v == fill ? '#' : v == light ? '+' : v == dark ? 'o' : '?';
            }
            rows[r] = new string(line);
        }
        return rows;
    }

    static string[] Trim(string[] rows, int margin)
    {
        var t = new string[rows.Length - 2 * margin];
        for (int i = 0; i < t.Length; i++) t[i] = rows[i + margin].Substring(margin, rows[i + margin].Length - 2 * margin);
        return t;
    }

    static int Main()
    {
        uint red = Cross.Red(Fill), pink = Cross.Lighter(red);
        Check("red: brighter than the red box, as the green check is", ((red >> 16) & 255) > 0xC0 && ((red >> 8) & 255) < 0x70);
        Check("red: the highlight is lighter", ((pink >> 8) & 255) > ((red >> 8) & 255));

        foreach (var scale in new[] { 1, 2, 3 })
        {
            int w, h;
            var check = Pixels(GameCheck, scale, 2, out w, out h);
            var art = Cross.Study(check, w, h);
            Check("scale " + scale + ": the art pixel size", art != null && art.Scale == scale);
            Check("scale " + scale + ": fill, highlight, outline", art != null && art.Fill == Fill && art.Highlight == Light && art.Outline == Dark);
            Check("scale " + scale + ": strokes two pixels wide", art != null && art.Stroke == 2);
            Check("scale " + scale + ": the box around the mark", art != null && art.X1 - art.X0 == 8 && art.Y1 - art.Y0 == 8);
            var cross = Cross.Draw(art, w, h);
            var got = Trim(Rows(cross, w, h, scale, red, pink, Dark), 2);
            bool same = string.Join("\n", got) == string.Join("\n", Expected);
            Check("scale " + scale + ": the cross, drawn the game's way", same);
            if (!same || scale == 1) foreach (var row in got) Console.WriteLine("      " + row);
            bool whole = true;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (cross[y * w + x] != cross[(y / scale * scale) * w + x / scale * scale]) whole = false;
            Check("scale " + scale + ": every art pixel drawn whole", whole);
        }

        // The game's toggle: two whole 13 by 13 pictures, the empty box and the box with the check.
        {
            string[] checkedBox =
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
            var emptyBox = new string[13];
            for (int r = 0; r < 13; r++)
            {
                var row = checkedBox[r].ToCharArray();
                if (r >= 2 && r <= 10) for (int c = 2; c <= 10; c++) row[c] = 'r';
                emptyBox[r] = new string(row);
            }
            uint Box = 0xFF9B4A3Du, Frame = 0xFFC4C4B0u;
            Func<string[], uint[]> pixels = rows =>
            {
                var px = new uint[13 * 13];
                for (int r = 0; r < 13; r++)
                    for (int c = 0; c < 13; c++)
                    {
                        char ch = rows[r][c];
                        px[(12 - r) * 13 + c] = ch == 'r' ? Box : ch == 'w' ? Frame : ch == '#' ? Fill : ch == '+' ? Light : ch == '.' && r >= 2 && r <= 10 && c >= 2 && c <= 10 ? Dark : 0u;
                    }
                return px;
            };
            var made = Cross.FromBoxes(pixels(checkedBox), pixels(emptyBox), 13, 13);
            Check("boxes: a picture comes back", made != null);
            bool frame = true, inside = true;
            for (int r = 0; r < 13; r++)
                for (int c = 0; c < 13; c++)
                {
                    var v = made[(12 - r) * 13 + c];
                    if (r < 2 || r > 10 || c < 2 || c > 10) { if (v != pixels(emptyBox)[(12 - r) * 13 + c]) frame = false; continue; }
                    char want = Expected[r - 2][c - 2];
                    uint expect = want == '#' ? red : want == '+' ? pink : want == 'o' ? Dark : Box;
                    if (v != expect) inside = false;
                }
            Check("boxes: the box and its frame are kept", frame);
            Check("boxes: the cross drawn where the check was, as from the check alone", inside);
            Check("boxes: different sizes, nothing", Cross.FromBoxes(new uint[4], new uint[9], 2, 2) == null);
            Check("boxes: the same picture twice, nothing", Cross.FromBoxes(pixels(emptyBox), pixels(emptyBox), 13, 13) == null);
        }

        // The pictures the mod ships for the offer give the same cross.
        {
            uint[] cb, eb; int gw, gh;
            Cross.GameBoxes(out cb, out eb, out gw, out gh);
            var offered = Cross.FromBoxes(cb, eb, gw, gh);
            bool same = gw == 13 && gh == 13 && offered != null;
            for (int r = 0; same && r < 9; r++)
                for (int c = 0; c < 9; c++)
                {
                    char want = Expected[r][c];
                    uint expect = want == '#' ? red : want == '+' ? pink : want == 'o' ? Dark : Cross.GameBox;
                    if (offered[(12 - (r + 2)) * 13 + c + 2] != expect) same = false;
                }
            Check("shipped: the offered cross is the game's box with the cross", same);
        }

        Check("nothing opaque, nothing to study", Cross.Study(new uint[16], 4, 4) == null);
        Check("a mark of one colour has no highlight", Cross.Study(new uint[] { 0, 0, 0, 0, Fill, 0, 0, 0, 0 }, 3, 3).Highlight == Fill);

        Console.WriteLine(failed == 0 ? "all passed" : failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
