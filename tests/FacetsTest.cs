// Checks how the Item Filter's three-state ticks are read from and written into its expression
// (src/Facets.Pure.cs), outside the game.
//   csc -out:FacetsTest.exe tests\FacetsTest.cs src\*.Pure.cs && FacetsTest.exe
using System;
using DemandHighlight;

static class FacetsTest
{
    static int failed;

    static void Check(string what, bool ok, string got = null)
    {
        if (!ok) failed++;
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + what + (!ok && got != null ? "\n      got: " + got : ""));
    }

    static Pred P(string text) { return Expr.Parse(text).Pred; }
    static string S(Pred p) { return p == null ? "<nothing>" : p.ToString(); }

    static int Main()
    {
        var food = Pred.Type("FOOD");
        var med = Pred.Type("MEDICAL");
        var junk = Pred.Type("JUNK");
        const Facets.State Off = Facets.State.Off, In = Facets.State.In, Out = Facets.State.Out;

        Check("cycle: off, in, out, off", Facets.Next(Off) == In && Facets.Next(In) == Out && Facets.Next(Out) == Off);

        // Reading.
        var f = Facets.Read(P("stolen and not contraband and type:(FOOD or MEDICAL) and not type:JUNK and not owned"));
        Check("read: status in and out", f.Of(Pred.Stolen) == In && f.Of(Pred.Contraband) == Out);
        Check("read: types in and out", f.OfType("FOOD") == In && f.OfType("MEDICAL") == In && f.OfType("JUNK") == Out && f.OfType("SEED") == Off);
        Check("read: a type's counts leave out the types' in clause, keep the out clause",
            f.AllBut(Facets.Part.Types).Equals(P("stolen and not contraband and not type:JUNK and not owned")));
        Check("read: an out tick's count leaves out both", f.AllBut(Facets.Part.Types, true).Equals(P("stolen and not contraband and not owned")));
        Check("read: status counts likewise", f.AllBut(Facets.Part.Status).Equals(P("not contraband and type:(FOOD or MEDICAL) and not type:JUNK and not owned")) &&
            f.AllBut(Facets.Part.Status, true).Equals(P("type:(FOOD or MEDICAL) and not type:JUNK and not owned")));
        Check("read: not of several is out for each", Facets.Read(P("not (stolen or contraband)")).Of(Pred.Contraband) == Out);
        Check("read: typed -type:x is out", Facets.Read(P("-type:JUNK")).OfType("JUNK") == Out);
        var mixed = Facets.Read(P("type:FOOD or stolen"));
        Check("read: a mixed or is not a facet", mixed.Of(Pred.Stolen) == Off && mixed.OfType("FOOD") == Off);
        Check("read: not of a mixed or is not a facet", Facets.Read(P("not (type:FOOD or stolen)")).OfType("FOOD") == Off);
        Check("read: nothing", Facets.Read(null).AllBut(Facets.Part.Types).Equals(Pred.True));
        var two = Facets.Read(P("type:FOOD and type:MEDICAL"));
        Check("read: only the first clause of a kind is the facet", two.Has("FOOD") && !two.Has("MEDICAL") &&
            two.AllBut(Facets.Part.Types).Equals(med));

        // Setting.
        Check("in: into nothing", S(Facets.Set(null, food, In)) == "type:FOOD");
        Check("in: joins its clause", S(Facets.Set(food, med, In)) == "type:(FOOD or MEDICAL)");
        Check("out: into nothing", S(Facets.Set(null, junk, Out)) == "not type:JUNK");
        Check("out: joins its clause", S(Facets.Set(P("not type:JUNK"), med, Out)) == "not type:(JUNK or MEDICAL)");
        Check("in to out moves the atom", S(Facets.Set(P("type:(FOOD or MEDICAL)"), med, Out)) == "type:FOOD and not type:MEDICAL");
        Check("out to off removes it", S(Facets.Set(P("type:FOOD and not type:MEDICAL"), med, Off)) == "type:FOOD");
        Check("the last one off is nothing", Facets.Set(P("not type:JUNK"), junk, Off) == null);
        Check("order: status in, status out, types in, types out, then the rest",
            S(Facets.Set(Facets.Set(Facets.Set(Facets.Set(P("not owned"), junk, Out), food, In), Pred.Contraband, Out), Pred.Stolen, In)) ==
            "stolen and not contraband and type:FOOD and not type:JUNK and not owned");
        Check("status out alone", S(Facets.Set(null, Pred.Stolen, Out)) == "not stolen");
        Check("an or at the top is kept, in brackets", S(Facets.Set(P("type:FOOD or stolen"), med, In)) == "type:MEDICAL and (type:FOOD or stolen)");
        Check("setting the same state twice is once", S(Facets.Set(food, food, In)) == "type:FOOD");
        Check("off for what is not there changes nothing", S(Facets.Set(P("stolen"), food, Off)) == "stolen");
        Check("toggle: two states", S(Facets.Toggle(null, food, true)) == "type:FOOD" && Facets.Toggle(food, food, false) == null);

        // A full click cycle on one tick, from a filter with other parts, comes back to where it started.
        var start = P("stolen and type:FOOD and not owned");
        var p1 = Facets.Set(start, med, Facets.Next(Off));
        var p2 = Facets.Set(p1, med, Facets.Next(In));
        var p3 = Facets.Set(p2, med, Facets.Next(Out));
        Check("cycle: in", S(p1) == "stolen and type:(FOOD or MEDICAL) and not owned", S(p1));
        Check("cycle: out", S(p2) == "stolen and type:FOOD and not type:MEDICAL and not owned", S(p2));
        Check("cycle: back to the start", p3.Equals(start), S(p3));

        // Round trip through the text the window writes, in both styles.
        var built = Facets.Set(Facets.Set(Facets.Set(null, Pred.Stolen, In), food, In), junk, Out);
        Check("built by ticking, printed", built.ToString() == "stolen and type:FOOD and not type:JUNK", built.ToString());
        Check("built by ticking, short", built.ToString(null, Formatter.Style.Short) == "stolen + type:FOOD + -type:JUNK",
            built.ToString(null, Formatter.Style.Short));
        var again = Facets.Read(Expr.Parse(built.ToString(null, Formatter.Style.Short)).Pred);
        Check("printed, read back to the same ticks", again.Of(Pred.Stolen) == In && again.Has("FOOD") && again.OfType("JUNK") == Out);

        Console.WriteLine(failed == 0 ? "all passed" : failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
