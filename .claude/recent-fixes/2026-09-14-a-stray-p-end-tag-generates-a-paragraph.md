# A stray `</p>` generates a paragraph, and a float's decoration stays on the line

The last of the five defects found by rendering Acid2 against its reference image, plus a regression
[the float-containment fix](2026-09-14-a-formatting-context-root-contains-its-floats.md) introduced and
the user caught by looking at the mouth.

## `</p>` is the one end tag that generates an element

HTML [§13.2.6.4.7 "in body"](https://html.spec.whatwg.org/multipage/parsing.html#parsing-main-inbody):
"An end tag whose tag name is `p`: if the stack of open elements does not have a `p` element in button
scope, then this is a parse error; **insert an HTML element for a `p` start tag token with no
attributes**. Close a `p` element."

`HtmlParser.CloseElement` already carried a comment reasoning about exactly this case and concluding
that an unmatched end tag "is simply ignored". That is right for every end tag but this one — `p` and
`br` are the only two the spec answers by generating something, and `br` (turned into a *start* tag) is
still not implemented. The generated paragraph is empty and closed immediately, so the insertion point
does not move; what it changes is the **element count**, and therefore what the sibling combinators
match.

Acid2 tests precisely that. `<p><table>…</table></p><p class="bad">` gives `.picture` four paragraphs,
not two: `<table>` implies the first `</p>`, so the author's own `</p>` is stray and generates one, and
the fixture's trailing `</p>` generates another. `.picture p + table + p` then matches the *generated*
paragraph. With no element generated it fell through onto `p.bad` — the element the fixture writes that
rule to keep away from — which took a `margin-top: 3em` meant for the generated one and slid a bar down
across the middle of the face.

Verified against Chrome's own DOM for seven markup shapes before writing any assertion, including the
three controls that must generate nothing (`</span>`, `</em>`, `</blockquote>`).

## This makes the Acid2 *pixel* comparison worse, on purpose

Rows matching the reference image went 84/168 → 66/168, and that is the correct direction.

The reference is a 2005 artifact rendered under HTML4 parsing, where no element was generated. There
`p + p` ("shouldn't match anything", says the fixture's own comment) really did match nothing, so
`p.bad` stayed black with a yellow border. Under HTML5 it matches, giving `p.bad`
`background: maroon; z-index: 1`, which then paints over the scalp bar wherever the two coincide.

**Confirmed by measurement, not reasoning**: rendering the fixture in Chrome with `.intro` hidden so the
two fixed bars are visible and sampling them gives maroon at y=108, red at 120, yellow at 123, black at
144 — exactly the sequence this now produces. A spec-conformant HTML5 parser cannot reproduce the
reference image here, and Chrome does not either: its own face differs from the reference in this same
region, which is essentially the whole 624px of the Chrome-vs-reference difference measured at the start
of this work.

So "rows identical to the reference" stopped being the right yardstick for this region, and should not
be quoted for it.

## The float-decoration regression, found by looking at the mouth

`.smile div div` measured 96px where Chrome gives 120px, painting the mouth's yellow flanks black. The
cause was one line in the intrinsic walk:

```csharp
paddingSum = Math.Max(paddingSum, floatDecoration);
```

`paddingSum` is `Math.Max`-combined down a chain of boxes because a descendant's border/padding sits
*inside* its ancestor's. A float is not on that chain — CSS 2.1 §9.5 places it **beside** the content of
the block it is in, so its decoration sits beside the container's and genuinely **adds**. Merging the
two lost the float's whenever the container's was larger.

This was invisible until [the §10.3.7 shrink-to-fit
correction](2026-09-14-a-formatting-context-root-contains-its-floats.md): the abs branch used to treat
the (under-counted) result as a *content* width and add the box's own decoration back, which happened to
land on the right answer for this shape and the wrong one for `blockquote.first.one`. Correcting the
caller exposed the merge.

The float's decoration now stays in the width measured for it and is not folded into `paddingSum` at
all, which is also simpler than what it replaces. The declared-width branch beside it adds
`ActualBoxSizeIncludedWidth` back, since a declared width is a *content* width while what goes on the
line is the outer one (correctly zero under `box-sizing: border-box`) — `AFloatsDeclaredWidthAndMargins_LandOnTheLine`
caught that on the first run.

**Every box in Acid2's `.smile` subtree now matches Chrome exactly** — `.smile`, `div`, `div div`,
`span`, `em`, `strong`.

## Still wrong, and why it was not fixed here

The mouth's *lower* half still paints its yellow flanks at half width. The geometry is now correct, so
this is a **paint** defect: `.smile div div span` declares `height: 1em` around a 24px float, and
`FragmentEmitter.ExtentOf` grows its painted rect to 24px — CSS 2.1 §10.6.3 says a definite height is
the height and content overflows it.

Gating that extension on `CssBox.IsHeightCalculated` was tried and **reverted**: it fails three tests,
two of them issue #569's, because a flex item's height is *pinned by the engine* and sets the same flag
— and #569's extension exists precisely to paper over that pin. The engine carries no distinction
between an author-declared height and an engine-pinned one, so the real fix needs that distinction
rather than a gate. Confirmed pre-existing by rendering `origin/main`: identical there.

## Evidence

- New `StrayEndTagParsingTests` (10 tests) and two new `IntrinsicWidthWalkTests` cases. Against the
  unfixed code 7 of the 12 fail; the 5 that pass are the controls — a matched paragraph, the three
  other-stray-end-tag cases, and the float with no decoration of its own.
- `Acid2RegressionTests.FixedPositionMargin_ShiftsSecondParagraphBelowFirstsBlackBar` retargeted: it
  asserted two paragraphs with `p.bad` carrying the margin, which its own comment calls "Per the HTML4
  DTD". Its real subject — a `position: fixed` box dropping its own margin — is unchanged and still
  covered, now against the generated paragraph.
- Full suite: 11,385 passed, 0 failed, 9 skipped. Diff coverage 100%.
- `dotnet build PeachPDF.slnx` — 0 warnings. A `-t:Rebuild` could not be run: the source-generator DLL
  is held open by another process on this machine (an IDE language server loads it as an analyzer), and
  killing the user's processes to satisfy a build flag is not a trade worth making.
- MuPDF and PDFium agree on the face to 52px of 28,224 at integer scale. Through the showcase's own
  `ShrinkToFit` config they differ by 2,254px, all of it sub-pixel red hairlines at the fractional
  scale each rasteriser seams differently — checked by cropping both, structurally identical.
