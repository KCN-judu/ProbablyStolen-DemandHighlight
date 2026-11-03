namespace DemandHighlight
{
    // Whether a scene load means the store was left, so everything the mod kept from it is released. Uses only
    // System types, so tests/StoreExitTest.cs runs it outside the game.
    //
    // The store is the scene InventoryScene. Leaving it is a single-mode load of any other scene (the main menu is
    // EmporiumMenu); a scene loaded additively does not replace the store, and the store itself starts empty.
    public static class StoreExit
    {
        public const string StoreScene = "InventoryScene";

        public static bool Left(string scene, bool single)
        {
            return single && scene != StoreScene;
        }
    }
}
