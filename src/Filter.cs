using System.Collections.Generic;

namespace DemandHighlight
{
    // The item filter: mark everything in the shop of the chosen item types (the game's own categories, the ones
    // customers ask for) and/or everything stolen or contraband. While anything is chosen it replaces the
    // customer highlight; Rules walks and indexes it the same way.
    public static class Filter
    {
        static readonly HashSet<string> types = new HashSet<string>();
        static bool stolen, contraband;

        // Bumped on every change, so Rules drops its index and the outlines are recomputed.
        public static int Version { get; private set; }

        // Bumped when items enter or leave inventories or their tags change, so the counts are redone only then.
        public static int StockVersion;
        static Counts counts;
        static int countsVersion = -1;

        public static bool Active { get { return types.Count > 0 || stolen || contraband; } }
        public static bool Stolen { get { return stolen; } }
        public static bool Contraband { get { return contraband; } }
        public static bool Has(string type) { return types.Contains(type); }

        public static void SetType(string type, bool on)
        {
            if (on ? types.Add(type) : types.Remove(type)) Version++;
        }

        public static void SetStolen(bool on) { if (stolen != on) { stolen = on; Version++; } }
        public static void SetContraband(bool on) { if (contraband != on) { contraband = on; Version++; } }

        public static void Clear()
        {
            if (!Active) return;
            types.Clear();
            stolen = contraband = false;
            Version++;
        }

        // Contraband and stolen keep their own looks; a type match uses the filter look (Mark.Normal, drawn in the
        // filter colour).
        public static Mark Match(GameItem item)
        {
            // The game is asked only about what the filter wants.
            bool isContraband = contraband && ContrabandHelper.IsContraband(item);
            bool isStolen = stolen && !isContraband && StolenHelper.IsStolenItem(item);
            return FilterRule.Match(contraband, isContraband, stolen, isStolen, types, types.Count > 0 ? item.itemTypes : null);
        }

        public sealed class Counts
        {
            public readonly Dictionary<string, int> Types = new Dictionary<string, int>();
            public int Stolen, Contraband, Items;
        }

        // Everything in the shop, closed containers included, counted by type for the panel. Cached until the
        // stock changes.
        public static Counts Count()
        {
            if (counts != null && countsVersion == StockVersion) return counts;
            countsVersion = StockVersion;
            counts = new Counts();
            var shop = EmporiumEntry.Instance;
            if (shop == null) return counts;
            var seen = new HashSet<object>();
            CountNode(shop.invElement, counts, seen, 0);
            CountNode(shop.frontInvinvElement, counts, seen, 0);
            CountNode(shop.backInvinvElement, counts, seen, 0);
            CountNode(shop.backInvinvElementCounter, counts, seen, 0);
            CountNode(shop.showcaseElement, counts, seen, 0);
            CountNode(shop.bazarLeftinvElement, counts, seen, 0);
            CountNode(shop.hiddenElement, counts, seen, 0);
            CountNode(shop.soldElement, counts, seen, 0);
            return counts;
        }

        static void CountNode(GraphNodeStorage node, Counts counts, HashSet<object> seen, int depth)
        {
            if (node == null || depth > 16 || !seen.Add(node)) return;
            var item = node as GameItem;
            if (item != null)
            {
                counts.Items++;
                if (ContrabandHelper.IsContraband(item)) counts.Contraband++;
                if (StolenHelper.IsStolenItem(item)) counts.Stolen++;
                var list = item.itemTypes;
                if (list != null)
                    for (int i = 0; i < list.Count; i++)
                    {
                        var t = list[i];
                        if (string.IsNullOrEmpty(t)) continue;
                        int n;
                        counts.Types.TryGetValue(t, out n);
                        counts.Types[t] = n + 1;
                    }
            }
            var children = node.children;
            if (children == null) return;
            for (int i = 0; i < children.Count; i++) CountNode(children[i], counts, seen, depth + 1);
        }
    }
}
