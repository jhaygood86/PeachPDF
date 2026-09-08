# CSS token-list pooling and `IValueConverter` pipeline retyped to `IReadOnlyList<Token>` (issue #922, parts 3d-3f)

## The load-bearing idea

Continuing from the `Token` struct/slice-backed `Data` work
([`2026-09-07-allocation-free-css-tokenizer-token-struct-and-slice-backed-data.md`](2026-09-07-allocation-free-css-tokenizer-token-struct-and-slice-backed-data.md)),
a full-pipeline showcase benchmark (see "What was found by measuring it" below) showed that work alone was
a net *regression* in raw allocated bytes against `main`, not a win - closing that gap needed two more
mechanical passes:

**Part 3d - pool `CssValueParser.GetCssTokens`'s backing list.** `Pool.NewTokenList()`/
`ReturnTokenList()` (`src/PeachPDF/CSS/Model/Pool.cs`) plus a `PooledTokenList` RAII `ref struct` and
`CssValueParser.GetCssTokensPooled(...)`, used as `using var pooledTokens = GetCssTokensPooled(value);
List<Token> tokens = pooledTokens;`. Only safe where the list itself (not a sub-list/enumerable slice of
it) never survives past the `using` block - verified for all 60 production call sites before migrating
any of them: every `IPropertyValue` implementation defensively copies its tokens into a new `TokenValue`
in its own constructor before returning, and `Token` being a value type means reading individual tokens
out of a pooled list is always safe regardless of the list's own pooling. `StylesheetComposer.
FillDeclarations`'s own scratch `Dictionary<string, IProperty>` (never read once a declaration block
finishes) got the same lazy-allocate-and-pool treatment.

**Part 3f - retype the whole value-converter pipeline from `IEnumerable<Token>` to
`IReadOnlyList<Token>`.** Originally scoped as "just widen `ValueExtensions.cs`'s ~40 signatures," which
turned out to be inseparable from the interface underneath it: `IValueConverter.Convert(IEnumerable<Token>)`
is what every one of the ~63 `ValueConverters` implementations, `StructValueConverter<T>`, and
`IdentifierValueConverter` are typed against, so widening `ValueExtensions.cs` alone produced ~90 compile
errors the moment anything tried to pass one of its now-`IReadOnlyList<Token>` methods as a delegate into
that infrastructure. Retyped the whole chain instead: `IValueConverter.Convert`, both converter
wrapper classes' stored `Func<...>` fields, all ~63 `Convert(IReadOnlyList<Token> value)` implementations,
and `TokenValue` itself (already had `Count`/an indexer/`GetEnumerator()` - just needed the
`IReadOnlyList<Token>` interface declared) so a `TokenValue.Original` can flow into any of these methods
without a copy. This removed the 7 defensive `value as Token[] ?? value.ToArray()` copies in
`ValueExtensions.cs` for real - before the interface retyping, they were structurally necessary (the
methods couldn't assume `value` was already materialized); after it, every caller in the codebase already
hands in a `List<Token>`/`TokenValue`, so the copies were pure waste.

## What was found by measuring it, not by assuming it

**The struct/slice-backed `Token.Data` design (parts 1-3b alone) was a net regression against `main`, not
a win, on a full showcase-rendering benchmark - confirmed by measurement, then diagnosed, then closed
most of the way.** Instrumented `PeachPDF.TestHarness` (temporarily; not committed) to record
`GC.GetAllocatedBytesForCurrentThread()` deltas and wall-clock time around every showcase's `GeneratePdf`
call, ran all 110 showcases 3x per state in Release. Results, allocated bytes total across the suite:

| State | vs `main` | Elapsed (`GeneratePdf` total) |
|---|---|---|
| `main` | - | ~8.8-9.3s |
| Parts 1-3b only (struct + slice-backed `Data`) | **+3.8%** | ~8.8-9.2s (wash) |
| + Part 3d (pooling) | +3.4% | ~8.4-8.6s (~5% faster) |
| + Part 3f (`IReadOnlyList<Token>` pipeline) | **+1.8%** | ~8.0-8.6s (further improved) |

Root cause of the initial regression, confirmed with temporary counters on `Token` (not committed):
`sizeof(Token) = 64 bytes` (the struct pays for the union of every former subclass's fields - `_extra`,
`_isValid`, `Quote`, `_rangeStart`, `_rangeEnd`, `_arguments` - on every token, including the
simple/common kinds that dominate real CSS token counts), and because `Token` is a value type stored
*inline* in `List<Token>`'s backing array rather than as an 8-byte reference, every list resize copies
64-byte slots instead of 8-byte pointers - GC collection counts (gen0/1/2) were within noise of `main`'s,
confirming the "fewer separate heap objects" win doesn't show up as fewer collections either, only as
fewer *distinct* allocations of a now-larger total size. `Token.DataReadCount` vs `ConstructCount`
counters ruled out "the same token's `.Data` gets read multiple times" as a contributing cause (355,991
reads against 637,269 constructed tokens - well under 1:1, matching the design's intent that most tokens
are never read as a string at all). Parts 3d/3f don't touch the struct's size - they close the gap by
removing allocation *elsewhere in the pipeline* (list backing-array churn, the 7 redundant array copies,
and - per the `IReadOnlyList<Token>` retyping - avoiding a hidden LINQ/interface-dispatch enumerator
allocation on every `Convert(...)` call, previously typed against `IEnumerable<Token>`).

**A `git checkout -- <file>` mistake, caught and fixed within the same turn.** While reverting a failed
first attempt at the `ValueExtensions.cs` retyping, `git checkout -- ValueExtensions.cs` was used
expecting it to undo only that attempt - it instead restored the file to `HEAD` (`main`'s pre-session
state, the old 9-class `Token` hierarchy), since nothing in this branch has been committed. Caught
immediately from the diff shown back afterward (an untracked-content notification surfaced the restored
file's actual content, which visibly still referenced the deleted `UnitToken`/`NumberToken`/`KeywordToken`
classes). Fixed by reconstructing the correct prior (struct-based, pre-retyping) version from this
session's own earlier-read content and writing it back directly, then verifying with a full solution
rebuild and the full test suite before continuing - not by trusting the fix without re-verifying.
No data was actually lost (nothing was committed either side of the mistake), but it's a reminder that
`git checkout --`/`git restore` on an uncommitted branch reverts to the last commit, not "one step back."

**`ValueExtensions.ToPercentOrFraction`/`Converters.PercentOrFractionConverter`/
`OptionalPercentOrFractionConverter` are dead code** - confirmed via grep that nothing in
`css-properties.json` or anywhere else in `src/PeachPDF/` actually wires `OptionalPercentOrFractionConverter`
to a real property. Diff coverage flags 2 of its lines as uncovered; left alone rather than either writing
a test for genuinely unreachable code or deleting unrelated dead code as a side effect of this change.

## Deliberately not done

**The `Token` struct's own field layout (64 bytes) was not reduced.** Shrinking it - e.g. consolidating
`_extra`/`_rangeStart`/`_rangeEnd`/`Quote` into fewer slots - was one of three options raised after the
initial regression was found; parts 3d/3f (removing allocation elsewhere in the pipeline) were pursued
first since they were already-scoped, lower-risk plan items, and closed most of the gap (+3.8% to +1.8%)
without touching the struct itself. A further struct-shrink remains a real, not-yet-attempted option if
closing the remaining ~1.8% gap (or moving past parity into a genuine win) is wanted later.

**Part 3g (the plan's originally-scoped `ExtractFor` → `DataSpan` migration) needed no code changes.**
An audit of all 51 `ValueConverters` files' `ExtractFor` implementations found zero comparisons of
`token.Data` against a literal anywhere in `ExtractFor` - every such comparison in this codebase lives in
the `Convert(...)`/parsing methods instead, a different, unscoped body of work the original plan's part
3g investigation had not distinguished from `ExtractFor` itself.

## Evidence

Full net8.0 suite: 10259 passed / 0 failed / 9 skipped (pre-existing platform-specific skips) - run
repeatedly across both the pooling and the retyping passes, no flakes observed. Diff coverage 98% (gate
is 90%) against `main`. Zero-warning `dotnet build PeachPDF.slnx -t:Rebuild` across every project, both
target frameworks - including 2 `xUnit2013` warnings newly surfaced by `TokenValue` formally implementing
`IReadOnlyList<Token>` (an existing test's `Assert.Equal(1, tokenValue.Count)` now gets flagged by the
analyzer as a collection-size anti-pattern; fixed to `Assert.Single(tokenValue)`, found and closed before
finishing rather than left for CI). Every one of the ~124 initial `CS0535`/cascade compile errors from
the interface retyping was resolved with either a bare signature-parameter-type change or a documented,
individually-checked non-mechanical fix (13 files needed more than the signature line - `TokenValue.cs`'s
interface declaration accounted for most of the remaining cascade); spot-checked the highest-risk of
these directly (`TokenValue.cs`, `GradientConverter.cs`, `StringSetValueConverter.cs`'s `Skip(1)` →
`GetRange` rewrite, `CssValueParser.cs`) against the original logic before accepting them.
