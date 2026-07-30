# `unicode-bidi: plaintext` does not perform first-strong-character detection

Tracking issue: [#552](https://github.com/jhaygood86/PeachPDF/issues/552).

CSS Writing Modes 4 §2.2's `unicode-bidi: plaintext` should re-derive an element's paragraph
embedding direction from the first strong character in its own content (UAX #9 P2/P3) regardless of
its computed `direction` — the same first-strong-character detection HTML's `dir="auto"` already
performs correctly at the DOM layer (`DomParser.ResolveAutoDirectionality`).

`CssBidiParagraphResolver.ResolveParagraph` always derives the paragraph direction from
`paragraphRoot.Direction.Value` and never passes `BidiParagraphDirection.Auto` into
`BidiResolver.Resolve` — `Auto` has no production call site at all today, only
`BidiResolverConformanceTests` exercises it directly. `CssUnicodeBidiMapping.MapToPushes` also
downgrades an inline `unicode-bidi: plaintext` box's push to an ordinary isolate (`Lri`/`Rli`) rather
than an `Fsi` (first-strong isolate) push, so first-strong detection never happens for it either.

`<p dir="ltr" style="unicode-bidi:plaintext">שלום עולם (test)</p>` lays out identically to plain
`dir="ltr"` — no reordering at all — when the leading strong-R character should make its detected
base direction RTL.

**Deliberately out of scope for now.** `docs/html-css-support.md`'s `unicode-bidi` row currently lists
`plaintext` as a supported value; that note needs to be corrected to describe this gap the next time
that page is touched, alongside closing the gap itself.
