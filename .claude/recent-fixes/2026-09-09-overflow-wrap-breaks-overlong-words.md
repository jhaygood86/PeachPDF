# `overflow-wrap` and `word-wrap` now break overlong words

Closes [#696](https://github.com/jhaygood86/PeachPDF/issues/696).

## What was wrong

The old CSSOM had an `OverflowWrapProperty`, but the generated property registry deliberately marked
`overflow-wrap` unsupported. It was not inherited, `anywhere` was absent from the keyword map, and
neither the horizontal nor vertical line breaker consulted the computed value. `word-wrap` also had
no generated-registry alias. The result was the issue's long lake name overflowing unchanged for
both emergency values.

## The load-bearing idea

These are emergency opportunities, so the line breaker must not eagerly turn a word into one
fragment per character as `word-break: break-all` does. Hyphenation is tried first. If a normal break
before the overlong word exists, the word moves whole to the fresh line; only if it still does not
fit does layout choose the last extended-grapheme-cluster prefix that fits. This preserves the
priority CSS Text assigns ordinary opportunities and never invents a hyphen.

The prefix and suffix temporarily replace the original `CssRectWord`. They retain bidi, first-line,
font fallback, script, Arabic joining, USE, text-transform, and hyphenation-candidate metadata. The
pair links back to the original word for two rollback paths: a discarded fragmentainer line restores
the word before resumption, and a fresh full reflow restores every prior emergency split before
choosing against its new measure. This mirrors the existing hyphenation rollback invariant and keeps
temporary split state from becoming an unconditional break on later passes.

`anywhere` additionally uses the widest extended grapheme for min-content sizing. That value is
measured and cached lazily, only when an intrinsic-size query needs it, and only when `white-space`
permits wrapping. `break-word` deliberately continues contributing the whole word. The `word-wrap`
registry entry aliases the same inherited computed property, rather than maintaining a second value
that could diverge in the cascade; CSSOM reads, priority updates, and removals canonicalize the alias
to that same stored declaration.

## What was found by running it

The exact HTML attached to the issue is a useful distinction test: the normal paragraph still
overflows, while both emergency-value paragraphs first move the lake name below “excellent at Lake”
and then split it inside the gold box. It also confirmed that the existing hyphenation examples keep
their separate behavior.

The initial implementation covered repeated layout but left the cached `anywhere` min-content value
only on the original word. A direct split test exposed that an intrinsic-width read between mutation
and restoration could observe fragments without that cache, so the split now carries it onto both
temporary fragments.

Review then exposed two related intrinsic-sizing problems. A nested `white-space: nowrap` or `pre`
run contributed one grapheme to its parent's min-content width even though line layout correctly
refused to split it; the intrinsic walk now applies the same non-wrapping guard. Measuring every
grapheme eagerly during every ordinary word-measurement pass also made an inherited body-level
`anywhere` roughly three times slower in a 4,000-word stress case, so grapheme measurement now happens
only on demand during intrinsic sizing. An auto-table regression test found a second minimum-width
pass that still used the whole word, and that pass now uses the same intrinsic-width calculation.

The review also identified the CSSOM side of the `word-wrap` alias: declarations were stored under
the canonical `overflow-wrap` name, so querying the legacy spelling returned an empty value. Both
spellings now address the canonical declaration. Split restoration continues walking backward because
a suffix can itself be split; an explicit adjacency check and regression test now protect that
load-bearing invariant.

The nested-`nowrap` defect existed because "does `white-space` leave any soft wrap opportunity"
was written out independently in the line breaker and in the intrinsic walk, so the two could
disagree. Both now read `CssBox.WhiteSpacePermitsWrapping`, as does `GetMinMaxWidth`'s own
pre-existing min-content-is-max-content clamp, which was a third copy of the same test. This
matters for more than tidiness: css-text-3 also defines `break-spaces`, which wraps, and whoever
adds that value would otherwise have to find all three sites to stay consistent.
`GetMinMaxSumWords.StartsNewLine` deliberately keeps its own, different test (`nowrap` only, not
`pre`) and was left alone.

## Deliberate boundaries

This change does not add further `word-break` values; `keep-all` remains the separately documented
partial implementation. A grapheme wider than the line is unavoidable overflow, because splitting
inside that grapheme would corrupt the text. Prefix fitting uses a logarithmic search over grapheme
boundaries so exceptionally long URLs do not cause a quadratic succession of shaping calls.

## Evidence

- Exact issue HTML rendered and visually inspected: `normal` overflowed; `anywhere` and `break-word`
  stayed within the paragraph without inserted hyphens.
- Focused overflow-wrap/property/rollback/intrinsic/table tests: 56 passed.
- Source-generator tests: 119 passed.
- Full net8.0 suite: 10,451 passed, 9 skipped, with the one pre-existing locale-sensitive
  `FontSynthesisIntegrationTests` decimal-format assertion failure.
- Whole-solution rebuild: successful with 0 warnings and 0 errors.
- Changed production lines: 100% covered (278/278 coverable lines, measured from the Cobertura
  report; the local environment did not have the `diff-cover` executable).
