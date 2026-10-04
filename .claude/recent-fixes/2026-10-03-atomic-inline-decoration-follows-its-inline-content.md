# An atomic inline's own text decoration is drawn over its inline content, not its content box

Issue #1320. `a { display: inline-block; text-decoration: underline }` with no text (an icon-only
link: the icon is a background image) drew a stray link-coloured line across its whole content box.
Found on `biovitaal.mhtml`, where it read as a dark line over the shopping-cart icon in the header.

## What was wrong

`FragmentPainter.PaintBoxContent` chooses its decoration path from the fragment's line rectangles.
A block-level box has one rectangle with `Line: null` and goes through `PaintPropagatedDecoration`
(css-text-decor-3 §2.4: the decoration lands on the inline content). An atomic inline also has one
rectangle, but it is its border box *on the parent's line*, so it carries a `CssLineBox` and fell into
the per-own-line branch, which treats that rectangle as the decoration area. Same bug as the block
case in [2026-09-14-block-text-decoration-propagates-to-inline-content.md](2026-09-14-block-text-decoration-propagates-to-inline-content.md),
reached through a different predicate.

## The fix

The propagated path is now taken for `lines is [{ Line: null }] || (DecorationsWorthCollecting(box) &&
DomUtils.IsAtomicInline(box) && box is not CssBoxFormField)` (the form-control exception is below).
`DecorationContent.Of` walks the fragment's *children*, whose line boxes are the atomic inline's own
inner lines, so the spans are exactly its text. An empty one finds no content and draws nothing.

## Found by running it

- The wrapped-text case was not correct before the fix, despite what the issue says: the new
  `AtomicInlineUnderline_WrappedText_FollowsEachTextLine` fails on `main` too.
- A decorated `<img>`/`<math>` already drew nothing before the fix; their tests passed with or without
  it (they never reach the changed branch), so they were dropped as vacuous.
- Rasterized `biovitaal.mhtml` before/after through MuPDF: the line above the cart is gone, nothing else
  on the page moved.

## Evidence

- 11 new test cases in `TextDecorationPaintIntegrationTests.cs` (empty `inline-block`/`inline-flex`/
  `inline-grid`, short text, wrapped text with per-line extents, an atomic-inline-only child,
  block-level children, `inline-table`, `inline-flex` with text, an empty padded inline child with and
  without text beside it - the icon-span-in-a-link pattern, which draws nothing) plus a form-field pin; the empty/short/wrapped ones fail
  without the fix.
- **A form control stays on the old per-line path** (`box is not CssBoxFormField`; a probe of img/svg/iframe/math/video showed they draw nothing under either guard).
  `<img>`/`<math>`/`<object>` never reach this code (their content painters take over - a throw
  planted in the arm was not hit by 3009 related tests), but a form control without a content painter
  does: its text is its own `CssRectFormField` word on the root fragment, and
  `DecorationContent.Of` only collects spans from *children*, so the propagated path finds nothing. Before
  the guard an underlined `<input>` drew nothing where it used to draw a line across the whole control
  (found by probing `<input>`/`<select>`/`<textarea>`; `<button>` is an ordinary inline-block and was
  fine). Browsers underline the control's text; that is still not done - `FormFieldUnderline_KeepsItsControlWideLine`
  pins today's too-wide line, and it is the thing to change if control text ever becomes collectable
  inline content. An earlier draft of this note and the code comment claimed replaced elements never
  reach the branch; that was wrong for form controls.
- The atomic-inline check runs only for a box that declares a decoration
  (`DecorationsWorthCollecting` first), so undecorated boxes pay nothing for it.
- Vertical writing mode (accepted gap, see
  [no-vertical-writing-mode-layout.md](../accepted-gaps/no-vertical-writing-mode-layout.md)): an
  underlined `vertical-rl` inline-block used to get a full-height bar down its edge, empty or not; now
  an empty one gets nothing and a text one gets a line along its text. Only one column of a wrapped
  vertical inline-block is underlined - not addressed here.

## Review round: the decoration is drawn inside its own box's overflow clip

The maintainer's review found the fix made one common pattern worse: a truncated link
(`display:inline-block; overflow:hidden; white-space:nowrap`) now had its underline run along the
*unclipped* text, hundreds of pixels past the box, where `main` kept it inside. The same bug already
existed on `main` for a block-level box. Cause: a fragment carries the clip of its clipping *ancestor*
(`FragmentEmitter.OverflowClipOf` starts at the containing block), so a box's own `overflow` clip is in
force for its children but not for the decoration drawn at the box's own paint step - which is where the
propagated path draws it. `PaintPropagatedDecoration` now pushes the decorating box's own padding-edge
clip (rounded to its radius) around the lines. One place fixes the block-level and atomic-inline cases.
Tests: `PropagatedUnderline_OfAClippingBox_IsDrawnInsideItsOwnClip` (inline-block and block; fails with
the push removed) and a no-clip-pushed case.

**Not pinned, deliberately:** the review asked for a test that a padded non-atomic `<span>` still takes
the per-line path. Mutating the gate (dropping `IsAtomicInline`, keeping the form-control exclusion) is
distinguishable only by where a *vertically* padded or bordered inline's underline lands - and that
placement is itself wrong on the per-line path on `main` (`padding-bottom:12pt` moves the underline up
into the glyphs; Chrome keeps it below the text). Asserting it would pin a defect, so the gate is covered
by the form-control and empty/short/wrapped tests, and the placement bug is a separate issue (#1626).

### Known limits of the clip fix

- **`text-overflow: ellipsis`:** the underline span is built from every word's rectangle, including the
  words the ellipsis replaces, then clipped at the padding edge. Measured in the showcase PDF: the line is
  drawn out to 208.6pt and clipped at the box edge (122.5pt) while the ellipsis text ends at 119.2pt, so
  the visible line overshoots the ellipsis glyph by ~3pt. A browser stops before the ellipsis, so against
  Chrome the overshoot is the ellipsis plus that slack. Identical on `main` for a plain inline link in a
  truncating block - not specific to this change; tracked in #1625.
- **Fragmented clipping blocks:** the clip is built from the fragment's own rectangle
  (`RenderUtils.TryPushOverflowClip`, shared with the ancestor-clip hoist path), where the descendants'
  clip comes from the whole box. On a slice with no real top/bottom border it insets by a border that is
  not there, which can trim an underline within a border-width of that edge. Same limitation the existing
  helper has; not covered by a test.
- **Own-step draws in general:** a fragment carries only its clipping *ancestor's* clip, so anything
  drawn at the box's own paint step is outside its own `overflow` clip. Only the propagated decoration is
  clipped here; the per-line path (form controls) is not.
