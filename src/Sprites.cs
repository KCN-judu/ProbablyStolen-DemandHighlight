using System;
using System.Collections.Generic;
using UnityEngine;

namespace DemandHighlight
{
    // Builds the outline textures, cached by look: an item is drawn once per change, never per frame. The
    // geometry of each look is Outline's (Outline.Pure.cs); this class turns it into textures and sprites.
    // - Rectangular items (almost all of them) share one small nine-slice frame per look: fixed corners, edges and
    //   centre stretched (or tiled, for the striped look), so any size costs nothing to draw.
    // - Other shapes get a texture that follows their grid cells (L-shapes get an L outline), drawn 1:1 with the
    //   UI so lines stay crisp, cached by shape and size.
    //
    // Looks, white in the texture and tinted by the Image colour:
    //   wanted      solid line and a faint fill
    //   stolen      diagonal "caution tape" line and a faint fill
    //   contraband  heavy line, a thin inner line and a stronger fill
    //   contents    corner brackets only: something inside this container is wanted
    public static class Sprites
    {
        const int MaxSide = 512;
        const int CacheCap = 384;
        const int Inf = 1 << 20;

        struct Key : IEquatable<Key>
        {
            public long Shape;
            public int W, H, Look, Thickness, Corner;
            public bool Fill;
            public bool Equals(Key o)
            {
                return Shape == o.Shape && W == o.W && H == o.H && Look == o.Look && Thickness == o.Thickness &&
                    Corner == o.Corner && Fill == o.Fill;
            }
            public override bool Equals(object o) { return o is Key && Equals((Key)o); }
            public override int GetHashCode()
            {
                unchecked
                {
                    int h = (int)Shape ^ (int)(Shape >> 32);
                    h = h * 31 + W; h = h * 31 + H; h = h * 31 + Look; h = h * 31 + Thickness;
                    h = h * 31 + Corner; return h * 2 + (Fill ? 1 : 0);
                }
            }
        }

        static readonly Dictionary<Key, Sprite> cache = new Dictionary<Key, Sprite>();
        public static int Generation { get; private set; }

        // Scratch buffers, reused between bakes.

        public static readonly Shape Full = Shape.Full;

        public static Shape Read(GridShape shape)
        {
            return Shape.FromCells(shape.width, shape.height, (x, y) => shape.GetLocal(x, y) != 0);
        }

        public static void Clear()
        {
            foreach (var s in cache.Values)
                if (s != null) { UnityEngine.Object.Destroy(s.texture); UnityEngine.Object.Destroy(s); }
            cache.Clear();
            Generation++;
        }

        public static Sprite Get(Shape shape, float width, float height, Style style, int thickness, int corner, bool fill)
        {
            int w = Mathf.Clamp(Mathf.RoundToInt(width), 2, MaxSide);
            int h = Mathf.Clamp(Mathf.RoundToInt(height), 2, MaxSide);
            var key = new Key { Shape = shape.Hash, W = w, H = h, Look = LookOf(style), Thickness = thickness,
                Corner = corner, Fill = fill && !style.Contents };
            Sprite sprite;
            if (cache.TryGetValue(key, out sprite) && sprite != null) return sprite;
            if (cache.Count >= CacheCap) Clear();
            sprite = Bake(shape, w, h, style, thickness, corner, key.Fill, 0f, Vector4.zero);
            cache[key] = sprite;
            return sprite;
        }

        // Width of the fixed border of a nine-slice frame: the line (the heavy one has an inner line too), the
        // rounded corner and the corner brackets all fit inside it.
        public static int FrameBorder(int thickness, int corner) { return 3 * thickness + 4 + corner; }

        // The nine-slice frame for rectangular items and tabs. Its centre is one stripe period long, so the striped
        // look tiles without a seam.
        public static Sprite Frame(Style style, int thickness, int corner, bool fill)
        {
            int border = FrameBorder(thickness, corner);
            int size = 2 * border + 2 * (2 * thickness + 2);
            var key = new Key { Shape = -1, W = size, H = size, Look = LookOf(style), Thickness = thickness,
                Corner = corner, Fill = fill && !style.Contents };
            Sprite sprite;
            if (cache.TryGetValue(key, out sprite) && sprite != null) return sprite;
            if (cache.Count >= CacheCap) Clear();
            sprite = Bake(Full, size, size, style, thickness, corner, key.Fill, 3 * thickness + 4,
                new Vector4(border, border, border, border));
            cache[key] = sprite;
            return sprite;
        }

        static int LookOf(Style style) { return style.Contents ? 0 : (int)style.Mark; }

        // reach: how far corner brackets run (0 = from the cell size). border: nine-slice border of the sprite.
        static Sprite Bake(Shape shape, int w, int h, Style style, int t, int r, bool fill, float reach, Vector4 border)
        {
            int n = w * h;
            var alpha = Outline.Alpha(shape, w, h, style, t, r, fill, reach);
            var pixels = new Color32[n];
            for (int i = 0; i < n; i++) pixels[i] = new Color32(255, 255, 255, alpha[i]);

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                border);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
