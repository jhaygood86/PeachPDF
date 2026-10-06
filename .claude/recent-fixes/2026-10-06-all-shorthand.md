# `all` shorthand (#1288)

`all` is an ordinary CSS-OM shorthand (`AllProperty`, registered with `AddLogicalShorthand` at the end of the `PropertyFactory` constructor) whose longhand list is
computed from `_longhandsBuilder`, so a property added later is covered with no edit. Its converter is the five CSS-wide keywords only; `Export` hands every longhand the
keyword through the existing `IdentifierValue.ExtractFor`, so the cascade's keyword handling (`AssignCssBlock`, including the revert/revert-layer snapshots) needed no change.
SVG styling reads the same expanded longhand names (all 13 SVG-registry properties are CSS-OM longhands); MathML boxes go through the HTML cascade.

- Logical shorthand on purpose: the serializer must never fold longhands back into `all`.
- Excluded: `direction`, `unicode-bidi`, the legacy `-webkit-box-*`/`-moz-box-*` spellings, and alias spellings (`word-wrap`, `-webkit-backdrop-filter` are aliases the factory
  canonicalizes to the standard name). `AllShorthandTests` keeps the exclusion list explicit and fails when a longhand is neither covered nor on it.
- Found by running it: six longhands (`overflow-wrap`, `word-break`, `line-break`, `text-justify`, `text-anchor`, `block-ellipsis`) had converters without `OrDefault()`, so they
  rejected every CSS-wide keyword; `all` would have silently skipped them (a longhand that rejects the keyword is appended valueless and reads back as `initial`, which is wrong
  for `all: inherit`). Fixed at the source and guarded by `EveryCoveredLonghand_AcceptsEveryCssWideKeyword`.
- Found by running it: `PeriodicValueConverter.Construct` threw `IndexOutOfRangeException` for an unlabeled periodic (`border-image-width/-outset`) when the serializer tried to fold
  longhands back into a shorthand; it now declines to reconstruct.
- `Property.AllowsVarSubstitution` (default true, false for `all`): without it `all: var(--x)` was kept whole for cascade-time substitution, which cannot expand a keyword shorthand.
  A `var()` whose value is a keyword is therefore not supported in `all` (invalid at parse time), a deliberate narrowing.
