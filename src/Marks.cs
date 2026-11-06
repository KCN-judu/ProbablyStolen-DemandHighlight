using System;
using UnityEngine;

namespace DemandHighlight
{
    // The Item Filter's tick marks: the game's own check, and a cross drawn from it (Cross.Pure.cs) in the same pixel
    // art: same art pixel size, stroke, highlight on top faces and outline on four sides, in a red that stands out of
    // the red box as the green check does.
    //
    // Texture packs can replace the cross, by the texture pack project's rules for pictures made in code
    // (TexturePacksClient.cs): it is offered as
    //   333b8b44-6be8-47c9-bdfe-51a020b2bad2+Code~filter_exclude.png
    // with the size of the game's check. The cross shown is, in this order:
    //   1. a pack's picture, when a switched-on pack has one;
    //   2. the cross drawn from the check the toggle shows, so a pack that only redraws the check gets a cross that
    //      matches it;
    //   3. the check itself, when the check cannot be read.
    //
    // The offered cross is drawn from the game's toggle as it ships (Cross.GameBoxes), at start, so texture packs
    // find it when they read the offer after this mod has loaded; it is kept until the mod is disabled and holds no
    // game object. The cross the window shows is drawn from the toggle it has, kept until the store is left
    // (Release). The toggle's textures are usually not readable, so they are copied through a render texture first.
    public static class Marks
    {
        public const string Origin = "333b8b44-6be8-47c9-bdfe-51a020b2bad2";
        const string Container = "Code", Name = "filter_exclude";

        static Sprite offered;                // the cross offered to packs
        static bool described;
        static Sprite fromPack;               // a pack's cross, while one is applied
        static Sprite drawnFrom, drawn;       // a cross drawn for another check

        // The cross to show for the check a toggle has; null when there is none (the window then shows the check).
        // box: the toggle's empty box, when the check is drawn into a whole box picture (the game's toggle is two
        // pictures, checkbox and checkbox_checked); null when the check is a mark on its own.
        public static Sprite CrossFor(Sprite check, Sprite box)
        {
            if (fromPack != null) return fromPack;
            if (check == null) return null;
            if (check == drawnFrom && drawn != null) return drawn;
            Release();
            drawnFrom = check;
            drawn = Build(check, box, "demand_highlight_cross");
            return drawn;
        }

        // Once: how the game's toggle is made, to Player.log, so a cross that does not show can be explained.
        public static void Describe(UnityEngine.UI.Toggle toggle)
        {
            if (described || toggle == null) return;
            described = true;
            var parts = new System.Text.StringBuilder();
            parts.Append("toggle: graphic ").Append(Describe(toggle.graphic)).Append(", target ").Append(Describe(toggle.targetGraphic))
                .Append(", transition ").Append(toggle.transition).Append(", images");
            foreach (var image in toggle.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                parts.Append(' ').Append(image.gameObject.name).Append('=').Append(image.sprite != null
                    ? image.sprite.name + " " + (int)image.sprite.rect.width + "x" + (int)image.sprite.rect.height : "none");
            parts.Append(", components");
            foreach (var c in toggle.GetComponents<Component>()) parts.Append(' ').Append(c != null ? c.GetType().Name : "?");
            Debug.Log(DemandMod.Tag + parts);
        }

        static string Describe(UnityEngine.UI.Graphic g)
        {
            if (g == null) return "none";
            var image = g as UnityEngine.UI.Image;
            return g.GetType().Name + " " + g.gameObject.name + (image != null && image.sprite != null ? " (" + image.sprite.name + ")" : "");
        }

        // At start: draws the cross from the game's toggle as it ships and offers it to texture packs, which read the
        // offer when this mod has finished loading.
        public static void Offer()
        {
            TexturePacksClient.DeclareOrigin(DemandMod.ModId, Origin);
            uint[] checkedBox, emptyBox;
            int w, h;
            Cross.GameBoxes(out checkedBox, out emptyBox, out w, out h);
            offered = Sprite(Cross.FromBoxes(checkedBox, emptyBox, w, h), w, h, new Vector2(0.5f, 0.5f), 100f, Vector4.zero,
                "demand_highlight_filter_exclude");
            if (offered == null) return;
            TexturePacksClient.Offer(DemandMod.ModId, Container, Name, offered, s => fromPack = s == offered ? null : s);
            Debug.Log(DemandMod.Tag + "offered " + Origin + "+" + Container + "~" + Name + ".png to texture packs, " + w + "x" + h + " px");
        }

        public static void Withdraw()
        {
            TexturePacksClient.Withdraw(DemandMod.ModId);
            fromPack = null;
            Destroy(offered);
            offered = null;
            Release();
        }

        // Leaving the store: the cross drawn for another check goes; the offered one stays, packs hold it.
        public static void Release()
        {
            Destroy(drawn);
            drawn = drawnFrom = null;
        }

        static void Destroy(Sprite s)
        {
            if (s == null) return;
            if (s.texture != null) UnityEngine.Object.Destroy(s.texture);
            UnityEngine.Object.Destroy(s);
        }

        static Sprite Build(Sprite check, Sprite box, string name)
        {
            try
            {
                var r = check.textureRect;
                int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
                var px = Read(check.texture, Mathf.RoundToInt(r.x), Mathf.RoundToInt(r.y), w, h);
                if (px == null) { Debug.LogWarning(DemandMod.Tag + "could not read the check " + check.name); return null; }
                uint[] pixels = null;
                string how;
                var br = box != null ? box.textureRect : default(Rect);
                if (box != null && Mathf.RoundToInt(br.width) == w && Mathf.RoundToInt(br.height) == h)
                {
                    var empty = Read(box.texture, Mathf.RoundToInt(br.x), Mathf.RoundToInt(br.y), w, h);
                    pixels = Cross.FromBoxes(px, empty, w, h);
                    how = "from " + check.name + " against " + box.name;
                }
                else
                {
                    var art = Cross.Study(px, w, h);
                    if (art != null) pixels = Cross.Draw(art, w, h);
                    how = "from " + check.name + (art != null ? ", " + art.Scale + " px per art pixel, stroke " + art.Stroke : "");
                }
                if (pixels == null) { Debug.LogWarning(DemandMod.Tag + "found no mark to draw the exclude mark from, " + how); return null; }
                Debug.Log(DemandMod.Tag + "exclude mark drawn " + how + ", " + w + "x" + h + " px");
                var pivot = new Vector2(check.pivot.x / r.width, check.pivot.y / r.height);
                return Sprite(pixels, w, h, pivot, check.pixelsPerUnit, check.border, name);
            }
            catch (Exception e)
            {
                Debug.LogWarning(DemandMod.Tag + "could not draw the exclude mark: " + e.Message);
                return null;
            }
        }

        static Sprite Sprite(uint[] pixels, int w, int h, Vector2 pivot, float pixelsPerUnit, Vector4 border, string name)
        {
            if (pixels == null) return null;
            var colours = new Color32[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                uint c = pixels[i];
                colours[i] = new Color32((byte)(c >> 16), (byte)(c >> 8), (byte)c, (byte)(c >> 24));
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixels32(colours);
            tex.Apply(false, false);
            var sprite = UnityEngine.Sprite.Create(tex, new Rect(0, 0, w, h), pivot, pixelsPerUnit, 0, SpriteMeshType.FullRect, border);
            sprite.name = name;
            return sprite;
        }

        // A part of a texture as 0xAARRGGBB, rows from the bottom; read through a render texture, since game
        // textures are rarely readable.
        static uint[] Read(Texture2D tex, int x, int y, int w, int h)
        {
            if (tex == null || w <= 0 || h <= 0) return null;
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            var before = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(x, y, w, h), 0, 0);
                copy.Apply(false);
                var c = copy.GetPixels32();
                var px = new uint[c.Length];
                for (int i = 0; i < c.Length; i++) px[i] = ((uint)c[i].a << 24) | ((uint)c[i].r << 16) | ((uint)c[i].g << 8) | c[i].b;
                return px;
            }
            finally
            {
                RenderTexture.active = before;
                RenderTexture.ReleaseTemporary(rt);
                if (copy != null) UnityEngine.Object.Destroy(copy);
            }
        }
    }
}
