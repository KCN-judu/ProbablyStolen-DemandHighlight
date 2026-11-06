using System.Text;

namespace DemandHighlight
{
    // The filter language's formatter: a syntax tree written in the one canonical form. It works on the
    // tree, not the meaning, so text the player typed keeps its structure and order and only its spelling is made
    // regular; text made from a Pred is lifted to a tree first (Lift.Pure.cs) and written the same way.
    //
    //   text -> Lexer -> Parser -> Ast -> [Formatter] -> text
    //   Pred -> Lift -> Ast -> [Formatter] -> text
    //
    // Two styles, the same in everything but the operators:
    //   Words  not a and (b or c)        and, or, not, lower case
    //   Short  -a + (b | c)              +, |, - (no space after -)
    // In both:
    //   - one space around and/or, none inside brackets or around the colon
    //   - brackets wherever "and" and "or" meet, and around an "and" or "or" after "not"; nowhere else, so
    //     brackets the player added that change nothing go
    //   - keys and keywords lower case, in their one spelling: cond is condition, mine is owned, STOLEN is stolen
    //   - values as written, quoted only when they must be (a space or symbol; inside a group, also and/or/not)
    //   - a group keeps its key once: type:(Food or "Household Good")
    // Properties, checked in tests/ExprTest.cs: formatting twice gives the same text, and formatted text means what
    // the original meant.
    public static class Formatter
    {
        public enum Style { Words, Short }

        public static string Format(Ast node, Style style = Style.Words)
        {
            if (node == null) return "";
            var sb = new StringBuilder();
            new Writer { Sb = sb, Short = style == Style.Short }.Write(node, Context.Top, false);
            return sb.ToString();
        }

        // Where a node stands, for brackets.
        enum Context { Top, InOr, InAnd, InNot }

        sealed class Writer
        {
            public StringBuilder Sb;
            public bool Short;

            StringBuilder sb { get { return Sb; } }

            public void Write(Ast node, Context where, bool inScope)
            {
                var or = node as OrAst;
                if (or != null)
                {
                    bool paren = where == Context.InAnd || where == Context.InNot;
                    if (paren) sb.Append('(');
                    for (int i = 0; i < or.Parts.Count; i++)
                    {
                        if (i > 0) sb.Append(Short ? " | " : " or ");
                        Write(or.Parts[i], Context.InOr, inScope);
                    }
                    if (paren) sb.Append(')');
                    return;
                }
                var and = node as AndAst;
                if (and != null)
                {
                    bool paren = where == Context.InOr || where == Context.InNot;
                    if (paren) sb.Append('(');
                    for (int i = 0; i < and.Parts.Count; i++)
                    {
                        if (i > 0) sb.Append(Short ? " + " : " and ");
                        Write(and.Parts[i], Context.InAnd, inScope);
                    }
                    if (paren) sb.Append(')');
                    return;
                }
                var not = node as NotAst;
                if (not != null)
                {
                    sb.Append(Short ? "-" : "not ");
                    Write(not.Operand, Context.InNot, inScope);
                    return;
                }
                var scope = node as ScopeAst;
                if (scope != null)
                {
                    sb.Append(Key(scope.Key)).Append(":(");
                    Write(scope.Body, Context.Top, true);
                    sb.Append(')');
                    return;
                }
                var field = node as FieldAst;
                if (field != null)
                {
                    sb.Append(Key(field.Key)).Append(':');
                    Formatter.Value(sb, field.Value, false);
                    return;
                }
                var quoted = node as QuotedAst;
                if (quoted != null)
                {
                    Formatter.Value(sb, quoted.Text, inScope);
                    return;
                }
                var word = (WordAst)node;
                sb.Append(inScope ? word.Text : Keyword(word.Text));
            }
        }

        // A key in its one spelling; an unknown key as written (the binder reports it).
        static string Key(string key)
        {
            var k = key.ToLowerInvariant();
            switch (k)
            {
                case "cond": return "condition";
                case "type": case "tag": case "id": case "condition": return k;
                default: return key;
            }
        }

        // A word on its own: a keyword in its one spelling, anything else (a type's name) as written.
        static string Keyword(string word)
        {
            var w = word.ToLowerInvariant();
            switch (w)
            {
                case "mine": return "owned";
                case "owned": case "stolen": case "contraband": case "true": case "false": return w;
                default: return word;
            }
        }

        static void Value(StringBuilder sb, string text, bool inScope)
        {
            if (Bare(text, inScope)) { sb.Append(text); return; }
            sb.Append('"').Append(text.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
        }

        // Whether a value reads back as itself without quotes: one word for the lexer and, inside a group where words
        // are values, not one of the operators.
        public static bool Bare(string text, bool inScope)
        {
            if (text.Length == 0) return false;
            foreach (var ch in text) if (!Lexer.IsWordChar(ch)) return false;
            if (!inScope) return true;
            var w = text.ToLowerInvariant();
            return w != "and" && w != "or" && w != "not";
        }
    }
}
