using System;
using System.Collections.Generic;
using UnityEngine;

namespace DemandHighlight
{
    // The Item Filter window, a game window built with the game's own window builder (CustomUIBuilder), the kind its
    // storage-preset manager uses, so texture packs reskin it with everything else.
    //
    //   ITEM FILTER                                                                    X
    //   [ stolen and type:("Household Good" or Medical) and not type:Junk_________ ] (x)
    //   (how the box was read, or the error in red)                         [ ] Short
    //   Matches 8 items   1,204¢ total (1% of the shop)   20 cells   60¢ per cell (shop 45¢)
    //   [✓] Stolen 9    [ ] Contraband 2
    //   --------------------------------------------------------------------------------
    //   GOODS
    //   [✓] Household Good 5      [✓] Medical 3             [✗] J̶u̶n̶k̶ -4
    //   ...
    //
    // The box holds the filter, the one source of truth (Filter.cs). Ticks are a view of it with three states, off,
    // in (✓) and out (✗), cycled by clicking: they show the facets read from it, and a click edits it and writes it
    // back into the box. Counts follow a shop search's rule (Facets.Pure.cs): each tick counts with everything else
    // applied, so with Stolen ticked each type shows its stolen items; a ✗ shows how many it takes away. A typo
    // turns the box red and keeps the last filter that read. The type groups scroll; the rest stays put. The window
    // is built when opened, destroyed when closed, and reopens where it was left.
    public static class Panel
    {
        const string WindowId = "demand_highlight_filter";
        const string DimColor = "#8E887C", ErrorColor = "#D0614A";
        static readonly Color ErrorText = new Color(0.82f, 0.38f, 0.29f);
        const float Width = 820f, Height = 640f, CellWidth = 250f, CellHeight = 30f;
        const float ShowGap = 0.5f;

        static CustomUIWindow window;
        static CustomUIElement input, status, stats, stolenTick, contrabandTick;
        static Color? inputColor;
        // Each type's tick and name, so its count and state are rewritten in place.
        static readonly Dictionary<string, KeyValuePair<CustomUIElement, string>> typeTicks =
            new Dictionary<string, KeyValuePair<CustomUIElement, string>>();
        static RectTransform ticksHost, typesHost;
        static int shownStock = -1, shownFilter = -1;
        static float nextShow;
        static bool writing;     // ticks being set from the filter, not clicked
        static Vector2? lastPosition;
        static bool warned;

        // Set by DemandMod: L gives localized text, Changed asks for a new walk after a change.
        public static Func<string, string> L = k => k;
        public static Action Changed = () => { };

        // The "numbers line" option (DemandMod): off hides the line and the walk skips reading worth and cells.
        public static Func<bool> Numbers = () => true;

        public static void SetNumbers(bool on)
        {
            if (stats == null || !stats.IsAlive) return;
            stats.SetVisible(on);
            if (on) Show(Filter.Count());
        }

        // Families in reading order. A type the list does not know goes to "other".
        static readonly string[][] Families =
        {
            new[] { "fam_goods", "FOOD", "FRESH_FOOD", "PROCESSED_FOOD", "TREAT", "BREVAGE", "WATER", "WATER_RATION",
                "ALCOHOL", "SUBSTANCE", "HOUSEHOLD_GOOD", "MEDICAL", "ORGAN", "DOCUMENT", "ACCESS_CARD", "LOTTERY",
                "ART", "JUNK", "NOT_FOR_RESALE", "SUPPLY_CRATE", "STORAGE" },
            new[] { "fam_supplies", "MATERIAL", "BASIC_MATERIAL", "REFINED_MATERIAL", "SEED", "MODIFIED_SEED",
                "FARMING_SUPPLIES", "CHEMICAL_SUPPLIES", "MEDICAL_SUPPLIES", "WATER_PURIFICATION_SUPPLY",
                "OXYGEN_SUPPLY", "ENERGY_CELL", "FILTER" },
            new[] { "fam_gear", "TOOL", "MEDICAL_TOOL", "WEAPON", "MELEE_WEAPON", "FIREARM", "AMMUNITION", "MUNITION",
                "GUN_MOD", "MODDABLE", "ARMOR" },
            new[] { "fam_machines", "MACHINE", "MODULE", "NODE", "HYDROPONIC_MODULE", "ALARM_MODULE", "PRINTER_MODULE",
                "EVAPORATOR_MODULE" },
            new[] { "fam_grade", "MAKESHIFT", "LOWTECH", "MIDLINE", "HIGHEND", "RARE", "LUXURY_ITEM" },
            new[] { "fam_legal", "CONTRABAND", "RESTRICTED_1", "RESTRICTED_2", "RESTRICTED_3", "NARCOTIC", "POISON" },
        };

        public static IEnumerable<string> KnownTypes()
        {
            foreach (var family in Families)
                for (int i = 1; i < family.Length; i++) yield return family[i];
        }

        public static bool IsOpen { get { return window != null && window.IsAlive && window.IsVisible; } }

        // True while the player types in the box, so the mod's keys do not fire on letters.
        public static bool Typing
        {
            get
            {
                if (!IsOpen || input == null || !input.IsAlive || input.input == null) return false;
                return input.input.isFocused;
            }
        }

        public static void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public static void Close()
        {
            if (window != null && window.IsAlive) window.Close();
            Forget();
        }

        // Leaving the store: the window and its elements belong to the store's canvas.
        public static void Destroy()
        {
            Close();
        }

        static void Forget()
        {
            window = null;
            input = status = stats = stolenTick = contrabandTick = null;
            inputColor = null;
            typeTicks.Clear();
            marks.Clear();
            ticksHost = typesHost = null;
            shownStock = shownFilter = -1;
        }

        static void Open()
        {
            var ui = CustomUIManager.Instance;
            if (ui == null)
            {
                if (!warned) { warned = true; Debug.LogWarning(DemandMod.Tag + "the game's window manager is not ready"); }
                return;
            }
            var old = ui.GetWindow(WindowId);
            if (old != null && old.IsAlive) old.Close();
            LoadStyle();

            var b = ui.CreateWindow(WindowId, L("window_title"), "paper", null, CustomUIManager.Style.Auto)
                .SetSize(Width, Height)
                .SetDraggable(true)
                .SetCloseOnEscape(true)
                .OnClose(Closed);

            // The box and its clear cross; under it the line about the box with the Short tick at its end; then the
            // numbers.
            b.BeginRow(6f, TextAnchor.MiddleLeft);
            b.AddInput(L("expr_placeholder"), Filter.Text, Committed, null, Typed);
            input = b.Last;
            if (input != null)
            {
                input.SetPreferredSize(-1f, 32f);
                input.SetFlexible(1f, 0f);
                input.SetTooltip(L("tip_expr"));
                if (input.input != null && input.input.textComponent != null) inputColor = input.input.textComponent.color;
            }
            // The game's own red cross, the one its windows use for "remove"; its close button if the sprite is missing.
            var cross = ui.spriteRedXMark;
            if (cross != null) b.AddIconButton(cross, 24f, Clear, null, null);
            else b.AddCloseButton(Clear, null);
            b.WithTooltip(L("tip_clear"));
            b.End();
            b.BeginRow(12f, TextAnchor.MiddleLeft);
            b.AddLabel(" ", null);
            status = b.Last;
            if (status != null) status.SetFlexible(1f, 0f);
            b.AddToggle(L("short_form"), Filter.Style == Formatter.Style.Short, Short, null).WithTooltip(L("tip_short_form"));
            b.End();
            b.AddLabel(" ", null);
            stats = b.Last;
            if (stats != null && !Numbers()) stats.SetVisible(false);

            b.BeginRow(16f, TextAnchor.MiddleLeft);
            ticksHost = b.CurrentContainer;
            b.End();
            b.AddSeparator(null, 4);

            // The type groups, the only part that scrolls.
            b.BeginScrollFlexible(240f);
            typesHost = b.CurrentContainer;
            b.End();

            if (lastPosition.HasValue) b.SetPosition(lastPosition.Value); else b.Center();
            window = b.Show();
            Build();
            ShowStatus();
        }

        static void Closed()
        {
            if (window != null && window.Rect != null) lastPosition = window.Rect.anchoredPosition;
            Forget();
        }

        // Every keystroke: the text is compiled; when it reads it is the filter, and the ticks catch up on the next
        // refresh (at most ShowGap later).
        static void Typed(string text)
        {
            int before = Filter.Version;
            Filter.SetText(text);
            ShowStatus();
            if (Filter.Version != before) Changed();
        }

        // Enter, or leaving the box: the text is formatted, as an editor formats on save. Not while typing, so the
        // cursor is never moved under the player's hands; text with an error is left as it is.
        static void Committed(string text)
        {
            Typed(text);
            if (!Filter.Format()) return;
            if (input != null && input.IsAlive) input.SetString(Filter.Text, false);
            ShowStatus();
        }

        // The "short" tick: the box is written with +, |, - instead of and, or, not, from now on and at once. The
        // choice is kept between sessions.
        const string ShortPref = "DemandHighlight.shortForm";
        static bool styleLoaded;

        static void LoadStyle()
        {
            if (styleLoaded) return;
            styleLoaded = true;
            try { Filter.Style = PlayerPrefs.GetInt(ShortPref, 0) == 1 ? Formatter.Style.Short : Formatter.Style.Words; }
            catch (Exception) { }
        }

        static void Short(bool on)
        {
            try { PlayerPrefs.SetInt(ShortPref, on ? 1 : 0); } catch (Exception) { }
            if (!Filter.SetStyle(on ? Formatter.Style.Short : Formatter.Style.Words)) { ShowStatus(); return; }
            if (input != null && input.IsAlive) input.SetString(Filter.Text, false);
            ShowStatus();
        }

        // A click on a tick: the filter is edited, the box shows the new text, the counts follow at once.
        static void Ticked(Action edit)
        {
            if (writing) return;
            edit();
            if (input != null && input.IsAlive) input.SetString(Filter.Text, false);
            Changed();
            ShowStatus();
            Show(Filter.Count());
        }

        static void Clear()
        {
            Filter.Clear();
            if (input != null && input.IsAlive) input.SetString("", false);
            Changed();
            ShowStatus();
            Show(Filter.Count());
        }

        // The box in red while its text does not read, and the line under it: the error and where, how the text was
        // read when that differs from what is written (a type name resolved, a shorthand spelled out), or a hint
        // when the box is empty.
        static void ShowStatus()
        {
            var p = Filter.Parsed;
            if (input != null && input.IsAlive && input.input != null && input.input.textComponent != null && inputColor.HasValue)
                input.input.textComponent.color = p.Ok ? inputColor.Value : ErrorText;
            if (status == null || !status.IsAlive) return;
            string line;
            if (!p.Ok)
                line = "<color=" + ErrorColor + ">" + Problem(p) + "  " + L("err_pos").Replace("#", (p.Position + 1).ToString()) + "</color>";
            else if (Filter.Expression == null)
                line = "<color=" + DimColor + ">" + L("expr_hint") + "</color>";
            else
            {
                var reads = Filter.Expression.ToString(Filter.TypeText, Filter.Style);
                line = reads == Filter.Text.Trim() ? " " : "<color=" + DimColor + ">" + L("expr_reads").Replace("#", Escape(reads)) + "</color>";
            }
            status.SetText(line);
        }

        // Called every frame by DemandMod: while the window is open, the ticks, counts and numbers follow the stock
        // and the filter, at most every ShowGap seconds. Nothing is walked when neither changed.
        public static void Refresh()
        {
            if (!IsOpen) return;
            if (shownStock == Filter.StockVersion && shownFilter == Filter.Version) return;
            if (Time.unscaledTime < nextShow) return;
            Show(Filter.Count());
        }

        // Rewrites every tick (state and count) and the numbers line from one walk's result.
        static void Show(Filter.Counts c)
        {
            shownStock = Filter.StockVersion;
            shownFilter = Filter.Version;
            nextShow = Time.unscaledTime + ShowGap;
            writing = true;
            try
            {
                var st = Filter.Stolen;
                Set(stolenTick, L("filter_stolen"), st == Facets.State.Out ? c.StolenOut : c.Stolen, st);
                var ct = Filter.Contraband;
                Set(contrabandTick, L("filter_contraband"), ct == Facets.State.Out ? c.ContrabandOut : c.Contraband, ct);
                foreach (var pair in typeTicks)
                {
                    var state = Filter.OfType(pair.Key);
                    int n;
                    (state == Facets.State.Out ? c.TypesOut : c.Types).TryGetValue(pair.Key, out n);
                    Set(pair.Value.Key, pair.Value.Value, n, state);
                }
            }
            finally { writing = false; }
            if (Numbers()) ShowStats(c);
        }

        // A tick in one of its three states: empty, the game's check (in), or a cross drawn in the check's style
        // (out; Marks.cs). The window draws the mark itself: Unity's toggle keeps the box, the hover and the click,
        // but its mark is taken from it (Own), so the toggle's own on/off never shows and a click shows the next state
        // straight away, with no flash of the old one.
        sealed class Owned { public UnityEngine.UI.Image Mark; public Sprite Check, Box; }
        static readonly Dictionary<CustomUIElement, Owned> marks = new Dictionary<CustomUIElement, Owned>();

        static void Own(CustomUIElement tick)
        {
            if (tick == null || tick.toggle == null) return;
            Marks.Describe(tick.toggle);
            var mark = tick.toggle.graphic as UnityEngine.UI.Image;
            if (mark == null) return;
            var back = tick.toggle.targetGraphic as UnityEngine.UI.Image;
            tick.toggle.graphic = null;
            tick.toggle.toggleTransition = UnityEngine.UI.Toggle.ToggleTransition.None;
            mark.canvasRenderer.SetAlpha(1f);
            marks[tick] = new Owned { Mark = mark, Check = mark.sprite, Box = back != null && back != mark ? back.sprite : null };
        }

        static void Set(CustomUIElement tick, string name, int count, Facets.State state)
        {
            if (tick == null || !tick.IsAlive) return;
            Owned owned;
            if (marks.TryGetValue(tick, out owned) && owned.Mark != null)
            {
                var mark = owned.Mark;
                var cross = state == Facets.State.Out ? Marks.CrossFor(owned.Check, owned.Box) : null;
                var want = cross != null ? cross : owned.Check;
                if (mark.sprite != want) mark.sprite = want;
                bool on = state != Facets.State.Off;
                if (mark.enabled != on) mark.enabled = on;
            }
            else
            {
                bool on = state != Facets.State.Off;
                if (tick.GetBool() != on) tick.SetBool(on, false);
            }
            tick.SetText(Label(name, count, state));
        }

        // The numbers line. Without a filter it is the shop as a baseline:
        //   Shop  1,204 items   52,300¢ total   3,980 cells   13¢ per cell
        // with one, what matches against that baseline:
        //   Matches  23 items   4,120¢ total (8% of the shop)   41 cells   100¢ per cell (shop 13¢)
        // Labels dim, numbers bold in the window's own colour, so the numbers are what the eye finds.
        static void ShowStats(Filter.Counts c)
        {
            if (stats == null || !stats.IsAlive || !c.Totalled) return;
            var shown = c.Filtering ? c.Matched : c.Shop;
            var values = new Dictionary<string, string>
            {
                { "n", Number(shown.Items.ToString("N0")) },
                { "v", Number(Money(shown.Value)) },
                { "c", Number(shown.Cells.ToString("N0")) },
                { "d", Number(shown.PerCell < 0 ? "–" : Money(shown.PerCell)) },
                { "p", c.Filtering ? Stats.Share(shown.Value, c.Shop.Value) : null },
                { "a", c.Filtering && c.Shop.PerCell >= 0 ? Money(c.Shop.PerCell) : null },
            };
            var template = L(c.Filtering ? "stats_match" : "stats_shop");
            stats.SetText("<color=" + DimColor + ">" + Stats.Fill(template, values) + "</color>");
        }

        // A number stands out of the dim line: the colour is closed around it and opened again after.
        static string Number(string text) { return "</color><b>" + text + "</b><color=" + DimColor + ">"; }

        // Credits as the game writes them: the number, then its credit sign.
        static string Money(long value) { return value.ToString("N0") + GeneralHelper.CreditIcon(false); }

        static string Problem(Expr.Parsed p)
        {
            string key;
            switch (p.Problem)
            {
                case Expr.Problem.ExpectedTerm: key = "err_expected_term"; break;
                case Expr.Problem.ExpectedClose: key = "err_expected_close"; break;
                case Expr.Problem.ExpectedValue: key = "err_expected_value"; break;
                case Expr.Problem.UnknownKey: key = "err_unknown_key"; break;
                case Expr.Problem.UnknownWord: key = "err_unknown_word"; break;
                case Expr.Problem.UnclosedQuote: key = "err_unclosed_quote"; break;
                default: key = "err_unexpected"; break;
            }
            return L(key).Replace("#", Escape(p.Detail ?? ""));
        }

        // Typed text shown back must not open rich-text tags.
        static string Escape(string s) { return s.Replace("<", "<noparse><</noparse>"); }

        // Builds the ticks once per opening, in a layout that does not move: every type in the shop, grouped, most
        // common in the whole shop first. Show then fills in states and counts.
        static void Build()
        {
            if (!IsOpen || ticksHost == null || typesHost == null) return;
            var counts = Filter.Count();
            var ui = CustomUIManager.Instance;
            typeTicks.Clear();
            marks.Clear();

            CustomUIBuilder.ClearContainer(ticksHost);
            var t = new CustomUIBuilder(ui, window, ticksHost);
            t.AddToggle(L("filter_stolen"), Filter.Stolen != Facets.State.Off, _ => Ticked(() => Filter.SetStolen(Facets.Next(Filter.Stolen))), null)
                .WithTooltip(L("tip_stolen"));
            stolenTick = t.Last;
            Own(stolenTick);
            t.AddToggle(L("filter_contraband"), Filter.Contraband != Facets.State.Off, _ => Ticked(() => Filter.SetContraband(Facets.Next(Filter.Contraband))), null)
                .WithTooltip(L("tip_contraband"));
            contrabandTick = t.Last;
            Own(contrabandTick);
            CustomUIBuilder.RebuildLayout(ticksHost);

            CustomUIBuilder.ClearContainer(typesHost);
            var b = new CustomUIBuilder(ui, window, typesHost);
            if (counts.Present.Count == 0) b.AddLabel("<color=" + DimColor + ">" + L("no_items") + "</color>", null);
            else
            {
                bool first = true;
                foreach (var group in Groups(counts.Present))
                {
                    if (!first) b.AddSpace(10f);
                    first = false;
                    b.AddHeader(L(group.Key), null);
                    b.BeginGrid(3, CellWidth, CellHeight, 4f);
                    foreach (var pair in group.Value)
                    {
                        var type = pair.Key;
                        var name = TypeName(type);
                        b.AddToggle(name, Filter.OfType(type) != Facets.State.Off, _ => Ticked(() => Filter.SetType(type, Facets.Next(Filter.OfType(type)))), null)
                            .WithTooltip(L("tip_type").Replace("#", name));
                        typeTicks[type] = new KeyValuePair<CustomUIElement, string>(b.Last, name);
                        Own(b.Last);
                    }
                    b.End();
                }
            }
            CustomUIBuilder.RebuildLayout(typesHost);
            Show(counts);
        }

        // The groups present in the shop, in reading order, each sorted most common first. Unknown types go last.
        static List<KeyValuePair<string, List<KeyValuePair<string, int>>>> Groups(Dictionary<string, int> present)
        {
            var groups = new List<KeyValuePair<string, List<KeyValuePair<string, int>>>>();
            var known = new HashSet<string>();
            foreach (var family in Families)
            {
                var members = new List<KeyValuePair<string, int>>();
                for (int i = 1; i < family.Length; i++)
                {
                    known.Add(family[i]);
                    int n;
                    if (present.TryGetValue(family[i], out n)) members.Add(new KeyValuePair<string, int>(family[i], n));
                }
                if (members.Count > 0) groups.Add(new KeyValuePair<string, List<KeyValuePair<string, int>>>(family[0], members));
            }
            var other = new List<KeyValuePair<string, int>>();
            foreach (var pair in present) if (!known.Contains(pair.Key)) other.Add(pair);
            if (other.Count > 0) groups.Add(new KeyValuePair<string, List<KeyValuePair<string, int>>>("fam_other", other));
            foreach (var g in groups)
                g.Value.Sort((a, c) => a.Value != c.Value ? c.Value.CompareTo(a.Value) : string.CompareOrdinal(a.Key, c.Key));
            return groups;
        }

        // A name with its count dimmed behind it, so the names lead. A tick that would find nothing is dimmed as a
        // whole, unless it is ticked: what is chosen always reads plainly, so it can be unticked.
        // An excluded tick is struck through, and its count is how many it takes away.
        static string Label(string name, int count, Facets.State state)
        {
            if (state == Facets.State.Out) return "<s>" + name + "</s>  <color=" + DimColor + ">-" + count.ToString("N0") + "</color>";
            if (count == 0 && state == Facets.State.Off) return "<color=" + DimColor + ">" + name + "  0</color>";
            return name + "  <color=" + DimColor + ">" + count.ToString("N0") + "</color>";
        }

        // The game's own name for the type; the identifier in lower case when it has none.
        public static string TypeName(string type)
        {
            var name = TypeHelper.GetTypeDisplayName(type);
            return string.IsNullOrEmpty(name) || name.StartsWith("type_", StringComparison.Ordinal)
                ? type.ToLowerInvariant().Replace('_', ' ') : name;
        }

        // A button as wide as its text, pinned so the row cannot squeeze it, as the game's preset manager does.
        static void Fit(CustomUIElement e)
        {
            var r = e != null ? e.Rect : null;
            if (r == null) return;
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(r);
            float w = UnityEngine.UI.LayoutUtility.GetPreferredWidth(r);
            if (w <= 0f) w = UnityEngine.UI.LayoutUtility.GetMinWidth(r);
            if (w <= 0f) return;
            var le = r.gameObject.GetComponent<UnityEngine.UI.LayoutElement>();
            if (le == null) le = r.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            le.minWidth = le.preferredWidth = Mathf.Ceil(w * 1.4f);
            le.preferredHeight = 32f;
            le.flexibleWidth = 0f;
        }
    }
}
