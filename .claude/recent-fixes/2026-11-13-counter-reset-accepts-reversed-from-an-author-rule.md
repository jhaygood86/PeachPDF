# `counter-reset: reversed(<name>)` written in a stylesheet was dropped as invalid

`CounterListGrammar` (the render layer) understood `reversed(name)`, and the UA path for `<ol reversed>` produced it, but the CSS-OM grammar of `counter-reset` was `<ident> <integer>?` only. An author rule `ol { counter-reset: reversed(foo) }` was therefore an invalid declaration: the UA `counter-reset: list-item` stayed, `foo` was never reset, and every `li` made its own counter. `Converters.CounterNameOrReversedConverter` (an identifier, or `reversed(<ident>)`) is now the name part of the grammar in `css-properties.json`.

Found by WPT `css-lists/foo-counter-reversed-*` (all 11 failing; the named-counter twin of `li-value-reversed-*`). css-lists 37 -> 48 with this and the start-value change; `AnAuthorReversedCounterReset_IsAccepted_AndCountsDownFromTheItemCount`.
