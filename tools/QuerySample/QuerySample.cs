using System.Collections.Generic;
using UnityEngine;

namespace QuerySample
{
    // A development mod that uses Demand Highlight's query API the way another mod would: through the copied client
    // file (api/DemandHighlightQuery.cs), with no reference to Demand Highlight. Each time a shop loads, it runs a few
    // queries and writes the answers to Player.log:
    //
    //   [QuerySample] query API 1.0
    //   [QuerySample] type:FOOD -> 21 items, read as type:FOOD
    //   [QuerySample] type:(FOOD or MEDICAL) -stolen -> ...
    //   [QuerySample] stolen and shiny -> Invalid UnknownWord 'shiny' at 11
    //   [QuerySample] short form: not stolen and type:(food or medical) -> -stolen + type:(food | medical)
    //
    // With Demand Highlight off, it writes "query API not available" once and does nothing else. It never draws
    // anything and keeps no item after the line is written. Not part of the released mod; build it with
    // tools/QuerySample/build.sh.
    public sealed class QuerySampleMod : IMod
    {
        const string Tag = "[QuerySample] ";
        static GameObject runner;

        public void Init(ModManifest manifest) { }

        public void OnEnable()
        {
            if (runner != null) return;
            runner = new GameObject("QuerySample.Runner");
            Object.DontDestroyOnLoad(runner);
            runner.hideFlags = HideFlags.HideAndDontSave;
            runner.AddComponent<Runner>();
        }

        public void OnDisable()
        {
            if (runner != null) Object.Destroy(runner);
            runner = null;
        }

        public sealed class Runner : MonoBehaviour
        {
            bool inShop, toldMissing;
            readonly List<GameItem> found = new List<GameItem>();

            void Update()
            {
                bool shop = EmporiumEntry.Instance != null;
                if (shop == inShop) return;
                inShop = shop;
                if (!shop) return;
                if (!DemandHighlightQuery.Available)
                {
                    if (!toldMissing) Debug.Log(Tag + "query API not available");
                    toldMissing = true;
                    return;
                }
                Debug.Log(Tag + "query API " + DemandHighlightQuery.Version);
                Ask("type:FOOD");
                Ask("type:(FOOD or MEDICAL) -stolen");
                Ask("contraband " + DemandHighlightQuery.Field("id", "two words"));
                Ask("stolen and shiny");
                var f = DemandHighlightQuery.Format("not stolen and type:(food or medical)", true);
                Debug.Log(Tag + "short form: not stolen and type:(food or medical) -> " + (f.Ok ? f.Text : f.Status + " " + f.Message));
                found.Clear();
            }

            void Ask(string expression)
            {
                var r = DemandHighlightQuery.Run(expression, found);
                if (r.Ok)
                {
                    string first = found.Count > 0 && found[0] != null ? ", first " + found[0].identifier : "";
                    Debug.Log(Tag + expression + " -> " + r.Count + " items" + first + ", read as " + r.Text);
                }
                else if (r.Status == DemandHighlightQuery.Status.Invalid)
                    Debug.Log(Tag + expression + " -> Invalid " + r.Problem + " '" + r.Detail + "' at " + r.Position);
                else
                    Debug.Log(Tag + expression + " -> " + r.Status + ": " + r.Message);
                found.Clear();
            }
        }
    }
}
