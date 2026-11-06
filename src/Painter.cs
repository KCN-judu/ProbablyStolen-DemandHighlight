using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DemandHighlight
{
    // Puts one outline image on each marked item (and storage tab) and keeps it there. An outline is only
    // touched when its look, the item's shape or its size changed; everything else is a few field compares.
    //
    // Draws an Engine Result and nothing else: it does not know what was asked or why an item is marked. It adds one
    // child Image under each marked GameItemElement's RectTransform, or under its rotator when the item is turned.
    // Outlines are kept per item in a dictionary and hidden, not destroyed, when an item is not marked, so the next
    // result reuses them. Only the outlines shown by the last pass are checked for hiding, not every outline kept.
    // DestroyAll drops them all, which the store-exit release needs: a Frame holds a UI object of the store.
    public static class Painter
    {
        const string NodeName = "DemandHighlight";
        // Hidden outlines kept for reuse; above this many the hidden ones are destroyed.
        const int HiddenCap = 400;

        static readonly Color Green = new Color(0.36f, 0.92f, 0.42f, 0.95f);
        static readonly Color Amber = new Color(1f, 0.72f, 0.12f, 0.95f);
        static readonly Color Red = new Color(1f, 0.25f, 0.22f, 0.95f);
        static readonly Color Cyan = new Color(0.35f, 0.82f, 1f, 0.95f);

        // While the item filter is on, a plain match is drawn in cyan so it is not mistaken for a customer's wish.
        public static bool Filtering;

        // One outline: its UI objects, and the state it was last drawn with, so Item can skip a redraw that would change nothing.
        sealed class Frame
        {
            public object Key;
            public GameObject Root;
            public RectTransform Rect;
            public Image Image;
            public RectTransform Anchor;
            public int Seen;
            public bool Visible;
            // What is drawn now.
            public Style Style;
            public object Shape;
            public int Orientation, W, H, Generation;
            public bool Flipped, Drawn;
        }

        static readonly Dictionary<object, Frame> frames = new Dictionary<object, Frame>();
        static readonly List<object> scratch = new List<object>();
        // The outlines on screen after the last pass, and those this pass shows.
        static List<Frame> shown = new List<Frame>(), next = new List<Frame>();
        static int pass;

        public static int Thickness = 2, Corner = 2;
        public static bool Fill = true;

        // Outlines the result did not report are hidden only when the walk reached everything (a walk cut short by its
        // time budget leaves the rest as they were until it finishes).
        public static void Paint(Result result)
        {
            pass++;
            next.Clear();
            var items = result.Items;
            for (int i = 0; i < items.Count; i++) Item(items[i].Element, items[i].Style);
            var tabs = result.Tabs;
            for (int i = 0; i < tabs.Count; i++) Tabs(tabs[i]);
            if (result.Complete) HideUnseen();
            else
            {
                // Still on screen from earlier passes: keep tracking them.
                for (int i = 0; i < shown.Count; i++)
                    if (shown[i].Seen != pass && shown[i].Visible) next.Add(shown[i]);
                Swap();
            }
        }

        public static void HideAll()
        {
            pass++;
            next.Clear();
            HideUnseen();
        }

        public static void DestroyAll()
        {
            foreach (var f in frames.Values) if (f.Root != null) Object.Destroy(f.Root);
            frames.Clear();
            plates.Clear();
            shown.Clear();
            next.Clear();
        }

        public static Color ColorOf(Mark mark)
        {
            return mark == Mark.Contraband ? Red : mark == Mark.Stolen ? Amber : Filtering ? Cyan : Green;
        }

        // Outlines one item. A turned item is drawn in its rotator whose rect is the item before turning, so the
        // shape cells read from the game apply as they are; a mirrored item gets a mirrored texture.
        static void Item(GameItemElement element, Style style)
        {
            var shape = element.shape;
            var modified = element.modifiedShape;
            int orientation = (shape != null ? shape.orientation : 0) * 4 + (modified != null ? modified.orientation : 0);
            bool flipped = (shape != null && shape.flipped) || (modified != null && modified.flipped);

            // A turned item is drawn in its rotator, whose rect is the item before turning.
            RectTransform anchor = null;
            bool inRotator = false;
            if (orientation != 0)
            {
                var rotator = element.rotator;
                anchor = rotator != null ? rotator.rectTransform : null;
                inRotator = Measurable(anchor);
            }
            if (!inRotator) anchor = element.rectTransform;
            if (anchor == null) return;
            var rect = anchor.rect;
            int w = Mathf.RoundToInt(rect.width), h = Mathf.RoundToInt(rect.height);
            if (w < 2 || h < 2) return;

            var frame = Ensure(element, anchor);
            if (frame == null) return;
            Show(frame);
            if (frame.Drawn && frame.Style.Equals(style) && frame.Shape == shape && frame.Orientation == orientation &&
                frame.Flipped == flipped && frame.W == w && frame.H == h && frame.Generation == Sprites.Generation)
                return;

            var cells = shape != null && (orientation == 0 || inRotator) ? Sprites.Read(shape) : Sprites.Full;
            if (cells.Hash == Sprites.Full.Hash) DrawFrame(frame, style, Corner);
            else Draw(frame, Sprites.Get(cells, w, h, style, Thickness, Corner, Fill), style, inRotator && flipped ? -1f : 1f);
            frame.Shape = shape; frame.Orientation = orientation; frame.Flipped = flipped; frame.W = w; frame.H = h;
        }

        // Outlines the tab buttons of an open machine with the mark the result gives each page.
        static void Tabs(Result.TabMarks marks)
        {
            var plates = Plates(marks.Tabs, marks.Pages.Count);
            if (plates == null) return;
            for (int i = 0; i < plates.Count; i++)
            {
                var style = marks.Pages[i];
                if (style.Mark == Mark.None) continue;

                var plate = plates[i];
                var rect = plate.rect;
                int w = Mathf.RoundToInt(rect.width), h = Mathf.RoundToInt(rect.height);
                if (w < 2 || h < 2) continue;
                var frame = Ensure(plate, plate);
                if (frame == null) continue;
                Show(frame);
                if (frame.Drawn && frame.Style.Equals(style) && frame.W == w && frame.H == h &&
                    frame.Generation == Sprites.Generation)
                    continue;
                DrawFrame(frame, style, Mathf.Min(Corner, 2));
                frame.W = w; frame.H = h;
            }
        }

        // The tab buttons are every other child of the one child of the tabs that has two per page.
        static readonly List<RectTransform> plates = new List<RectTransform>();
        static List<RectTransform> Plates(MachineryTabs tabs, int count)
        {
            var root = tabs.rectTransform;
            if (root == null || count <= 0) return null;
            Transform strip = null;
            for (int i = 0; i < root.childCount; i++)
            {
                var c = root.GetChild(i);
                if (c != null && c.childCount == count * 2) { strip = c; break; }
            }
            if (strip == null) return null;
            plates.Clear();
            for (int i = 0; i < count; i++)
            {
                var plate = strip.GetChild(i * 2) as RectTransform;
                if (plate == null) return null;
                plates.Add(plate);
            }
            return plates;
        }

        // A rectangle: the shared nine-slice frame, sliced (or tiled, so the stripes keep their pitch), with one
        // texel per UI unit.
        static void DrawFrame(Frame frame, Style style, int corner)
        {
            var image = frame.Image;
            image.sprite = Sprites.Frame(style, Thickness, corner, Fill);
            image.type = style.Mark == Mark.Stolen && !style.Contents ? Image.Type.Tiled : Image.Type.Sliced;
            image.fillCenter = true;
            var canvas = image.canvas;
            float reference = canvas != null && canvas.referencePixelsPerUnit > 0f ? canvas.referencePixelsPerUnit : 100f;
            image.pixelsPerUnitMultiplier = Mathf.Clamp(reference / 100f, 0.01f, 100f);
            Finish(frame, style, 1f);
        }

        // Any other shape: a texture drawn for its exact size.
        static void Draw(Frame frame, Sprite sprite, Style style, float mirror)
        {
            frame.Image.type = Image.Type.Simple;
            frame.Image.pixelsPerUnitMultiplier = 1f;
            frame.Image.sprite = sprite;
            Finish(frame, style, mirror);
        }

        static void Finish(Frame frame, Style style, float mirror)
        {
            frame.Image.color = ColorOf(style.Mark);
            frame.Rect.localScale = new Vector3(mirror, 1f, 1f);
            frame.Style = style;
            frame.Generation = Sprites.Generation;
            frame.Drawn = true;
        }

        // The outline of key (an item or a tab button), created on first use and moved when the item's anchor changed.
        // The node ignores layout and raycasts so it never changes the game's UI behaviour.
        static Frame Ensure(object key, RectTransform anchor)
        {
            Frame frame;
            if (frames.TryGetValue(key, out frame))
            {
                if (frame.Root == null) { frames.Remove(key); frame = null; }
                else if (frame.Anchor != anchor)
                {
                    frame.Rect.SetParent(anchor, false);
                    frame.Rect.SetAsLastSibling();
                    frame.Anchor = anchor;
                    frame.Drawn = false;
                }
            }
            if (frame != null) return frame;

            var go = new GameObject(NodeName, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(anchor, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.SetAsLastSibling();
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            var image = go.AddComponent<Image>();
            image.raycastTarget = false;
            image.type = Image.Type.Simple;
            frame = new Frame { Key = key, Root = go, Rect = rt, Image = image, Anchor = anchor, Visible = true };
            frames[key] = frame;
            return frame;
        }

        // Marks the outline as shown by this pass, and makes it visible.
        static void Show(Frame frame)
        {
            if (frame.Seen != pass) { frame.Seen = pass; next.Add(frame); }
            if (frame.Visible) return;
            frame.Root.SetActive(true);
            frame.Visible = true;
        }

        // Hides the outlines the last pass showed and this one did not. Every other outline kept is already hidden, so
        // it is not looked at. Outlines of items that left the shop stay hidden for reuse until HiddenCap is passed.
        static void HideUnseen()
        {
            for (int i = 0; i < shown.Count; i++)
            {
                var f = shown[i];
                if (f.Seen == pass || !f.Visible) continue;
                if (f.Root != null) f.Root.SetActive(false);
                f.Visible = false;
            }
            Swap();
            if (frames.Count - shown.Count <= HiddenCap) return;
            scratch.Clear();
            foreach (var pair in frames)
            {
                var f = pair.Value;
                if (f.Root == null) scratch.Add(pair.Key);
                else if (!f.Visible) { Object.Destroy(f.Root); scratch.Add(pair.Key); }
            }
            for (int i = 0; i < scratch.Count; i++) frames.Remove(scratch[i]);
        }

        static void Swap()
        {
            var t = shown; shown = next; next = t;
            next.Clear();
        }

        static bool Measurable(RectTransform rt)
        {
            if (rt == null) return false;
            var r = rt.rect;
            return r.width >= 1f && r.height >= 1f;
        }
    }
}
