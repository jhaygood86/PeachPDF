# CSS 2.1 §9.5.1 rule 5 applies to the earlier floats of the same column (#2225)

Workstream D of the float issues plan. A float may not start above an earlier float's top; `DomUtils.GetLowestEarlierFloatTop` gave up (negative infinity) as soon as its climb reached a multi-column container, because a `Y` there is measured from the column's own top and floats of different columns are not comparable.

**Idea.** Compare only floats of the same column, as Gecko does (its float manager is per fragmentainer) and WebKit (last placed float's top). `HtmlContainerInt.NoteFloatColumn` records the `ColumnAreaKey` of the column fragmentainer a float is placed in (called from `CssLayoutEngine.FloatBox`); the climb, once it reaches a multi-column container, counts a float only if its recorded column equals the one the float being placed is in.

**Trap handled.** A container holding nothing but floats lays them out in its first column with no fragmentainer, so no key exists for any of them. "No column" is compared as a value: they match each other, which is the #2225 sample. A measurement pass has no fragmentainer and leaves records alone; floats recorded in a real pass then differ from the unrecorded current one and are not compared (the old behavior).

**Not done.** The Gecko "pushed or split past a break" sentinel (later floats pushed past the break too) is not modeled; a float that moves to the next column is recorded where it was placed last.

**Evidence.** Engine suite 19,388 passing on net10.0. `MulticolFloatIntegrationTests` gains the issue's sample (the third float lands beside the second) and a two-column guard that a float in column 2 is not held down by one near the foot of column 1.
