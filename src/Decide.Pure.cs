namespace DemandHighlight
{
    // The decisions behind every outline, on plain data only (System types, no Unity, no game objects), so
    // tests/DecideTest.cs can run them outside the game. Engine reads the game and passes what it found here.
    //
    // What is asked comes from the query (Plan): which looks are wanted and which items match. What the item is
    // comes from Facts. Nothing here knows about customers.

    // What an item adds to the containers above it: the strongest wanted mark, and the strongest illegality a search
    // (or a look at the customer's goods) would find.
    public struct Agg { public Mark Buyer, Illegal; }

    // What the item is, whoever asks. Read once per change of the item.
    public struct Facts
    {
        public Mark Illegal;         // contraband, stolen or neither
        public bool Locked;          // a lock hides what is inside
        public bool BehindLock;      // the item itself sits behind a lock
        public bool Immovable;       // fixed in place, never offered
        public bool Owned;           // the player's own item
        public bool HasContents;     // an open-able container whose contents a buyer checks
    }

    // How the item answers the current query. Read once per query (and per change of the item).
    public struct Verdict
    {
        public bool Want, Search, Goods, Filter;
        public bool BlocksSearch;    // a search cannot look inside it (asked only during a search)
    }

    public static class Decide
    {
        public static Mark Max(Mark a, Mark b) { return a > b ? a : b; }

        // Contraband outranks stolen.
        public static Mark Illegality(bool contraband, bool stolen)
        {
            return contraband ? Mark.Contraband : stolen ? Mark.Stolen : Mark.None;
        }

        // A match's look: its illegality if it has one, the plain look otherwise.
        static Mark Look(Facts f) { return f.Illegal != Mark.None ? f.Illegal : Mark.Normal; }

        // The item's own contribution. bought: the customer would take it, contents included when the query wants
        // whole containers (then every item inside must be taken too).
        public static Agg Self(Plan p, Facts f, Verdict v, bool insideAllBought, out bool bought)
        {
            var self = default(Agg);
            bought = false;
            if (p.Filtering)
            {
                if (v.Filter) self.Buyer = Look(f);
                return self;
            }
            if (p.Want != null)
            {
                bought = v.Want && (!(p.Whole && f.HasContents) || insideAllBought);
                if (bought) self.Buyer = Look(f);
            }
            if (v.Search || v.Goods) self.Illegal = f.Illegal;
            return self;
        }

        // What the containers above see: the item itself unless it is behind a lock, and what is inside it unless
        // it is locked (or, for illegality, blocks a search). The filter ignores locks.
        public static Agg Report(Plan p, Agg self, Agg inside, Facts f, Verdict v)
        {
            var up = p.Filtering || !f.BehindLock ? self : default(Agg);
            if (p.Filtering || !f.Locked)
            {
                up.Buyer = Max(up.Buyer, inside.Buyer);
                if (!v.BlocksSearch) up.Illegal = Max(up.Illegal, inside.Illegal);
            }
            return up;
        }

        // The outline drawn on one item. A direct match draws its mark; otherwise a match inside draws corner
        // brackets (Contents), unless something hides the inside from whoever is looking.
        public static Style Display(Plan p, Facts f, Verdict v, Agg self, Agg inside, bool lockedAbove, bool blockedAbove)
        {
            if (p.Filtering)
                return self.Buyer != Mark.None ? new Style(self.Buyer, false) : new Style(inside.Buyer, true);
            if (lockedAbove || f.BehindLock || f.Immovable) return default(Style);
            if (p.Search != null)
            {
                if (blockedAbove) return default(Style);
                if (self.Illegal != Mark.None) return new Style(self.Illegal, false);
                if (f.Locked || v.BlocksSearch) return default(Style);
                return new Style(inside.Illegal, true);
            }
            if (p.Goods != null && !f.Owned)
            {
                if (self.Illegal != Mark.None) return new Style(self.Illegal, false);
                if (f.Locked) return default(Style);
                return new Style(inside.Illegal, true);
            }
            if (p.Want != null)
            {
                if (self.Buyer != Mark.None) return new Style(self.Buyer, false);
                if (f.Locked) return default(Style);
                return new Style(inside.Buyer, true);
            }
            return default(Style);
        }
    }
}
