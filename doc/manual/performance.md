# Performance

Demand Highlight does very little work per frame, even in a store with thousands of items.

## Work only on change

While nothing changes, the mod does not walk the shop. A walk starts when the customer or their intent changes, an item is validated, turned or has a tag changed, an item enters or leaves an inventory, or a container window opens or closes. Tooltips and other windows are ignored. Changes are batched into at most one walk every 0.15 s. The only repeated work is a check of the customer's intent every 0.5 s.

## One question per item

The game's own "would this customer buy it" check, run on a container, lists every item inside and checks each one again, recursively, which is quadratic in a deep store. The mod asks the game only about the item itself and rebuilds the same rule bottom-up: a container is bought when it is acceptable itself and, unless it is locked, has no window or is an advanced gun, everything inside it is bought. Each item is checked once per customer.

## A record per item

Each item keeps its verdict, its lock, ownership and contraband flags, and a summary of what is inside it. A change clears the changed item's record and marks only the summaries of the containers above it as stale.

## Only what is on screen

A walk visits the shop inventories and open containers. Closed containers answer from their record.

## Big jobs over several frames

A walk stops after 2.5 ms and continues on the next frame, so a new customer in a huge store does not cause one long frame. Outlines appear over a few frames instead.

## Cheap drawing

Rectangular items share one small nine-slice frame per look, so an outline of any size reuses the same texture. Other shapes get a texture drawn for their exact cells, cached by shape and size. An outline is redrawn only when its look, shape or size changed.

## Nothing kept after leaving the store

A store's items, their UI, windows and customers all point at each other, so a single reference kept after leaving the store would keep the whole store in memory. When another scene replaces the store (`SceneManager.sceneLoaded` in single mode for any scene other than `InventoryScene`, such as the main menu), the mod drops its item records, outlines, last customer and Item Filter window. The next store builds them again. The filter's choices are kept.

## Logging

Any refresh over 8 ms is written to the game's `Player.log` as `[DemandHighlight] slow refresh: ...`, with the time spent walking and drawing, so you can tell whether a stutter comes from this mod.

## Tested logic

The decisions need neither Unity nor the game, so they live apart from the code that reads the game, in files ending in `.Pure.cs`. `Decide.Pure.cs` chooses each item's outline, what it passes on to the containers above it, and the Item Filter's matches. `Outline.Pure.cs` computes the outline image of each look, for any cell shape. `Style.Pure.cs` combines marks. `StoreExit.Pure.cs` decides which scene loads leave the store.

Three tests run these files outside the game, with only the C# compiler. `tests/DecideTest.cs` covers buying, selling, inspections, locks, blocked searches and the Item Filter. `tests/OutlineTest.cs` covers shapes, corners, rounding and the four looks. `tests/StoreExitTest.cs` covers leaving the store. Each prints ok or FAIL per case and exits with 1 if anything failed. `tests/run.sh` compiles and runs them all.

Starting is all or nothing: if a method the mod hooks is missing, for example after a game update, everything already set up is taken down again, one line is written to `Player.log` and the mod stays off.
