// Checks the item filter's expression syntax (src/Expr.Pure.cs): parsing, errors, and that every predicate prints as
// text that parses back to it, outside the game.
//   csc -out:ExprTest.exe tests\ExprTest.cs src\*.Pure.cs && ExprTest.exe
using System;
using System.Collections.Generic;
using DemandHighlight;

static class ExprTest
{
    static int failed;

    static void Check(string what, bool ok)
    {
        if (!ok) failed++;
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + what);
    }

    static bool Means(string text, Pred expected)
    {
        var p = Expr.Parse(text);
        if (!p.Ok) { Console.WriteLine("      error: " + p.Error + " at " + p.Position); return false; }
        if (!p.Pred.Equals(expected)) Console.WriteLine("      got: " + p.Pred);
        return p.Pred.Equals(expected);
    }

    static bool Fails(string text) { return !Expr.Parse(text).Ok; }

    static bool RoundTrip(Pred p)
    {
        var text = p.ToString();
        var back = Expr.Parse(text);
        if (!back.Ok || !back.Pred.Equals(p)) Console.WriteLine("      printed: " + text);
        return back.Ok && back.Pred.Equals(p);
    }

    static int Main()
    {
        var food = Pred.Type("FOOD");
        var med = Pred.Type("MEDICAL");

        // Each stage on its own. Lexer: characters to tokens, with places.
        var tokens = Lexer.Lex("type:\"a b\" -(x|y)");
        var kinds = new List<TokenKind>();
        foreach (var t in tokens) kinds.Add(t.Kind);
        Check("lexer: tokens", string.Join(" ", kinds) == "Word Colon Quoted Minus Open Word Bar Word Close End");
        Check("lexer: a quoted text without quotes, its span with them", tokens[2].Text == "a b" && tokens[2].Span.Start == 5 && tokens[2].Span.Length == 5);
        Check("lexer: never fails, a stray character is a Bad token", Lexer.Lex("a # b")[1].Kind == TokenKind.Bad);
        Check("lexer: an unclosed quote is a Bad token at the end", Lexer.Lex("x \"ab")[1].Problem == Expr.Problem.UnclosedQuote &&
            Lexer.Lex("x \"ab")[1].Span.Start == 5);

        // Parser: grammar only. A word that no binder would know still parses.
        var tree = Parser.Parse(Lexer.Lex("shiny or type:x gizmo"));
        var or = tree as OrAst;
        Check("parser: or of and", or != null && or.Parts.Count == 2 && or.Parts[0] is WordAst && or.Parts[1] is AndAst);
        var and = or != null ? or.Parts[1] as AndAst : null;
        var field = and != null ? and.Parts[0] as FieldAst : null;
        Check("parser: a field keeps key, value and places", field != null && field.Key == "type" && field.Value == "x" &&
            field.KeySpan.Start == 9 && field.ValueSpan.Start == 14);
        Check("parser: spans cover what was written", or != null && or.Span.Start == 0 && or.Span.End == 21);
        Check("parser: brackets leave no node", Parser.Parse(Lexer.Lex("((stolen))")) is WordAst);
        Check("parser: a space before the colon makes a word, then a stray colon", Fails("type :x"));
        Check("parser: nothing is no tree", Parser.Parse(Lexer.Lex("  ")) == null);

        // Binder: meaning, and errors with the span of what was written.
        var unknown = Expr.Parse("stolen or colour:red");
        Check("binder: an unknown key, with its span", unknown.Problem == Expr.Problem.UnknownKey && unknown.Span.Start == 10 && unknown.Span.Length == 6);

        // Terms.
        Check("a type", Means("type:FOOD", food));
        Check("types are upper-cased", Means("type:food", food));
        Check("keys and words are not case-sensitive", Means("TYPE:food and STOLEN", Pred.And(food, Pred.Stolen)));
        Check("a tag", Means("tag:locked_container_tag", Pred.Tag("LOCKED_CONTAINER_TAG")));
        Check("an identifier is kept as typed", Means("id:spacerFoodVariety", Pred.Id("spacerFoodVariety")));
        Check("a condition", Means("condition:broken", Pred.Condition("broken")));
        Check("cond is short for condition", Means("cond:broken", Pred.Condition("broken")));
        Check("owned, and mine for owned", Means("owned", Pred.Owned) && Means("mine", Pred.Owned));
        Check("a quoted value", Means("id:\"two words\"", Pred.Id("two words")));
        Check("a quote inside quotes", Means("id:\"say \\\"hi\\\"\"", Pred.Id("say \"hi\"")));
        Check("a value in another script", Means("id:苹果", Pred.Id("苹果")));

        // Operators.
        Check("and", Means("type:FOOD and stolen", Pred.And(food, Pred.Stolen)));
        Check("side by side is and", Means("type:FOOD stolen", Pred.And(food, Pred.Stolen)));
        Check("+ is and", Means("type:FOOD + stolen", Pred.And(food, Pred.Stolen)));
        Check("or", Means("type:FOOD or type:MEDICAL", Pred.Or(food, med)));
        Check("| is or", Means("type:FOOD | type:MEDICAL", Pred.Or(food, med)));
        Check("not and -", Means("not stolen", Pred.Not(Pred.Stolen)) && Means("-stolen", Pred.Not(Pred.Stolen)));
        Check("& and ! are not operators", Fails("stolen & owned") && Fails("!stolen"));
        Check("and binds tighter than or", Means("type:FOOD or type:MEDICAL and stolen", Pred.Or(food, Pred.And(med, Pred.Stolen))));
        Check("brackets", Means("(type:FOOD or type:MEDICAL) and owned", Pred.And(Pred.Or(food, med), Pred.Owned)));
        Check("not binds to one term", Means("not stolen and owned", Pred.And(Pred.Not(Pred.Stolen), Pred.Owned)));
        Check("a keyword inside a word stays a word", Means("id:orange or id:notebook", Pred.Or(Pred.Id("orange"), Pred.Id("notebook"))));
        Check("empty text is no condition", Means("   ", Pred.True));

        // Errors are reported, never thrown.
        Check("error: unknown key", Fails("colour:red"));
        Check("error: unknown word", Fails("shiny"));
        Check("error: missing value", Fails("type:"));
        Check("error: unclosed bracket", Fails("(stolen"));
        Check("error: stray bracket", Fails("stolen)"));
        Check("error: unclosed quote", Fails("id:\"abc"));
        Check("error: dangling and", Fails("stolen and"));
        var err = Expr.Parse("stolen and shiny");
        Check("error: says where", !err.Ok && err.Position == 11 && err.Error.Contains("shiny"));
        Check("error: says what, as a code", err.Problem == Expr.Problem.UnknownWord && err.Detail == "shiny");
        Check("error codes", Expr.Parse("(stolen").Problem == Expr.Problem.ExpectedClose &&
            Expr.Parse("type:").Problem == Expr.Problem.ExpectedValue && Expr.Parse("type:").Detail == "type" &&
            Expr.Parse("id:\"abc").Problem == Expr.Problem.UnclosedQuote && Expr.Parse("stolen)").Problem == Expr.Problem.Unexpected &&
            Expr.Parse("colour:red").Problem == Expr.Problem.UnknownKey && Expr.Parse("stolen and").Problem == Expr.Problem.ExpectedTerm);
        Check("no error, no code", Expr.Parse("stolen").Problem == Expr.Problem.None);

        // Localized names through a resolver.
        Binder.Resolve zh = (key, value) => key == "type" && value == "食物" ? "FOOD" : null;
        var p = Expr.Parse("type:食物", zh);
        Check("resolver: a localized type name", p.Ok && p.Pred.Equals(food));
        var bare = Expr.Parse("食物 -stolen", zh);
        Check("resolver: a type name on its own is that type", bare.Ok && bare.Pred.Equals(Pred.And(food, Pred.Not(Pred.Stolen))));
        Check("resolver: an unknown plain word is still an error", Expr.Parse("饮料", zh).Problem == Expr.Problem.UnknownWord);
        Check("resolver: an unknown type: value is upper-cased", Expr.Parse("type:gizmo", zh).Pred.Equals(Pred.Type("GIZMO")));

        // Every predicate a player can write prints as text that parses back to it.
        Check("round trip: a type", RoundTrip(food));
        Check("round trip: and, or, not",
            RoundTrip(Pred.And(Pred.Or(food, med), Pred.Not(Pred.Stolen), Pred.Owned)));
        Check("round trip: or inside and inside or", RoundTrip(Pred.Or(Pred.And(food, Pred.Or(med, Pred.Contraband)), Pred.Stolen)));
        Check("round trip: a value that needs quotes", RoundTrip(Pred.Id("two words")));
        Check("round trip: identifiers in a set", RoundTrip(Pred.Not(Pred.IdIn(new[] { "apple", "pear" }))));
        Check("round trip: a barter term",
            RoundTrip(Pred.And(Pred.Owned, Pred.Or(Pred.Id("moonshine"), Pred.And(Pred.Tag("MEDICAL_TAG"), Pred.Not(Pred.Condition("broken")))))));
        Check("round trip: what ticking builds", RoundTrip(Pred.And(Pred.Stolen, Pred.Or(food, med), Pred.Not(Pred.Owned))));
        Check("an or of types is one group",
            Pred.And(Pred.Stolen, Pred.Or(food, med)).ToString() == "stolen and type:(FOOD or MEDICAL)");
        Check("a set of identifiers is one group", Pred.Not(Pred.IdIn(new[] { "apple", "pear" })).ToString() == "not id:(apple or pear)");
        Check("a mixed or is not grouped", Pred.Or(food, Pred.Stolen).ToString() == "type:FOOD or stolen");

        // Groups: a key over a bracketed expression.
        Check("group: any of", Means("type:(food or medical)", Pred.Or(food, med)));
        Check("group: quoted values", Means("type:(\"household good\" | food)", Pred.Or(Pred.Type("HOUSEHOLD GOOD"), food)));
        Check("group: and, not inside", Means("type:(food -medical)", Pred.And(food, Pred.Not(med))));
        Check("group: a word inside is a value, not a keyword", Means("type:(stolen)", Pred.Type("STOLEN")));
        Check("group: a field inside keeps its key", Means("type:(food or tag:x)", Pred.Or(food, Pred.Tag("X"))));
        Check("group: nested", Means("tag:(a or type:(food or medical))", Pred.Or(Pred.Tag("A"), food, med)));
        Check("group: an unknown key", Expr.Parse("colour:(red)").Problem == Expr.Problem.UnknownKey);
        Check("group: unclosed", Expr.Parse("type:(food").Problem == Expr.Problem.ExpectedClose);
        Check("quoted text outside a group means nothing", Expr.Parse("\"food\"").Problem == Expr.Problem.Unexpected);

        // The formatter: one canonical form, idempotent, and it never changes what text means.
        Check("format: operators in words, brackets where and and or meet",
            Expr.Format("-stolen + type:food | type:medical") == "(not stolen and type:food) or type:medical");
        Check("format: redundant brackets go", Expr.Format("((stolen))  and  (owned)") == "stolen and owned");
        Check("format: keys and keywords in one spelling", Expr.Format("COND:broken MINE STOLEN") == "condition:broken and owned and stolen");
        Check("format: quotes only when needed", Expr.Format("id:\"apple\" id:\"two words\"") == "id:apple and id:\"two words\"");
        Check("format: a group keeps its key once", Expr.Format("type:( food |\"household good\" )") == "type:(food or \"household good\")");
        Check("format: and/or/not as values are quoted in a group", Expr.Format("type:(\"or\" food)") == "type:(\"or\" and food)");
        Check("format: not before a group of and", Expr.Format("not (stolen owned)") == "not (stolen and owned)");
        Check("format: a type name on its own stays as written", Expr.Format("食物 -stolen", (k, v) => v == "食物" ? "FOOD" : null) == "食物 and not stolen");
        Check("format: text that does not read is not formatted", Expr.Format("stolen and") == null);
        var corpus = new[]
        {
            "stolen", "-stolen + food | medical", "type:(food or medical) owned", "not (a or b) and c or d",
            "id:(apple or \"two words\") cond:broken", "((x))", "a b c or d e", "tag:(a or type:(b c))", "-(-x)",
        };
        Binder.Resolve any = (k, v) => k == "type" ? v.ToUpperInvariant() : null;
        bool idempotent = true, faithful = true;
        foreach (var t in corpus)
        {
            var once = Expr.Format(t, any);
            var twice = Expr.Format(once, any);
            if (once != twice) { idempotent = false; Console.WriteLine("      not idempotent: " + t + " -> " + once + " -> " + twice); }
            if (!Expr.Parse(t, any).Pred.Equals(Expr.Parse(once, any).Pred)) { faithful = false; Console.WriteLine("      meaning changed: " + t + " -> " + once); }
        }
        Check("format: twice is once", idempotent);
        Check("format: the meaning is kept", faithful);

        // Types written in the player's language, and read back to the same predicate.
        var names = new Dictionary<string, string> { { "FOOD", "食物" }, { "LUXURY_ITEM", "Luxury Item" }, { "CONTRABAND", "Contraband" }, { "ODD", "奇怪" } };
        Binder.Resolve local = (key, value) =>
        {
            if (key != "type") return null;
            foreach (var pair in names) if (pair.Value.Equals(value, StringComparison.OrdinalIgnoreCase) && pair.Key != "ODD") return pair.Key;
            return null;
        };
        Func<string, string> written = t => { string n; return names.TryGetValue(t, out n) ? Lift.TypeName(t, n, local) : null; };
        Check("local: the name in the player's language", Lift.TypeName("FOOD", "食物", local) == "食物");
        Check("local: printed always after type:", Pred.Type("CONTRABAND").ToString(written) == "type:Contraband");
        Check("local: a name with a space is quoted", Pred.Type("LUXURY_ITEM").ToString(written) == "type:\"Luxury Item\"");
        Check("local: a name that does not read back is not used", Lift.TypeName("ODD", "奇怪", local) == null);
        var mixed = Pred.And(Pred.Or(food, Pred.Type("LUXURY_ITEM"), Pred.Type("CONTRABAND"), Pred.Type("ODD")), Pred.Not(Pred.Stolen));
        var text = mixed.ToString(written);
        Check("local: printed with names", text == "type:(食物 or \"Luxury Item\" or Contraband or ODD) and not stolen");
        var back = Expr.Parse(text, local);
        Check("local: reads back to the same predicate", back.Ok && back.Pred.Equals(mixed));

        Console.WriteLine(failed == 0 ? "all passed" : failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
