# `<hr>` paints through the ordinary border path (issue #1225)

`HrFragmentPainter` drew each of the rule's four sides itself, as one flat polygon filled with the
raw declared color, through a `BordersDrawHandler.DrawBorder` shim whose own doc comment already
called itself legacy. It read `BorderTopStyle`/`BorderLeftStyle`/etc. nowhere, so every bevelled,
patterned and `double` rule painted as a solid slab — and only on `<hr>`, since nothing else reached
that shim.

The fix is a deletion: drop the painter, drop its arm in `FragmentContentPainters.For`, drop
`DrawBorder`/`GetBandPoints`. An `<hr>` is an ordinary block box whose rule *is* its border, so the
generic `FragmentPainter.PaintBoxContent` → `BordersDrawHandler.DrawBoxBorders` path was already the
right one; nothing had to be built. 108 lines out of the library against one line and one UA rule in
— both of those for the two consequences below, not for the rule's paint.

## The guards the old painter had were not load-bearing

It only painted the left/right/bottom sides once `rect.Height > 1`, and only filled the background
once `> 2`. The issue flagged those as something a replacement has to preserve. They are not: a
browser paints all four sides of a 2px-tall rule, mitred at the ends, which is exactly what
`DrawBoxEdges` does with the rule's real `WholeBoxRect`. Keeping the guards would have reintroduced
the same class of special case the change is removing.

## Layout was also rewriting computed style, and that had to go with it

`CssBoxHr.PerformLayoutImp` overwrote `BorderTopStyle`/`BorderBottomStyle` with `solid` and both
widths with `1px` whenever the resolved height was two units or less and both horizontal borders were
under one unit. The UA default's `1px` is 0.75pt, so this fired on *every* default rule: after layout
a plain `<hr>` reported `styleT=Solid styleR=Inset styleB=Solid styleL=Inset`. Fixing the painter
alone would have left the default rule still painting `solid` on two of its four sides.

It also silently discarded an author's `border-style` on any thin rule, and turned `border: none`
into two visible 1px lines. Both are gone. The height fallback it was tangled with
(`height < 1 → borders; still < 1 → 2`) stays: it is what gives a borderless rule a height at all,
and removing it is a different question.

Worth knowing: the rewrite's width half was a no-op for the default rule (`"1px"` overwriting a value
that was already `1px`), and its style half was not. Reading it as "forces a 1px rule" is the wrong
model — it forced `solid`.

## What the test asserts, and why it is that shape

The regression here is silent: the old path painted something plausible for every style, nothing
threw, and no content-stream token went missing — a substring check on `/Pattern` or a polygon count
would have passed throughout. So `HrBorderStylePaintIntegrationTests` compares a rule against the
zero-height `<div>` carrying the identical border, over the whole ordered `TestRecordingGraphics.Log`
normalized to each box's own border-box origin, for all seven non-`solid` styles. The two are the
same box; any difference is the rule's own paint path inventing something. Plus one test pinning
Chrome's literal `#2c2c2c`/`#d4d4d4` for the issue's `2px inset #808080` repro, so a future change to
`BorderBevelColors` cannot quietly drift the rule with it.

## Two things the fix newly made load-bearing, and so had to come with it

**`hr[color], hr[noshade] { border-style: solid }`.** The HTML Standard pairs that rule with the
`hr { border-style: inset }` the UA sheet already had. While the rule painted flat regardless,
`noshade` was right by accident; the moment `border-style` started working, `<hr noshade>` began
rendering engraved — the exact thing the attribute exists to turn off. Added to `CssDefaults`.
Specificity (0,1,1) beats the bare `hr` rule whatever their order, so it needed no repositioning.

**Valueless attributes were stored as null, so `[attr]` never matched them.**
`HtmlParser.ParseHtmlTag` did `x.First().Value!` over the token's attributes; the tokenizer reports a
valueless attribute with a null value, and the HTML Standard gives it the empty string. So
`hr[noshade]` matched nothing, while `hr[noshade=""]` matched — and the same held for `[disabled]`,
`[hidden]`, `[required]` and every other boolean attribute in the language. One `?? string.Empty`.
The blast radius is the reason to say so here: this changes what `TryGetAttribute(name, default)`
returns for *any* valueless attribute — `""` now, the caller's own default before — so every caller
that branches on "absent" sees one more attribute as present. Presentational-attribute translation is
the one to watch; `attr()` is not, since `CssContentEngine` already passes `""` as its default. The
whole suite was run against it specifically to find out, and no test moved.

## A separate defect found while verifying, deliberately not fixed here: issue #1229

Rasterizing the repro showed the next block overlapping the rule. It is not this change's doing —
before and after place content identically — and it is not a paint problem: an `<hr>` advances the
block flow by a constant 2 units no matter what its resolved height is, so anything after a rule
thicker than about 1px lands on top of it. The equivalent zero-height `<div>` advances correctly, so
it is specific to `CssBoxHr`; the constant 2 is that file's own `height = 2` fallback, which suggests
the following sibling is placed against an `ActualBottom` written before the rule's real height was
resolved and never re-placed. Measurements are in the issue. The showcase's new `<hr>` swatches give
each rule a bottom margin, which is why the pairs there read as pairs.

## And a second one: issue #1230

`CssBoxHr` resolves a percentage `width` against a basis it has already reduced by its own left and
right border widths, so `<hr style="width:50%; border:4px solid">` in a 200pt block is 97pt of content
where CSS 2.1 §10.2 (and the equivalent `<div>`) says 100pt. Also pre-existing, also untouched. It is
worth knowing here because it is a trap for anyone writing the showcase or a test that pairs a rule
against its equivalent `<div>`: declare no width at all. `width: 100%` makes the two disagree by twice
the border width — the div's border box overflows its cell and the rule's does not — and the first
version of this change's showcase section did exactly that and shipped four visibly mismatched pairs
past a review of the code.

## Evidence

Full suite green on net8.0 (12629 passed; the one failure,
`CssLayoutEngineTableTests...Issue1157`, fails identically on a clean `main`). The library gains one
executable line (`?? string.Empty`, hit 91414 times in the run) and one UA stylesheet rule; everything
else added is a comment, and everything else changed is a deletion. Whole solution rebuilds with zero
warnings.
The `border_style` showcase gained an `<hr>`-vs-`<div>` section. Verified by diffing each rule's raster
strip against its div's, in *both* PDFium and MuPDF: mean channel difference 1.07-2.74 across both
rows in both engines, i.e. antialiasing. Reading the page was not enough on its own — the mismatch
that measurement caught was visible in the render and went unnoticed twice.
