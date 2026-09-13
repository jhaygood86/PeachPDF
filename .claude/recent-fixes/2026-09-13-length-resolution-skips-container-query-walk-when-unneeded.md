# Every length resolution walked the ancestor chain looking for a container query, even non-cq units

## What was wrong

`CssBox.FindNearestQueryContainer` (`src/PeachPDF/Html/Core/Dom/CssBox.cs`) walks every ancestor box to
find the nearest eligible `@container` query container — real, non-trivial work for a deeply nested
document. Both overloads of `CssValueParser.ParseLength` (the typed-`Length` one and the raw-`string`
one that the vast majority of `Length`-typed CSS box-geometry properties actually resolve through) called
`box.GetContainerRelativeUnitBasis()` — which does this ancestor walk — **unconditionally, for every
length resolution**, regardless of whether the length being resolved was a `cq*` unit (`cqw`/`cqh`/`cqi`/
`cqb`/`cqmin`/`cqmax`) at all. An ordinary `10px`/`50%`/`2em` paid the same ancestor-walk cost as an
actual `40cqw`.

## The fix

Added `Length.IsContainerRelative` (true only for the six `cq*` `Unit` values — `Length.ToPixels`'s own
switch confirms `containerWidthPt`/`containerHeightPt`/`containerInlineSizePt`/`containerBlockSizePt` are
only ever read in those six branches, so skipping the walk for every other unit changes nothing about the
result). The typed-`Length` overload of `ParseLength` checks it directly; the string-based overload
(where the unit isn't known until later, and the string could be a `calc()` expression mixing units) uses
a cheap, conservative pre-filter instead — every `cq*` unit's own name contains "cq", so a length string
that doesn't contain "cq" anywhere cannot possibly resolve to one. A false-positive match (e.g. "cq"
appearing inside a custom-property-derived string that isn't actually a container-relative unit) only
costs the walk it would have paid anyway; it can never produce a wrong answer.

## Evidence

`dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0 --filter "FullyQualifiedName~Container"`:
295 passed, 0 failed — including every container-query-unit-resolution test, confirming the six `cq*`
units still resolve exactly as before. Full suite: 11035 passed, 0 failed, 9 skipped. Combined with the
UA-stylesheet caching fix, contributed to the coverage-collecting full-suite run dropping from 56s to
~50s (see that fix's own doc for the combined before/after).
