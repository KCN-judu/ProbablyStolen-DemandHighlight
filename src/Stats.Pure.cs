using System;
using System.Collections.Generic;
using System.Text;

namespace DemandHighlight
{
    // The Item Filter's numbers: how many items, what they are worth, how many grid cells they take, and the worth per
    // cell, for the whole shop and for what the filter matches; and the line that shows them. Plain data only, so
    // tests/StatsTest.cs runs it outside the game.
    public struct Totals
    {
        public int Items;
        public long Value, Cells;

        public void Add(long value, long cells)
        {
            Items++;
            Value += value;
            Cells += cells;
        }

        // Worth per cell, rounded; -1 when there are no cells to divide by.
        public long PerCell { get { return Cells > 0 ? (long)Math.Round((double)Value / Cells, MidpointRounding.AwayFromZero) : -1; } }
    }

    public static class Stats
    {
        // A part of a whole as a percentage for people: "8%", "<1%" for a sliver, "100%"; null when the whole is
        // nothing, so the caller leaves the share out.
        public static string Share(long part, long whole)
        {
            if (whole <= 0) return null;
            if (part <= 0) return "0%";
            double p = 100.0 * part / whole;
            if (p < 1) return "<1%";
            return ((long)Math.Round(p, MidpointRounding.AwayFromZero)).ToString() + "%";
        }

        // Fills {name} slots in a localized template. Unknown slots stay as written, so a translation with a typo
        // still shows something readable; a slot whose value is null is removed together with the brackets around it,
        // so "{v}（{p}）" drops the share when there is none.
        public static string Fill(string template, IDictionary<string, string> values)
        {
            var sb = new StringBuilder(template.Length + 32);
            int i = 0;
            while (i < template.Length)
            {
                char c = template[i];
                if (c == '{')
                {
                    int end = template.IndexOf('}', i + 1);
                    if (end > i)
                    {
                        var name = template.Substring(i + 1, end - i - 1);
                        string value;
                        if (values.TryGetValue(name, out value))
                        {
                            if (value != null) sb.Append(value);
                            else DropOpen(sb, ref end, template);
                            i = end + 1;
                            continue;
                        }
                    }
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        const string Opens = "(（[", Closes = ")）]";

        // A removed slot inside brackets takes the whole bracketed group with it, words around the slot and the space
        // before the group included: "4,120 (8% of the shop)" becomes "4,120".
        static void DropOpen(StringBuilder sb, ref int end, string template)
        {
            int open = -1;
            for (int k = sb.Length - 1; k >= 0; k--)
            {
                if (Closes.IndexOf(sb[k]) >= 0) return;
                if (Opens.IndexOf(sb[k]) >= 0) { open = k; break; }
            }
            if (open < 0) return;
            char close = Closes[Opens.IndexOf(sb[open])];
            int after = template.IndexOf(close, end + 1);
            if (after < 0 || template.IndexOf('{', end + 1, after - end - 1) >= 0) return;
            sb.Length = open;
            while (sb.Length > 0 && (sb[sb.Length - 1] == ' ' || sb[sb.Length - 1] == '　')) sb.Length--;
            end = after;
        }
    }
}
