# Filter language

The [Item Filter](item-filter.md)'s box takes a small language. This page is its full definition. `tests/LanguageTest.cs` checks every rule here; when one changes, both change.

## Words and symbols

Text is read as tokens, left to right. Spaces separate tokens and mean nothing else.

| Token | Written as |
| --- | --- |
| word | letters of any script, digits, `_`, `.`, `'`: `stolen`, `食物`, `x.y`, `it's` |
| quoted text | `"..."`; inside, `\"` is a quote and `\\` a backslash |
| colon | `:` |
| brackets | `(` and `)` |
| and | `and` or `+` |
| or | `or` or `\|` |
| not | `not` or `-` |

`-` and `+` are never part of a word: `a-b` is `a`, not, `b`. Any other symbol (`&`, `!`, `#` and so on) is an error.

`and`, `or` and `not` are operators only as whole words, in any case: `Or` is or, `orange` and `notebook` are words.

## Terms

| Term | Means |
| --- | --- |
| `stolen`, `contraband`, `owned` (or `mine`) | the item is stolen, contraband, yours |
| `true`, `false` | every item, no item |
| a type's name on its own, `food` or `食物` | items of that type |
| `type:food`, `type:"Household Good"` | items of that type, by name in your language or by identifier; case, spaces and underscores do not matter |
| `tag:LOCKED_CONTAINER_TAG` | items with that tag |
| `id:spacerFoodVariety` | items with that identifier |
| `condition:broken` (or `cond:`) | items in that condition |
| `key:( ... )` | the key over a group, below |
| `( ... )` | brackets, for grouping |

No space is allowed around the colon: `type: food` and `type :food` are errors.

### Groups

`key:( ... )` applies the key to every word and quoted text inside the brackets. `type:(food or medical)` is `type:food or type:medical`. Inside, the operators and brackets work as everywhere: `type:(food -junk)` is food that is not junk. A word inside is always a value, so `type:(stolen)` is the type called stolen; a key written inside keeps its own key: `type:(food or tag:x)`. To use `and`, `or` or `not` as a value, quote it: `type:("or")`. Groups can nest.

## Precedence and associativity

From the tightest to the loosest:

| Level | Operators | Kind | Reads |
| --- | --- | --- | --- |
| 1 | terms, `key:( )`, `( )` | atoms | |
| 2 | `not`, `-` | prefix, right to left | `not not a` is `not (not a)` |
| 3 | `and`, `+`, two terms side by side | n-ary, left to right | `a b c` is `a and b and c` |
| 4 | `or`, `\|` | n-ary, left to right | `a or b or c` |

So:

- `a or b and c` is `a or (b and c)`.
- `not a b` is `(not a) and b`; `not` takes one term, and brackets give it more: `not (a b)`.
- `-a b | c` is `((not a) and b) or c`.
- `type:x y` is `type:x and y`: a key takes one value unless it is given a group.

`and` and `or` are associative, so how a run of one of them is grouped never changes what it means: `(a b) c` and `a (b c)` are the same filter.

## Errors

The first error in the text is reported, with the character it is at. The box turns red and the last filter that read stays in force.

| Error | Example |
| --- | --- |
| something is missing at the end | `stolen and`, `-` |
| a `)` is missing | `(stolen`, `type:(food` |
| a key has no value | `type:`, `type: food` |
| a key is unknown | `colour:red` |
| a word is not a keyword or a type | `shiny` |
| a symbol where none belongs | `stolen)`, `a or or b`, `a & b`, `"food"` outside a group |
| a quote is not closed | `id:"abc` |

## The canonical form

The box rewrites what you typed in one regular form when you press Enter or leave it, the way a code editor formats on save, and the ticks write in the same form. What you wrote keeps its structure and order; only its spelling changes:

- `and`, `or`, `not` in words, or as `+`, `|`, `-` with the window's **Short** tick;
- one space around `and` and `or`; none after `-`, around the colon or inside brackets;
- brackets wherever `and` and `or` meet, and after `not` around an `and` or `or`; brackets that change nothing are removed;
- keys and keywords in lower case, in one spelling: `cond` becomes `condition`, `mine` becomes `owned`;
- values as written, quoted only when they must be;
- several types or values of one key chosen together are written as one group: `type:(Food or Medical)`;
- types are written by their names in your language.

| Typed | Words | Short |
| --- | --- | --- |
| `-a b \| c` | `(not a and b) or c` | `(-a + b) \| c` |
| `a or b and c` | `a or (b and c)` | `a \| (b + c)` |
| `not (a b)` | `not (a and b)` | `-(a + b)` |
| `type:( x  \|y )` | `type:(x or y)` | `type:(x \| y)` |
| `((a)) ((b))` | `a and b` | `a + b` |

Formatting never changes what the text means, formatting twice gives the same text, and each style formats the other's text into its own.
