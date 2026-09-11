# DOM: a box must never hold both its own words and child boxes

`CssLayoutEngine.FlowBox` opens with:

```csharp
var boxes = box.Boxes;
if (boxes.Count is 0 && box.Words.Count > 0) boxes = [box];
```

A box that holds words **and** child boxes therefore has its own words silently skipped — the inline
flow visits the children and never self-iterates. Nothing asserts this, nothing logs it, and the box
still measures, sizes and paints its background and borders exactly as expected.

## The measured symptom

The word keeps whatever coordinates some earlier pass left on it, and it stays flagged
`CssRect.AwaitsTheNextFragmentainer`: `CreateLineBoxes`'s prologue sets that on the whole subtree every
pass (`CssBox.AwaitPlacement`), and only `CssRect.Top`'s setter clears it. With no inline flow placing
the word, the only thing that can clear it is an incidental `OffsetTop` on the box — which
`CssLayoutEngineFlex.AssignLocations` skips whenever the move is under 0.01. `FragmentEmitter.BuildDraft`
then drops the word, so it produces no `TextFragment` and does not exist in the PDF.

What that looks like from outside: a box of exactly the right size, with its background painted, and no
text. The box tree is intact and only the fragment tree is wrong, so a test that walks `CssBox.Words`
passes. Assert on `BoxFragment.Words` instead (`FragmentPaintHarness.FragmentOf`).

## What produced it

Pseudo-element synthesis. `CssData.DoesSelectorMatch` creates a `::before`/`::after` box as a side effect
of matching, and a `*::before` compound used to reach anonymous text boxes, giving one children. That
particular route is closed (a pseudo-element is only generated on a box with an `HtmlTag`), but the
invariant is the durable part: **any** new code that adds a child box to a box that carries words —
generated content, a synthesized marker, a fixup pass — reintroduces the same silent text loss.

If a box genuinely needs both, `FlowBox` has to flow the box itself alongside its children, in document
order. Adding the child is not the safe half of that change.
