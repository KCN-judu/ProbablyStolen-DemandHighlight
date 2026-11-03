using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DemandHighlight
{
    // Puts one outline image on each marked item (and storage tab) and keeps it there. An outline is only
    // touched when its look, the item's shape or its size changed; everything else is a few field compares.
    public static class Painter
    {
        const string NodeName = "DemandHighlight";
        const int HiddenCap = 400;

        static readonly Color Green = new Color(0.36f, 0.92f, 0.42f, 0.95f);
        static readonly Color Amber = new Color(1f, 0.72f, 0.12f, 0.95f);
        static readonly Color Red = new Color(1f, 0.25f, 0.22f, 0.95f);
        static readonly Color Cyan = new Color(0.35f, 0.82f, 1f, 0.95f);

        // While the item filter is on, a plain match is drawn in cyan so it is not mistaken for a customer's wish.
        public static bool Filtering;

        sealed class Frame
        {
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
        static int pass;

        public static int Thickness = 2, Corner = 2;
        public static bool Fill = true;

        // complete: the walk reached everything; only then are outlines it did not report hidden (a walk cut short
        // by its time budget leaves the rest as they were until it finishes).
        public static void Paint(bool complete)
        {
            pass++;
            var hits = Rules.Hits;
            for (int i = 0; i < hits.Count; i++) Item(hits[i].Element, hits[i].Style);
            var tabs = Rules.Tabs;
            for (int i = 0; i < tabs.Count; i++) Tabs(tabs[i]);
            if (complete) HideUnseen();
        }

        public static void HideAll()
        {
            pass++;
            HideUnseen();
        }

        public static void DestroyAll()
        {
            foreach (var f in frames.Values) if (f.Root != null) Object.Destroy(f.Root);
            frames.Clear();
            plates.Clear();
        }

        public static Color ColorOf(Mark mark)
        {
            return mark == Mark.Contraband ? Red : mark == Mark.Stolen ? Amber : Filtering ? Cyan : Green;
        }

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
            frame.Seen = pass;
            Show(frame);
            if (frame.Drawn && frame.Style.Equals(style) && frame.Shape == shape && frame.Orientation == orientation &&
                frame.Flipped == flipped && frame.W == w && frame.H == h && frame.Generation == Sprites.Generation)
                return;

            var cells = shape != null && (orientation == 0 || inRotator) ? Sprites.Read(shape) : Sprites.Full;
            if (cells.Hash == Sprites.Full.Hash) DrawFrame(frame, style, Corner);
            else Draw(frame, Sprites.Get(cells, w, h, style, Thickness, Corner, Fill), style, inRotator && flipped ? -1f : 1f);
            frame.Shape = shape; frame.Orientation = orientation; frame.Flipped = flipped; frame.W = w; frame.H = h;
        }

        static void Tabs(Rules.TabsHit hit)
        {
            var plates = Plates(hit.Tabs, hit.Pages.Count);
            if (plates == null) return;
            for (int i = 0; i < plates.Count; i++)
            {
                var style = default(Style);
                var page = hit.Pages[i];
                var items = page != null ? page.childItems : null;
                if (items != null)
                    for (int k = 0; k < items.Count; k++) style = Style.Max(style, Rules.StyleOf(items[k]));
                if (style.Mark == Mark.None) continue;

                var plate = plates[i];
                var rect = plate.rect;
                int w = Mathf.RoundToInt(rect.width), h = Mathf.RoundToInt(rect.height);
                if (w < 2 || h < 2) continue;
                var frame = Ensure(plate, plate);
                if (frame == null) continue;
                frame.Seen = pass;
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
            frame = new Frame { Root = go, Rect = rt, Image = image, Anchor = anchor, Visible = true };
            frames[key] = frame;
            return frame;
        }

        static void Show(Frame frame)
        {
            if (frame.Visible) return;
            frame.Root.SetActive(true);
            frame.Visible = true;
        }

        static void HideUnseen()
        {
            scratch.Clear();
            int hidden = 0;
            foreach (var pair in frames)
            {
                var f = pair.Value;
                if (f.Root == null) { scratch.Add(pair.Key); continue; }
                if (f.Seen == pass) continue;
                if (f.Visible) { f.Root.SetActive(false); f.Visible = false; }
                hidden++;
            }
            // Items that left the shop keep a hidden outline for reuse; drop them when there are too many.
            if (hidden > HiddenCap)
                foreach (var pair in frames)
                    if (pair.Value.Root != null && !pair.Value.Visible) { Object.Destroy(pair.Value.Root); scratch.Add(pair.Key); }
            for (int i = 0; i < scratch.Count; i++) frames.Remove(scratch[i]);
        }

        static bool Measurable(RectTransform rt)
        {
            if (rt == null) return false;
            var r = rt.rect;
            return r.width >= 1f && r.height >= 1f;
        }
    }
}
