# Performance

Demand Highlight is built to stay out of the frame budget, even in a store with thousands of items.

## Work only on change

Nothing runs while nothing changes. The mod reacts to the customer or their intent changing, an item being validated or having a tag changed, an item entering or leaving an inventory, and a container window opening or closing. Tooltips and other windows are ignored. Changes are batched into at most one walk every 0.15 s, and there is no periodic refresh.

## One question per item

The game's own "would this customer buy it" check, run on a container, lists every item inside and checks each one again, recursively, which is quadratic in a deep store. The mod asks the game only about the item itself and rebuilds the same rule bottom-up: a container is bought when it is acceptable itself and, unless it is locked, has no window or is an advanced gun, everything inside it is bought. Each item is checked once per customer.

## A record per item

Each item keeps its verdict, its lock, ownership and contraband flags, and a summary of what is inside it. A change clears the changed item's record and marks only the summaries of the containers above it as stale.

## Only what is on screen

A walk visits the shop inventories and open containers. Closed containers answer from their record.

## Big jobs over several frames

A walk stops after 2.5 ms and continues on the next frame, so a new customer in a huge store never causes one long frame. Outlines appear over a few frames instead.

## Cheap drawing

Rectangular items share one small nine-slice frame per look, so an outline of any size costs nothing to draw. Other shapes get a texture drawn for their exact cells, cached by shape and size. An outline is redrawn only when its look, shape or size changed.

## Logging

Any refresh over 8 ms is written to the game's `Player.log` as `[DemandHighlight] slow refresh: ...`, with the time spent walking and drawing, so a stutter can be traced to this mod or ruled out.
