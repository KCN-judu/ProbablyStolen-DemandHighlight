using System.Collections.Generic;
using System.Diagnostics;

namespace DemandHighlight
{
    // The game's rules for what a customer wants, applied in one walk over what is on screen, with every
    // verdict cached per item.
    //
    // Cost model:
    // - Each item asks the game once per customer (or filter) about itself only (IsClientBuyingThisItem with
    //   ignoreContents). The game's own check, run on a container, lists every descendant and re-checks each one
    //   recursively, which is quadratic in a deep store. The same rule is rebuilt here bottom-up instead: a
    //   container is bought when it is acceptable itself and, unless it is locked, has no window or is an
    //   advanced gun, every item anywhere inside it is bought.
    // - The per-item record holds that verdict and the tag-based flags. A change to an item clears its own
    //   record and marks the "inside" part of every container above it stale. Nothing else is recomputed.
    // - A walk visits only what is on screen (shop inventories and open containers). Closed containers answer
    //   from their record.
    // - A walk that would take more than Budget milliseconds stops computing and finishes on later frames, so a
    //   new customer in a huge store never costs one long frame. Items not computed yet stay unmarked for a frame.
    public static class Rules
    {
        const int MaxDepth = 16;
        const double Budget = 2.5;

        struct Kind { public bool Buying, Selling, Inspector; }

        // What an item adds to the containers above it.
        struct Agg { public Mark Buyer, Illegal; }

        // What an item reports to its container.
        struct Up
        {
            public Agg Agg;
            public bool Bought;   // the game would buy this item, contents included
            public bool AllBought; // Bought, and every item inside it is bought too
            public bool Complete;
        }

        sealed class Entry
        {
            public int Epoch = -1;          // self part valid while equal to Rules.epoch
            public bool SelfOk;             // acceptable on its own (contents ignored)
            public Mark Illegal, FilterMark;
            public bool Locked, ContentsChecked, BehindLock, Immovable, BlocksSearch, Owned;

            public int InsideEpoch = -1;    // inside part valid while equal to Rules.epoch
            public Agg Inside;
            public bool InsideAllBought;

            public int Walk;                // last walk that visited it
            public Up Last;
            public Style Style;
        }

        public struct Hit { public GameItemElement Element; public Style Style; }

        public sealed class TabsHit
        {
            public MachineryTabs Tabs;
            public readonly List<GameInventory> Pages = new List<GameInventory>();
        }

        static StoreClient client;
        static Kind kind;
        static bool clientGoods;
        // Filter mode: marks what matches the item filter instead of what the customer wants. Locks don't apply:
        // it is for sorting your own stock.
        static bool filtering;

        static readonly Dictionary<GameItem, Entry> entries = new Dictionary<GameItem, Entry>();
        static int epoch;
        static StoreClient epochClient;
        static Kind epochKind;
        static bool epochGoods;
        static int epochFilter = -1;

        static int walk;
        static readonly Stopwatch clock = new Stopwatch();
        static bool outOfTime;
        static int sinceCheck;

        public static readonly List<Hit> Hits = new List<Hit>();
        public static readonly List<TabsHit> Tabs = new List<TabsHit>();
        static readonly List<TabsHit> tabsPool = new List<TabsHit>();
        static readonly HashSet<object> nodesSeen = new HashSet<object>();

        // Statistics of the last walk, for the timing log.
        public static int Visited, Computed;

        // An item changed: its own record is stale, and so is the inside of every container above it.
        public static void Forget(GameItem item)
        {
            if (item == null) return;
            Entry e;
            if (entries.TryGetValue(item, out e)) { e.Epoch = -1; e.InsideEpoch = -1; }
            ForgetAbove(item.parentInventory);
        }

        // Something entered or left this node: the inside of every container from here up is stale.
        public static void ForgetAbove(GraphNodeStorage node)
        {
            var item = node as GameItem;
            if (item == null)
            {
                var inventory = node as GameInventory;
                item = inventory != null ? inventory.GetParentItem() : null;
            }
            for (int depth = 0; item != null && depth < MaxDepth; depth++)
            {
                Entry e;
                if (entries.TryGetValue(item, out e)) e.InsideEpoch = -1;
                var inventory = item.parentInventory;
                item = inventory != null ? inventory.GetParentItem() : null;
            }
        }

        public static void ForgetAll()
        {
            epoch++;
            // Items that left the shop keep their record; drop them all now and then.
            if (entries.Count > 4 * Visited + 4096) entries.Clear();
        }

        public static StoreClient CurrentClient()
        {
            // PlayerStore.Instance creates the store when there is none, and outside the shop (main menu, loading)
            // that fails. EmporiumEntry.Instance only reads a field: no shop on screen, no customer.
            if (EmporiumEntry.Instance == null) return null;
            var store = PlayerStore.Instance;
            if (store == null || store.currentClientInstance == null) return null;
            return store.currentClientInstance.GetClientBlueprint();
        }

        // Cheap fingerprint of what the customer is doing, polled to notice a change of intent.
        public static int Intent(StoreClient c)
        {
            if (c == null) return 0;
            return 1 | ((int)c.clientIntent == 4 ? 2 : 0) | (c.IsClientBuying() ? 4 : 0) | (c.IsClientSelling() ? 8 : 0);
        }

        // Fills Hits (every shown item with a mark) and Tabs. Returns false when there is nothing to mark.
        // complete is false when the time budget ran out; walk again next frame.
        public static bool Walk(StoreClient current, bool highlightClientGoods, out bool complete)
        {
            complete = true;
            Hits.Clear();
            Tabs.Clear();
            nodesSeen.Clear();
            Visited = Computed = 0;
            walk++;
            client = current;
            clientGoods = highlightClientGoods;
            var shop = EmporiumEntry.Instance;
            if (shop == null) return false;

            filtering = Filter.Active;
            if (filtering)
            {
                if (epochFilter != Filter.Version) { ForgetAll(); epochFilter = Filter.Version; epochClient = null; }
            }
            else
            {
                if (epochFilter != -1) { ForgetAll(); epochFilter = -1; epochClient = null; }
                if (client == null) return false;
                kind.Inspector = (int)client.clientIntent == 4;
                kind.Buying = client.IsClientBuying();
                kind.Selling = client.IsClientSelling();
                if (!kind.Inspector && !kind.Buying && !(kind.Selling && clientGoods)) return false;
                if (client != epochClient || kind.Inspector != epochKind.Inspector || kind.Buying != epochKind.Buying ||
                    kind.Selling != epochKind.Selling || clientGoods != epochGoods)
                {
                    ForgetAll();
                    epochClient = client; epochKind = kind; epochGoods = clientGoods;
                }
            }

            outOfTime = false;
            sinceCheck = 0;
            clock.Reset();
            clock.Start();
            complete &= Root(shop.invElement);
            complete &= Root(shop.frontInvinvElement);
            complete &= Root(shop.backInvinvElement);
            complete &= Root(shop.backInvinvElementCounter);
            complete &= Root(shop.showcaseElement);
            complete &= Root(shop.bazarLeftinvElement);
            complete &= Root(shop.hiddenElement);
            complete &= Root(shop.soldElement);
            clock.Stop();
            return true;
        }

        public static Style StyleOf(GameItem item)
        {
            Entry e;
            return item != null && entries.TryGetValue(item, out e) && e.Walk == walk ? e.Style : default(Style);
        }

        static bool Root(GameInventory inventory)
        {
            if (inventory == null || !nodesSeen.Add(inventory)) return true;
            var items = inventory.childItems;
            if (items == null) return true;
            bool complete = true;
            for (int i = 0; i < items.Count; i++)
                if (items[i] != null) complete &= Item(items[i], 0, false, false, true).Complete;
            return complete;
        }

        // Checked every few items so the stopwatch costs nothing noticeable.
        static bool OverBudget()
        {
            if (outOfTime) return true;
            if (++sinceCheck < 16) return false;
            sinceCheck = 0;
            outOfTime = clock.Elapsed.TotalMilliseconds > Budget;
            return outOfTime;
        }

        // lockedAbove / blockedAbove: a container above is locked / hides its contents from a search.
        // shown: every container above is open, so the item is on screen.
        static Up Item(GameItem item, int depth, bool lockedAbove, bool blockedAbove, bool shown)
        {
            Entry e;
            if (!entries.TryGetValue(item, out e)) { e = new Entry(); entries[item] = e; }
            if (e.Walk == walk) return e.Last;
            e.Walk = walk;
            e.Style = default(Style);
            Visited++;

            var up = new Up { Complete = true };
            if (e.Epoch != epoch)
            {
                if (OverBudget()) { up.Complete = false; e.Last = up; return up; }
                ComputeSelf(item, e);
            }

            bool opened = shown && IsOpened(item);

            // Children: open containers are walked (their contents are on screen); closed ones answer from the
            // record while nothing inside changed.
            var inside = e.Inside;
            bool allBought = e.InsideAllBought;
            bool insideOk = e.InsideEpoch == epoch;
            if (opened || !insideOk)
            {
                if (depth >= MaxDepth) { inside = default(Agg); allBought = true; insideOk = true; }
                else if (!opened && OverBudget()) insideOk = false;
                else
                {
                    inside = default(Agg);
                    allBought = true;
                    bool complete = true;
                    Children(item.children, depth + 1, lockedAbove || e.Locked, blockedAbove || e.BlocksSearch, opened,
                        ref inside, ref allBought, ref complete);
                    insideOk = complete;
                    if (complete) { e.Inside = inside; e.InsideAllBought = allBought; e.InsideEpoch = epoch; }
                }
            }
            if (!insideOk) { up.Complete = false; e.Last = up; return up; }

            var self = default(Agg);
            if (filtering) self.Buyer = e.FilterMark;
            else
            {
                if (kind.Buying)
                {
                    up.Bought = e.SelfOk && (!e.ContentsChecked || allBought);
                    if (up.Bought) self.Buyer = e.Illegal != Mark.None ? e.Illegal : Mark.Normal;
                }
                if (kind.Inspector || (kind.Selling && clientGoods)) self.Illegal = e.Illegal;
            }
            up.AllBought = up.Bought && allBought;

            if (shown)
            {
                var style = Display(e, self, inside, lockedAbove, blockedAbove);
                e.Style = style;
                var element = item as GameItemElement;
                if (style.Mark != Mark.None && element != null)
                    Hits.Add(new Hit { Element = element, Style = style });
                if (opened) FindTabs(item);
            }

            // What the containers above see: this item (unless it is behind a lock) and, unless it is locked
            // (or blocks a search during an inspection), what is inside it.
            if (filtering || !e.BehindLock) up.Agg = self;
            if (!e.Locked)
            {
                up.Agg.Buyer = Max(up.Agg.Buyer, inside.Buyer);
                if (!e.BlocksSearch) up.Agg.Illegal = Max(up.Agg.Illegal, inside.Illegal);
            }
            e.Last = up;
            return up;
        }

        static void ComputeSelf(GameItem item, Entry e)
        {
            Computed++;
            e.Epoch = epoch;
            if (filtering)
            {
                e.FilterMark = Filter.Match(item);
                e.Locked = e.BlocksSearch = e.BehindLock = e.Immovable = false;
                return;
            }
            e.Illegal = Illegality(item);
            e.Locked = IsLocked(item);
            e.BehindLock = item.IsTag("ITEM_BEHIND_LOCK_TAG");
            e.Immovable = IsImmovable(item);
            e.BlocksSearch = kind.Inspector && ContrabandHelper.IsBlockSearchContainer(item);
            e.Owned = GeneralHelper.IsItemOwned(item);
            if (kind.Buying)
            {
                e.SelfOk = client.IsClientBuyingThisItem(item, true);
                e.ContentsChecked = !LockHelper.IsCurrentlyLocked(item) && item.contentWindow != null &&
                    !item.IsTag("ADV_GUN_TAG");
            }
        }

        static void Children(List<GraphNodeStorage> nodes, int depth, bool lockedAbove, bool blockedAbove, bool shown,
            ref Agg inside, ref bool allBought, ref bool complete)
        {
            if (nodes == null) return;
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node == null) continue;
                var item = node as GameItem;
                if (item != null)
                {
                    var u = Item(item, depth, lockedAbove, blockedAbove, shown);
                    if (!u.Complete) { complete = false; continue; }
                    inside.Buyer = Max(inside.Buyer, u.Agg.Buyer);
                    inside.Illegal = Max(inside.Illegal, u.Agg.Illegal);
                    allBought &= u.AllBought;
                }
                else if (depth < MaxDepth && nodesSeen.Add(node))
                    Children(node.children, depth + 1, lockedAbove, blockedAbove, shown, ref inside, ref allBought, ref complete);
            }
        }

        static Style Display(Entry e, Agg self, Agg inside, bool lockedAbove, bool blockedAbove)
        {
            if (filtering)
                return self.Buyer != Mark.None ? new Style(self.Buyer, false) : new Style(inside.Buyer, true);
            if (lockedAbove || e.BehindLock || e.Immovable) return default(Style);
            if (kind.Inspector)
            {
                if (blockedAbove) return default(Style);
                if (self.Illegal != Mark.None) return new Style(self.Illegal, false);
                if (e.Locked || e.BlocksSearch) return default(Style);
                return new Style(inside.Illegal, true);
            }
            if (kind.Selling && !e.Owned)
            {
                if (!clientGoods) return default(Style);
                if (self.Illegal != Mark.None) return new Style(self.Illegal, false);
                if (e.Locked) return default(Style);
                return new Style(inside.Illegal, true);
            }
            if (kind.Buying)
            {
                if (self.Buyer != Mark.None) return new Style(self.Buyer, false);
                if (e.Locked) return default(Style);
                return new Style(inside.Buyer, true);
            }
            return default(Style);
        }

        // An open machine with storage tabs: remember its pages so the tabs can be marked after the walk.
        static void FindTabs(GameItem item)
        {
            var window = item.contentWindow;
            var row = window != null ? window.childElement as MachineryPanelRow : null;
            if (row == null) return;
            var tabs = TabsOf(row);
            if (tabs == null || tabs.count <= 0) return;
            if (tabsPool.Count <= Tabs.Count) tabsPool.Add(new TabsHit());
            var hit = tabsPool[Tabs.Count];
            hit.Tabs = tabs;
            hit.Pages.Clear();
            for (int i = 0; i < tabs.count; i++) hit.Pages.Add(PageInventory(tabs, i));
            Tabs.Add(hit);
        }

        static MachineryTabs TabsOf(MachineryPanelRow row)
        {
            var panel = row.GetPanel("storage");
            var tabs = panel != null ? panel.content as MachineryTabs : null;
            if (tabs != null) return tabs;
            var panels = row.Panels;
            if (panels == null) return null;
            for (int i = 0; i < panels.Count; i++)
            {
                tabs = panels[i] != null ? panels[i].content as MachineryTabs : null;
                if (tabs != null) return tabs;
            }
            return null;
        }

        static GameInventory PageInventory(MachineryTabs tabs, int index)
        {
            var page = tabs.GetPage(index);
            var intake = page as MachineryIntake;
            if (intake != null) return intake.content as GameInventory;
            return page as GameInventory;
        }

        static Mark Max(Mark a, Mark b) { return a > b ? a : b; }

        static Mark Illegality(GameItem item)
        {
            if (ContrabandHelper.IsContraband(item)) return Mark.Contraband;
            if (StolenHelper.IsStolenItem(item)) return Mark.Stolen;
            return Mark.None;
        }

        static bool IsOpened(GameItem item)
        {
            var window = item.contentWindow;
            return window != null && window.IsVisible();
        }

        static bool IsLocked(GameItem item)
        {
            if (item.IsTag("LOCKED_CONTAINER_TAG") && LockStatus(item)) return true;
            if (item.IsGameItemType("SUPPLY_CRATE") && LockStatus(item)) return true;
            var window = item.contentWindow;
            return window != null && window.isLocked;
        }

        static bool LockStatus(GameItem item)
        {
            var tag = item.GetTagReadonly("LOCK_STATUS");
            return tag != null && tag.GetBool();
        }

        static bool IsImmovable(GameItem item)
        {
            if (item.IsTag("DEBRIS_PILE_TAG")) return true;
            var mayRemove = item.mayInventoryRemoveItemFunc;
            return mayRemove != null && !mayRemove(item, item.parentInventory);
        }
    }
}
