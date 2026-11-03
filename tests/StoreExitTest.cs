// Checks which scene loads release what the mod kept from the store (src/StoreExit.Pure.cs), outside the game.
//   tests/run.sh, or: csc -out:StoreExitTest.exe tests\StoreExitTest.cs src\StoreExit.Pure.cs && StoreExitTest.exe
using System;
using DemandHighlight;

static class StoreExitTest
{
    static int failed;

    static void Check(string what, bool expected, bool got)
    {
        if (got != expected) failed++;
        Console.WriteLine((got == expected ? "ok    " : "FAIL  ") + what + (got == expected ? "" : " (got " + got + ")"));
    }

    static int Main()
    {
        Check("back to the main menu, release", true, StoreExit.Left("EmporiumMenu", true));
        Check("any other scene replacing the store, release", true, StoreExit.Left("LoadingScene", true));
        Check("the store loading, keep", false, StoreExit.Left("InventoryScene", true));
        Check("a scene added on top of the store, keep", false, StoreExit.Left("EmporiumMenu", false));
        Check("the store added on top, keep", false, StoreExit.Left("InventoryScene", false));
        Check("a scene without a name replacing the store, release", true, StoreExit.Left(null, true));

        Console.WriteLine(failed == 0 ? "all passed" : failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
