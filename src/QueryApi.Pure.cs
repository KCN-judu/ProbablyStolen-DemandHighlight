using System;
using System.Collections.Generic;

namespace DemandHighlight
{
    // The public query API's contract and its host side, as plain data, so tests/QueryApiTest.cs runs the same code
    // the game does, with stand-in items.
    //
    // Other mods cannot reference this mod's assembly (the game resolves a mod's references inside that mod's own
    // folder only), so the API is published the way the collection's other cross-mod contracts are: delegates of
    // .NET and game types only, in slots of the AppDomain every mod shares, read through a small file the other mod
    // copies in (api/DemandHighlightQuery.cs). The slots:
    //
    //   demand_highlight.query.version   string     "1.0": major.minor of this contract; the slot names carry the major
    //   demand_highlight.query.v1.run    Func<string, List<GameItem>, object[]>
    //   demand_highlight.query.v1.format Func<string, bool, object[]>
    //
    // run compiles the expression with the Item Filter's language (Lexer, Parser, Binder) and evaluates the predicate
    // on every item of the shop, closed containers included, adding the matches to the caller's list (cleared
    // first; null to only count). format writes an expression in its canonical form, words (false) or short (true).
    // Both answer with the same row:
    //
    //   [0] int    status     Status below
    //   [1] int    count      items that matched (run), 0 otherwise
    //   [2] string problem    for Invalid: the problem's name (Expr.Problem), else null
    //   [3] string detail     the word or character the problem is about, else null
    //   [4] int    position   where in the text the problem is (0-based), else -1
    //   [5] int    length     how many characters it covers, else 0
    //   [6] string message    what went wrong, in English, for logs; null on success
    //   [7] string text       the expression in canonical form (words) when it compiled, else null
    //
    // A new field goes at the end and raises the minor version; a changed meaning raises the major one and gets new
    // slot names, so a client written for v1 keeps working or is told plainly that the API is not there.
    public static class QueryApi
    {
        public const string VersionSlot = "demand_highlight.query.version";
        public const string RunSlot = "demand_highlight.query.v1.run";
        public const string FormatSlot = "demand_highlight.query.v1.format";
        public const string Version = "1.0";
        public const int Fields = 8;

        public enum Status
        {
            Ok = 0,
            Invalid = 1,       // the expression does not compile: problem, detail, position and length say why
            NotReady = 2,      // no shop is loaded (the main menu, a loading screen)
            Failed = 3,        // the expression compiled, but evaluating it failed in the game; message says how
            BadArgument = 4,   // the expression is null or blank
            WrongThread = 5,   // called from a thread other than the game's main thread
        }

        // How the host reaches the game; the game side fills this in, tests give stand-ins.
        public sealed class Host<T> where T : class
        {
            public Binder.Resolve Resolve;            // type names as the player writes them
            public Func<bool> Ready;                  // a shop is loaded
            public Func<bool> OnMainThread;           // the caller is on the game's main thread
            public Action<Action<T>> EachItem;        // every item of the shop, each once
            public Func<T, IItem> View;               // an item as the predicate reads it
        }

        public static object[] Run<T>(Host<T> host, string expression, List<T> into) where T : class
        {
            if (into != null) into.Clear();
            if (!host.OnMainThread()) return Row(Status.WrongThread, "call the query API from the game's main thread");
            if (expression == null || expression.Trim().Length == 0) return Row(Status.BadArgument, "the expression is empty");
            var parsed = Expr.Parse(expression, host.Resolve);
            if (!parsed.Ok) return Invalid(parsed);
            if (!host.Ready()) return Row(Status.NotReady, "no shop is loaded");
            var pred = parsed.Pred;
            int count = 0;
            try
            {
                host.EachItem(item =>
                {
                    if (!pred.Eval(host.View(item))) return;
                    count++;
                    if (into != null) into.Add(item);
                });
            }
            catch (Exception e)
            {
                if (into != null) into.Clear();
                return Row(Status.Failed, "the query failed in the game: " + e.GetType().Name + ": " + e.Message);
            }
            var row = Row(Status.Ok, null);
            row[1] = count;
            row[7] = pred.ToString();
            return row;
        }

        public static object[] Format(Binder.Resolve resolve, Func<bool> onMainThread, string expression, bool shortForm)
        {
            if (!onMainThread()) return Row(Status.WrongThread, "call the query API from the game's main thread");
            if (expression == null || expression.Trim().Length == 0) return Row(Status.BadArgument, "the expression is empty");
            var parsed = Expr.Parse(expression, resolve);
            if (!parsed.Ok) return Invalid(parsed);
            var row = Row(Status.Ok, null);
            row[7] = Expr.Format(expression, resolve, shortForm ? Formatter.Style.Short : Formatter.Style.Words);
            return row;
        }

        // The delegates as they go into the slots. They never throw: anything unexpected (the game failing while a
        // type name is looked up, say) is a Failed row, so a caller's mod is never broken by this one.
        public static Func<string, List<T>, object[]> RunDelegate<T>(Host<T> host) where T : class
        {
            return (expression, into) =>
            {
                try { return Run(host, expression, into); }
                catch (Exception e)
                {
                    if (into != null) into.Clear();
                    return Row(Status.Failed, "the query failed: " + e.GetType().Name + ": " + e.Message);
                }
            };
        }

        public static Func<string, bool, object[]> FormatDelegate<T>(Host<T> host) where T : class
        {
            return (expression, shortForm) =>
            {
                try { return Format(host.Resolve, host.OnMainThread, expression, shortForm); }
                catch (Exception e) { return Row(Status.Failed, "formatting failed: " + e.GetType().Name + ": " + e.Message); }
            };
        }

        static object[] Invalid(Expr.Parsed p)
        {
            var row = Row(Status.Invalid, p.Error);
            row[2] = p.Problem.ToString();
            row[3] = p.Detail ?? "";
            row[4] = p.Span.Start;
            row[5] = p.Span.Length;
            return row;
        }

        static object[] Row(Status status, string message)
        {
            return new object[] { (int)status, 0, null, null, -1, 0, message, null };
        }
    }
}
