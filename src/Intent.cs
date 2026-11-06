using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DemandHighlight
{
    // The game side of the front half: reads the customer at the counter and the item filter into plain snapshots for
    // Intent.Pure.cs, and answers the questions only the game can answer (Ask) for the back half.
    //
    // Reading never changes the game. The one call into the game's rules, the buying check, can add missing tags to the
    // item it looks at; those are taken off again (see Buys).
    // An item read straight from the game, for predicates that never ask the game a question (the Item Filter's).
    // Engine keeps its own cached view; the type and condition reads are shared.
    public sealed class LiveItem : IItem
    {
        public GameItem Item;

        public string Id { get { return Item.identifier; } }
        public bool Owned { get { return GeneralHelper.IsItemOwned(Item); } }
        public bool Contraband { get { return ContrabandHelper.IsContraband(Item); } }
        public bool Stolen { get { return StolenHelper.IsStolenItem(Item); } }
        public bool HasTag(string tag) { return Item.IsTag(tag); }
        public bool HasType(string type) { return HasType(Item, type); }
        public bool HasCondition(string condition) { return HasCondition(Item, condition); }
        public bool Ask(string oracle) { return false; }

        public static bool HasType(GameItem item, string type)
        {
            var types = item.itemTypes;
            return types != null && types.Contains(type);
        }

        public static bool HasCondition(GameItem item, string condition)
        {
            var features = item.itemFeatures;
            if (features == null) return false;
            for (int i = 0; i < features.Count; i++)
            {
                var feature = features[i];
                if (feature == null || !feature.useCondition) continue;
                var current = feature.GetCurrentItemCondition();
                if (current != null && current.identifier == condition) return true;
            }
            return false;
        }
    }

    public static class GameIntent
    {
        static StoreClient asked;
        static readonly HashSet<string> keysBefore = new HashSet<string>();
        static readonly List<string> keysAdded = new List<string>();

        public static StoreClient CurrentClient()
        {
            // PlayerStore.Instance creates the store when there is none, and outside the shop (main menu, loading)
            // that fails. EmporiumEntry.Instance only reads a field: no shop on screen, no customer.
            if (EmporiumEntry.Instance == null) return null;
            var store = PlayerStore.Instance;
            if (store == null || store.currentClientInstance == null) return null;
            return store.currentClientInstance.GetClientBlueprint();
        }

        // The query for what is at the counter now (or what the filter asks for). Also remembers the customer the
        // query's questions are about, for Ask.
        public static Query Read(StoreClient client, bool clientGoods)
        {
            asked = client;
            return Intent.Distill(View(client), FilterState(), clientGoods);
        }

        public static ClientView View(StoreClient c)
        {
            if (c == null) return null;
            var v = new ClientView();
            v.Id = c.identifier ?? "";
            v.Intent = (int)c.clientIntent;
            v.Token = RuntimeHelpers.GetHashCode(c).ToString();
            v.BuyingTags = Strings(c.clientBuyingTagList);
            v.BuyingIds = Strings(c.clientBuyingIdList);
            v.Variety = c.isRequireVariety;
            v.Taken = Strings(c.varietyTrackingList);
            var offer = c.barterOffer;
            if (offer != null && offer.conditions != null)
            {
                var terms = new List<BarterTerm>();
                foreach (var condition in offer.conditions)
                {
                    if (condition == null) continue;
                    terms.Add(new BarterTerm
                    {
                        Id = condition.requiredItemId ?? "",
                        Tag = condition.requiredItemTag ?? "",
                        Blacklist = Strings(condition.blacklistedConditionId)
                    });
                }
                v.Barter = terms.ToArray();
            }
            return v;
        }

        static FilterView FilterState()
        {
            return Filter.View();
        }

        static string[] Strings(List<string> list)
        {
            return list != null ? list.ToArray() : new string[0];
        }

        // Answers a query's Ask leaf for one item. Only the buying check exists so far; an unknown question is answered
        // no, so a query from a newer front never marks things it cannot judge.
        public static bool Ask(string oracle, GameItem item)
        {
            if (oracle.StartsWith(Intent.BuysOracle + ":")) return asked != null && Buys(asked, item);
            return false;
        }

        // The game's buying check, contents ignored. It reads tags through GetTagReadonly in its refusal checks
        // (water, locks), which adds missing tags to the item; with contents ignored it reads only the item itself, so
        // any tag key the call added to that item is removed again afterwards, leaving the item as it was.
        static bool Buys(StoreClient client, GameItem item)
        {
            var state = item.modifiedState;
            var dict = state != null ? state.dict : null;
            if (dict == null) return client.IsClientBuyingThisItem(item, true);
            keysBefore.Clear();
            foreach (var key in dict.Keys) keysBefore.Add(key);
            int count = dict.Count;
            bool buying = client.IsClientBuyingThisItem(item, true);
            if (dict.Count != count)
            {
                keysAdded.Clear();
                foreach (var key in dict.Keys) if (!keysBefore.Contains(key)) keysAdded.Add(key);
                for (int i = 0; i < keysAdded.Count; i++) dict.Remove(keysAdded[i]);
            }
            return buying;
        }

        // Leaving the store: the customer goes.
        public static void Release() { asked = null; }
    }
}
