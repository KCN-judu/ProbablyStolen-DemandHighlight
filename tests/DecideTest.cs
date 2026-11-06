// Checks which outline each item gets and what it reports to its container (src/Decide.Pure.cs), for each kind of
// query, outside the game.
//   csc -out:DecideTest.exe tests\DecideTest.cs src\Decide.Pure.cs src\Query.Pure.cs src\Style.Pure.cs && DecideTest.exe
using System;
using DemandHighlight;

static class DecideTest
{
    static int failed;
    static readonly Pred Illegal = Pred.Or(Pred.Contraband, Pred.Stolen);
    static readonly Plan Buying = Plan.Of(Query.Want(Pred.Ask("buys"), true)),
        Barter = Plan.Of(Query.Want(Pred.Owned, false)),
        Inspector = Plan.Of(Query.Search(Illegal)),
        Goods = Plan.Of(Query.Goods(Illegal)),
        Filtering = Plan.Of(Query.Filter(Pred.Type("FOOD"))),
        Idle = Plan.Of(Query.Nothing);

    static void Check(string what, bool ok)
    {
        if (!ok) failed++;
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + what);
    }

    static bool Is(Style s, Mark mark, bool contents) { return s.Mark == mark && s.Contents == contents; }
    static Agg A(Mark buyer, Mark illegal) { return new Agg { Buyer = buyer, Illegal = illegal }; }

    // The outline of one item, computed the way Engine does it.
    static Style Show(Plan p, Facts f, Verdict v, Agg inside, bool insideAllBought = true, bool lockedAbove = false,
        bool blockedAbove = false)
    {
        bool bought;
        var self = Decide.Self(p, f, v, insideAllBought, out bought);
        return Decide.Display(p, f, v, self, inside, lockedAbove, blockedAbove);
    }

    static int Main()
    {
        var mine = new Facts { Owned = true };
        var want = new Verdict { Want = true };
        var none = new Verdict();
        var nothing = default(Agg);

        // Buying.
        Check("buying: a wanted item gets the wanted look", Is(Show(Buying, mine, want, nothing), Mark.Normal, false));
        Check("buying: a wanted stolen item gets the stolen look",
            Is(Show(Buying, new Facts { Owned = true, Illegal = Mark.Stolen }, want, nothing), Mark.Stolen, false));
        Check("buying: a wanted contraband item gets the contraband look",
            Is(Show(Buying, new Facts { Owned = true, Illegal = Mark.Contraband }, want, nothing), Mark.Contraband, false));
        Check("buying: a container with something wanted inside gets corner brackets",
            Is(Show(Buying, mine, none, A(Mark.Normal, Mark.None)), Mark.Normal, true));
        Check("buying: a locked container hides what is inside",
            Is(Show(Buying, new Facts { Owned = true, Locked = true }, none, A(Mark.Normal, Mark.None)), Mark.None, false));
        Check("buying: an item behind a lock is not marked", Is(Show(Buying, new Facts { BehindLock = true }, want, nothing), Mark.None, false));
        Check("buying: an immovable item is not marked", Is(Show(Buying, new Facts { Immovable = true }, want, nothing), Mark.None, false));
        Check("buying: nothing inside a locked container above is marked",
            Is(Show(Buying, mine, want, nothing, lockedAbove: true), Mark.None, false));
        bool bought;
        Decide.Self(Buying, new Facts { HasContents = true }, want, false, out bought);
        Check("buying: a container whose contents are checked is not bought if something inside is not", !bought);
        Decide.Self(Buying, new Facts { HasContents = false }, want, false, out bought);
        Check("buying: a container whose contents are not checked is bought on its own", bought);
        Check("idle: nothing is marked when nothing is asked", Is(Show(Idle, mine, want, A(Mark.Normal, Mark.Normal)), Mark.None, false));

        // Barter: the item itself is taken, whatever is inside.
        Decide.Self(Barter, new Facts { HasContents = true, Owned = true }, want, false, out bought);
        Check("barter: a matching container counts without its contents", bought);
        Check("barter: a matching item gets the wanted look", Is(Show(Barter, mine, want, nothing), Mark.Normal, false));

        // Inspection.
        var contraband = new Facts { Illegal = Mark.Contraband, Owned = true };
        var flagged = new Verdict { Search = true };
        Check("inspection: contraband is marked", Is(Show(Inspector, contraband, flagged, nothing), Mark.Contraband, false));
        Check("inspection: what a buyer would want is not marked", Is(Show(Inspector, mine, want, nothing), Mark.None, false));
        Check("inspection: a container with stolen goods inside gets corner brackets",
            Is(Show(Inspector, mine, none, A(Mark.None, Mark.Stolen)), Mark.Stolen, true));
        Check("inspection: a locked container hides stolen goods inside",
            Is(Show(Inspector, new Facts { Locked = true, Owned = true }, none, A(Mark.None, Mark.Stolen)), Mark.None, false));
        Check("inspection: a container that blocks the search hides what is inside",
            Is(Show(Inspector, mine, new Verdict { BlocksSearch = true }, A(Mark.None, Mark.Contraband)), Mark.None, false));
        Check("inspection: nothing inside a blocking container above is marked",
            Is(Show(Inspector, contraband, flagged, nothing, blockedAbove: true), Mark.None, false));

        // The customer's goods.
        var theirs = new Facts { Illegal = Mark.Stolen };
        Check("goods: the customer's stolen goods are marked",
            Is(Show(Goods, theirs, new Verdict { Goods = true }, nothing), Mark.Stolen, false));
        Check("goods: the player's own illegal goods are not marked as the customer's",
            Is(Show(Goods, new Facts { Illegal = Mark.Stolen, Owned = true }, new Verdict { Goods = true }, nothing), Mark.None, false));

        // Item Filter.
        var matched = new Verdict { Filter = true };
        Check("filter: a matching item gets the filter look", Is(Show(Filtering, mine, matched, nothing), Mark.Normal, false));
        Check("filter: a matching stolen item gets the stolen look",
            Is(Show(Filtering, new Facts { Illegal = Mark.Stolen }, matched, nothing), Mark.Stolen, false));
        Check("filter: a container with a match inside gets corner brackets",
            Is(Show(Filtering, mine, none, A(Mark.Stolen, Mark.None)), Mark.Stolen, true));
        Check("filter: locks do not hide anything",
            Is(Show(Filtering, new Facts { Locked = true }, none, A(Mark.Normal, Mark.None), lockedAbove: true), Mark.Normal, true));

        // What containers above see.
        var self = A(Mark.Normal, Mark.Stolen);
        var inside = A(Mark.Contraband, Mark.Contraband);
        Check("report: an open container passes on itself and its contents",
            Decide.Report(Buying, self, inside, mine, none).Buyer == Mark.Contraband);
        Check("report: an item behind a lock passes on only what is inside it",
            Decide.Report(Buying, self, A(Mark.None, Mark.None), new Facts { BehindLock = true }, none).Buyer == Mark.None);
        Check("report: a locked container passes on only itself",
            Decide.Report(Buying, self, inside, new Facts { Locked = true }, none).Buyer == Mark.Normal);
        var blocked = Decide.Report(Inspector, self, inside, mine, new Verdict { BlocksSearch = true });
        Check("report: a container that blocks the search hides illegal goods inside but not wanted ones",
            blocked.Illegal == Mark.Stolen && blocked.Buyer == Mark.Contraband);
        Check("report: the filter sees inside locked containers",
            Decide.Report(Filtering, default(Agg), inside, new Facts { Locked = true }, none).Buyer == Mark.Contraband);

        // Marks.
        Check("illegality: contraband outranks stolen", Decide.Illegality(true, true) == Mark.Contraband);
        Check("illegality: stolen", Decide.Illegality(false, true) == Mark.Stolen);
        Check("style: the stronger mark wins", Is(Style.Max(new Style(Mark.Stolen, false), new Style(Mark.Contraband, true)), Mark.Contraband, true));
        Check("style: at the same mark a direct match beats corner brackets",
            Is(Style.Max(new Style(Mark.Normal, true), new Style(Mark.Normal, false)), Mark.Normal, false));
        Check("style: no mark never has corner brackets", !new Style(Mark.None, true).Contents);

        Console.WriteLine(failed == 0 ? "all passed" : failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
