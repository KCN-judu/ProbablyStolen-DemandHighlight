// For a mod that makes sprites in code (an asset bundle, Sprite.Create): lets texture packs replace them.
// Copy this file into your mod. It needs no reference to Texture Packs, and works the same whether or not Texture
// Packs is installed: with it absent, nothing reads what is published here and the mod behaves as if the file were
// not there.
//
// Rule: Texture Packs keeps `apply` and the sprites until its next rebuild or until your mod is disabled, so `apply`
// must not capture a store object (GameItem, PixelWindow, TreeNodeRender, scene objects). Keep the current sprite in
// a static field that your drawing code reads, or call Withdraw when the store is left.
//
//   static Sprite crusherNow;      // what your drawing code reads
//   void OnEnable()
//   {
//       crusherNow = crusherSprite;
//       TexturePacksClient.DeclareOrigin("my_mod", "3f2a9c1e-5b7d-4e2a-9c1e-0a1b2c3d4e5f");   // once, optional
//       TexturePacksClient.Offer("my_mod", "Code", "crusher_body", crusherSprite, s => crusherNow = s);
//   }
//   void OnDisable() { TexturePacksClient.Withdraw("my_mod"); }
//
// Offer lists one picture: its container (a word of your choice, since the game decodes only Items and Paper; those
// two come from your Items/ and Paper/ folders without any code), its name, the sprite as you made it, and what to do
// with a sprite. `apply` is called with a pack's sprite, and with your original to put it back. Your mod keeps
// owning its objects. A pack names the picture as <your mod's UUID, or its id>+<container>~<name>, for example
// my_mod+Code~crusher_body.png, and its file must have the size of your sprite.
//
// Call Offer from OnEnable. Texture Packs reads the list again whenever a mod finishes loading, so it does not matter
// which of the two mods starts first. The UUID is optional but recommended: without it a pack names your mod by its
// id, which cannot tell apart two mods that share one. The pack builder warns about that.
//
// How it works: two slots of the AppDomain, which every mod's assembly shares, hold the list and the UUID under
// "texture_packs.v1.*" keys. Texture Packs reads them; nothing is written back.
using System;
using System.Collections.Generic;
using UnityEngine;

public static class TexturePacksClient
{
    const string SpritesPrefix = "texture_packs.v1.sprites.";
    const string OriginPrefix = "texture_packs.v1.origin.";

    static readonly Dictionary<string, List<object[]>> offered = new Dictionary<string, List<object[]>>();

    // Announces the mod's UUID (hyphenated, 36 characters). Pick it once for the mod and keep it for good.
    public static void DeclareOrigin(string modId, string uuid)
    {
        AppDomain.CurrentDomain.SetData(OriginPrefix + modId, uuid);
    }

    // Offers one picture to texture packs. See the notes at the top of this file.
    public static void Offer(string modId, string container, string name, Sprite original, Action<Sprite> apply)
    {
        List<object[]> list;
        if (!offered.TryGetValue(modId, out list))
        {
            offered[modId] = list = new List<object[]>();
            // The reader gets a copy, so the list can change while it reads.
            AppDomain.CurrentDomain.SetData(SpritesPrefix + modId, (Func<IEnumerable<object[]>>)(() => list.ToArray()));
        }
        list.Add(new object[] { container, name, original, apply });
    }

    // Takes everything back, for the mod's OnDisable. Texture Packs puts the originals back the next time it reads.
    public static void Withdraw(string modId)
    {
        offered.Remove(modId);
        AppDomain.CurrentDomain.SetData(SpritesPrefix + modId, null);
        AppDomain.CurrentDomain.SetData(OriginPrefix + modId, null);
    }
}
