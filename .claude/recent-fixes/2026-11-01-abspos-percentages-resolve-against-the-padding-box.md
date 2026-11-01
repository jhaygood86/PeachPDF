# Absolute-box percentages resolve against one padding-box measure (#224)

CSS 2.1 §10.1/§10.2 make an absolutely positioned box's containing block the **padding box** of the
nearest positioned ancestor, and percentage `left`/`right`/`width` (and `top`/`bottom`/`height`) resolve
against it. Three different bases were in use for the same ancestor: `CssBox` placement used the border
box (`ActualWidth`), the auto-size fill and percentage `width` used the content box
(`PercentageBase(box).Size` / `PageAwareWidthBasis`), and the vertical placement had already been fixed
to the padding box (`AbsoluteContainingBlockHeight`).

Now `CssBox.AbsoluteContainingBlockWidth`/`AbsoluteContainingBlockHeight` are the single measure, used by
the `left`/`right`/`top`/`bottom` placement and by `CssLayoutEngine.GetBoxWidth`/`GetBoxHeight`
(percentage width/height and both auto fills) through `AbsolutePaddingBoxWidth`/`Height`.

Trap: the new base applies only when the nearest positioned ancestor really is positioned (or an inline
containing block). With none, `GetNearestPositionedAncestor` returns the root, and the ordinary
page-aware basis (`PageAwareWidthBasis`) must stay, or per-page measures regress.

Found by running: before the fix, with a 100pt box + 10pt padding + 5pt border, `left:10%; right:10%`
filled 80pt (content box), `top/bottom:10%` filled 104pt (border box); both are 96pt (80% of 120) now.
Length insets and unpadded ancestors are unchanged. Evidence: four new
`AbsolutePositioningIntegrationTests`; full `PeachBrackets.Engine.Tests` (17013) and `PeachPDF.Tests` green on net8.0.
