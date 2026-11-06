using System;
using System.Collections.Generic;
using System.Text;

namespace DemandHighlight
{
    // The language between the two halves of the mod. The front (Intent) turns what a customer wants, or what the item
    // filter asks for, into a Query; the back (Engine) only ever sees Queries and answers them over the store. Neither
    // half knows how the other works.
    //
    // Both types are tagged unions: one sealed class, a Kind tag, and the fields that kind uses. Values are immutable
    // and compared by structure, so "the customer still wants the same thing" is Query.Equals, and the back keeps its
    // cached answers exactly as long as that holds. The constructors normalise (flatten, sort, drop neutral parts), so
    // two queries that mean the same are equal.
    //
    // Plain data only (System types), so tests/QueryTest.cs runs it outside the game.

    // What a predicate can ask about one item. Engine answers from the game; tests answer from plain data.
    public interface IItem
    {
        string Id { get; }
        bool Owned { get; }
        bool Contraband { get; }
        bool Stolen { get; }
        bool HasTag(string tag);
        bool HasType(string type);
        // One of the item's current conditions (worn, broken and so on), by the game's condition identifier.
        bool HasCondition(string condition);
        // A question only the game can answer, by name. The name says everything the answer depends on, so an answer
        // can be kept for as long as the item and the name stay the same.
        bool Ask(string oracle);
    }

    public sealed class Pred : IEquatable<Pred>
    {
        public enum Op : byte { True, False, Owned, Contraband, Stolen, Id, Tag, Type, Condition, IdIn, Ask, Not, And, Or }

        public readonly Op Kind;
        public readonly string Text;      // Id, Tag, Type, Condition, Ask
        public readonly string[] Texts;   // IdIn: sorted, no duplicates
        public readonly Pred[] Args;      // Not (one), And, Or (two or more, no nesting of the same kind)

        Pred(Op kind, string text = null, string[] texts = null, Pred[] args = null)
        {
            Kind = kind; Text = text; Texts = texts; Args = args;
        }

        public static readonly Pred True = new Pred(Op.True), False = new Pred(Op.False);
        public static readonly Pred Owned = new Pred(Op.Owned), Contraband = new Pred(Op.Contraband), Stolen = new Pred(Op.Stolen);

        public static Pred Id(string id) { return new Pred(Op.Id, id ?? ""); }
        public static Pred Tag(string tag) { return new Pred(Op.Tag, tag ?? ""); }
        public static Pred Type(string type) { return new Pred(Op.Type, type ?? ""); }
        public static Pred Condition(string condition) { return new Pred(Op.Condition, condition ?? ""); }
        public static Pred Ask(string oracle) { return new Pred(Op.Ask, oracle ?? ""); }

        public static Pred IdIn(IEnumerable<string> ids)
        {
            var set = Sorted(ids);
            if (set.Length == 0) return False;
            return set.Length == 1 ? Id(set[0]) : new Pred(Op.IdIn, null, set);
        }

        public static Pred Not(Pred p)
        {
            if (p.Kind == Op.True) return False;
            if (p.Kind == Op.False) return True;
            if (p.Kind == Op.Not) return p.Args[0];
            return new Pred(Op.Not, null, null, new[] { p });
        }

        public static Pred And(params Pred[] ps) { return Join(Op.And, ps); }
        public static Pred Or(params Pred[] ps) { return Join(Op.Or, ps); }
        public static Pred And(IEnumerable<Pred> ps) { return Join(Op.And, ps); }
        public static Pred Or(IEnumerable<Pred> ps) { return Join(Op.Or, ps); }

        // And/Or: nested parts of the same kind are flattened, the neutral part dropped, the absorbing part wins, and
        // repeats are dropped. In an Or, identifiers are gathered into one IdIn where the first of them stood, so
        // "id:a or id:b" and IdIn(a, b) are the same value. Otherwise order is kept: it is the order the parts are
        // asked in, cheap checks first.
        static Pred Join(Op op, IEnumerable<Pred> ps)
        {
            var neutral = op == Op.And ? Op.True : Op.False;
            var absorbing = op == Op.And ? Op.False : Op.True;
            var parts = new List<Pred>();
            List<string> ids = null;
            int idsAt = -1;
            foreach (var p in ps)
            {
                if (p == null || p.Kind == neutral) continue;
                if (p.Kind == absorbing) return p;
                var inner = p.Kind == op ? p.Args : new[] { p };
                foreach (var q in inner)
                {
                    if (op == Op.Or && (q.Kind == Op.Id || q.Kind == Op.IdIn))
                    {
                        if (ids == null) { ids = new List<string>(); idsAt = parts.Count; parts.Add(null); }
                        if (q.Kind == Op.Id) ids.Add(q.Text); else ids.AddRange(q.Texts);
                        continue;
                    }
                    if (!parts.Contains(q)) parts.Add(q);
                }
            }
            if (ids != null) parts[idsAt] = IdIn(ids);
            if (parts.Count == 0) return op == Op.And ? True : False;
            return parts.Count == 1 ? parts[0] : new Pred(op, null, null, parts.ToArray());
        }

        public bool Eval(IItem item)
        {
            switch (Kind)
            {
                case Op.True: return true;
                case Op.False: return false;
                case Op.Owned: return item.Owned;
                case Op.Contraband: return item.Contraband;
                case Op.Stolen: return item.Stolen;
                case Op.Id: return string.Equals(item.Id, Text, StringComparison.Ordinal);
                case Op.Tag: return item.HasTag(Text);
                case Op.Type: return item.HasType(Text);
                case Op.Condition: return item.HasCondition(Text);
                case Op.IdIn: return item.Id != null && Array.BinarySearch(Texts, item.Id, StringComparer.Ordinal) >= 0;
                case Op.Ask: return item.Ask(Text);
                case Op.Not: return !Args[0].Eval(item);
                case Op.And:
                    for (int i = 0; i < Args.Length; i++) if (!Args[i].Eval(item)) return false;
                    return true;
                case Op.Or:
                    for (int i = 0; i < Args.Length; i++) if (Args[i].Eval(item)) return true;
                    return false;
                default: throw new InvalidOperationException("unknown predicate " + Kind);
            }
        }

        public bool Equals(Pred other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null || Kind != other.Kind || Text != other.Text) return false;
            return SameTexts(Texts, other.Texts) && SameArgs(Args, other.Args);
        }

        public override bool Equals(object obj) { return Equals(obj as Pred); }

        public override int GetHashCode()
        {
            int h = (int)Kind * 397 ^ (Text != null ? Text.GetHashCode() : 0);
            if (Texts != null) foreach (var t in Texts) h = h * 31 + t.GetHashCode();
            if (Args != null) foreach (var a in Args) h = h * 31 + a.GetHashCode();
            return h;
        }

        // Written in the filter's expression syntax, in its canonical form (Lift, then Formatter), so a predicate
        // prints as text that parses back to it.
        public override string ToString() { return Formatter.Format(Lift.FromPred(this, null)); }

        // As ToString, but each type is written by its name in the player's language (see Lift.TypeName).
        public string ToString(Func<string, string> typeName, Formatter.Style style = Formatter.Style.Words)
        {
            return Formatter.Format(Lift.FromPred(this, typeName), style);
        }

        internal static string[] Sorted(IEnumerable<string> items)
        {
            var set = new SortedSet<string>(StringComparer.Ordinal);
            if (items != null) foreach (var s in items) if (!string.IsNullOrEmpty(s)) set.Add(s);
            var a = new string[set.Count];
            set.CopyTo(a);
            return a;
        }

        internal static bool SameTexts(string[] a, string[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            return true;
        }

        static bool SameArgs(Pred[] a, Pred[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (!a[i].Equals(b[i])) return false;
            return true;
        }
    }

    public sealed class Query : IEquatable<Query>
    {
        public enum Op : byte { Nothing, Want, Search, Goods, Filter, Union }

        public readonly Op Kind;
        // Want, Search, Goods: which items the query is about.
        public readonly Pred Match;
        // Want: a container counts only with everything inside it (a buyer takes a box with its contents; a barterer
        // takes the item itself).
        public readonly bool Whole;
        // Union: two or more parts, none of them a Union or Nothing.
        public readonly Query[] Parts;

        Query(Op kind, Pred match = null, bool whole = false, Query[] parts = null)
        {
            Kind = kind; Match = match; Whole = whole; Parts = parts;
        }

        // Mark nothing.
        public static readonly Query Nothing = new Query(Op.Nothing);

        // What the customer would take: matching items get the wanted look (or their illegal one).
        public static Query Want(Pred match, bool whole)
        {
            return match.Kind == Pred.Op.False ? Nothing : new Query(Op.Want, match, whole);
        }

        // An inspection: matching items show their illegality; locks and containers that block a search hide what is inside.
        public static Query Search(Pred match)
        {
            return match.Kind == Pred.Op.False ? Nothing : new Query(Op.Search, match);
        }

        // The customer's own goods on the counter: matching items that are not the player's show their illegality.
        public static Query Goods(Pred match)
        {
            return match.Kind == Pred.Op.False ? Nothing : new Query(Op.Goods, match);
        }

        // The item filter, from its panel or a typed expression. Matching items get the filter look (or their illegal
        // one). Locks don't apply: it is for sorting the player's own stock.
        public static Query Filter(Pred match)
        {
            return match.Kind == Pred.Op.False ? Nothing : new Query(Op.Filter, match);
        }

        public static Query Union(IEnumerable<Query> qs)
        {
            var parts = new List<Query>();
            foreach (var q in qs)
            {
                if (q == null || q.Kind == Op.Nothing) continue;
                var inner = q.Kind == Op.Union ? q.Parts : new[] { q };
                foreach (var p in inner) if (!parts.Contains(p)) parts.Add(p);
            }
            if (parts.Count == 0) return Nothing;
            return parts.Count == 1 ? parts[0] : new Query(Op.Union, null, false, parts.ToArray());
        }

        public static Query Union(params Query[] qs) { return Union((IEnumerable<Query>)qs); }

        public bool Equals(Query other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null || Kind != other.Kind || Whole != other.Whole || !Equals(Match, other.Match)) return false;
            if (Parts == null || other.Parts == null) return Parts == other.Parts;
            if (Parts.Length != other.Parts.Length) return false;
            for (int i = 0; i < Parts.Length; i++) if (!Parts[i].Equals(other.Parts[i])) return false;
            return true;
        }

        public override bool Equals(object obj) { return Equals(obj as Query); }

        public override int GetHashCode()
        {
            int h = (int)Kind * 397 ^ (Match != null ? Match.GetHashCode() : 0);
            h = h * 31 + (Whole ? 1 : 0);
            if (Parts != null) foreach (var p in Parts) h = h * 31 + p.GetHashCode();
            return h;
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case Op.Nothing: return "nothing";
                case Op.Want: return "want" + (Whole ? " whole" : "") + " (" + Match + ")";
                case Op.Search: return "search (" + Match + ")";
                case Op.Goods: return "goods (" + Match + ")";
                case Op.Filter: return "filter (" + Match + ")";
                default:
                    var parts = new string[Parts.Length];
                    for (int i = 0; i < Parts.Length; i++) parts[i] = Parts[i].ToString();
                    return string.Join(" + ", parts);
            }
        }
    }

    // A query laid out for answering item by item: which kinds of look are asked for, and the predicate of each.
    // Built once per new query. A filter replaces everything else, as the item filter replaces the customer highlight.
    public sealed class Plan
    {
        public readonly Pred Want, Search, Goods, Filter;   // null when not asked
        public readonly bool Whole;

        Plan(Pred want, bool whole, Pred search, Pred goods, Pred filter)
        {
            Want = want; Whole = whole; Search = search; Goods = goods; Filter = filter;
        }

        public bool Filtering { get { return Filter != null; } }
        public bool Any { get { return Filter != null || Want != null || Search != null || Goods != null; } }

        // Two wants (or searches, or goods) in one union are asked as one: either matches. A union wants whole
        // containers if any of its wants does.
        public static Plan Of(Query q)
        {
            Pred want = null, search = null, goods = null, filter = null;
            bool whole = false;
            var parts = q.Kind == Query.Op.Union ? q.Parts : new[] { q };
            foreach (var p in parts)
            {
                switch (p.Kind)
                {
                    case Query.Op.Want: want = want == null ? p.Match : Pred.Or(want, p.Match); whole |= p.Whole; break;
                    case Query.Op.Search: search = search == null ? p.Match : Pred.Or(search, p.Match); break;
                    case Query.Op.Goods: goods = goods == null ? p.Match : Pred.Or(goods, p.Match); break;
                    case Query.Op.Filter: filter = filter == null ? p.Match : Pred.Or(filter, p.Match); break;
                }
            }
            return filter != null ? new Plan(null, false, null, null, filter) : new Plan(want, whole, search, goods, null);
        }
    }
}
