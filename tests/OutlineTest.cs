// Checks the outline geometry (src/Outline.Pure.cs) on plain shapes, outside the game.
//   csc -out:OutlineTest.exe tests\OutlineTest.cs src\Outline.Pure.cs src\Style.Pure.cs && OutlineTest.exe
using System;
using DemandHighlight;

static class OutlineTest
{
    static int failed;

    static void Check(string what, bool ok)
    {
        if (!ok) failed++;
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + what);
    }

    // An L: the top row full, the bottom row only its left cell.
    //   X X
    //   X .
    static readonly Shape L = Shape.FromCells(2, 2, (x, y) => !(x == 1 && y == 1));

    static byte At(byte[] a, int w, int x, int y) { return a[y * w + x]; }

    static int Main()
    {
        Check("a fully covered shape is the shared full shape", Shape.FromCells(3, 2, (x, y) => true).Hash == Shape.Full.Hash);
        Check("an L is not the full shape", L.Hash != Shape.Full.Hash);
        Check("two Ls hash the same", Shape.FromCells(2, 2, (x, y) => !(x == 1 && y == 1)).Hash == L.Hash);
        Check("a rotated L hashes differently", Shape.FromCells(2, 2, (x, y) => !(x == 0 && y == 1)).Hash != L.Hash);

        Check("a rectangle has four outer corners", Outline.Corners(Shape.Full, 10, 10).Count == 4);
        Check("an L has five outer corners", Outline.Corners(L, 20, 20).Count == 5);

        // Pixel rows go bottom-up, so the missing cell of the L is the bottom-right quarter.
        var mask = Outline.Mask(L, 20, 20);
        Check("the mask covers the top-left of the L", mask[19 * 20 + 0]);
        Check("the mask covers the bottom-left of the L", mask[0 * 20 + 0]);
        Check("the mask leaves out the missing bottom-right cell", !mask[0 * 20 + 19]);

        var dist = new int[9];
        Outline.Distance(new[] { false, false, false, false, true, false, false, false, false }, 3, 3, true, dist);
        Check("a lone pixel is one step from the outside", dist[4] == 3);

        const int w = 40, h = 40, t = 2;
        var wanted = Outline.Alpha(Shape.Full, w, h, new Style(Mark.Normal, false), t, 0, true, 0f);
        Check("wanted: the edge is drawn solid", At(wanted, w, 0, 20) == 255);
        Check("wanted: the middle has the faint fill", At(wanted, w, 20, 20) == 30);
        var bare = Outline.Alpha(Shape.Full, w, h, new Style(Mark.Normal, false), t, 0, false, 0f);
        Check("wanted without fill: the middle is clear", At(bare, w, 20, 20) == 0);

        var contraband = Outline.Alpha(Shape.Full, w, h, new Style(Mark.Contraband, false), t, 0, true, 0f);
        Check("contraband: the middle has the stronger fill", At(contraband, w, 20, 20) == 44);
        bool inner = false;
        for (int x = 4; x < 20; x++) if (At(contraband, w, x, 20) == 255 && At(contraband, w, x - 1, 20) != 255) inner = true;
        Check("contraband: a thin inner line runs inside the heavy one", inner);

        var stolen = Outline.Alpha(Shape.Full, w, h, new Style(Mark.Stolen, false), t, 0, true, 0f);
        bool bright = false, dim = false;
        for (int x = 0; x < w; x++) { if (At(stolen, w, x, 0) == 255) bright = true; if (At(stolen, w, x, 0) == 80) dim = true; }
        Check("stolen: the edge is striped like caution tape", bright && dim);

        var brackets = Outline.Alpha(Shape.Full, w, h, new Style(Mark.Normal, true), t, 0, true, 0f);
        Check("contents: a corner is drawn", At(brackets, w, 0, 0) == 255);
        Check("contents: the middle of an edge is not", At(brackets, w, 20, 0) == 0);
        Check("contents: there is no fill", At(brackets, w, 20, 20) == 0);

        var shapeL = Outline.Alpha(L, w, h, new Style(Mark.Normal, false), t, 0, true, 0f);
        Check("an L outline leaves the missing cell clear", At(shapeL, w, 35, 5) == 0);
        Check("an L outline draws the inner edge of the missing cell", At(shapeL, w, 19, 5) == 255);

        var round = Outline.Alpha(Shape.Full, w, h, new Style(Mark.Normal, false), t, 3, true, 0f);
        Check("rounded corners cut the very corner pixel", At(round, w, 0, 0) == 0);
        Check("rounded corners keep the edges", At(round, w, 0, 20) == 255);

        Console.WriteLine(failed == 0 ? "all passed" : failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
