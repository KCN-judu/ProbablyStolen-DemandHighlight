# Changelog

## 1.1.2

- Leaving the store (back to the main menu, for example) no longer keeps it in memory. The mod's item records, outlines, last customer and Item Filter window pointed at the store's items, and through them at the whole store; they are now dropped when another scene replaces the store. Nothing changes inside the store, and the filter's choices are kept.

## 1.1.1

- Starting the mod is all or nothing: if the game no longer has a method the mod hooks (after a game update, for example), everything it already set up is taken down again, one line is written to Player.log and the mod stays off, instead of running half enabled.
- The highlight no longer changes items by looking at them. Reading a lock or asking whether a customer buys an item used to add missing tags (such as a lock status on every supply crate) to the item, and they were saved; tags are now read without being added, and any the game's buying check adds are removed again.
- The outline decisions and geometry are separated from the game code and covered by tests that run outside the game. Nothing changes in play.

## 1.1.0

- In-game text in every language the game supports: German, Spanish, French, Italian, Japanese, Korean, Polish, Brazilian Portuguese and Russian, besides English and Chinese.

## 1.0.0 (pre-release)

First public release.

- Outlines what the customer at the counter wants, with a different look for wanted, stolen, contraband and "something inside".
- Outlines follow each item's grid shape. Machine storage tabs are marked too.
- New Item Filter window: mark your stock by type, stolen or contraband.
- Recalculates only when something changes, walks only what is on screen, and keeps an index of closed containers.
- Both keys are bound in the game's options menu. There are no default keys.
