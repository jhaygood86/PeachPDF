# Generated content (`::before`/`::after`) is excluded from bidi paragraph resolution

Tracking issue: [#551](https://github.com/jhaygood86/PeachPDF/issues/551).

`CssBidiParagraphResolver.Flatten` builds one paragraph's logical-order text by walking a box's
children and appending each **child**'s `Text` — it never appends the paragraph-root box's own
`Text`. For an ordinary element this is fine, since its text always lives on an anonymous child text
box. But `CssContentEngine.ApplyContent` sets `Text` directly on a `::before`/`::after`
generated-content box, and when that box is itself a childless paragraph root, its own text is never
captured and it never gets a `BidiLevels` array. `CssBox.ParseToWords` then falls back to one uniform
level from `Direction`, so the whole generated-content string is treated as a single homogeneous run
and fully reversed/mirrored under `direction: rtl` — even plain Latin text or digits that UAX #9
(I1/I2) says should stay left-to-right.

`<style>p::before { content: "abc 123 "; }</style><p dir="rtl">שלום</p>` renders the generated
content as `cba 321` instead of `abc 123`.

**Deliberately out of scope for now.** Fixing this requires tracing exactly where pseudo-element
content application sits relative to `CssBidiParagraphResolver.AssignBidiLevels` in `DomParser`'s
pipeline (content may be applied after paragraph resolution already ran, or the childless-root case
in `Flatten` needs its own branch, or both) — real work, not a one-line fix, and out of scope for the
change that discovered it.
