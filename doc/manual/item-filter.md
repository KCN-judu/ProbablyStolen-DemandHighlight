# Item Filter

The Item Filter marks your own stock by type, stolen or contraband, or by anything you can write as an expression, whoever is at the counter. Use it to sort the shop.

## Opening it

Press the key bound to **Open item filter** in the game's options menu. It is an ordinary game window: drag it by its title, close it with Esc or its cross, and it opens again where you left it. Texture packs that reskin the game's windows reskin it too.

## The window

From top to bottom:

- **The box**, with a red cross beside it that empties it. The box is the filter (see below).
- **One line under the box**, with the **Short** tick at its end: what is wrong and at which character when the text does not read, with the box itself in red; how the text was read when that differs from what you wrote (a type name resolved, `-` spelled out as `not`); a hint when the box is empty. While the text has an error, the last filter that read keeps working.
- **The numbers** (see below).
- **Stolen** and **Contraband**, each with its count.
- **Types**: one tick per item type found in the shop, with counts. These are the game's own categories, the ones customers ask for, such as Food, Medical, Tool or Luxury Item. They are grouped into Goods, Supplies, Gear, Machines, Grade and Restricted, with any other type under Other, three to a row, the most common first. Only this part scrolls.

Hover anything to see what it does.

## The numbers

With nothing chosen, the line shows the whole shop, as a baseline:

`Shop  1,204 items   52,300¢ total   3,980 cells   13¢ per cell`

With a filter on, it shows what matches, against that baseline:

`Matches  23 items   4,120¢ total (8% of the shop)   41 cells   100¢ per cell (shop 13¢)`

- **Items** counts everything in the shop, closed containers included.
- **Total** is each item's own estimated worth, the one its tooltip shows, without what is inside it, so a container and its contents are each counted once.
- **Cells** are the grid cells each item's shape takes, wherever it sits, inside containers too.
- **Per cell** is total worth divided by cells. Compare it with the shop's to see which goods earn their space.

The numbers follow the stock and the filter while the window is open, at most twice a second.

## The box is the filter

There is one filter, the text in the box. The ticks are another way to see and edit it:

- A tick has three states, cycled by clicking: off, ✓ (include) and ✗ (exclude), then off again. The ✗ is drawn from the game's own check mark, in the same pixel art.
- Ticking writes into the box. Tick Stolen, then Household Good and Medical, and the box reads `stolen and type:("Household Good" or Medical)`. Click Junk twice for ✗ and `and not type:Junk` is added.
- Typing moves the ticks. Type `type:Food -owned` and Food shows as ticked.
- Within the status ticks or within the types, any ✓ will do; every ✗ must hold, and so must the two groups and anything else in the box. So the example above is stolen goods that are household goods or medical.
- Types are written by their names in your language, always with `type:`, so the type Contraband (`type:Contraband`) never looks like the word `contraband`. Types ticked together are written as one group, and brackets wherever `and` and `or` meet.
- An expression of another shape, such as `type:Food or stolen`, shows nothing ticked; ticking then adds to it: `type:Medical and (type:Food or stolen)`.
- If the box has an error when you tick, the tick edits the last filter that read, and the box is rewritten from it.

The red cross empties the box.

### Counts

Each tick's count is what it would find with the rest of the filter: everything in the box except its own group. With Stolen ticked, each type shows how many stolen items it has; with Medical ticked, Stolen shows how many stolen medical items there are. A ✗ tick is struck through and shows how many items it takes away (`-4`). A tick that would find nothing is dimmed but still works. The layout does not move as the counts change: types keep their places, ordered by how common they are in the whole shop.

## Expressions

The box takes a small language; [Filter language](filter-language.md) defines it in full. In short:

| Write | Means |
| --- | --- |
| `food`, `type:食物`, `type:"Household Good"` | a type, by its name in your language or its identifier |
| `type:(food or medical)` | any of these types: a key over a group |
| `stolen`, `contraband`, `owned` | stolen, contraband, yours |
| `tag:X`, `id:X`, `condition:X` | a tag, an item identifier, a condition |
| `a b`, `a and b`, `a + b` | both |
| `a or b`, `a \| b` | either |
| `not a`, `-a` | not |
| `( ... )` | grouping |

`not` binds tightest, then `and`, then `or`: `-stolen food | medical` is (not stolen and food) or medical. The box writes brackets wherever `and` and `or` meet, so its text never relies on this.

When you press Enter or leave the box, the text is rewritten in its regular form, as a code editor formats on save: `-stolen food` becomes `not stolen and food`. The **Short** tick beside the box writes `+`, `|` and `-` instead; both are always understood.

Examples:

- `food -stolen`: food that is not stolen.
- `type:(food or medical) owned`: your food and medical goods.
- `contraband -owned`: contraband that is not yours.

## Marking

- Every matching item in the shop is marked until you clear the filter.
- Type matches are drawn in cyan with the solid style. Stolen goods and contraband keep their amber and red styles.
- Containers holding matches get corner brackets.
- Locks are ignored, because the filter is for sorting your own stock.

Turning the filter on pauses the customer highlight, and clearing it brings the highlight back. While you type in the box, the mod's own keys do nothing.

## Texture packs

The ✗ can be replaced by a texture pack. Its file is named

`333b8b44-6be8-47c9-bdfe-51a020b2bad2+Code~filter_exclude.png`

and is the whole box with the ✗ in it, 13 by 13 pixels, the size of the game's checkbox pictures; `textures.schema.tsv` in the mod's folder lists it. Without such a file, the ✗ is found by comparing the game's checked and empty boxes and drawn into the empty one, so a pack that only redraws the checkbox gets a matching ✗ for free. The ✓ is the game's own and is replaced like any of the game's pictures.
