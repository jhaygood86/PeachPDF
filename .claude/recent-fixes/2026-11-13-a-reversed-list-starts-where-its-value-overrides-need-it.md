# A reversed counter starts where its first `value`/`counter-set` needs it

`<ol reversed>` started at the number of items, so `<li value=7>` third in the list (`9, 8, 7`) counted from 6 and showed `6, 5, 7`. css-lists-3's reversed counter takes the start that makes the first element which sets the counter land on the value it sets; with nothing setting it, the last incrementing element lands on the negation of its own increment (1 for the default `-1`).

`CssCounterEngine.ReversedInitialValue` walks the scope in document order summing each element's increment (explicit `counter-increment` for the name, else the reversed default `-1`) and answers `set value - sum so far` at the first element whose `counter-set` (or `<li value>`) names the counter, else `-lastIncrement - totalSum`. For default increments and no sets that is the old count + 1.

Derived from WPT `css-lists/li-value-reversed-*` (001, 002, 003, 005, 006's last-increment rule, 013); css-lists +12.

## Not done
`li-value-reversed-006*`: the sibling `ol`s' `counters(list-item)` nests (`2.17`) where the reference has the bare `17`; I could not square that with the spec and left it. `-014`, `-016`, `-018`, `-019`, `-023`..`-028` (nested reversed lists, `counter-set` on non-items) were not looked at.
