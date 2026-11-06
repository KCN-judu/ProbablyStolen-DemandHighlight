// Checks the Item Filter's numbers and the line that shows them (src/Stats.Pure.cs), outside the game.
//   csc -out:StatsTest.exe tests\StatsTest.cs src\*.Pure.cs && StatsTest.exe
using System;
using System.Collections.Generic;
using DemandHighlight;

static class StatsTest
{
    static int failed;

    static void Check(string what, bool ok)
    {
        if (!ok) failed++;
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + what);
    }

    static int Main()
    {
        var t = new Totals();
        t.Add(100, 2);
        t.Add(50, 1);
        Check("totals add up", t.Items == 2 && t.Value == 150 && t.Cells == 3);
        Check("per cell", t.PerCell == 50);
        var odd = new Totals();
        odd.Add(5, 2);
        Check("per cell rounds half away from zero", odd.PerCell == 3);
        Check("no cells, no per cell", new Totals().PerCell == -1);

        Check("share", Stats.Share(8, 100) == "8%");
        Check("share rounds", Stats.Share(1, 3) == "33%");
        Check("a sliver is <1%", Stats.Share(1, 1000) == "<1%");
        Check("nothing is 0%", Stats.Share(0, 1000) == "0%");
        Check("all is 100%", Stats.Share(5, 5) == "100%");
        Check("no whole, no share", Stats.Share(5, 0) == null);

        var v = new Dictionary<string, string> { { "n", "23" }, { "v", "4,120" }, { "p", "8%" } };
        Check("fill", Stats.Fill("{n} items, {v} ({p})", v) == "23 items, 4,120 (8%)");
        Check("an unknown slot stays", Stats.Fill("{n} {x}", v) == "23 {x}");
        Check("an unclosed brace stays", Stats.Fill("{n} {v", v) == "23 {v");
        v["p"] = null;
        Check("a missing value drops its brackets", Stats.Fill("{v} ({p}) left", v) == "4,120 left");
        Check("full-width brackets too", Stats.Fill("{v}（{p}）", v) == "4,120");
        Check("words inside the brackets go too", Stats.Fill("{v} total ({p} of the shop)   {n} items", v) == "4,120 total   23 items");
        Check("full-width, words before the slot", Stats.Fill("总值 {v}（全店 {p}）　占", v) == "总值 4,120　占");
        Check("the whole group goes, filled slots before it included", Stats.Fill("x ({n} of {p})", v) == "x");
        Check("a missing value outside brackets is just empty", Stats.Fill("[{v}] {p}!", v) == "[4,120] !");

        Console.WriteLine(failed == 0 ? "all passed" : failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
