// The filter language's specification as tests, the way a compiler is tested: tables of input and expected output
// for each stage, then properties over many random trees. doc/manual/filter-language.md states the same rules in
// words; a rule changed in one place is changed in the other.
//   csc -out:LanguageTest.exe tests\LanguageTest.cs src\*.Pure.cs && LanguageTest.exe
using System;
using System.Collections.Generic;
using System.Text;
using DemandHighlight;

static class LanguageTest
{
    static int failed, passed;

    static void Check(string what, bool ok, string got = null)
    {
        if (ok) { passed++; return; }
        failed++;
        Console.WriteLine("FAIL  " + what + (got != null ? "\n      got: " + got : ""));
    }

    static string Tree(string text)
    {
        try { var t = Parser.Parse(Lexer.Lex(text)); return t == null ? "<empty>" : t.ToString(); }
        catch (Expr.CompileError e) { return "error " + e.Problem + " at " + e.Span.Start; }
    }

    static string Tokens(string text)
    {
        var parts = new List<string>();
        foreach (var t in Lexer.Lex(text))
            parts.Add(t.Kind == TokenKind.Word || t.Kind == TokenKind.Quoted || t.Kind == TokenKind.Bad ? t.Kind + ":" + t.Text : t.Kind.ToString());
        return string.Join(" ", parts);
    }

    static int Main()
    {
        // 1. Lexical rules. Words are letters (any script), digits, _ . '; everything else is a symbol or a mistake.
        var lexer = new[]
        {
            new[] { "a b", "Word:a Word:b End" },
            new[] { "a+b|c", "Word:a Plus Word:b Bar Word:c End" },
            new[] { "-a", "Minus Word:a End" },
            new[] { "a-b", "Word:a Minus Word:b End" },                 // - is never part of a word
            new[] { "type:x", "Word:type Colon Word:x End" },
            new[] { "type:(x)", "Word:type Colon Open Word:x Close End" },
            new[] { "\"two words\"", "Quoted:two words End" },
            new[] { "\"say \\\"hi\\\"\"", "Quoted:say \"hi\" End" },
            new[] { "食物 Ö.k_1's", "Word:食物 Word:Ö.k_1's End" },
            new[] { "a & b", "Word:a Bad:& Word:b End" },                // & and ! are not operators
            new[] { "!a", "Bad:! Word:a End" },
            new[] { "\"open", "Bad:\" End" },
            new[] { "   ", "End" },
        };
        foreach (var c in lexer) Check("lex " + c[0], Tokens(c[0]) == c[1], Tokens(c[0]));

        // 2. Grammar: precedence and associativity, as the tree the parser builds.
        //    tightest first: atom, not/-, and/+/side by side, or/|
        var grammar = new[]
        {
            // and: n-ary, three spellings
            new[] { "a b c", "(and a b c)" },
            new[] { "a and b and c", "(and a b c)" },
            new[] { "a + b + c", "(and a b c)" },
            new[] { "a and b + c d", "(and a b c d)" },
            // or: n-ary, two spellings
            new[] { "a or b or c", "(or a b c)" },
            new[] { "a | b | c", "(or a b c)" },
            new[] { "a or b | c", "(or a b c)" },
            // and binds tighter than or
            new[] { "a or b and c", "(or a (and b c))" },
            new[] { "a and b or c", "(or (and a b) c)" },
            new[] { "a b | c d", "(or (and a b) (and c d))" },
            new[] { "a+b|c", "(or (and a b) c)" },
            // not binds tightest, to one term, right to left
            new[] { "not a b", "(and (not a) b)" },
            new[] { "-a b", "(and (not a) b)" },
            new[] { "not a or b", "(or (not a) b)" },
            new[] { "a -b", "(and a (not b))" },
            new[] { "a-b", "(and a (not b))" },
            new[] { "not not a", "(not (not a))" },
            new[] { "--a", "(not (not a))" },
            new[] { "not -a", "(not (not a))" },
            new[] { "-(a b)", "(not (and a b))" },
            new[] { "-type:x", "(not (field type x))" },
            new[] { "-type:(x | y)", "(not (scope type (or x y)))" },
            // brackets group and leave no node
            new[] { "(a or b) c", "(and (or a b) c)" },
            new[] { "a (b or c)", "(and a (or b c))" },
            new[] { "((a))", "a" },
            new[] { "(a b) (c d)", "(and (and a b) (and c d))" },  // grouped runs stay as written; meaning is the same
            // keys
            new[] { "type:x", "(field type x)" },
            new[] { "type:\"x y\"", "(field type \"x y\")" },
            new[] { "type:x y", "(and (field type x) y)" },
            new[] { "type:(a b | c)", "(scope type (or (and a b) c))" },
            new[] { "type:(a) b", "(and (scope type a) b)" },
            new[] { "type:(\"or\" | b)", "(scope type (or \"or\" b))" },
            new[] { "tag:(a type:(b c))", "(scope tag (and a (scope type (and b c))))" },
            // keywords only as whole words, in any case
            new[] { "a OR b", "(or a b)" },
            new[] { "a And b", "(and a b)" },
            new[] { "NOT a", "(not a)" },
            new[] { "orange notebook android", "(and orange notebook android)" },
            new[] { "type:or", "(field type or)" },            // after a colon, a word is a value
            new[] { "", "<empty>" },
        };
        foreach (var c in grammar) Check("parse " + c[0], Tree(c[0]) == c[1], Tree(c[0]));

        // 3. Errors: the first one in the text, with its place.
        var errors = new[]
        {
            new[] { "a and", "error ExpectedTerm at 5" },
            new[] { "-", "error ExpectedTerm at 1" },
            new[] { "a or", "error ExpectedTerm at 4" },
            new[] { "(a", "error ExpectedClose at 2" },
            new[] { "type:(a", "error ExpectedClose at 7" },
            new[] { "a)", "error Unexpected at 1" },
            new[] { "| a", "error Unexpected at 0" },
            new[] { "a or or b", "error Unexpected at 5" },
            new[] { "a and or b", "error Unexpected at 6" },
            new[] { "and", "error Unexpected at 0" },
            new[] { "a & b", "error Unexpected at 2" },
            new[] { "!a", "error Unexpected at 0" },
            new[] { "type:", "error ExpectedValue at 5" },
            new[] { "type: x", "error ExpectedValue at 5" },
            new[] { "type:x:y", "error Unexpected at 6" },
            new[] { "type :x", "error Unexpected at 5" },
            new[] { "\"ab", "error UnclosedQuote at 3" },
            new[] { "type:\"ab", "error UnclosedQuote at 8" },
            new[] { "a ) b (", "error Unexpected at 2" },     // the first of two mistakes
        };
        foreach (var c in errors) Check("error " + c[0], Tree(c[0]) == c[1], Tree(c[0]));

        // 4. Binding errors, after the grammar: what does not mean anything, with the span of what was written.
        Binder.Resolve types = (k, v) => k == "type" && (v == "food" || v == "食物") ? "FOOD" : null;
        var binding = new[]
        {
            new[] { "shiny", "UnknownWord 0 5" },
            new[] { "stolen and shiny", "UnknownWord 11 5" },
            new[] { "colour:red", "UnknownKey 0 6" },
            new[] { "colour:(red)", "UnknownKey 0 6" },
            new[] { "\"food\"", "Unexpected 0 6" },              // quoted text means something only inside a group
        };
        foreach (var c in binding)
        {
            var p = Expr.Parse(c[0], types);
            var got = p.Problem + " " + p.Span.Start + " " + p.Span.Length;
            Check("bind " + c[0], got == c[1], got);
        }

        // 5. Formatting, in both styles.
        var formats = new[]
        {
            //  input                    words                          short
            new[] { "-a b | c",              "(not a and b) or c",          "(-a + b) | c" },
            new[] { "a or b and c",          "a or (b and c)",              "a | (b + c)" },
            new[] { "a b c",                 "a and b and c",               "a + b + c" },
            new[] { "not (a or b)",          "not (a or b)",                "-(a | b)" },
            new[] { "not (a b)",             "not (a and b)",               "-(a + b)" },
            new[] { "--a",                   "not not a",                   "--a" },
            new[] { "((a)) ((b))",           "a and b",                     "a + b" },
            new[] { "(a b) (c d)",           "a and b and c and d",         "a + b + c + d" },
            new[] { "(a | b) | c",           "a or b or c",                 "a | b | c" },
            new[] { "type:( x  |y )",        "type:(x or y)",               "type:(x | y)" },
            new[] { "type:(x y | z)",        "type:((x and y) or z)",       "type:((x + y) | z)" },
            new[] { "-type:(x|y) stolen",    "not type:(x or y) and stolen", "-type:(x | y) + stolen" },
            new[] { "TYPE:x COND:y MINE",    "type:x and condition:y and owned", "type:x + condition:y + owned" },
            new[] { "id:\"x\" id:\"a b\"",   "id:x and id:\"a b\"",         "id:x + id:\"a b\"" },
            new[] { "type:(\"or\" \"x\")",   "type:(\"or\" and x)",         "type:(\"or\" + x)" },
            new[] { "a OR NOT b",            "a or not b",                  "a | -b" },
        };
        Binder.Resolve any = (k, v) => k == "type" ? v.ToUpperInvariant() : null;
        foreach (var c in formats)
        {
            var words = Expr.Format(c[0], any, Formatter.Style.Words);
            var brief = Expr.Format(c[0], any, Formatter.Style.Short);
            Check("format words " + c[0], words == c[1], words);
            Check("format short " + c[0], brief == c[2], brief);
        }

        // 6. Properties over many random trees: for every tree, in both styles,
        //    - the formatted text parses back to the same tree, up to how runs of and/or are grouped and quoting;
        //    - formatting is idempotent, and the two styles format each other's text alike;
        //    - the meaning (the bound Pred) is the tree's meaning.
        var random = new Random(20261106);
        int trees = 2000, broken = 0;
        for (int i = 0; i < trees; i++)
        {
            var tree = RandomTree(random, 4, false);
            var expected = Shape(tree);
            var meaning = Binder.Bind(tree, any);
            foreach (var style in new[] { Formatter.Style.Words, Formatter.Style.Short })
            {
                var text = Formatter.Format(tree, style);
                string problem = null;
                Ast back = null;
                try { back = Parser.Parse(Lexer.Lex(text)); } catch (Expr.CompileError e) { problem = "does not parse: " + e.Message; }
                if (problem == null && Shape(back) != expected) problem = "parses to " + Shape(back) + ", not " + expected;
                if (problem == null && Formatter.Format(back, style) != text) problem = "not idempotent: " + Formatter.Format(back, style);
                var other = style == Formatter.Style.Words ? Formatter.Style.Short : Formatter.Style.Words;
                if (problem == null && Expr.Format(text, any, other) != Formatter.Format(tree, other)) problem = "styles disagree";
                if (problem == null && !Expr.Parse(text, any).Pred.Equals(meaning)) problem = "meaning changed";
                if (problem != null)
                {
                    if (broken++ < 5) Console.WriteLine("FAIL  random tree " + tree + " as " + style + ": " + text + "\n      " + problem);
                }
            }
        }
        Check("random trees: " + trees + " in both styles", broken == 0, broken + " broken");

        // 7. Pred to text to Pred, through Lift and the formatter, in both styles.
        var preds = new List<Pred>();
        for (int i = 0; i < 500; i++) preds.Add(Binder.Bind(RandomTree(random, 4, false), any));
        int lost = 0;
        foreach (var p in preds)
            foreach (var style in new[] { Formatter.Style.Words, Formatter.Style.Short })
            {
                var text = p.ToString(null, style);
                var back = Expr.Parse(text, any);
                if (!back.Ok || !back.Pred.Equals(p)) { if (lost++ < 5) Console.WriteLine("FAIL  pred " + p + " as " + style + ": " + text); }
            }
        Check("pred round trips: 500 in both styles", lost == 0, lost + " lost");

        Console.WriteLine(failed == 0 ? "all passed (" + passed + " checks)" : failed + " failed, " + passed + " passed");
        return failed == 0 ? 0 : 1;
    }

    // A random tree of every kind of node. Words outside a group are keywords or names; inside a group, values,
    // some of which need quotes ("or", "two words").
    static readonly string[] Words = { "stolen", "contraband", "owned", "food", "medical", "x", "y", "食物" };
    static readonly string[] Values = { "a", "b", "or", "and", "not", "two words", "x.y", "食物", "say \"hi\"" };
    static readonly string[] Keys = { "type", "tag", "id", "condition" };

    static Ast RandomTree(Random r, int depth, bool inScope)
    {
        int pick = depth <= 0 ? r.Next(3) : r.Next(7);
        switch (pick)
        {
            case 0:
                if (inScope)
                {
                    var v = Values[r.Next(Values.Length)];
                    return Formatter.Bare(v, true) ? (Ast)new WordAst { Text = v } : new QuotedAst { Text = v };
                }
                return new WordAst { Text = Words[r.Next(Words.Length)] };
            case 1:
                var value = Values[r.Next(Values.Length)];
                return new FieldAst { Key = Keys[r.Next(Keys.Length)], Value = value, Quoted = !Formatter.Bare(value, false) };
            case 2:
                return new WordAst { Text = inScope ? "a" : "stolen" };
            case 3:
                return new NotAst { Operand = RandomTree(r, depth - 1, inScope) };
            case 4:
                return new ScopeAst { Key = Keys[r.Next(Keys.Length)], Body = RandomTree(r, depth - 1, true) };
            case 5:
                var and = new AndAst();
                for (int i = 0, n = 2 + r.Next(2); i < n; i++) and.Parts.Add(RandomTree(r, depth - 1, inScope));
                return and;
            default:
                var or = new OrAst();
                for (int i = 0, n = 2 + r.Next(2); i < n; i++) or.Parts.Add(RandomTree(r, depth - 1, inScope));
                return or;
        }
    }

    // A tree's shape up to what the language says does not matter: runs of one operator are flattened (and, or are
    // associative), a value is the same quoted or not, and keyword case is ignored.
    static string Shape(Ast node)
    {
        var sb = new StringBuilder();
        Shape(sb, node);
        return sb.ToString();
    }

    static void Shape(StringBuilder sb, Ast node)
    {
        var or = node as OrAst;
        var and = node as AndAst;
        if (or != null || and != null)
        {
            sb.Append(or != null ? "(or" : "(and");
            foreach (var p in Flat(node)) { sb.Append(' '); Shape(sb, p); }
            sb.Append(')');
            return;
        }
        var not = node as NotAst;
        if (not != null) { sb.Append("(not "); Shape(sb, not.Operand); sb.Append(')'); return; }
        var scope = node as ScopeAst;
        if (scope != null) { sb.Append("(scope ").Append(scope.Key.ToLowerInvariant()).Append(' '); Shape(sb, scope.Body); sb.Append(')'); return; }
        var field = node as FieldAst;
        if (field != null) { sb.Append("(field ").Append(field.Key.ToLowerInvariant()).Append(" [").Append(field.Value).Append("])"); return; }
        var quoted = node as QuotedAst;
        sb.Append('[').Append(quoted != null ? quoted.Text : ((WordAst)node).Text).Append(']');
    }

    static IEnumerable<Ast> Flat(Ast node)
    {
        var parts = node is OrAst ? ((OrAst)node).Parts : ((AndAst)node).Parts;
        foreach (var p in parts)
        {
            if (p.GetType() == node.GetType()) foreach (var q in Flat(p)) yield return q;
            else yield return p;
        }
    }
}
