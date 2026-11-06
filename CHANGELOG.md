# Changelog

## 1.2.0

- New: customers who barter. Your items that meet any of their terms are outlined.
- Fixed: for a customer who wants variety, items of a kind the customer had already taken stayed outlined. The game stops accepting every item of that kind once one is sold, but the mod only refreshed the sold item.
- Fixed: the flexi buyer got no outlines until an item was moved. It arrives wanting nothing and chooses at the end of its dialogue (the kind of goods your store holds most value in, sometimes with variety), and the mod did not notice the change.
- Both fixes come from a new core. What the customer wants, or what the Item Filter asks for, is now written down as a query, compared with the last one every 0.5 s, and answered over the store by a part that knows nothing about customers; drawing only sees the answer. Any change in what is wanted is a different query, so this kind of bug cannot come back for a customer the mod does not know about. Answers that still hold are kept: after a sale to a variety buyer, no item is asked about again.
- The Item Filter is now an ordinary game window: you can drag it, Esc closes it, it opens where you left it, and texture packs reskin it with the other windows. The type ticks sit three to a row, grouped, and only they scroll.
- New: the Item Filter is a search box. The text in it is the filter, and the ticks are a view of it: ticking writes into the box (`stolen and type:("Household Good" or Medical)`), typing moves the ticks. As in a shop's search, any tick within the status ticks or within the types will do, and the groups together must all hold. Types can be written by their names in your language; a word on its own that names a type is that type. A typo turns the box red, says what is wrong and where, and keeps the last filter that read.
- The filter language is defined in full on its own manual page: words and symbols, terms, groups (`type:(food or medical)`), precedence and associativity, errors and the canonical form. `and`/`+`, `or`/`|` and `not`/`-` are both understood. Enter, or leaving the box, rewrites the text in its regular form, as an editor formats on save; the **Short** tick writes `+ | -` instead of words.
- Ticks have three states: click once to include (✓), again to exclude (✗, written as `not type:Junk`), once more to clear. An excluded tick shows a red cross drawn from the game's own check mark, in the same pixel art (so a texture pack's check gets a matching cross), is struck through and shows how many items it takes away. The window draws the marks itself, so a click shows the next state at once, with no flash of the old one. The box's clear button is now the game's red cross, and the Short tick sits on the line under the box.
- Texture packs can replace the ✗ (`333b8b44-6be8-47c9-bdfe-51a020b2bad2+Code~filter_exclude.png`, listed in `textures.schema.tsv`). A pack that only redraws the game's check gets a ✗ drawn from its check. The mod now has a UUID for texture packs, in `manifest.xml` and declared from code.
- Each tick's count is what it would find with the rest of the filter: with Stolen ticked, every type shows its stolen items. Ticks that would find nothing are dimmed, and the layout does not move as counts change.
- New: a line of numbers in the Item Filter: how many items match, what they are worth and what share of the shop's worth that is, how many grid cells they take, and their worth per cell next to the shop's. With nothing chosen, it shows the whole shop. The option **Numbers in the item filter** turns it off.
- New, for mod authors: a query API. Another mod can ask which items of the shop match an Item Filter expression and get them back, without the outlines or the window. Copy `api/DemandHighlightQuery.cs` into your mod; no reference to this mod is needed. Contract 1.0; see the Query API page of the manual. `tools/QuerySample` is a small development mod that uses it.
- Changed: the filter's ticks used to add up as "any of these". Now Stolen and Contraband are one group, the types another, and the two must both hold, as in a shop's search: Stolen with Food ticked is stolen food, not stolen goods plus all food.
- With the Item Filter on, a stolen or contraband item that matches a type now shows its stolen or contraband look, as it does for a customer, instead of the filter look.
- Hiding outlines no longer looks at every outline kept for reuse, only at those shown last time.
- `Player.log` gets one line for each new query: what is asked, and how many items were visited, judged, asked about and outlined.

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
