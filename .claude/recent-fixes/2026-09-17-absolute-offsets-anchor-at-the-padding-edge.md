# An absolute box's top/left anchor at the padding edge

`top`/`left` on an absolutely positioned box were measured from its containing block's **content**
edge while `right`/`bottom` were measured from its **padding** edge, so one box landed in two
different places depending on which pair of offsets placed it. Issue #1160.

## The load-bearing idea

`CssBox.cs`'s absolute branch already intended the right thing — its comment says "measured from the
containing block's PADDING edge (ClientLeft/ClientTop — inside the border)". The defect is that
`ClientLeft` is not the padding edge:

    public double ClientLeft => Location.X + ActualBorderLeftWidth + ActualPaddingLeft;

That is the **content** edge. The padding edge is `Location.X + ActualBorderLeftWidth`. The code was
written against the name, and the name is misleading in a specific way: in the DOM,
`element.clientLeft` is the border width and the client rect is the padding box, so `ClientLeft`
reads as "the padding edge" to anyone who knows the DOM and is not.

`right`/`bottom` never went through this expression, which is why only two of the four offsets were
wrong. That asymmetry is the sharpest symptom and is what the contrast test pins.

## What running it found, rather than reading it

Measured against Chrome 141 (`--headless --print-to-pdf`, same font set) on a `position: relative`
box with `padding: 30pt 40pt; border: 10pt`, an absolute child's text origin in points:

| offsets | Chrome | before | after |
| --- | --- | --- | --- |
| `top: 0; left: 0` | (30.0, 29.95) | (70.0, 60.0) | (30.0, 30.0) |
| `bottom: 0; right: 0` | (396.11, 138.7) | (396.11, 138.75) | (396.11, 138.75) |

The `before` delta is exactly `padding-left` and `padding-top`. With the ancestor's padding set to
zero every row agrees before and after, which is the whole reason this survived: **every existing
fixture in `AbsolutePositioningIntegrationTests` sets `padding: 0` on the positioned ancestor**, so
the padding edge and the content edge were the same point in all of them and the two readings could
not be told apart. The full suite passes unchanged with the fix applied and no new test — 12,211
passed either way — so a green suite was never going to find this.

Reverting `CssBox.cs` alone with the new tests in place fails
`AbsoluteZeroInsets_AnchorAtTheAncestorsPaddingEdge_NotItsContentEdge` with `Actual: 50, Expected:
10` — the 40pt padding-left, arrived at from the other direction.

## Deliberately not done

Two neighbouring behaviours differ from Chrome in the same fixture and are **not** touched here,
because they resolve through `ActualWidth`/`ActualHeight` rather than through the origin computed
above, which is a different code path with a much wider blast radius:

- `width: 50%` on the absolute child resolves against the ancestor's 300pt content box (150pt) where
  Chrome uses its 380pt padding box (189.75pt);
- `top: 50%; left: 50%` places the child at (270.0, 60.0) where Chrome gives (220.0, 89.95).

Both are recorded on #1160 rather than guessed at. Note that `docs/html-css-support.md` already
claims the padding box for percentages too, so the docs are ahead of the code there and no doc
change belongs in this fix.

## Evidence

- `PeachPDF.Tests` on net8.0: 12,214 passed, 0 failed, 9 skipped (12,211 before the three new tests).
- `dotnet build PeachPDF.Tests/PeachPDF.Tests.csproj -t:Rebuild`: 6 warnings, all pre-existing
  CS1998s, none added. (Local SDK 9 box via the net8.0-only tweak, so totals will not match CI's.)
- Mutation check as above.
