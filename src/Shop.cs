using System;
using System.Collections.Generic;

namespace DemandHighlight
{
    // Every item of the shop, each once: the shop inventories, the counter, the showcase, the market stall, the
    // hidden and sold areas, and everything inside them, closed containers included. Depth is capped and nodes are
    // visited once, as in Engine. Used by the Item Filter's counts and by the public query API; holds nothing.
    public static class Shop
    {
        public static bool Loaded { get { return EmporiumEntry.Instance != null; } }

        public static void Each(Action<GameItem> visit)
        {
            var shop = EmporiumEntry.Instance;
            if (shop == null) return;
            var seen = new HashSet<object>();
            Node(shop.invElement, visit, seen, 0);
            Node(shop.frontInvinvElement, visit, seen, 0);
            Node(shop.backInvinvElement, visit, seen, 0);
            Node(shop.backInvinvElementCounter, visit, seen, 0);
            Node(shop.showcaseElement, visit, seen, 0);
            Node(shop.bazarLeftinvElement, visit, seen, 0);
            Node(shop.hiddenElement, visit, seen, 0);
            Node(shop.soldElement, visit, seen, 0);
        }

        static void Node(GraphNodeStorage node, Action<GameItem> visit, HashSet<object> seen, int depth)
        {
            if (node == null || depth > 16 || !seen.Add(node)) return;
            var item = node as GameItem;
            if (item != null) visit(item);
            var children = node.children;
            if (children == null) return;
            for (int i = 0; i < children.Count; i++) Node(children[i], visit, seen, depth + 1);
        }
    }
}
