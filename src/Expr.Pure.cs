using System;

namespace DemandHighlight
{
    // The Item Filter's language, built like a small compiler, one stage per file:
    //
    //   text --Lexer--> tokens --Parser--> Ast --Binder--> Pred
    //                                       |               |
    //                                       |     Lift <----+
    //                                       v      |
    //                                   Formatter <+--> text
    //
    //   Lexer.Pure.cs  Parser.Pure.cs  Ast.Pure.cs  Binder.Pure.cs  Query.Pure.cs (Pred)  Lift.Pure.cs  Formatter.Pure.cs
    //
    // The lexer knows characters, the parser grammar, the binder meaning (keywords, keys, the player's type names).
    // Pred is the intermediate form everything else works on: evaluation and the window's ticks (Facets.Pure.cs).
    // The formatter writes trees in the one canonical form; Lift turns a Pred back into a tree for it. This file runs
    // the stages and turns the first error into a result: the text never throws.
    //
    //   type:食物 and not stolen          (type:FOOD or type:MEDICAL) owned          -contraband "id:two words"
    //
    // Plain data only, so tests/ExprTest.cs runs it outside the game.
    public static class Expr
    {
        // What went wrong, so the window can say it in the player's language. Detail holds the word or character.
        public enum Problem { None, ExpectedTerm, ExpectedClose, ExpectedValue, UnknownKey, UnknownWord, Unexpected, UnclosedQuote }

        // An error from any stage, with where it is. Thrown inside the stages, caught by Parse.
        public sealed class CompileError : Exception
        {
            public readonly Problem Problem;
            public readonly string Detail;
            public readonly Span Span;

            public CompileError(Problem problem, string detail, Span span) : base(Describe(problem, detail ?? ""))
            {
                Problem = problem; Detail = detail ?? ""; Span = span;
            }
        }

        public sealed class Parsed
        {
            public Pred Pred;        // null when the text has an error
            public string Error;     // what is wrong, in English, for the log; null when fine
            public Problem Problem;  // the same, as a code
            public string Detail;    // the word or character the error is about, or ""
            public Span Span;        // where in the text the error is
            public int Position { get { return Span.Start; } }
            public bool Ok { get { return Pred != null; } }
        }

        // Empty text is "true" (no condition): the caller decides whether that means "no expression".
        public static Parsed Parse(string text, Binder.Resolve resolve = null)
        {
            try
            {
                var tokens = Lexer.Lex(text ?? "");
                var tree = Parser.Parse(tokens);
                return new Parsed { Pred = Binder.Bind(tree, resolve) };
            }
            catch (CompileError e)
            {
                return new Parsed { Error = e.Message, Problem = e.Problem, Detail = e.Detail, Span = e.Span };
            }
        }

        // Typed text in the canonical form, keeping what it says and how it is built; null when it does not read
        // (resolve as for Parse, so only text that binds is formatted).
        public static string Format(string text, Binder.Resolve resolve = null, Formatter.Style style = Formatter.Style.Words)
        {
            try
            {
                var tree = Parser.Parse(Lexer.Lex(text ?? ""));
                Binder.Bind(tree, resolve);
                return Formatter.Format(tree, style);
            }
            catch (CompileError) { return null; }
        }

        static string Describe(Problem problem, string detail)
        {
            switch (problem)
            {
                case Problem.ExpectedTerm: return "expected a term";
                case Problem.ExpectedClose: return "expected ')'";
                case Problem.ExpectedValue: return "expected a value after '" + detail + ":'";
                case Problem.UnknownKey: return "unknown key '" + detail + "'";
                case Problem.UnknownWord: return "unknown word '" + detail + "'";
                case Problem.UnclosedQuote: return "unclosed quote";
                default: return "unexpected '" + detail + "'";
            }
        }
    }
}
