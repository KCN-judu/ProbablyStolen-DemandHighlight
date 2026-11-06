using System.Collections.Generic;

namespace DemandHighlight
{
    // The second stage of the filter language: tokens to a syntax tree (Ast). Grammar only; meaning is the binder's.
    //
    //   Lexer -> [Parser] -> Ast -> Binder -> Pred (-> Lift -> Ast -> Formatter -> text)
    //
    //   expr  = or
    //   or    = and { ("or" | "|") and }
    //   and   = not { ["and" | "+"] not }        two terms side by side mean "and", as in a search box
    //   not   = ("not" | "-") not | atom
    //   atom  = "(" expr ")" | scope | field | word | quoted
    //   scope = word ":" "(" expr ")"             the key over a group: type:(Food or Medical)
    //   field = word ":" (word | quoted)          no space around the colon
    //
    // Precedence, tightest first (doc/manual/filter-language.md has the full rules):
    //   1  atoms: key:value, key:( ... ), ( ... ), words, quoted text
    //   2  not, -     prefix, right to left: "not not a" is not (not a)
    //   3  and, +, side by side     n-ary, read left to right
    //   4  or, |                    n-ary, read left to right
    // "and" and "or" are associative, so how a run of one of them is grouped never changes what it means; the tree
    // keeps a run as one node with its parts in order. and/or/not are operators only as whole words: "orange" and
    // "notebook" are words, "Or" is or.
    //
    // The first error stops the parse; it is thrown as Expr.CompileError and Expr.Parse turns it into a result.
    public sealed class Parser
    {
        readonly List<Token> tokens;
        int at;

        Parser(List<Token> tokens) { this.tokens = tokens; }

        // Null for text with nothing in it.
        public static Ast Parse(List<Token> tokens)
        {
            var p = new Parser(tokens);
            if (p.Peek.Kind == TokenKind.End) return null;
            var tree = p.Or();
            if (p.Peek.Kind != TokenKind.End) throw Unexpected(p.Peek);
            return tree;
        }

        Token Peek { get { return tokens[at]; } }
        Token Next() { return tokens[at++]; }

        Ast Or()
        {
            var first = And();
            if (!AtOr) return first;
            var node = new OrAst();
            node.Parts.Add(first);
            while (AtOr)
            {
                Next();
                node.Parts.Add(And());
            }
            node.Span = Span.Between(first.Span, node.Parts[node.Parts.Count - 1].Span);
            return node;
        }

        bool AtOr { get { return Peek.Kind == TokenKind.Bar || Peek.Is("or"); } }

        Ast And()
        {
            var first = Not();
            var node = new AndAst();
            node.Parts.Add(first);
            while (true)
            {
                var t = Peek;
                if (t.Kind == TokenKind.End || t.Kind == TokenKind.Close || AtOr) break;
                if (t.Kind == TokenKind.Plus || t.Is("and")) Next();
                node.Parts.Add(Not());
            }
            if (node.Parts.Count == 1) return first;
            node.Span = Span.Between(first.Span, node.Parts[node.Parts.Count - 1].Span);
            return node;
        }

        Ast Not()
        {
            var t = Peek;
            if (t.Kind == TokenKind.Minus || t.Is("not"))
            {
                Next();
                var operand = Not();
                return new NotAst { Operand = operand, Span = Span.Between(t.Span, operand.Span) };
            }
            return Atom();
        }

        Ast Atom()
        {
            var t = Next();
            switch (t.Kind)
            {
                case TokenKind.End:
                    at--;
                    throw Error(Expr.Problem.ExpectedTerm, "", t.Span.Start);
                case TokenKind.Open:
                    var inner = Or();
                    var close = Peek;
                    if (close.Kind != TokenKind.Close) throw Error(Expr.Problem.ExpectedClose, "", close.Span.Start);
                    Next();
                    inner.Span = Span.Between(t.Span, close.Span);
                    return inner;
                case TokenKind.Word:
                    var colon = Peek;
                    if (colon.Kind != TokenKind.Colon || colon.Span.Start != t.Span.End)
                    {
                        // and/or are reserved: where a term is expected they are a mistake ("a and or b"), never a
                        // word. Quoted, inside a group, they are values: type:("or").
                        if (t.Is("and") || t.Is("or")) throw Unexpected(t);
                        return new WordAst { Text = t.Text, Span = t.Span };
                    }
                    Next();
                    var value = Peek;
                    // key:( ... ): the key over a whole group.
                    if (value.Kind == TokenKind.Open && value.Span.Start == colon.Span.End)
                    {
                        Next();
                        var body = Or();
                        var end = Peek;
                        if (end.Kind != TokenKind.Close) throw Error(Expr.Problem.ExpectedClose, "", end.Span.Start);
                        Next();
                        return new ScopeAst { Key = t.Text, KeySpan = t.Span, Body = body, Span = Span.Between(t.Span, end.Span) };
                    }
                    // An unclosed quote right after the colon is that, not a missing value.
                    if (value.Kind == TokenKind.Bad && value.Problem == Expr.Problem.UnclosedQuote) throw Unexpected(value);
                    if ((value.Kind != TokenKind.Word && value.Kind != TokenKind.Quoted) || value.Span.Start != colon.Span.End)
                        throw Error(Expr.Problem.ExpectedValue, t.Text, colon.Span.End);
                    Next();
                    return new FieldAst
                    {
                        Key = t.Text, KeySpan = t.Span, Value = value.Text, ValueSpan = value.Span,
                        Quoted = value.Kind == TokenKind.Quoted, Span = Span.Between(t.Span, value.Span),
                    };
                case TokenKind.Quoted:
                    // Grammar allows quoted text anywhere; only inside key:( ... ) does it mean something (Binder).
                    return new QuotedAst { Text = t.Text, Span = t.Span };
                default:
                    throw Unexpected(t);
            }
        }

        static Expr.CompileError Unexpected(Token t)
        {
            return Error(t.Kind == TokenKind.Bad ? t.Problem : Expr.Problem.Unexpected, t.Text, t.Span.Start);
        }

        static Expr.CompileError Error(Expr.Problem problem, string detail, int position)
        {
            return new Expr.CompileError(problem, detail, new Span(position, 0));
        }
    }
}
