// Checks which outline each item gets and what it reports to its container (src/Decide.Pure.cs), and the Item
// Filter's matching, outside the game.
//   csc -out:DecideTest.exe tests\DecideTest.cs src\Decide.Pure.cs src\Style.Pure.cs && DecideTest.exe
using System;
using System.Collections.Generic;
using DemandHighlight;

static class DecideTest
{
    static int failed;
    static readonly Kind Buying = new Kind { Buying = true }, Selling = new Kind { Selling = true },
        Inspector = new Kind { Inspector = true }, Idle = new Kind();

    static void Check(string what, bool ok)
    {
        if (!ok) failed++;
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + what);
    }

    static bool Is(Style s, Mark mark, bool contents) { return s.Mark == mark && s.Contents == contents; }
    static Agg A(Mark buyer, Mark illegal) { return new Agg { Buyer = buyer, Illegal = illegal }; }

    // The outline of one item, computed the way Rules does it.
    static Style Show(Kind kind, Facts f, Agg inside, bool insideAllBought = true, bool filtering = false,
        bool clientGoods = false, bool lockedAbove = false, bool blockedAbove = false)
    {
        bool bought;
        var self = Decide.Self(kind, filtering, clientGoods, f, insideAllBought, out bought);
        return Decide.Display(kind, filtering, clientGoods, f, self, inside, lockedAbove, blockedAbove);
    }

    static int Main()
    {
        var wanted = new Facts { SelfOk = true, Owned = true };
        var plain = new Facts { Owned = true };
        var nothing = default(Agg);

        // Buying.
        Check("buying: a wanted item gets the wanted look", Is(Show(Buying, wanted, nothing), Mark.Normal, false));
        Check("buying: a wanted stolen item gets the stolen look",
            Is(Show(Buying, new Facts { SelfOk = true, Owned = true, Illegal = Mark.Stolen }, nothing), Mark.Stolen, false));
        Check("buying: a wanted contraband item gets the contraband look",
            Is(Show(Buying, new Facts { SelfOk = true, Owned = true, Illegal = Mark.Contraband }, nothing), Mark.Contraband, false));
        Check("buying: a container with something wanted inside gets corner brackets",
            Is(Show(Buying, plain, A(Mark.Normal, Mark.None)), Mark.Normal, true));
        Check("buying: a locked container hides what is inside",
            Is(Show(Buying, new Facts { Owned = true, Locked = true }, A(Mark.Normal, Mark.None)), Mark.None, false));
        Check("buying: an item behind a lock is not marked", Is(Show(Buying, new Facts { SelfOk = true, BehindLock = true }, nothing), Mark.None, false));
        Check("buying: an immovable item is not marked", Is(Show(Buying, new Facts { SelfOk = true, Immovable = true }, nothing), Mark.None, false));
        Check("buying: nothing inside a locked container above is marked",
            Is(Show(Buying, wanted, nothing, lockedAbove: true), Mark.None, false));
        bool bought;
        Decide.Self(Buying, false, false, new Facts { SelfOk = true, ContentsChecked = true }, false, out bought);
        Check("buying: a container whose contents are checked is not bought if something inside is not", !bought);
        Decide.Self(Buying, false, false, new Facts { SelfOk = true, ContentsChecked = false }, false, out bought);
        Check("buying: a container whose contents are not checked is bought on its own", bought);
        Check("idle: nothing is marked without a customer", Is(Show(Idle, wanted, A(Mark.Normal, Mark.Normal)), Mark.None, false));

        // Inspection.
        var contraband = new Facts { Illegal = Mark.Contraband, Owned = true };
        Check("inspection: contraband is marked", Is(Show(Inspector, contraband, nothing), Mark.Contraband, false));
        Check("inspection: what the buyer wants is not marked", Is(Show(Inspector, wanted, nothing), Mark.None, false));
        Check("inspection: a container with stolen goods inside gets corner brackets",
            Is(Show(Inspector, plain, A(Mark.None, Mark.Stolen)), Mark.Stolen, true));
        Check("inspection: a locked container hides stolen goods inside",
            Is(Show(Inspector, new Facts { Locked = true, Owned = true }, A(Mark.None, Mark.Stolen)), Mark.None, false));
        Check("inspection: a container that blocks the search hides what is inside",
            Is(Show(Inspector, new Facts { BlocksSearch = true, Owned = true }, A(Mark.None, Mark.Contraband)), Mark.None, false));
        Check("inspection: nothing inside a blocking container above is marked",
            Is(Show(Inspector, contraband, nothing, blockedAbove: true), Mark.None, false));

        // Selling.
        var theirs = new Facts { Illegal = Mark.Stolen };
        Check("selling: the customer's goods are not marked with the option off", Is(Show(Selling, theirs, nothing), Mark.None, false));
        Check("selling: the customer's stolen goods are marked with the option on",
            Is(Show(Selling, theirs, nothing, clientGoods: true), Mark.Stolen, false));

        // Item Filter.
        Check("filter: a matching item gets its filter mark",
            Is(Show(Buying, new Facts { FilterMark = Mark.Normal }, nothing, filtering: true), Mark.Normal, false));
        Check("filter: a container with a match inside gets corner brackets",
            Is(Show(Buying, plain, A(Mark.Stolen, Mark.None), filtering: true), Mark.Stolen, true));
        Check("filter: the customer does not matter",
            Is(Show(Idle, new Facts { FilterMark = Mark.Contraband }, nothing, filtering: true), Mark.Contraband, false));

        // What containers above see.
        var self = A(Mark.Normal, Mark.Stolen);
        var inside = A(Mark.Contraband, Mark.Contraband);
        Check("report: an open container passes on itself and its contents",
            Decide.Report(self, inside, plain, false).Buyer == Mark.Contraband);
        Check("report: an item behind a lock passes on only what is inside it",
            Decide.Report(self, A(Mark.None, Mark.None), new Facts { BehindLock = true }, false).Buyer == Mark.None);
        Check("report: a locked container passes on only itself",
            Decide.Report(self, inside, new Facts { Locked = true }, false).Buyer == Mark.Normal);
        var blocked = Decide.Report(self, inside, new Facts { BlocksSearch = true }, false);
        Check("report: a container that blocks the search hides illegal goods inside but not wanted ones",
            blocked.Illegal == Mark.Stolen && blocked.Buyer == Mark.Contraband);

        // Marks.
        Check("illegality: contraband outranks stolen", Decide.Illegality(true, true) == Mark.Contraband);
        Check("illegality: stolen", Decide.Illegality(false, true) == Mark.Stolen);
        Check("style: the stronger mark wins", Is(Style.Max(new Style(Mark.Stolen, false), new Style(Mark.Contraband, true)), Mark.Contraband, true));
        Check("style: at the same mark a direct match beats corner brackets",
            Is(Style.Max(new Style(Mark.Normal, true), new Style(Mark.Normal, false)), Mark.Normal, false));
        Check("style: no mark never has corner brackets", !new Style(Mark.None, true).Contents);

        // Filter matching.
        var types = new HashSet<string> { "FOOD" };
        Check("match: contraband when contraband is wanted", FilterRule.Match(true, true, false, false, null, null) == Mark.Contraband);
        Check("match: stolen when stolen is wanted", FilterRule.Match(false, false, true, true, null, null) == Mark.Stolen);
        Check("match: a wanted type", FilterRule.Match(false, false, false, false, types, new List<string> { "TOOL", "FOOD" }) == Mark.Normal);
        Check("match: an unwanted type", FilterRule.Match(false, false, false, false, types, new List<string> { "TOOL" }) == Mark.None);
        Check("match: an item without types", FilterRule.Match(false, false, false, false, types, null) == Mark.None);
        Check("match: empty type names are skipped", FilterRule.Match(false, false, false, false, types, new List<string> { null }) == Mark.None);
        Check("match: stolen beats a type match", FilterRule.Match(false, false, true, true, types, new List<string> { "FOOD" }) == Mark.Stolen);

        Console.WriteLine(failed == 0 ? "all passed" : failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
