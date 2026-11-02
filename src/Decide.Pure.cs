using System.Collections.Generic;

namespace DemandHighlight
{
    // The decisions behind every outline, on plain data only (System types, no Unity, no game objects), so
    // tests/DecideTest.cs can run them outside the game. Rules reads the game and passes what it found here.

    // What the customer at the counter is doing.
    public struct Kind { public bool Buying, Selling, Inspector; }

    // What an item adds to the containers above it: the strongest mark the buyer wants, and the strongest
    // illegality an inspector (or a watchful seller) would find.
    public struct Agg { public Mark Buyer, Illegal; }

    // What the game says about one item, read once per change.
    public struct Facts
    {
        public bool SelfOk;          // the customer accepts it on its own (contents ignored)
        public bool ContentsChecked; // the customer also checks what is inside it
        public Mark Illegal;         // contraband, stolen or neither
        public Mark FilterMark;      // what the Item Filter makes of it
        public bool Locked;          // a lock hides what is inside
        public bool BehindLock;      // the item itself sits behind a lock
        public bool Immovable;       // fixed in place, never offered
        public bool BlocksSearch;    // an inspector cannot search inside it
        public bool Owned;           // the player's own item
    }

    public static class Decide
    {
        public static Mark Max(Mark a, Mark b) { return a > b ? a : b; }

        // Contraband outranks stolen.
        public static Mark Illegality(bool contraband, bool stolen)
        {
            return contraband ? Mark.Contraband : stolen ? Mark.Stolen : Mark.None;
        }

        // The item's own contribution. bought: the customer would take it, contents included, which needs every
        // item inside to be taken too when the customer checks contents.
        public static Agg Self(Kind kind, bool filtering, bool clientGoods, Facts f, bool insideAllBought, out bool bought)
        {
            var self = default(Agg);
            bought = false;
            if (filtering) { self.Buyer = f.FilterMark; return self; }
            if (kind.Buying)
            {
                bought = f.SelfOk && (!f.ContentsChecked || insideAllBought);
                if (bought) self.Buyer = f.Illegal != Mark.None ? f.Illegal : Mark.Normal;
            }
            if (kind.Inspector || (kind.Selling && clientGoods)) self.Illegal = f.Illegal;
            return self;
        }

        // What the containers above see: the item itself unless it is behind a lock, and what is inside it unless
        // it is locked (or, for illegality, blocks a search).
        public static Agg Report(Agg self, Agg inside, Facts f, bool filtering)
        {
            var up = filtering || !f.BehindLock ? self : default(Agg);
            if (!f.Locked)
            {
                up.Buyer = Max(up.Buyer, inside.Buyer);
                if (!f.BlocksSearch) up.Illegal = Max(up.Illegal, inside.Illegal);
            }
            return up;
        }

        // The outline drawn on one item. A direct match draws its mark; otherwise a match inside draws corner
        // brackets (Contents), unless something hides the inside from whoever is looking.
        public static Style Display(Kind kind, bool filtering, bool clientGoods, Facts f, Agg self, Agg inside,
            bool lockedAbove, bool blockedAbove)
        {
            if (filtering)
                return self.Buyer != Mark.None ? new Style(self.Buyer, false) : new Style(inside.Buyer, true);
            if (lockedAbove || f.BehindLock || f.Immovable) return default(Style);
            if (kind.Inspector)
            {
                if (blockedAbove) return default(Style);
                if (self.Illegal != Mark.None) return new Style(self.Illegal, false);
                if (f.Locked || f.BlocksSearch) return default(Style);
                return new Style(inside.Illegal, true);
            }
            if (kind.Selling && !f.Owned)
            {
                if (!clientGoods) return default(Style);
                if (self.Illegal != Mark.None) return new Style(self.Illegal, false);
                if (f.Locked) return default(Style);
                return new Style(inside.Illegal, true);
            }
            if (kind.Buying)
            {
                if (self.Buyer != Mark.None) return new Style(self.Buyer, false);
                if (f.Locked) return default(Style);
                return new Style(inside.Buyer, true);
            }
            return default(Style);
        }
    }

    public static class FilterRule
    {
        // Contraband and stolen keep their own looks; a type match uses the filter look (Mark.Normal, drawn in the
        // filter colour). itemTypes may be null or hold nulls.
        public static Mark Match(bool wantContraband, bool isContraband, bool wantStolen, bool isStolen,
            ICollection<string> wantedTypes, IList<string> itemTypes)
        {
            if (wantContraband && isContraband) return Mark.Contraband;
            if (wantStolen && isStolen) return Mark.Stolen;
            if (wantedTypes != null && wantedTypes.Count > 0 && itemTypes != null)
                for (int i = 0; i < itemTypes.Count; i++)
                    if (itemTypes[i] != null && wantedTypes.Contains(itemTypes[i])) return Mark.Normal;
            return Mark.None;
        }
    }
}
