using System;
using System.Collections.Generic;

namespace DemandHighlight
{
    // The front half: what a customer wants, or what the item filter asks for, turned into a Query. Works on plain
    // snapshots (ClientView, FilterView) that Intent.cs reads from the game, so tests/IntentTest.cs runs it outside it.
    //
    // A customer goes through Rules in order; the first rule that recognises them gives the query, and a customer no
    // rule recognises wants nothing that can be shown. A new kind of customer is a new rule; nothing else changes.

    // One customer at the counter, as plain data.
    public sealed class ClientView
    {
        public string Id = "";
        // The game's StoreClient.ClientIntent, as a number (see Intent.Buy and the others).
        public int Intent;
        // Who this customer object is, so a new customer with the same wants still gets fresh answers from the game.
        public string Token = "";
        public string[] BuyingTags = new string[0], BuyingIds = new string[0];
        // Wants variety: refuses anything with the identifier of an item it already took (Taken).
        public bool Variety;
        public string[] Taken = new string[0];
        // A barter's terms: the customer takes an item that meets any one of them. Null for anyone else.
        public BarterTerm[] Barter;
    }

    // One of a barter's terms, as the game's BarterCondition states it: an item with this identifier (if set), this
    // tag (if set), and none of these conditions.
    public sealed class BarterTerm
    {
        public string Id = "", Tag = "";
        public string[] Blacklist = new string[0];
    }

    // The item filter: its expression, the one source of truth (the window's ticks only edit it, see Facets.Pure.cs).
    // Null when the filter asks for nothing.
    public sealed class FilterView
    {
        public Pred Expression;
    }

    public static class Intent
    {
        // StoreClient.ClientIntent values the rules read.
        public const int Buy = 1, Sell = 2, SellAndBuy = 3, Inspection = 4, Barter = 6;

        // The flexi buyer arrives wanting nothing and picks a kind of goods when its dialogue ends.
        public const string FlexiBuyer = "lower_level_flexi_buyer";

        // The name the game's own buying check is asked under. It holds everything that check depends on: the customer
        // object and its wants (but not the variety list, which Not(IdIn) applies on top), so an answer stays valid
        // exactly while the name is the same.
        public const string BuysOracle = "buys";

        public delegate Query Rule(ClientView client, bool clientGoods);

        public static readonly Rule[] Rules = { InspectionRule, BarterRule, FlexiWaiting, TradeRule };

        // The filter, when it asks for anything, replaces the customer, as before.
        public static Query Distill(ClientView client, FilterView filter, bool clientGoods)
        {
            var f = FilterQuery(filter);
            if (f.Kind != Query.Op.Nothing) return f;
            if (client == null) return Query.Nothing;
            foreach (var rule in Rules)
            {
                var q = rule(client, clientGoods);
                if (q != null) return q;
            }
            return Query.Nothing;
        }

        // An inspection: contraband and stolen goods.
        static Query InspectionRule(ClientView c, bool clientGoods)
        {
            return c.Intent == Inspection ? Query.Search(Pred.Or(Pred.Contraband, Pred.Stolen)) : null;
        }

        // A barter: the player's items that meet any of the terms. The customer takes the item itself, so a container
        // does not need everything inside it to match.
        static Query BarterRule(ClientView c, bool clientGoods)
        {
            if (c.Intent != Barter) return null;
            if (c.Barter == null || c.Barter.Length == 0) return Query.Nothing;
            var terms = new List<Pred>();
            foreach (var t in c.Barter) terms.Add(Term(t));
            return Query.Want(Pred.And(Pred.Owned, Pred.Or(terms)), false);
        }

        public static Pred Term(BarterTerm t)
        {
            var parts = new List<Pred>();
            if (!string.IsNullOrEmpty(t.Id)) parts.Add(Pred.Id(t.Id));
            if (!string.IsNullOrEmpty(t.Tag)) parts.Add(Pred.Tag(t.Tag));
            var bad = new List<Pred>();
            if (t.Blacklist != null) foreach (var b in t.Blacklist) if (!string.IsNullOrEmpty(b)) bad.Add(Pred.Condition(b));
            parts.Add(Pred.Not(Pred.Or(bad)));
            return Pred.And(parts);
        }

        // The flexi buyer before it has chosen: nothing yet. Recognised by who it is and by its empty wants, so once
        // it has chosen it is an ordinary buyer.
        static Query FlexiWaiting(ClientView c, bool clientGoods)
        {
            bool waiting = c.Id == FlexiBuyer && c.BuyingTags.Length == 0 && c.BuyingIds.Length == 0;
            return waiting ? Query.Nothing : null;
        }

        // Buying, selling, or both. A buyer gets what the game's buying check accepts; one who also sells takes only
        // the player's items; one who wants variety refuses what it already took. A seller's own goods are shown only
        // with the option on.
        static Query TradeRule(ClientView c, bool clientGoods)
        {
            bool buying = c.Intent == Buy || c.Intent == SellAndBuy;
            bool selling = c.Intent == Sell || c.Intent == SellAndBuy;
            if (!buying && !selling) return null;
            var parts = new List<Query>();
            if (buying)
            {
                var p = Pred.Ask(BuysName(c));
                if (selling) p = Pred.And(Pred.Owned, p);
                if (c.Variety) p = Pred.And(p, Pred.Not(Pred.IdIn(c.Taken)));
                parts.Add(Query.Want(p, true));
            }
            if (selling && clientGoods) parts.Add(Query.Goods(Pred.Or(Pred.Contraband, Pred.Stolen)));
            return Query.Union(parts);
        }

        public static string BuysName(ClientView c)
        {
            return BuysOracle + ":" + c.Token + ":" + c.Id + ":" + string.Join(",", Pred.Sorted(c.BuyingTags)) + ":" +
                string.Join(",", Pred.Sorted(c.BuyingIds)) + (c.Variety ? ":variety" : "");
        }

        // The filter, when its expression asks for anything. "true" (an empty box) asks for nothing.
        public static Query FilterQuery(FilterView f)
        {
            if (f == null || f.Expression == null || f.Expression.Equals(Pred.True)) return Query.Nothing;
            return Query.Filter(f.Expression);
        }
    }
}
