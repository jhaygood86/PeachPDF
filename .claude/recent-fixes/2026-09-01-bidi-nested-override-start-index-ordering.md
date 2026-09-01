## Nested/sibling CSS bidi overrides sharing a start index pushed in the wrong nesting order

Fixes GitHub issue #575.

**Load-bearing idea:** `CssBidiParagraphResolver.Flatten` builds the shared `overrides` list
`BidiResolver.ResolveExplicitLevels` consumes for CSS `unicode-bidi` boxes, and that consumer relies
entirely on **list order** to know which of two overrides sharing a `Start` index is the outer one (it
pushes same-start overrides in list order, so whichever is last in the list ends up on top of the
explicit-level stack). The old code appended a box's own override only *after* recursing into its
children, so whenever a child box's override shared the exact same `Start` as its parent (parent has no
text of its own before the child begins - the overwhelmingly common shape for a `dir`-bearing wrapper
around another `dir`-bearing element), the child's override landed in the list before the parent's:
backwards from the real DOM nesting.

**What running it showed, not just reading it:** for two opposite-direction spans sharing a start
index, this isn't just a misordering that happens to still look right - it computes a numerically
different embedding level. Built a probe test rendering `<p>A<span dir="ltr"><span
dir="rtl">B</span>C</span></p>` and read `CssBox.BidiLevels` directly: pre-fix gave levels `A=0 B=2
C=2`, post-fix gives `A=0 B=4 C=2`. Pre-fix, "B" and "C" collapse onto the identical level 2, erasing
the isolate boundary between them; post-fix "B" correctly lands one level-3 (odd/RTL) scope deeper,
which UAX#9 I2 then bumps to 4 for being strong-L at an odd level. This matters practically because
`CssLayoutEngine`'s word-splitting relies on level *value* changes (not just parity) to place run
boundaries even with no intervening whitespace - see `CssLayoutEngineBidiTests.RtlParagraph_...SplitsIntoItsOwnWord`
for the same mechanism firing on digit runs.

**The fix:** record `insertAt = overrides.Count` *before* recursing into the child, then
`overrides.Insert(insertAt + i, ...)` the box's own override(s) there once its `length` is known (after
recursion returns). This keeps every earlier sibling's overrides before `insertAt` (unaffected) while
guaranteeing this box's own override sits ahead of anything a nested descendant added during the
recursive call - list order now tracks DOM nesting depth exactly, regardless of whether the box has any
text of its own before its children.

**Deliberately not done:** the `List<T>.Insert` is O(n) from shifting elements, but the overrides list
only grows with boxes that have a non-normal `unicode-bidi` (rare relative to total paragraph text), so
this isn't worth a different data structure.

**Evidence:** `PeachPDF.Tests/Html/Core/Dom/CssBidiParagraphResolverTests.cs`
(`NestedOppositeDirectionSpans_SharingStartIndex_ResolveOuterToInnerLevels`) asserts the exact byte
levels above; confirmed it fails on the pre-fix code (`A=0 B=2 C=2`, `Assert.Equal(4, ...)` fails
`Actual: 2`) and passes post-fix. Full suite: 9411 passed, 0 failed, 9 skipped (unrelated
platform-specific MIME tests). `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings. Diff coverage
against `main`: 100% (3/3 changed lines in `CssBidiParagraphResolver.cs`).

**Related but out of scope:** issue #576 (BD13 chaining for isolates nested 3+ deep that all close at
the identical index) is a separate, already-documented gap this fix does not touch - it concerns the
X10/BD13 isolating-run-sequence chaining logic in `BidiResolver.ComputeIsolatingRunSequences`, not the
override-list construction this fix corrects.
