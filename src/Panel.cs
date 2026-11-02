using System;
using System.Collections.Generic;
using TMPro;

namespace DemandHighlight
{
    // The filter window, built from the game's own machinery widgets so it looks like a machine panel.
    //
    //   ITEM FILTER
    //   [ STATUS ]  [x] Stolen 9   [ ] Contraband 49              [ Clear ]
    //   [ TYPES  ]  GOODS            SUPPLIES          GEAR ...
    //               [ ] Household 392 [ ] Material 83   [ ] Tool 38
    //               ...
    //
    // Panels are stacked, the status strip on one line, so the window stays low and narrow. Types are grouped
    // into families with a header each, families are packed into balanced columns, every toggle is left-aligned
    // so the eye runs down one edge, and counts are dimmed so the names lead. Explanations live in tooltips.
    // The lists are rebuilt each time the window opens.
    public static class Panel
    {
        const int TextSize = 6;
        const string CountColor = "#8E887C";

        static PixelWindow window;
        static MachineryPanel statusPanel, typesPanel;
        static MachineryPanelRow row;

        public static Func<string, string> L = k => k;
        public static Action Changed = () => { };

        // What the window shows was built for this stock and filter; reopening with both unchanged just shows it.
        static int builtStock = -1, builtFilter = -1;

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

        public static bool IsOpen { get { return window != null && window.handler != null && window.IsVisible(); } }

        public static void Toggle()
        {
            if (IsOpen) window.Hide();
            else Open();
        }

        public static void Close()
        {
            if (IsOpen) window.Hide();
        }

        public static void Destroy()
        {
            Close();
            window = null; row = null; statusPanel = typesPanel = null;
        }

        static void Open()
        {
            if (window == null || window.handler == null)
            {
                statusPanel = MachineHelper.Panel(L("panel_status"), new MachineryStack(false, 8), "control", false);
                typesPanel = MachineHelper.Panel(L("panel_types"), new MachineryStack(false, 8), "control", false);
                row = new MachineryPanelRow(new[] { statusPanel, typesPanel }).SetColumns(1);
                window = new PixelWindow(true, L("window_title"), WindowStyle.Machinery);
                window.Attach(row);
            }
            if (builtStock != Filter.StockVersion || builtFilter != Filter.Version) Rebuild();
            window.Show();
        }

        // A toggle already shows its new state; remember that the window matches the filter.
        static void Toggled() { builtFilter = Filter.Version; Changed(); }

        static void Rebuild()
        {
            var counts = Filter.Count();
            builtStock = Filter.StockVersion;
            builtFilter = Filter.Version;

            var stolen = Toggle(L("filter_stolen"), counts.Stolen, Filter.Stolen, on => { Filter.SetStolen(on); Toggled(); });
            stolen.SetDescription(L("tip_stolen"));
            var contraband = Toggle(L("filter_contraband"), counts.Contraband, Filter.Contraband,
                on => { Filter.SetContraband(on); Toggled(); });
            contraband.SetDescription(L("tip_contraband"));
            var clear = new MachineryButton(L("button_clear"), () => { Filter.Clear(); Changed(); Rebuild(); }, -1, -1);
            clear.SetDescription(L("tip_clear"));
            statusPanel.SetContent(MachineryStack.Horizontal(stolen, contraband, clear)
                .SetAlign(MachineryStack.Align.Center));

            typesPanel.SetContent(counts.Types.Count == 0 ? (PixelElement)Text(L("no_items")) : BuildFamilies(counts.Types));
            row.Validate();
            window.Validate();
        }

        // Family blocks (header + toggles), packed in order into columns of roughly equal height.
        static PixelElement BuildFamilies(Dictionary<string, int> present)
        {
            var blocks = new List<KeyValuePair<string, List<KeyValuePair<string, int>>>>();
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
                if (members.Count > 0) blocks.Add(new KeyValuePair<string, List<KeyValuePair<string, int>>>(family[0], members));
            }
            var other = new List<KeyValuePair<string, int>>();
            foreach (var pair in present) if (!known.Contains(pair.Key)) other.Add(pair);
            if (other.Count > 0) blocks.Add(new KeyValuePair<string, List<KeyValuePair<string, int>>>("fam_other", other));

            int rows = 0;
            foreach (var b in blocks) rows += b.Value.Count + 2;
            int columnCount = rows > 40 ? 4 : rows > 18 ? 3 : 2;
            int target = (rows + columnCount - 1) / columnCount;

            var columns = new MachineryStack(false, 10).SetAlign(MachineryStack.Align.Start);
            MachineryStack column = null;
            int height = 0;
            foreach (var block in blocks)
            {
                int h = block.Value.Count + 2;
                if (column == null || (height > 0 && height + h > target + 2))
                {
                    column = new MachineryStack(true, 2).SetAlign(MachineryStack.Align.Start);
                    columns.Add(column);
                    height = 0;
                }
                if (height > 0) column.Add(new EmptyPixelElement(1, 4));
                column.Add(new MachineryLabel(L(block.Key)));
                block.Value.Sort((a, b) => a.Value != b.Value ? b.Value.CompareTo(a.Value) : string.CompareOrdinal(a.Key, b.Key));
                foreach (var pair in block.Value)
                {
                    var type = pair.Key;
                    var name = TypeName(type);
                    var toggle = Toggle(name, pair.Value, Filter.Has(type), on => { Filter.SetType(type, on); Toggled(); });
                    toggle.SetDescription(L("tip_type").Replace("#", name));
                    column.Add(toggle);
                }
                height += h;
            }
            return columns;
        }

        static MachineryRadioButton Toggle(string name, int count, bool on, Action<bool> changed)
        {
            return new MachineryRadioButton(name + "  <color=" + CountColor + ">" + count + "</color>", on, changed);
        }

        static MachineryText Text(string text)
        {
            return new MachineryText(text, TextSize, HorizontalAlignmentOptions.Left, false);
        }

        // The game's own name for the type; the raw id when it has none.
        static string TypeName(string type)
        {
            var name = TypeHelper.GetTypeDisplayName(type);
            return string.IsNullOrEmpty(name) || name.StartsWith("type_", StringComparison.Ordinal)
                ? type.ToLowerInvariant().Replace('_', ' ') : name;
        }
    }
}
