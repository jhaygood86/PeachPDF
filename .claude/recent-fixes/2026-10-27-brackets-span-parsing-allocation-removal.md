# PeachBrackets allocation removal: selector walk, class tokens, number/length/data-URI parsing

Behavior-preserving. Batch D of the perf evaluation (vectorization / Span opportunities); no profile was taken, the choices come from reading the hot paths.

- `CssData.DoesSelectorMatch(ComplexSelector, ...)` walked `complexSelector.Reverse().ToList()` for every rule x node. `ComplexSelector` now has an indexer, `Entries` (the backing list as `IReadOnlyList`) and `Subject` (the last compound), and the match walks backwards by index. `LastOrDefault()`/`ToList()` on a `ComplexSelector` elsewhere in `CssData` use those too.
- Class lookups (`_classIndex`, `_consequentialClassIndex`, `ClassSelector`, `[attr~=]`) read the attribute through `SpaceSeparatedTokens` (spans, U+0020 only, empty tokens skipped - exactly `Split(' ')` minus the empties). On net9+ the index lookup uses the dictionary's `ReadOnlySpan<char>` alternate lookup (the indexes are `OrdinalIgnoreCase`); net8 builds one string per token under `#if`. HTML also separates class tokens on tab/newline/form feed; the matcher never did and still does not - that is a separate behavior decision, left alone.
- `CssLength`, `CssValueParser`, `SvgValueParsers`, `SvgTreeBuilder`, `SvgTransformOrigin` parse numbers from spans with `InvariantCulture` instead of `Substring` + current-culture. `CssLength`'s too-short error path used a current-culture `double.TryParse`; it is invariant now (`HasError` is set either way).
- `StartsWith("table-")`/`"min-"`/`"max-"` are ordinal; `"#"` is the char overload.
- `DataUriUtils.TryDecodeDataUri` slices the header/payload as spans, skips `Uri.UnescapeDataString` when there is no `%`, and decodes base64 with `Convert.TryFromBase64Chars` into one buffer (whitespace/line-wrapped base64 is still accepted, as with `FromBase64String`). The header scan still counts the first part (so `data:base64,...` reads as base64 with mime `base64`, as before).
- `CssBox` word creation reuses the word string for the original text when the transformed and source text are the same instance.
- `SvgTransformParser.IsValidCssTransform` has no `Enumerable.Range(...).All(...)` closures, and lowercases the function name once in `BuildMatrix`.
- `CssNamedStringEngine` collapses whitespace with a span loop (`char.IsWhiteSpace`, the same predicate .NET's `\s` uses) instead of a generated regex.

Evidence: `SpanParsingEquivalenceTests` (token walk vs `Split`, class/attribute/combinator box-tree assertions, lengths under a `de-DE` culture, data URIs plain/wrapped/escaped/invalid, whitespace collapse vs the old regex, transform grammar table), plus the existing suites.
