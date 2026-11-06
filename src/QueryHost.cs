using System;
using System.Collections.Generic;
using System.Threading;

namespace DemandHighlight
{
    // The public query API in the game: puts QueryApi's delegates into their AppDomain slots when the mod is enabled
    // and takes them out when it is disabled. Queries run on the same parts as the Item Filter: its language (type
    // names in the player's language included), Pred.Eval on LiveItem, and the shop walk (Shop.Each). Nothing of the
    // highlight (Engine, Painter, the window) is touched or needed, and nothing is kept between calls: each run walks
    // the shop once, and the matches go into the caller's list.
    public static class QueryHost
    {
        static int mainThread = -1;

        public static void Publish()
        {
            mainThread = Thread.CurrentThread.ManagedThreadId;
            var view = new LiveItem();
            var host = new QueryApi.Host<GameItem>
            {
                Resolve = Filter.ResolveType,
                Ready = () => Shop.Loaded,
                OnMainThread = () => Thread.CurrentThread.ManagedThreadId == mainThread,
                EachItem = visit =>
                {
                    try { Shop.Each(visit); }
                    finally { view.Item = null; }
                },
                View = item => { view.Item = item; return view; },
            };
            var domain = AppDomain.CurrentDomain;
            domain.SetData(QueryApi.RunSlot, QueryApi.RunDelegate(host));
            domain.SetData(QueryApi.FormatSlot, QueryApi.FormatDelegate(host));
            domain.SetData(QueryApi.VersionSlot, QueryApi.Version);
        }

        public static void Withdraw()
        {
            var domain = AppDomain.CurrentDomain;
            domain.SetData(QueryApi.RunSlot, null);
            domain.SetData(QueryApi.FormatSlot, null);
            domain.SetData(QueryApi.VersionSlot, null);
        }
    }
}
