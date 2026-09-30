# `bolder`/`lighter` follow the CSS Fonts 4 table, not CSS 2.1's

`FontWeightResolver.Resolve` (`src/PeachPDF/Html/Core/Utils/FontWeightResolver.cs`) stepped `font-weight:
bolder`/`lighter` from the parent's resolved weight using CSS 2.1 §15.6's worked table (bolder: <400→400,
≤500→700, else 900; lighter: ≤500→100, ≤700→400, else 700). CSS Fonts 4 §2.2.1 has different breakpoints -
100, 350, 550, 750 - and its own "no change" cells. Fixes #1446.

## What the spec actually says (fetched fresh, not from the issue's summary)

The issue that filed this, and this plan's own worked breakpoints, both stated "no change below 100 for
both keywords." That is wrong for `bolder` - only `lighter` has a "no change" cell below 100. Fetching the
raw table from `https://www.w3.org/TR/css-fonts-4/` (`<table class="data" id="bolderlighter">`) directly,
row by row:

| Inherited value (w) | bolder | lighter |
| --- | --- | --- |
| w < 100 | 400 | No change |
| 100 ≤ w < 350 | 400 | 100 |
| 350 ≤ w < 550 | 700 | 100 |
| 550 ≤ w < 750 | 900 | 400 |
| 750 ≤ w < 900 | 900 | 700 |
| 900 ≤ w | No change | 700 |

`bolder` below 100 still steps *up* to 400 (the same cell its [100,350) row lands on) - it is `lighter`
that leaves a sub-100 weight untouched. Getting this backwards would have shipped the exact kind of bug
this fix exists to close. Two web searches through `WebFetch` (which summarizes via a small model) each
gave a *different*, wrong split for the top/bottom "no change" rows before the raw HTML settled it -
worth remembering that a spec citation from a fetch-and-summarize tool is not itself authoritative;
pulling the raw table HTML was necessary here.

## Simplification that fell out of the table

Because `bolder`'s <100 and [100,350) rows both resolve to 400, and its [550,750)/[750,900) rows both
resolve to 900, the six-row table collapses to a four-arm switch with only one "no change" (top) branch:

```csharp
FontWeightKeyword.Bolder => parentWeight switch
{
    < 350 => 400,
    < 550 => 700,
    < 900 => 900,
    _ => parentWeight
},
```

Symmetrically, `lighter`'s [100,350) and [350,550) rows both resolve to 100, collapsing to:

```csharp
FontWeightKeyword.Lighter => parentWeight switch
{
    < 100 => parentWeight,
    < 550 => 100,
    < 750 => 400,
    _ => 700
},
```

`parentWeight` is a `double` already (the engine resolves fractional `font-weight`), so the same
comparisons apply unchanged to a fractional parent - no separate fractional-path code was needed.

## Where the two tables actually disagree

The two tables agree at every multiple of 100 (which is all CSS 2.1's table tabulates) and diverge only
in the open bands between them - confirmed by running both tables' formulas over every integer and
finding the mismatches:

- `bolder`: (350,400) and (500,550) - e.g. 380 and 520 both used to give a different value.
- `lighter`: <100, (500,550), and (700,750) - e.g. a weight under 100 (old: forced to 100; new: left
  unchanged), 520, and 720.
- `bolder` above 900: CSS 2.1's table has no upper bound (`else => 900`), so any weight over 900 (e.g.
  950) used to clamp to 900; Fonts 4 leaves it unchanged instead.

Tests added at both the unit level (`FontWeightResolverTests`: whole-number divergence cases, a
fractional divergence case per keyword, the sub-100 case for each keyword separately since they behave
differently there, and the >900 "no change is not a 900 clamp" case) and through the real cascade
(`FontShorthandIntegrationTests.FontWeight_BolderLighter_StepsRelativeToRealParentWeight` gained divergent
rows; `FontFaceMatchingOrderIntegrationTests` gained
`AFractionalWeightInADivergentBand_StepsByTheCssFonts4Table`). The pre-existing fractional-weight
integration tests (450.5/550.5) were left alone - checked against both tables and both land in bands where
they agree, so they were never proof of which table was in force.

## Evidence

`dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0 --filter "FullyQualifiedName~FontWeight"`:
97 passed, 0 failed (Debug). Full suite and `--configuration Release` run before opening the PR; diff
coverage checked with `git add -A` first per the standing rule.

## Not done

Deleted `.claude/accepted-gaps/font-weight-bolder-lighter-follow-the-css-2-1-table.md` (the gap named in
that file is exactly what this fix closes) and updated the two `docs/**` mentions of the old CSS2.1 table
(`docs/html-css-support.md`'s `font-weight` row, `docs/usage-examples.md`'s font-matching section) to
describe the Fonts 4 bands instead, free of issue numbers per the docs convention.
