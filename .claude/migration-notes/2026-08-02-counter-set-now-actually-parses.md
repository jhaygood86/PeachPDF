# `counter-set` now actually works

Previously, `counter-set` had no `PropertyFactory` registration at all (no `PropertyNames.CounterSet`
constant, no `CounterSetProperty` class) — a `counter-set: <name> <value>` declaration was
unrecognized CSS and silently dropped by the parser before it ever reached a `StyleDeclaration`, even
though `CssBox.CounterSet` → `CssCounterEngine.ApplyCounterSets` were already fully wired to consume
it. `docs/html-css-support.md` had claimed "Full support" for `counter-set`, and `css-properties.json`
declared it as a `cssDataType: "cssom"` (pass-through) property — both incorrect, since the property
never reached storage at all.

`counter-set` is now registered exactly like its sibling `counter-reset` (same `[ <counter-name>
<integer>? ]+ | none` grammar), plus a `StyleDeclaration.CounterSet` convenience property matching the
other 234 CSS properties' typed wrappers. `counter-set: x 5` now parses, cascades, and reaches
`content: counter(x)`/list markers as CSS Lists Level 3 §2.3 specifies — this was a bug fix, not a
behavior change an author was ever relying on (the property could not have done anything before).
