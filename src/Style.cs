namespace DemandHighlight
{
    // How much an item matters to the current customer. Higher wins when several items share a mark.
    public enum Mark : byte { None = 0, Normal = 1, Stolen = 2, Contraband = 3 }

    // What is drawn on one item: its mark, and whether the mark comes from what is inside it.
    public struct Style
    {
        public Mark Mark;
        public bool Contents;

        public Style(Mark mark, bool contents) { Mark = mark; Contents = mark != Mark.None && contents; }

        public bool Equals(Style other) { return Mark == other.Mark && Contents == other.Contents; }

        // Tabs and ancestors take the strongest mark; a direct match beats "something inside".
        public static Style Max(Style a, Style b)
        {
            if (a.Mark != b.Mark) return a.Mark > b.Mark ? a : b;
            return new Style(a.Mark, a.Contents && b.Contents);
        }
    }
}
