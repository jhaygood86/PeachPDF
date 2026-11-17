# The table `rules` attribute draws only the row-group lines

Tracked in [#2426](https://github.com/jhaygood86/PeachPDF/issues/2426).

`DomParser.TranslateTableRulesAttribute` maps `<table rules>` as the HTML Standard's rendering section says for two cases only:
any value other than `none` sets `border-collapse: collapse`, and `rules="groups"` gives each `thead` and `tbody` a 1px solid
`border-block-end`.

Not mapped: the column-group borders `rules="groups"` also asks for, and the row, column and cell lines of `rules="rows"`,
`rules="cols"` and `rules="all"`. A table using one of those switches to the collapsed model but draws no rules.

Left out because only `rules="groups"` has a WPT reftest (`css/css-tables/rules-groups.html`) to check the rendering against. Adding
the rest means more presentational hints in the same method, on `colgroup`, `tr` and the cells.
