// Demand Highlight's query API, for other mods: find the items of the shop that match an Item Filter expression,
// such as  type:(food or medical) not stolen  , without the highlight or the filter window.
//
// Copy this file into your mod. It needs no reference to Demand Highlight, and works the same whether or not
// Demand Highlight is installed: without it (or with a version whose contract this file does not know), every call
// says NotInstalled and nothing else happens.
//
//   var found = new List<GameItem>();
//   var r = DemandHighlightQuery.Run("type:(food or medical) not stolen", found);
//   if (r.Ok) Debug.Log("[MyMod] " + r.Count + " items: " + r.Text);
//   else if (r.Status == DemandHighlightQuery.Status.Invalid) Debug.Log("[MyMod] at " + r.Position + ": " + r.Message);
//
// Rules, from Demand Highlight's side:
//   - Call from the game's main thread (Unity's), as any game code. Another thread gets WrongThread.
//   - Items belong to the shop. Use them while it is loaded and let them go when the scene changes: keeping one
//     keeps the whole store in memory. Run clears your list first; reuse it between calls.
//   - Each Run walks the shop once, closed containers included. Nothing is cached between calls, so run it when you
//     need an answer, not every frame.
//   - The language is the Item Filter's (Demand Highlight's manual, "Filter language"). Type names are read in the
//     player's language too, so prefer identifiers (type:FOOD) in code; Field and Quote below build terms safely.
//
// How it works: Demand Highlight puts two delegates and a version string in slots of the AppDomain, which every
// mod's assembly shares, when it is enabled, and takes them out when it is disabled. This file reads them on each
// call. Contract 1.x: a result row is object[] { int status, int count, string problem, string detail,
// int position, int length, string message, string text }; later minor versions only add fields at the end.
using System;
using System.Collections.Generic;
using System.Text;

public static class DemandHighlightQuery
{
    const string VersionSlot = "demand_highlight.query.version";
    const string RunSlot = "demand_highlight.query.v1.run";
    const string FormatSlot = "demand_highlight.query.v1.format";

    public enum Status
    {
        Ok = 0,
        Invalid = 1,        // the expression does not compile; Problem, Detail, Position and Length say why
        NotReady = 2,       // no shop is loaded (main menu, loading screen)
        Failed = 3,         // the expression compiled, but evaluating it failed in the game; Message says how
        BadArgument = 4,    // the expression is null or blank
        WrongThread = 5,    // not called from the game's main thread
        NotInstalled = 100, // Demand Highlight is not enabled, or offers no contract this file knows
    }

    public sealed class Result
    {
        public Status Status;
        public int Count;          // Run: how many items matched
        public string Problem;     // Invalid: ExpectedTerm, ExpectedClose, ExpectedValue, UnknownKey, UnknownWord,
                                   // Unexpected or UnclosedQuote
        public string Detail;      // Invalid: the word or character the problem is about
        public int Position = -1;  // Invalid: where in the expression (0-based)
        public int Length;         // Invalid: how many characters
        public string Message;     // what went wrong, in English, for logs
        public string Text;        // the expression in canonical form (Run: words; Format: the style asked for)
        public bool Ok { get { return Status == Status.Ok; } }
    }

    // The contract's version ("1.0"), or null when Demand Highlight is not there.
    public static string Version
    {
        get
        {
            var v = AppDomain.CurrentDomain.GetData(VersionSlot) as string;
            return v != null && v.StartsWith("1.", StringComparison.Ordinal) ? v : null;
        }
    }

    public static bool Available { get { return Version != null && AppDomain.CurrentDomain.GetData(RunSlot) is Func<string, List<GameItem>, object[]>; } }

    // The items of the shop that match the expression, added to `into` (cleared first). `into` may be null to only
    // count.
    public static Result Run(string expression, List<GameItem> into)
    {
        var run = Version != null ? AppDomain.CurrentDomain.GetData(RunSlot) as Func<string, List<GameItem>, object[]> : null;
        if (run == null)
        {
            if (into != null) into.Clear();
            return NotInstalled();
        }
        return Read(run(expression, into));
    }

    // The expression written in its canonical form: and/or/not in words, or +, |, - when shortForm is true.
    public static Result Format(string expression, bool shortForm)
    {
        var format = Version != null ? AppDomain.CurrentDomain.GetData(FormatSlot) as Func<string, bool, object[]> : null;
        if (format == null) return NotInstalled();
        return Read(format(expression, shortForm));
    }

    // key:value with the value quoted when the language needs it, e.g. Field("id", "two words") is id:"two words".
    public static string Field(string key, string value) { return key + ":" + Quote(value); }

    // A value as the language reads it back unchanged: bare when it is one word, quoted otherwise (and always quoted
    // when it is and, or or not, which inside a group would be operators).
    public static string Quote(string value)
    {
        if (value == null) value = "";
        bool bare = value.Length > 0;
        foreach (var c in value)
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '\'')) { bare = false; break; }
        var lower = value.ToLowerInvariant();
        if (lower == "and" || lower == "or" || lower == "not") bare = false;
        if (bare) return value;
        var sb = new StringBuilder("\"");
        foreach (var c in value)
        {
            if (c == '"' || c == '\\') sb.Append('\\');
            sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    static Result NotInstalled()
    {
        return new Result { Status = Status.NotInstalled, Message = "Demand Highlight's query API 1.x is not available" };
    }

    static Result Read(object[] row)
    {
        if (row == null || row.Length < 8) return new Result { Status = Status.Failed, Message = "unexpected answer from Demand Highlight" };
        return new Result
        {
            Status = (Status)(int)row[0],
            Count = (int)row[1],
            Problem = row[2] as string,
            Detail = row[3] as string,
            Position = (int)row[4],
            Length = (int)row[5],
            Message = row[6] as string,
            Text = row[7] as string,
        };
    }
}
