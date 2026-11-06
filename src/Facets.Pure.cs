using System;
using System.Collections.Generic;

namespace DemandHighlight
{
    // The Item Filter's ticks, read from and written into its expression, which is the one source of truth.
    //
    // A tick has three states: off, in (✓) and out (✗). The expression is taken as clauses joined by "and"; four
    // kinds of clause are the window's facets, the first clause of each kind:
    //   status in    stolen / contraband, alone or joined by "or"           stolen
    //   status out   not, then the same                                      not contraband
    //   types in     types, alone or joined by "or"                          type:(Food or Medical)
    //   types out    not, then the same                                      not type:(Junk or Seed)
    // Every other clause is kept as written and narrows everything.
    //   stolen and not contraband and type:(Food or Medical) and not type:Junk and not owned
    //   -> stolen in, contraband out, Food and Medical in, Junk out; rest: not owned
    // An expression of another shape ("type:Food or stolen") has no facets: nothing shows as ticked, and a tick adds
    // its facet as a new clause.
    //
    // Counts follow a shop search's rule. A facet's "in" ticks are any-of, so a facet's counts leave out its own "in"
    // clause and keep everything else, its "out" clause included: each count is what ticking would add to what the
    // filter finds now. An "out" tick shows how many it takes away, counted with its own "out" clause left out too.
    // Plain data only, so tests/FacetsTest.cs runs it outside the game.
    public sealed class Facets
    {
        public enum Part { Status, Types }
        public enum State { Off, In, Out }

        // The next state of a clicked tick: off, in, out, off.
        public static State Next(State s) { return s == State.Off ? State.In : s == State.In ? State.Out : State.Off; }

        // Clause kinds in the order a filter built by ticking is written.
        enum Kind { StatusIn, StatusOut, TypesIn, TypesOut }

        readonly List<Pred> clauses = new List<Pred>();
        readonly int[] at = { -1, -1, -1, -1 };
        readonly Dictionary<Pred, State> states = new Dictionary<Pred, State>();

        public State Of(Pred atom)
        {
            State s;
            return states.TryGetValue(atom, out s) ? s : State.Off;
        }

        public State OfType(string type) { return Of(Pred.Type(type)); }
        public bool Has(string type) { return OfType(type) == State.In; }

        // Reads an expression; null or "true" has no clauses.
        public static Facets Read(Pred p)
        {
            var f = new Facets();
            if (p == null || p.Kind == Pred.Op.True) return f;
            if (p.Kind == Pred.Op.And) f.clauses.AddRange(p.Args); else f.clauses.Add(p);
            for (int i = 0; i < f.clauses.Count; i++)
            {
                var c = f.clauses[i];
                bool negated = c.Kind == Pred.Op.Not;
                var body = negated ? c.Args[0] : c;
                Kind kind;
                if (AllOf(body, IsStatus)) kind = negated ? Kind.StatusOut : Kind.StatusIn;
                else if (AllOf(body, IsType)) kind = negated ? Kind.TypesOut : Kind.TypesIn;
                else continue;
                if (f.at[(int)kind] >= 0) continue;
                f.at[(int)kind] = i;
                foreach (var a in Atoms(body)) if (!f.states.ContainsKey(a)) f.states[a] = negated ? State.Out : State.In;
            }
            return f;
        }

        // The expression with one tick set to a state. The atom leaves both of its facet's clauses and joins the one
        // its state names. A clause that becomes empty is dropped; a missing one is added in the order above, after
        // the facet clauses before it. Null when nothing is left.
        public static Pred Set(Pred p, Pred atom, State state)
        {
            var f = Read(p);
            bool status = IsStatus(atom);
            var inKind = status ? Kind.StatusIn : Kind.TypesIn;
            var outKind = status ? Kind.StatusOut : Kind.TypesOut;

            var parts = new List<Pred>(f.clauses);
            var slots = new Dictionary<Kind, List<Pred>>
            {
                { inKind, f.AtomsOf(inKind) },
                { outKind, f.AtomsOf(outKind) },
            };
            foreach (var list in slots.Values) list.RemoveAll(a => a.Equals(atom));
            if (state == State.In) slots[inKind].Add(atom);
            if (state == State.Out) slots[outKind].Add(atom);

            // Replace or drop the existing clauses, from the back so indexes stay right; then add the new ones.
            var at = (int[])f.at.Clone();
            foreach (var kind in new[] { outKind, inKind })
            {
                int i = at[(int)kind];
                if (i < 0) continue;
                var clause = Clause(kind, slots[kind]);
                if (clause != null) parts[i] = clause;
                else
                {
                    parts.RemoveAt(i);
                    for (int k = 0; k < at.Length; k++) if (at[k] > i) at[k]--;
                    at[(int)kind] = -1;
                }
            }
            foreach (var kind in new[] { inKind, outKind })
            {
                if (at[(int)kind] >= 0) continue;
                var clause = Clause(kind, slots[kind]);
                if (clause == null) continue;
                int insert = 0;
                for (int k = 0; k < (int)kind; k++) if (at[k] >= 0) insert = Math.Max(insert, at[k] + 1);
                insert = Math.Min(insert, parts.Count);
                parts.Insert(insert, clause);
                for (int k = 0; k < at.Length; k++) if (at[k] >= insert) at[k]++;
                at[(int)kind] = insert;
            }
            if (parts.Count == 0) return null;
            var result = Pred.And(parts);
            return result.Kind == Pred.Op.True ? null : result;
        }

        // Two states, for callers that only include: on is in, off is off.
        public static Pred Toggle(Pred p, Pred atom, bool on) { return Set(p, atom, on ? State.In : State.Off); }

        // What an item must pass for a tick in this facet to count it: every clause but the facet's "in" clause; for
        // an "out" tick (withoutOut), the facet's "out" clause goes too. True when nothing else is asked.
        public Pred AllBut(Part part, bool withoutOut = false)
        {
            int skipIn = at[(int)(part == Part.Status ? Kind.StatusIn : Kind.TypesIn)];
            int skipOut = withoutOut ? at[(int)(part == Part.Status ? Kind.StatusOut : Kind.TypesOut)] : -1;
            var kept = new List<Pred>();
            for (int i = 0; i < clauses.Count; i++) if (i != skipIn && i != skipOut) kept.Add(clauses[i]);
            return Pred.And(kept);
        }

        List<Pred> AtomsOf(Kind kind)
        {
            int i = at[(int)kind];
            if (i < 0) return new List<Pred>();
            var c = clauses[i];
            return new List<Pred>(Atoms(c.Kind == Pred.Op.Not ? c.Args[0] : c));
        }

        static Pred Clause(Kind kind, List<Pred> atoms)
        {
            if (atoms.Count == 0) return null;
            var any = Pred.Or(atoms);
            return kind == Kind.StatusOut || kind == Kind.TypesOut ? Pred.Not(any) : any;
        }

        static bool IsStatus(Pred a) { return a.Kind == Pred.Op.Stolen || a.Kind == Pred.Op.Contraband; }
        static bool IsType(Pred a) { return a.Kind == Pred.Op.Type; }

        static IEnumerable<Pred> Atoms(Pred c)
        {
            if (c.Kind == Pred.Op.Or) return c.Args;
            return new[] { c };
        }

        static bool AllOf(Pred c, Func<Pred, bool> test)
        {
            foreach (var a in Atoms(c)) if (!test(a)) return false;
            return true;
        }
    }
}
