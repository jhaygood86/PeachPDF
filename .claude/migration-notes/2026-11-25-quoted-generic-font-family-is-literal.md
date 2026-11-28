# Quoted generic font-family names are literal family names

Before: `font-family: "serif"` (or `'monospace'`, ...) was treated as the generic keyword, because quotes were stripped before lookup. A quoted family name in a page-margin box was also ignored entirely (the default font was used).

Now (CSS Fonts 4 section 2.1): a quoted name is always a family name. `"serif"` selects a family literally registered as `serif` and is skipped if none exists; write `serif` unquoted for the generic. Quoted/unquoted spellings of an ordinary family name behave identically in the body and in margin boxes, string escapes are decoded, and a comma inside a quoted name no longer splits the list.
