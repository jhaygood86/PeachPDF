# text-wrap: balance is greedy-search based; pretty/stable, white-space-trim and wrap-inside are not implemented

`text-wrap-style: balance` runs the normal greedy flow, then re-runs it with the wrap boundary narrowed by a
binary search (`CssLayoutEngine.BalanceLines`) to the narrowest measure that keeps the same line count. It is
not the optimal-fit algorithm browsers use, and it only applies to a block of 2 to 6 lines that completes in
one fragmentainer, in horizontal writing mode. A paragraph that breaks across pages or columns keeps its
greedy lines, since narrowing one measure cannot balance across a break. Vertical writing mode and wrapped SVG
text are not balanced.

`text-wrap-style: pretty` and `stable` are accepted and lay out as `auto`. `white-space-trim` and
`wrap-inside` (CSS Text 4) are not recognised at all.

Why left: a real `pretty`/optimal-fit breaker needs the line breaker to carry candidate breaks instead of
streaming greedily, a much larger change than the property plumbing. Balanced layout is the common use
(headings), and the search covers it.
