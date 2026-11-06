using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DemandHighlight
{
    // The back half: answers a Query over everything in the store that is on screen, and hands the answer to the
    // painter as a Result. It knows nothing about customers or the item filter; the front (Intent) turns those into
    // queries.
    //
    // Cost model:
    // - Three kinds of answer are kept per item, each for exactly as long as it holds:
    //     facts (illegal, locked, owned...): until the item changes;
    //     the game's answer to an Ask: until the item changes or the question's name does (the name holds everything
    //       the answer depends on);
    //     the verdict on the query, and the summary of what is inside a container: until the query changes (by value)
    //       or the item, or something inside it, changes.
    //   So a sale to a variety buyer, which only adds an identifier to Not(IdIn ...), re-judges items without asking
    //   the game again.
    // - Each item asks the game about itself only (contents ignored). The game's own check, run on a container, lists
    //   every descendant and re-checks each one recursively, which is quadratic in a deep store. Containers are judged
    //   bottom-up here instead, from the summaries of what is inside them.
    // - A walk visits only what is on screen (the shop's inventories and open containers). Closed containers answer
    //   from their summary.
    // - A walk that would take more than Budget milliseconds stops computing and finishes on later frames, so a new
    //   query in a huge store never costs one long frame. Items not computed yet stay unmarked for a frame.
    public static class Engine
    {
        const int MaxDepth = 16;
        const double Budget = 2.5;

        // What an item reports to its container.
        struct Up
        {
            public Agg Agg;
            public bool Bought;    // wanted, contents included when the query wants whole containers
            public bool AllBought; // Bought, and every item inside it is bought too
            public bool Complete;
        }

        sealed class Entry
        {
            public bool FactsKnown;
            public Facts F;
            public bool Contraband, Stolen;

            public string AskName;         // the question last asked of the game about this item, and its answer
            public bool AskAnswer;

            public int VerdictEpoch = -1;   // the verdict is for the query of this epoch
            public Verdict V;

            public int InsideEpoch = -1;    // the summary of what is inside is for the query of this epoch
            public Agg Inside;
            public bool InsideAllBought;

            public int Walk;                // last walk that visited it
            public Up Last;
            public Style Style;
        }

        static readonly Dictionary<GameItem, Entry> entries = new Dictionary<GameItem, Entry>();
        static Query query;
        static Plan plan;
        static int epoch;
        static Func<string, GameItem, bool> ask;

        static int walk;
        static readonly Stopwatch clock = new Stopwatch();
        static bool outOfTime;
        static int sinceCheck;
        static readonly HashSet<object> nodesSeen = new HashSet<object>();
        static readonly List<MachineryTabs> tabsFound = new List<MachineryTabs>();
        static readonly ItemView view = new ItemView();

        // The answer handed to the painter. Filled by Run, read until the next Run.
        public static readonly Result Result = new Result();

        // Statistics of the last walk, for the log.
        public static int Visited, Computed, Asked;

        public static Query Current { get { return query; } }

        // An item changed: everything kept about it is stale, and so is the inside of every container above it.
        public static void Forget(GameItem item)
        {
            if (item == null) return;
            Entry e;
            if (entries.TryGetValue(item, out e))
            {
                e.FactsKnown = false;
                e.AskName = null;
                e.VerdictEpoch = -1;
                e.InsideEpoch = -1;
            }
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

        // Leaving the store: every record and every reference to its items, windows and customer goes.
        public static void Release()
        {
            entries.Clear();
            query = null;
            plan = null;
            ask = null;
            Result.Clear();
            nodesSeen.Clear();
            tabsFound.Clear();
            view.Item = null;
            view.Entry = null;
        }

        // Answers q into Result. asker answers the query's Ask leaves for one item; it is only called when an answer is
        // not kept. Returns false when the query marks nothing (Result is then empty). complete is false when the time
        // budget ran out; run again next frame.
        public static bool Run(Query q, Func<string, GameItem, bool> asker, out bool complete)
        {
            complete = true;
            Result.Clear();
            nodesSeen.Clear();
            tabsFound.Clear();
            int lastVisited = Visited;
            Visited = Computed = Asked = 0;
            walk++;
            ask = asker;
            if (!q.Equals(query))
            {
                query = q;
                plan = Plan.Of(q);
                epoch++;
                // Items that left the shop keep their record; drop them all now and then.
                if (entries.Count > 4 * lastVisited + 4096) entries.Clear();
            }
            if (!plan.Any) return false;
            var shop = EmporiumEntry.Instance;
            if (shop == null) return false;

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

            // Each storage tab takes the strongest mark of the items on its page.
            for (int i = 0; i < tabsFound.Count; i++) Result.AddTabs(tabsFound[i], PageStyles(tabsFound[i]));
            Result.Complete = complete;
            return true;
        }

        // Walks the top-level items of one shop inventory. nodesSeen stops an inventory that two roots share from being
        // walked twice in one pass.
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
            if (!e.FactsKnown || e.VerdictEpoch != epoch)
            {
                if (OverBudget()) { up.Complete = false; e.Last = up; return up; }
                Judge(item, e);
            }

            bool opened = shown && IsOpened(item);

            // Children: open containers are walked (their contents are on screen); closed ones answer from their
            // summary while nothing inside changed.
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
                    Children(item.children, depth + 1, lockedAbove || e.F.Locked, blockedAbove || e.V.BlocksSearch, opened,
                        ref inside, ref allBought, ref complete);
                    insideOk = complete;
                    if (complete) { e.Inside = inside; e.InsideAllBought = allBought; e.InsideEpoch = epoch; }
                }
            }
            if (!insideOk) { up.Complete = false; e.Last = up; return up; }

            var self = Decide.Self(plan, e.F, e.V, allBought, out up.Bought);
            up.AllBought = up.Bought && allBought;

            if (shown)
            {
                var style = Decide.Display(plan, e.F, e.V, self, inside, lockedAbove, blockedAbove);
                e.Style = style;
                var element = item as GameItemElement;
                if (style.Mark != Mark.None && element != null) Result.AddItem(element, style);
                if (opened) FindTabs(item);
            }

            // What the containers above see: this item (unless it is behind a lock) and, unless it is locked (or
            // blocks a search), what is inside it.
            up.Agg = Decide.Report(plan, self, inside, e.F, e.V);
            e.Last = up;
            return up;
        }

        // Reads what is missing: the item's facts (after a change) and its verdict on the query (after a change of
        // either).
        static void Judge(GameItem item, Entry e)
        {
            Computed++;
            if (!e.FactsKnown)
            {
                e.Contraband = ContrabandHelper.IsContraband(item);
                e.Stolen = StolenHelper.IsStolenItem(item);
                var f = new Facts();
                f.Illegal = Decide.Illegality(e.Contraband, e.Stolen);
                f.Locked = IsLocked(item);
                f.BehindLock = item.IsTag("ITEM_BEHIND_LOCK_TAG");
                f.Immovable = IsImmovable(item);
                f.Owned = GeneralHelper.IsItemOwned(item);
                f.HasContents = !CurrentlyLocked(item) && item.contentWindow != null && !item.IsTag("ADV_GUN_TAG");
                e.F = f;
                e.FactsKnown = true;
            }
            view.Item = item;
            view.Entry = e;
            var v = new Verdict();
            v.Want = plan.Want != null && plan.Want.Eval(view);
            v.Search = plan.Search != null && plan.Search.Eval(view);
            v.Goods = plan.Goods != null && plan.Goods.Eval(view);
            v.Filter = plan.Filter != null && plan.Filter.Eval(view);
            v.BlocksSearch = plan.Search != null && ContrabandHelper.IsBlockSearchContainer(item);
            view.Item = null;
            view.Entry = null;
            e.V = v;
            e.VerdictEpoch = epoch;
        }

        // The game seen through IItem, for one item at a time. Facts are read from the entry; the rest from the item.
        sealed class ItemView : IItem
        {
            public GameItem Item;
            public Entry Entry;

            public string Id { get { return Item.identifier; } }
            public bool Owned { get { return Entry.F.Owned; } }
            public bool Contraband { get { return Entry.Contraband; } }
            public bool Stolen { get { return Entry.Stolen; } }

            public bool HasTag(string tag) { return Item.IsTag(tag); }

            public bool HasType(string type) { return LiveItem.HasType(Item, type); }
            public bool HasCondition(string condition) { return LiveItem.HasCondition(Item, condition); }

            public bool Ask(string oracle)
            {
                if (Entry.AskName == oracle) return Entry.AskAnswer;
                bool answer = ask != null && ask(oracle, Item);
                Asked++;
                Entry.AskName = oracle;
                Entry.AskAnswer = answer;
                return answer;
            }
        }

        // Visits the items under a container. A child that is not an item (an inventory or other node) is searched too,
        // once per walk, so items one node deeper still count as inside.
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
                    inside.Buyer = Decide.Max(inside.Buyer, u.Agg.Buyer);
                    inside.Illegal = Decide.Max(inside.Illegal, u.Agg.Illegal);
                    allBought &= u.AllBought;
                }
                else if (depth < MaxDepth && nodesSeen.Add(node))
                    Children(node.children, depth + 1, lockedAbove, blockedAbove, shown, ref inside, ref allBought, ref complete);
            }
        }

        // An open machine with storage tabs: remember it so its tabs can be marked after the walk.
        static void FindTabs(GameItem item)
        {
            var window = item.contentWindow;
            var row = window != null ? window.childElement as MachineryPanelRow : null;
            if (row == null) return;
            var tabs = TabsOf(row);
            if (tabs != null && tabs.count > 0) tabsFound.Add(tabs);
        }

        // The strongest mark of this walk among the items on each page.
        static readonly List<Style> pageStyles = new List<Style>();
        static List<Style> PageStyles(MachineryTabs tabs)
        {
            pageStyles.Clear();
            for (int i = 0; i < tabs.count; i++)
            {
                var style = default(Style);
                var page = PageInventory(tabs, i);
                var items = page != null ? page.childItems : null;
                if (items != null)
                    for (int k = 0; k < items.Count; k++)
                    {
                        Entry e;
                        if (items[k] != null && entries.TryGetValue(items[k], out e) && e.Walk == walk)
                            style = Style.Max(style, e.Style);
                    }
                pageStyles.Add(style);
            }
            return pageStyles;
        }

        // The storage tabs of an open machine: the panel named "storage" when it has them, else the first panel that does.
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

        static bool IsOpened(GameItem item)
        {
            var window = item.contentWindow;
            return window != null && window.IsVisible();
        }

        // Locked by the tag the game sets on lockable containers and supply crates, or by an open window that reports it.
        static bool IsLocked(GameItem item)
        {
            if (item.IsTag("LOCKED_CONTAINER_TAG") && LockStatus(item)) return true;
            if (item.IsGameItemType("SUPPLY_CRATE") && LockStatus(item)) return true;
            var window = item.contentWindow;
            return window != null && window.isLocked;
        }

        // Reading a tag must not change the item. GameItem.GetTagReadonly (and the game helpers built on it) adds a
        // missing tag to the item, which is then saved, so tags are looked up in the item's dictionary instead.
        static TagState PeekTag(GameItem item, string key)
        {
            var state = item.modifiedState;
            var dict = state != null ? state.dict : null;
            TagState tag;
            return dict != null && dict.TryGetValue(key, out tag) ? tag : null;
        }

        // A missing LOCK_STATUS means not locked.
        static bool LockStatus(GameItem item)
        {
            var tag = PeekTag(item, "LOCK_STATUS");
            return tag != null && tag.GetBool();
        }

        // LockHelper.IsCurrentlyLocked, without its GetTagReadonly.
        static bool CurrentlyLocked(GameItem item)
        {
            return LockHelper.IsLockedContainer(item) && LockStatus(item);
        }

        // Debris piles and anything the game's own removal check refuses are never offered to a customer, so they get no outline.
        static bool IsImmovable(GameItem item)
        {
            if (item.IsTag("DEBRIS_PILE_TAG")) return true;
            var mayRemove = item.mayInventoryRemoveItemFunc;
            return mayRemove != null && !mayRemove(item, item.parentInventory);
        }
    }

    // What the painter draws: the marked items, and the marked tabs of open machines. Holds store objects only until the
    // next run (or Clear, when leaving the store).
    public sealed class Result
    {
        public struct Hit { public GameItemElement Element; public Style Style; }

        public sealed class TabMarks
        {
            public MachineryTabs Tabs;
            public readonly List<Style> Pages = new List<Style>();
        }

        public readonly List<Hit> Items = new List<Hit>();
        public readonly List<TabMarks> Tabs = new List<TabMarks>();
        // The walk reached everything: anything not in the result is not marked.
        public bool Complete;

        readonly List<TabMarks> pool = new List<TabMarks>();

        internal void Clear()
        {
            Items.Clear();
            for (int i = 0; i < Tabs.Count; i++) { Tabs[i].Tabs = null; Tabs[i].Pages.Clear(); }
            Tabs.Clear();
            Complete = false;
        }

        internal void AddItem(GameItemElement element, Style style)
        {
            Items.Add(new Hit { Element = element, Style = style });
        }

        internal void AddTabs(MachineryTabs tabs, List<Style> pages)
        {
            if (pool.Count <= Tabs.Count) pool.Add(new TabMarks());
            var marks = pool[Tabs.Count];
            marks.Tabs = tabs;
            marks.Pages.Clear();
            marks.Pages.AddRange(pages);
            Tabs.Add(marks);
        }
    }
}
