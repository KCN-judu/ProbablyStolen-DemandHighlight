using System;
using System.Collections.Generic;

namespace DemandHighlight
{
    // The Item Filter's state: one expression, the one source of truth. The text box shows it; the window's ticks are
    // read from it and edit it (Facets.Pure.cs), and then it is written back into the box. GameIntent turns it into a
    // query, which replaces the customer highlight while it asks for anything; Engine answers it like any other.
    //
    // Also counts the shop for the window. Holds no game object: only text, predicates, type names and counters, so
    // nothing here has to be released when the store is left.
    public static class Filter
    {
        // The text as written and how it read. Text that does not read keeps the last expression that did, so a
        // half-typed word does not blank the shop, and a tick then edits that last expression.
        static string text = "";
        static Expr.Parsed parsed = Expr.Parse("");
        static Pred expression;
        static Facets facets = Facets.Read(null);

        // Bumped on every change of the expression; the query and the window's counts follow it.
        public static int Version { get; private set; }

        // Bumped when items enter or leave inventories or their tags change, so the counts are redone only then.
        public static int StockVersion;
        static Counts counts;
        static int countsVersion = -1;

        public static bool Active { get { return expression != null; } }
        public static string Text { get { return text; } }
        public static Expr.Parsed Parsed { get { return parsed; } }
        public static Pred Expression { get { return expression; } }
        // Each tick's state, read from the expression: off, in (✓) or out (✗).
        public static Facets.State Stolen { get { return facets.Of(Pred.Stolen); } }
        public static Facets.State Contraband { get { return facets.Of(Pred.Contraband); } }
        public static Facets.State OfType(string type) { return facets.OfType(type); }

        // The player typed: the text is compiled, and when it reads it becomes the expression.
        public static void SetText(string value)
        {
            value = value ?? "";
            if (value == text) return;
            text = value;
            parsed = Expr.Parse(text, ResolveType);
            if (parsed.Ok) Set(parsed.Pred);
        }

        // How the filter is written back: and/or/not in words, or +, |, - (the window's "short" tick).
        public static Formatter.Style Style = Formatter.Style.Words;

        // A new style rewrites the text at once, when it reads. True when the text changed.
        public static bool SetStyle(Formatter.Style style)
        {
            if (style == Style) return false;
            Style = style;
            return Format();
        }

        // The typed text in its canonical form (Formatter), keeping how it is built. True when the text changed;
        // text with an error is left alone. The meaning cannot change, so the expression stays as it is.
        public static bool Format()
        {
            if (!parsed.Ok) return false;
            var formatted = Expr.Format(text, ResolveType, Style);
            if (formatted == null || formatted == text) return false;
            text = formatted;
            parsed = Expr.Parse(text, ResolveType);
            return true;
        }

        // A tick: the expression is edited and written back as text, formatted, in the window's own form (type names
        // in the player's language, one group per facet: stolen and type:(Food or Medical)).
        public static void SetType(string type, Facets.State state) { Tick(Pred.Type(type), state); }
        public static void SetStolen(Facets.State state) { Tick(Pred.Stolen, state); }
        public static void SetContraband(Facets.State state) { Tick(Pred.Contraband, state); }

        static void Tick(Pred atom, Facets.State state)
        {
            Set(Facets.Set(expression, atom, state));
            text = expression == null ? "" : expression.ToString(TypeText, Style);
            parsed = Expr.Parse(text, ResolveType);
        }

        public static void Clear()
        {
            text = "";
            parsed = Expr.Parse("");
            Set(null);
        }

        static void Set(Pred next)
        {
            if (next != null && next.Equals(Pred.True)) next = null;
            if (next == null ? expression == null : next.Equals(expression)) return;
            expression = next;
            facets = Facets.Read(expression);
            Version++;
        }

        // A type's name as the window writes it: its name in the current language when that reads back to the same
        // type, null (the identifier) otherwise. The formatter puts it after type:.
        public static string TypeText(string type)
        {
            return Lift.TypeName(type, Panel.TypeName(type), ResolveType);
        }

        public static FilterView View() { return new FilterView { Expression = expression }; }

        // A type as the player wrote it: the game's identifier in any case, with spaces or underscores, or the type's
        // name in the current language ("食物", "luxury item"). Null when no type is called that.
        static Dictionary<string, string> names;
        static string namesFor;

        public static string ResolveType(string key, string value)
        {
            if (key != "type" || string.IsNullOrEmpty(value)) return null;
            // Built again when the stock changes (new types) or the language does (the name of one type tells).
            var current = StockVersion + "|" + Panel.TypeName("FOOD");
            if (names == null || namesFor != current)
            {
                namesFor = current;
                names = new Dictionary<string, string>();
                var all = TypeHelper.ALL_TYPES_LIST;
                if (all != null) foreach (var t in all) if (!string.IsNullOrEmpty(t)) AddName(t, t);
                foreach (var t in Panel.KnownTypes()) AddName(t, t);
                if (counts != null) foreach (var t in counts.Present.Keys) AddName(t, t);
                foreach (var t in new List<string>(names.Values)) AddName(Panel.TypeName(t), t);
            }
            string id;
            return names.TryGetValue(Squash(value), out id) ? id : null;
        }

        static void AddName(string name, string type)
        {
            var key = Squash(name);
            if (key.Length > 0 && !names.ContainsKey(key)) names[key] = type;
        }

        // Lower case without spaces or underscores, so "Luxury Item", "luxury_item" and "LUXURYITEM" are one name.
        static string Squash(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var c in s) if (c != ' ' && c != '_') sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        public sealed class Counts
        {
            public readonly Dictionary<string, int> Types = new Dictionary<string, int>();     // with the rest of the filter applied
            public readonly Dictionary<string, int> TypesOut = new Dictionary<string, int>();  // the same without the types' exclusions, for ✗ ticks
            public readonly Dictionary<string, int> Present = new Dictionary<string, int>();   // the whole shop, for the layout
            public int Stolen, Contraband, Items;
            public int StolenOut, ContrabandOut;   // without the status exclusions, for ✗ ticks
            public Totals Shop, Matched;   // Matched only means something while Filtering
            public bool Filtering;
            public bool Totalled;          // false when the numbers option is off: Shop and Matched are left empty
        }

        static int countsFilter = -1;

        // Everything in the shop, closed containers included: counted by type for the window, and totalled (items,
        // worth, cells) for the whole shop and for what the filter matches. One walk does both; it is redone when the
        // stock or the filter changed, at most once per call site's own pace.
        public static Counts Count()
        {
            bool totals = Panel.Numbers();
            if (counts != null && countsVersion == StockVersion && countsFilter == Version && counts.Totalled == totals) return counts;
            countsVersion = StockVersion;
            countsFilter = Version;
            counts = new Counts { Totalled = totals };
            if (!Shop.Loaded) return counts;
            counts.Filtering = expression != null;
            // A facet's counts are what its ticks would add, with every other part of the filter applied: tick Stolen
            // and each type shows how many stolen items it has. True (no other part) skips the check.
            var walk = new Walk
            {
                Counts = counts,
                ForStatus = Useful(facets.AllBut(Facets.Part.Status)),
                ForTypes = Useful(facets.AllBut(Facets.Part.Types)),
                ForStatusOut = Useful(facets.AllBut(Facets.Part.Status, true)),
                ForTypesOut = Useful(facets.AllBut(Facets.Part.Types, true)),
                Match = expression,
                View = new LiveItem(),
            };
            Shop.Each(walk.Item);
            walk.View.Item = null;
            return counts;
        }

        static Pred Useful(Pred p) { return p == null || p.Equals(Pred.True) ? null : p; }

        // What one walk of the shop (Shop.Each) adds up, item by item.
        sealed class Walk
        {
            public Counts Counts;
            public Pred ForStatus, ForTypes, ForStatusOut, ForTypesOut, Match;
            public LiveItem View;
            public void Item(GameItem item)
            {
                var c = Counts;
                c.Items++;
                View.Item = item;
                bool contraband = ContrabandHelper.IsContraband(item), stolen = StolenHelper.IsStolenItem(item);
                if (contraband || stolen)
                {
                    if (ForStatus == null || ForStatus.Eval(View))
                    {
                        if (contraband) c.Contraband++;
                        if (stolen) c.Stolen++;
                    }
                    if (ForStatusOut == null || ForStatusOut.Eval(View))
                    {
                        if (contraband) c.ContrabandOut++;
                        if (stolen) c.StolenOut++;
                    }
                }
                // Every type in the shop gets an entry, at 0 when the rest of the filter leaves none of it, so the
                // window keeps its layout while ticks change.
                bool countTypes = ForTypes == null || ForTypes.Eval(View);
                bool countTypesOut = ForTypesOut == null || ForTypesOut.Eval(View);
                var list = item.itemTypes;
                if (list != null)
                    for (int i = 0; i < list.Count; i++)
                    {
                        var t = list[i];
                        if (string.IsNullOrEmpty(t)) continue;
                        int n;
                        c.Types.TryGetValue(t, out n);
                        c.Types[t] = countTypes ? n + 1 : n;
                        c.TypesOut.TryGetValue(t, out n);
                        c.TypesOut[t] = countTypesOut ? n + 1 : n;
                        c.Present.TryGetValue(t, out n);
                        c.Present[t] = n + 1;
                    }

                if (!c.Totalled) return;
                long value = Worth(item), cells = Cells(item);
                c.Shop.Add(value, cells);
                if (Match != null && Match.Eval(View)) c.Matched.Add(value, cells);
            }
        }

        // The item's own worth, as the game's tooltip estimates it, without what is inside: a container and its
        // contents are each counted once.
        static long Worth(GameItem item)
        {
            try { return Math.Max(0L, item.GetCurrentValue(false, false, true, false, 0L)); }
            catch (Exception) { return 0L; }
        }

        // The grid cells its shape takes.
        static long Cells(GameItem item)
        {
            try { return item.shape != null ? item.GetItemSize() : 0; }
            catch (Exception) { return 0; }
        }
    }
}
