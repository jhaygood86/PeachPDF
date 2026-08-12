# target-text(): before/after/first-letter modes are not supported

`target-text(<target>, before | after | first-letter)` (css-content-3 §5) parses successfully —
`TargetTextFunctionConverter`'s grammar accepts all four modes — but `CssContentEngine.ResolveTargetText`
only resolves the default `content` mode (the target element's own text); the other three silently
resolve to nothing, the same as an unresolved target. The underlying plumbing already exists and is
reused elsewhere in the codebase (`CssContentEngine.ExtractPseudoElementContent`, `ExtractFirstLetter`,
both consulted by `content()` inside `string-set`), so wiring up the remaining modes is a small,
self-contained follow-up rather than a structural gap. See
[Generated Content](docs/html-css-support.md#generated-content).
Tracked in [#719](https://github.com/jhaygood86/PeachPDF/issues/719).
