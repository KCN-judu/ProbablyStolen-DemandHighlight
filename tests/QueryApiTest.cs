// extra: api/DemandHighlightQuery.cs
// Checks the public query API end to end, outside the game: the client file other mods copy in
// (api/DemandHighlightQuery.cs) talking through real AppDomain slots to the host side (src/QueryApi.Pure.cs), with
// stand-in items in place of the game's. The game's own GameItem is replaced by the small class below.
//   csc -out:QueryApiTest.exe tests\QueryApiTest.cs api\DemandHighlightQuery.cs src\*.Pure.cs && QueryApiTest.exe
using System;
using System.Collections.Generic;
using DemandHighlight;

// Stands in for the game's GameItem: what the client file names, and an item the host's view can read.
public class GameItem : IItem
{
    public string Id { get; set; }
    public bool Owned { get; set; }
    public bool Contraband { get; set; }
    public bool Stolen { get; set; }
    public readonly HashSet<string> Types = new HashSet<string>();
    public bool HasTag(string tag) { return false; }
    public bool HasType(string type) { return Types.Contains(type); }
    public bool HasCondition(string condition) { return false; }
    public bool Ask(string oracle) { return false; }
}

static class QueryApiTest
{
    static int failed;

    static void Check(string what, bool ok)
    {
        if (!ok) failed++;
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + what);
    }

    static GameItem Item(string id, string type, bool stolen = false, bool owned = true)
    {
        var i = new GameItem { Id = id, Stolen = stolen, Owned = owned };
        i.Types.Add(type);
        return i;
    }

    static int Main()
    {
        var shop = new List<GameItem>
        {
            Item("apple", "FOOD"), Item("bread", "FOOD", stolen: true), Item("bandage", "MEDICAL"),
            Item("wrench", "TOOL"), Item("pills", "MEDICAL", stolen: true, owned: false),
        };
        bool ready = true, mainThread = true, broken = false;
        var host = new QueryApi.Host<GameItem>
        {
            Resolve = (key, value) => key == "type" && value.ToLowerInvariant() == "food" ? "FOOD" : null,
            Ready = () => ready,
            OnMainThread = () => mainThread,
            EachItem = visit =>
            {
                foreach (var i in shop)
                {
                    if (broken) throw new InvalidOperationException("the shop moved");
                    visit(i);
                }
            },
            View = i => i,
        };

        // Not installed yet: the client says so and touches nothing.
        var found = new List<GameItem> { shop[0] };
        Check("absent: not available", !DemandHighlightQuery.Available && DemandHighlightQuery.Version == null);
        var absent = DemandHighlightQuery.Run("stolen", found);
        Check("absent: NotInstalled, and the list is cleared", absent.Status == DemandHighlightQuery.Status.NotInstalled && found.Count == 0);
        Check("absent: format too", DemandHighlightQuery.Format("stolen", false).Status == DemandHighlightQuery.Status.NotInstalled);

        // Published as the game does it.
        var domain = AppDomain.CurrentDomain;
        domain.SetData(QueryApi.RunSlot, QueryApi.RunDelegate(host));
        domain.SetData(QueryApi.FormatSlot, QueryApi.FormatDelegate(host));
        domain.SetData(QueryApi.VersionSlot, QueryApi.Version);
        Check("published: available, version 1.0", DemandHighlightQuery.Available && DemandHighlightQuery.Version == "1.0");

        var r = DemandHighlightQuery.Run("type:(FOOD or MEDICAL) -stolen", found);
        Check("run: the matching items, in walk order", r.Ok && r.Count == 2 && found.Count == 2 && found[0].Id == "apple" && found[1].Id == "bandage");
        Check("run: the expression in canonical form", r.Text == "type:(FOOD or MEDICAL) and not stolen");
        Check("run: no problem fields on success", r.Problem == null && r.Position == -1 && r.Message == null);
        var counted = DemandHighlightQuery.Run("stolen", null);
        Check("run: a null list only counts", counted.Ok && counted.Count == 2);
        Check("run: type names as the player writes them", DemandHighlightQuery.Run("food", found).Count == 2);
        Check("run: the list is cleared before filling", DemandHighlightQuery.Run("type:TOOL", found).Count == 1 && found.Count == 1);
        Check("run: nothing matches is ok, empty", DemandHighlightQuery.Run("type:SEED", found).Ok && found.Count == 0);
        Check("run: precedence as the language says", DemandHighlightQuery.Run("type:FOOD or type:MEDICAL stolen", null).Count == 3);

        // Errors the caller can tell apart.
        found.Add(shop[0]);
        var bad = DemandHighlightQuery.Run("stolen and shiny", found);
        Check("invalid: an unknown word, where and how long", bad.Status == DemandHighlightQuery.Status.Invalid &&
            bad.Problem == "UnknownWord" && bad.Detail == "shiny" && bad.Position == 11 && bad.Length == 5 && found.Count == 0);
        var key = DemandHighlightQuery.Run("colour:red", null);
        Check("invalid: an unknown key", key.Status == DemandHighlightQuery.Status.Invalid && key.Problem == "UnknownKey" && key.Position == 0);
        var syntax = DemandHighlightQuery.Run("(stolen", null);
        Check("invalid: a syntax error", syntax.Status == DemandHighlightQuery.Status.Invalid && syntax.Problem == "ExpectedClose" && syntax.Position == 7);
        Check("bad argument: null", DemandHighlightQuery.Run(null, null).Status == DemandHighlightQuery.Status.BadArgument);
        Check("bad argument: blank", DemandHighlightQuery.Run("   ", null).Status == DemandHighlightQuery.Status.BadArgument);
        ready = false;
        Check("not ready: no shop", DemandHighlightQuery.Run("stolen", null).Status == DemandHighlightQuery.Status.NotReady);
        Check("not ready: an invalid expression is still reported as invalid", DemandHighlightQuery.Run("(", null).Status == DemandHighlightQuery.Status.Invalid);
        ready = true;
        mainThread = false;
        Check("wrong thread", DemandHighlightQuery.Run("stolen", null).Status == DemandHighlightQuery.Status.WrongThread);
        mainThread = true;
        broken = true;
        found.Add(shop[0]);
        var failure = DemandHighlightQuery.Run("stolen", found);
        Check("failed: an exception in the game is a failure, with its message, and no partial list",
            failure.Status == DemandHighlightQuery.Status.Failed && failure.Message.Contains("the shop moved") && found.Count == 0);
        broken = false;
        var resolve = host.Resolve;
        host.Resolve = (k, v) => { throw new InvalidOperationException("names not loaded"); };
        var thrown = DemandHighlightQuery.Run("food", found);
        Check("failed: an exception while compiling never reaches the caller",
            thrown.Status == DemandHighlightQuery.Status.Failed && thrown.Message.Contains("names not loaded") && found.Count == 0);
        Check("failed: format too", DemandHighlightQuery.Format("food", false).Status == DemandHighlightQuery.Status.Failed);
        host.Resolve = resolve;

        // Format.
        var words = DemandHighlightQuery.Format("-stolen + type:(food | medical)", false);
        var brief = DemandHighlightQuery.Format("not stolen and type:(food or medical)", true);
        Check("format: words", words.Ok && words.Text == "not stolen and type:(food or medical)");
        Check("format: short", brief.Ok && brief.Text == "-stolen + type:(food | medical)");
        Check("format: errors as for run", DemandHighlightQuery.Format("a and", false).Problem == "ExpectedTerm");

        // Building terms safely.
        Check("quote: one word stays bare", DemandHighlightQuery.Quote("spacerFoodVariety") == "spacerFoodVariety");
        Check("quote: spaces and quotes", DemandHighlightQuery.Field("id", "say \"hi\" now") == "id:\"say \\\"hi\\\" now\"");
        Check("quote: and/or/not are quoted", DemandHighlightQuery.Quote("Or") == "\"Or\"");
        foreach (var v in new[] { "plain", "two words", "a-b", "x:y", "q\"uote", "back\\slash", "or", "食物" })
        {
            var p = Expr.Parse(DemandHighlightQuery.Field("id", v));
            Check("quote: id:" + v + " reads back", p.Ok && p.Pred.Equals(Pred.Id(v)));
            var g = Expr.Parse("id:(" + DemandHighlightQuery.Quote(v) + " or x)");
            Check("quote: " + v + " inside a group reads back", g.Ok && g.Pred.Equals(Pred.Or(Pred.Id(v), Pred.Id("x"))));
        }

        // A later major version is not mistaken for this one; withdrawing takes it all back.
        domain.SetData(QueryApi.VersionSlot, "2.0");
        Check("version: 2.x is not this contract", !DemandHighlightQuery.Available && DemandHighlightQuery.Run("stolen", null).Status == DemandHighlightQuery.Status.NotInstalled);
        domain.SetData(QueryApi.VersionSlot, "1.3");
        Check("version: a later 1.x still works", DemandHighlightQuery.Run("stolen", null).Ok);
        domain.SetData(QueryApi.RunSlot, null); domain.SetData(QueryApi.FormatSlot, null); domain.SetData(QueryApi.VersionSlot, null);
        Check("withdrawn: not available again", !DemandHighlightQuery.Available);

        Console.WriteLine(failed == 0 ? "all passed" : failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
