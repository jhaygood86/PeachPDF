# Empty-fragment anchor link (`<a href="#">`) crashed PDF generation

`PdfGenerator.HandleLinks`'s local `ResolveAnchorTarget(anchorId)` helper called
`HtmlContainer.GetElementRectangle(anchorId)` unconditionally for every link where
`LinkElementData.IsAnchor` is true (`Href[0] == '#'`). `LinkElementData.AnchorId` returns
`string.Empty` when `Href` is exactly `"#"` (length 1) rather than `"#some-id"` - a very common
real-world pattern for a JS-driven no-op link (`<a href="#">click me</a>`, popular in copy-pasted
"kitchen sink" HTML samples - this is exactly how issue #801 was found). `GetElementRectangle`
asserts its `elementId` argument is non-null/non-empty via `ArgChecker.AssertArgNotNullOrEmpty` and
throws `ArgumentNullException("elementId")`, which surfaced to the user as a generation-halting crash
for any document containing such a link - no way to opt out or work around it short of stripping the
link from the HTML first.

**Fix:** `ResolveAnchorTarget` now returns `null` immediately when `anchorId.Length == 0`, before
calling `GetElementRectangle`. Both call sites that reach it already treat a `null` result as "no
target, don't create an annotation" (`if (target is not { } t) continue;` in both the main
`HandleLinks` loop and `HandleRunningElementLinks`), so a single guard in the shared helper fixes both
paths - the running-element path (`PdfGenerator.cs`'s `HandleRunningElementLinks`, which independently
derives `anchorId = href[1..]` from the running-element box's own `href` attribute) had the exact same
bug via its own code path into the same helper.

**Not done:** `BookmarkOutlineBuilder.ApplyDestination` was already correct - it guards with
`target.Length > 1 && target[0] == '#'` before slicing, so `target[1..]` is never empty there. That
existing pattern is what confirmed the fix here should live in the shared resolver rather than only
guarding the AnchorId-producing side.

**Evidence:** reproduced the crash with the exact HTML from issue #801 (a large HTML5-kitchen-sink
sample containing `<a href="#">Home</a>` inside a `<menuitem>`) via the CLI - confirmed the pre-fix
build throws `System.ArgumentNullException: Value cannot be null. (Parameter 'elementId')` mid-paint,
and the post-fix build generates the PDF successfully. Added
`AnchorLink_EmptyFragment_DoesNotThrowAndAddsNoAnnotation` to
`PeachPDF.Tests/Integration/AnchorLinkDestinationTests.cs` asserting generation doesn't throw and adds
no annotation for `<a href="#">`. Full `dotnet test --framework net8.0` suite (9107 passed, 9 skipped,
0 failed) and `dotnet build PeachPDF.slnx -t:Rebuild` (0 warnings) both clean.
