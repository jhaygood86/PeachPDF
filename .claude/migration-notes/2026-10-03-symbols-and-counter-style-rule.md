# `symbols()` and `@counter-style` now work

Before: `list-style-type: symbols(...)` or an author `@counter-style` name was treated as an unknown style,
so markers rendered as plain `decimal` numbers. Now they render with the declared symbols, prefix/suffix,
padding and fallback. A name with no matching `@counter-style` still renders as `decimal`, but the
declaration is now valid (it used to be dropped, so an inherited style could previously shine through).
