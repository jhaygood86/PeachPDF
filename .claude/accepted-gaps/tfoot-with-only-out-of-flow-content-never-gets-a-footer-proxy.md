# A `<tfoot>` whose only content is out-of-flow never gets a footer proxy created

Tracking issue: [#791](https://github.com/jhaygood86/PeachPDF/issues/791).

Found while writing regression tests for [#787](https://github.com/jhaygood86/PeachPDF/issues/787).
`CssLayoutEngineTable`'s first-page closing-footer creation gates on `_footerHeight > 0`
(`src/PeachPDF/Html/Core/Dom/CssLayoutEngineTable.cs`), and `_footerHeight` is derived from the
footer's own natural in-flow content height - out-of-flow content (`position: absolute`/`fixed`)
contributes nothing to that by definition. A `<tfoot>` whose only content is out-of-flow therefore
measures `_footerHeight == 0`, and no `CssProxyBox` is ever created for it - the footer's content
never appears in the rendered output at all, not even at the wrong position.

The equivalent `<thead>` path has no such height gate - a `<thead>` whose only content is
out-of-flow still gets its proxy created and its out-of-flow content still resolves/paints
correctly, confirmed directly by `AbsoluteInTheadCell_NoPositionedAncestor_ResolvesAgainstPageContentOrigin`
(`src/PeachPDF.Tests/Integration/AbsolutePositioningIntegrationTests.cs`). So this is a
`<tfoot>`-specific asymmetry, not a general limitation of out-of-flow content inside a repeating
group - `AbsoluteInTfootCell_NoPositionedAncestor_ResolvesAgainstPageContentOrigin` in the same file
routes around it by giving the footer cell some ordinary in-flow content alongside the out-of-flow
box.

Not attempted as part of #787's own fix, since it's a footer-proxy-creation gap unrelated to the
containing-block resolution bug that issue targets. A real fix likely means deciding a footer with
only out-of-flow content should still get a proxy (occupying no in-flow room, but still resolving/
painting its out-of-flow content, matching the `<thead>` path) - see the tracking issue for the
full reasoning.
