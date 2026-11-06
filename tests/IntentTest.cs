// Checks how the front half turns customers and the item filter into queries (src/Intent.Pure.cs), outside the game.
//   csc -out:IntentTest.exe tests\IntentTest.cs src\*.Pure.cs && IntentTest.exe
using System;
using DemandHighlight;

static class IntentTest
{
    static int failed;

    static void Check(string what, bool ok)
    {
        if (!ok) failed++;
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + what);
    }

    static ClientView Client(int intent, string id = "someone", string[] tags = null)
    {
        return new ClientView { Id = id, Intent = intent, Token = "7", BuyingTags = tags ?? new[] { "FOOD" } };
    }

    static Query Q(ClientView c, bool goods = false) { return Intent.Distill(c, null, goods); }

    static int Main()
    {
        var illegal = Pred.Or(Pred.Contraband, Pred.Stolen);

        // Customers.
        Check("no customer: nothing", Q(null).Equals(Query.Nothing));
        Check("inspection: contraband and stolen goods", Q(Client(Intent.Inspection)).Equals(Query.Search(illegal)));
        var buyer = Client(Intent.Buy);
        Check("buyer: what the game's buying check accepts, whole containers",
            Q(buyer).Equals(Query.Want(Pred.Ask(Intent.BuysName(buyer)), true)));
        Check("seller: nothing with the goods option off", Q(Client(Intent.Sell)).Equals(Query.Nothing));
        Check("seller: their illegal goods with the option on", Q(Client(Intent.Sell), true).Equals(Query.Goods(illegal)));
        var both = Q(Client(Intent.SellAndBuy), true);
        Check("buyer and seller: the player's items they accept, and their own illegal goods",
            both.Kind == Query.Op.Union && Plan.Of(both).Want.Equals(Pred.And(Pred.Owned, Pred.Ask(Intent.BuysName(Client(Intent.SellAndBuy))))) &&
            Plan.Of(both).Goods != null);
        Check("anyone else: nothing", Q(Client(5)).Equals(Query.Nothing));

        // The question asked of the game names everything its answer depends on.
        var a = Client(Intent.Buy);
        var b = Client(Intent.Buy, tags: new[] { "FOOD", "MEDICAL" });
        var c = Client(Intent.Buy);
        c.Token = "8";
        Check("buys: different wants ask a different question", Intent.BuysName(a) != Intent.BuysName(b));
        Check("buys: a different customer asks a different question", Intent.BuysName(a) != Intent.BuysName(c));
        Check("buys: the order of the wants does not matter",
            Intent.BuysName(Client(Intent.Buy, tags: new[] { "B", "A" })) == Intent.BuysName(Client(Intent.Buy, tags: new[] { "A", "B" })));

        // Variety: a sale changes the query, but not the question asked of the game.
        var v1 = Client(Intent.Buy);
        v1.Variety = true;
        var v2 = Client(Intent.Buy);
        v2.Variety = true;
        v2.Taken = new[] { "apple" };
        Check("variety: nothing taken yet refuses nothing", Plan.Of(Q(v1)).Want.Equals(Pred.Ask(Intent.BuysName(v1))));
        Check("variety: a sale refuses that identifier",
            Plan.Of(Q(v2)).Want.Equals(Pred.And(Pred.Ask(Intent.BuysName(v2)), Pred.Not(Pred.Id("apple")))));
        Check("variety: a sale keeps the game's answers (same question)", Intent.BuysName(v1) == Intent.BuysName(v2));
        Check("variety: a sale is a new query", !Q(v1).Equals(Q(v2)));

        // The flexi buyer waits for its dialogue to end, then is an ordinary buyer.
        var waiting = Client(Intent.Buy, Intent.FlexiBuyer, new string[0]);
        Check("flexi buyer: nothing before choosing", Q(waiting).Equals(Query.Nothing));
        var chosen = Client(Intent.Buy, Intent.FlexiBuyer, new[] { "MEDICAL" });
        Check("flexi buyer: a buyer once it has chosen", Q(chosen).Equals(Query.Want(Pred.Ask(Intent.BuysName(chosen)), true)));
        Check("flexi buyer: choosing is a new query", !Q(waiting).Equals(Q(chosen)));
        Check("another buyer with no listed wants is still asked (the game may accept by other rules)",
            Q(Client(Intent.Buy, "water_buyer", new string[0])).Kind == Query.Op.Want);

        // Barter.
        var barter = Client(Intent.Barter);
        barter.Barter = new[]
        {
            new BarterTerm { Id = "moonshine" },
            new BarterTerm { Tag = "MEDICAL_TAG", Blacklist = new[] { "broken" } }
        };
        var expected = Query.Want(Pred.And(Pred.Owned, Pred.Or(Pred.Id("moonshine"),
            Pred.And(Pred.Tag("MEDICAL_TAG"), Pred.Not(Pred.Condition("broken"))))), false);
        Check("barter: the player's items that meet any term, not whole containers", Q(barter).Equals(expected));
        var empty = Client(Intent.Barter);
        Check("barter: no terms, nothing", Q(empty).Equals(Query.Nothing));
        Check("barter term: an empty term matches anything", Intent.Term(new BarterTerm()).Equals(Pred.True));

        // The item filter replaces the customer.
        var filter = new FilterView { Expression = Pred.And(Pred.Stolen, Pred.Or(Pred.Type("FOOD"), Pred.Type("MEDICAL"))) };
        var fq = Intent.Distill(buyer, filter, false);
        Check("filter: replaces the customer", fq.Kind == Query.Op.Filter);
        Check("filter: is its expression", fq.Equals(Query.Filter(filter.Expression)));
        Check("filter: nothing chosen leaves the customer", Intent.Distill(buyer, new FilterView(), false).Equals(Q(buyer)));
        Check("filter: an empty expression is no filter", Intent.Distill(buyer, new FilterView { Expression = Pred.True }, false).Equals(Q(buyer)));

        Console.WriteLine(failed == 0 ? "all passed" : failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
