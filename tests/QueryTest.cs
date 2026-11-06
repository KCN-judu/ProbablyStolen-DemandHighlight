// Checks the query language (src/Query.Pure.cs): that equal meanings make equal values, that predicates answer
// items correctly, and how a query is laid out for the back half, outside the game.
//   csc -out:QueryTest.exe tests\QueryTest.cs src\*.Pure.cs && QueryTest.exe
using System;
using System.Collections.Generic;
using DemandHighlight;

// An item made of plain data. Asks are counted, to check that the back half can tell when the game is asked.
sealed class FakeItem : IItem
{
    public string Id { get; set; }
    public bool Owned { get; set; }
    public bool Contraband { get; set; }
    public bool Stolen { get; set; }
    public HashSet<string> Tags = new HashSet<string>(), Types = new HashSet<string>(), Conditions = new HashSet<string>();
    public Dictionary<string, bool> Answers = new Dictionary<string, bool>();
    public int Asked;

    public bool HasTag(string tag) { return Tags.Contains(tag); }
    public bool HasType(string type) { return Types.Contains(type); }
    public bool HasCondition(string condition) { return Conditions.Contains(condition); }
    public bool Ask(string oracle) { Asked++; bool a; return Answers.TryGetValue(oracle, out a) && a; }
}

static class QueryTest
{
    static int failed;

    static void Check(string what, bool ok)
    {
        if (!ok) failed++;
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + what);
    }

    static int Main()
    {
        var food = Pred.Type("FOOD");
        var med = Pred.Type("MEDICAL");

        // Normal forms: the same meaning is the same value.
        Check("and: order of building does not matter for nesting", Pred.And(Pred.And(food, med), Pred.Owned).Equals(Pred.And(food, med, Pred.Owned)));
        Check("and: true is dropped", Pred.And(Pred.True, food).Equals(food));
        Check("and: false absorbs", Pred.And(food, Pred.False).Equals(Pred.False));
        Check("or: false is dropped", Pred.Or(Pred.False, food).Equals(food));
        Check("or: true absorbs", Pred.Or(food, Pred.True).Equals(Pred.True));
        Check("or: repeats are dropped", Pred.Or(food, food).Equals(food));
        Check("and of nothing is true", Pred.And(new Pred[0]).Equals(Pred.True));
        Check("or of nothing is false", Pred.Or(new Pred[0]).Equals(Pred.False));
        Check("not not is dropped", Pred.Not(Pred.Not(food)).Equals(food));
        Check("id in: sorted and without repeats", Pred.IdIn(new[] { "b", "a", "b" }).Equals(Pred.IdIn(new[] { "a", "b" })));
        Check("id in: one identifier is a plain id", Pred.IdIn(new[] { "a" }).Equals(Pred.Id("a")));
        Check("id in: none is false", Pred.IdIn(new string[0]).Equals(Pred.False));
        Check("or of identifiers is id in", Pred.Or(Pred.Id("b"), food, Pred.Id("a")).Equals(Pred.Or(Pred.IdIn(new[] { "a", "b" }), food)));
        Check("different predicates differ", !food.Equals(med));
        Check("equal predicates hash alike", Pred.And(food, med).GetHashCode() == Pred.And(food, med).GetHashCode());

        // Evaluation.
        var item = new FakeItem { Id = "apple", Owned = true, Stolen = true };
        item.Types.Add("FOOD");
        item.Tags.Add("FRESH");
        item.Conditions.Add("rotten");
        Check("type", food.Eval(item) && !med.Eval(item));
        Check("tag", Pred.Tag("FRESH").Eval(item));
        Check("condition", Pred.Condition("rotten").Eval(item) && !Pred.Condition("broken").Eval(item));
        Check("id in", Pred.IdIn(new[] { "pear", "apple" }).Eval(item) && !Pred.IdIn(new[] { "pear", "plum" }).Eval(item));
        Check("owned and stolen", Pred.And(Pred.Owned, Pred.Stolen).Eval(item));
        Check("not", !Pred.Not(food).Eval(item));
        item.Answers["buys:1"] = true;
        Check("ask goes to the item", Pred.Ask("buys:1").Eval(item) && !Pred.Ask("buys:2").Eval(item));
        item.Asked = 0;
        Pred.And(Pred.Contraband, Pred.Ask("buys:1")).Eval(item);
        Check("and stops at the first false part, so a cheap check spares asking the game", item.Asked == 0);

        // Queries.
        var buy = Query.Want(Pred.Ask("buys:1"), true);
        Check("want of false is nothing", Query.Want(Pred.False, true).Equals(Query.Nothing));
        Check("union of one part is that part", Query.Union(buy).Equals(buy));
        Check("union drops nothing", Query.Union(Query.Nothing, buy).Equals(buy));
        Check("union of nothing is nothing", Query.Union(new Query[0]).Equals(Query.Nothing));
        Check("whole matters", !Query.Want(food, true).Equals(Query.Want(food, false)));
        Check("filter of the same choices is equal", Query.Filter(Pred.Or(food, med)).Equals(Query.Filter(Pred.Or(food, med))));

        // Layout for the back half.
        var both = Plan.Of(Query.Union(buy, Query.Goods(Pred.Stolen)));
        Check("plan: a union asks for both looks", both.Want != null && both.Goods != null && both.Search == null && both.Whole);
        var filter = Plan.Of(Query.Union(buy, Query.Filter(food)));
        Check("plan: a filter replaces everything else", filter.Filtering && filter.Want == null);
        Check("plan: nothing asks for nothing", !Plan.Of(Query.Nothing).Any);
        var two = Plan.Of(Query.Union(Query.Want(food, false), Query.Want(med, true)));
        Check("plan: two wants are asked as either", two.Want.Equals(Pred.Or(food, med)) && two.Whole);

        Console.WriteLine(failed == 0 ? "all passed" : failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
