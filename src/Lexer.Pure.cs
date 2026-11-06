using System.Collections.Generic;
using System.Text;

namespace DemandHighlight
{
    // Where in the text a token or a node is: from Start, Length characters. End is one past the last.
    public struct Span
    {
        public readonly int Start, Length;
        public Span(int start, int length) { Start = start; Length = length; }
        public int End { get { return Start + Length; } }
        public static Span Between(Span a, Span b) { return new Span(a.Start, b.End - a.Start); }
    }

    public enum TokenKind
    {
        Word,       // letters (any script), digits, _ . '      stolen, type, 食物, Luxury
        Quoted,     // "text", with \" and \\ inside              "two words"
        Colon,      // :
        Open,       // (
        Close,      // )
        Bar,        // |   or
        Plus,       // +   and
        Minus,      // -   not
        End,        // after the last token
        Bad,        // a character the language has no use for, or an unclosed quote; Problem says which
    }

    public sealed class Token
    {
        public TokenKind Kind;
        public string Text;             // a word as written; a quoted text without its quotes; the character for symbols
        public Span Span;
        public Expr.Problem Problem;    // for Bad only

        // A word that is this keyword: and, or, not. Not case-sensitive; "orange" is a word, not "or".
        public bool Is(string keyword)
        {
            return Kind == TokenKind.Word && string.Equals(Text, keyword, System.StringComparison.OrdinalIgnoreCase);
        }
    }

    // The first stage of the filter language: text to tokens, each with its place in the text.
    //
    //   Lexer -> Parser -> Binder -> Pred (-> Lift -> Ast -> Formatter -> text)
    //
    // The lexer knows characters only. It never fails: what it cannot read becomes a Bad token, and the parser
    // reports it when it gets there, so the error shown is always the first one in the text.
    public static class Lexer
    {
        // Letters (any script, so localized names work), digits, '_', '.' and '\''.
        public static bool IsWordChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '\'';
        }

        public static List<Token> Lex(string s)
        {
            var tokens = new List<Token>();
            int i = 0;
            while (true)
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                if (i >= s.Length)
                {
                    tokens.Add(new Token { Kind = TokenKind.End, Text = "", Span = new Span(s.Length, 0) });
                    return tokens;
                }
                int start = i;
                char c = s[i];
                if (IsWordChar(c))
                {
                    while (i < s.Length && IsWordChar(s[i])) i++;
                    tokens.Add(new Token { Kind = TokenKind.Word, Text = s.Substring(start, i - start), Span = new Span(start, i - start) });
                    continue;
                }
                if (c == '"')
                {
                    i++;
                    var sb = new StringBuilder();
                    bool closed = false;
                    while (i < s.Length)
                    {
                        char d = s[i++];
                        if (d == '"') { closed = true; break; }
                        if (d == '\\' && i < s.Length) d = s[i++];
                        sb.Append(d);
                    }
                    tokens.Add(closed
                        ? new Token { Kind = TokenKind.Quoted, Text = sb.ToString(), Span = new Span(start, i - start) }
                        // Reported where the text ends, the place the closing quote is missing from.
                        : new Token { Kind = TokenKind.Bad, Text = "\"", Span = new Span(s.Length, 0), Problem = Expr.Problem.UnclosedQuote });
                    continue;
                }
                i++;
                var kind = Symbol(c);
                tokens.Add(new Token
                {
                    Kind = kind, Text = c.ToString(), Span = new Span(start, 1),
                    Problem = kind == TokenKind.Bad ? Expr.Problem.Unexpected : Expr.Problem.None,
                });
            }
        }

        static TokenKind Symbol(char c)
        {
            switch (c)
            {
                case ':': return TokenKind.Colon;
                case '(': return TokenKind.Open;
                case ')': return TokenKind.Close;
                case '|': return TokenKind.Bar;
                case '+': return TokenKind.Plus;
                case '-': return TokenKind.Minus;
                default: return TokenKind.Bad;
            }
        }
    }
}
