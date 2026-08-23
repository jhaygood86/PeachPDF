# Vertical rtl inline content anchors to final bottom edge

_Landed 2026-08-23._

[Issue #797](https://github.com/jhaygood86/PeachPDF/issues/797): a `direction: rtl` vertical box
(`vertical-rl`/`vertical-lr`) with inline-only content positioned every word far outside its own real
bottom edge. Reproduced with plain, unstyled text — no `text-align`, bidi, hyphenation, floats, or
positioning involved. Pre-existing, from the earlier #761/#778-era `CreateVerticalLineBoxes` work;
surfaced while building and testing #768.

**Root cause was a guard bug, not a missing feature.** `CreateVerticalLineBoxes` places words using a
`WritingModeFrame` built against a provisional bottom edge (an auto-height box's own documented
page-height wrap-limit fallback, or a definite height's `DefiniteContentHeight`). Under
`direction: rtl`, `WritingModeFrame.InlineStartIsBottom` is true, so every word's physical `Top` is
anchored against whatever `clientBottom` the frame was built with. The method's own finalize pass
(`ApplyVerticalTextAlignment` → `ApplyVerticalFlushAlignment`, added by #768) is supposed to re-anchor
words to the box's real final edge — but `ApplyVerticalFlushAlignment`'s guard
(`if (toBottom ? !(diff > 0) : !(diff < 0)) return;`) silently discarded the correction whenever
`diff` (the shift needed to flush the column) came out negative for the `toBottom: true` branch. That
branch is exactly what CSS-initial `text-align: start` resolves to under vertical+RTL, which is why
plain, unstyled text reproduced the bug 100% of the time — `text-align: left`/`end`'s `toBottom: false`
branch happened to apply the same-sign correction by coincidence of guard direction, masking the defect
for that half of the alignment matrix. Fixed by replacing the directional guard with an unconditional
near-zero-only skip (`if (!(Math.Abs(diff) > 0)) return;`), matching the sibling
`ApplyVerticalCenterAlignment`'s own (already-correct) guard shape — `diff` is, by construction, exactly
the needed shift; there's no direction in which a nonzero value should be discarded.

**A second, latent gap needed the #778 precedent to close properly.** Even with the guard fixed, the
locally-computed "final" bottom edge `CreateVerticalLineBoxes` uses for its finalize pass is not
actually final: `min-height`/`max-height` clamping (`CssLayoutEngine.ApplyHeight`/`GetBoxHeight`) only
runs later, in `CssBox.PerformLayoutEpilogue`, and — unlike this method's own local height math —
applies regardless of whether the box's own `height` is auto or an explicit length. A post-change review
agent proved this live (a `height: 50pt; min-height: 400pt` RTL vertical box with plain text still
landed words flush around 70pt instead of the real ~420pt bottom) after the guard fix alone had already
passed the literal issue repro. This is the same class of bug #778 hit for this box's own block-level
children, whose fix (`CssBox._pendingCrossAxisRtlReflection`, consumed from `PerformLayoutEpilogue`
right after `ApplyHeight`) is the direct precedent reused here: the finalize loop
(`ApplyVerticalTextAlignment`/`ApplyVerticalBidiReordering`/`BubbleRectangles`/`AssignRectanglesToBoxes`)
was extracted into a new `internal static CssLayoutEngine.FinalizeVerticalLineBoxes`, and for every
`direction: rtl` vertical box (not just auto-height ones — deferring is a no-op whenever not strictly
needed, so there's no reason to gate it more narrowly), `CreateVerticalLineBoxes` now sets a new
`CssBox._pendingVerticalInlineFinalize` flag instead of finalizing immediately.
`PerformLayoutEpilogue` consumes it right after `CssLayoutEngine.ApplyHeight`, calling
`FinalizeVerticalLineBoxes` with `WritingModeFrame.For(this)` and this box's own now-live
`ClientTop`/`ClientBottom`. Unlike the block-children case, a plain `bool` flag is enough — the epilogue
runs on the very box whose `LineBoxes`/`Client*`/`WritingMode`/`Direction` are already live instance
state, so nothing needs to be captured ahead of time. No new `OffsetContentTop`-style geometry-shifting
primitive was needed either: the finalize pass already recomputes word positions from scratch
(`GetColumnContentExtent` re-scans current words, computes a fresh `diff`, writes `word.Top`) rather
than shifting by a delta, so `blockBox.Location` is guaranteed untouched by construction — deferring
just means calling that same recomputation later, with the true edges.

LTR vertical is unaffected either way (its natural placement never depends on `clientBottom`), and
`LayoutOutOfFlowDescendants` stays unconditional and unmoved relative to the branch, since it resolves
only against `ClientLeft`/`ClientTop`/`ClientRight` (already final) and never reads line-box/word
alignment results. New tests in `VerticalTextAlignIntegrationTests.cs` (the literal repro, a
`vertical-lr` variant, explicit `text-align: right`, and a `text-align: left` regression guard) and
`VerticalWritingModeLayoutIntegrationTests.cs` (`min-height` growing an auto-height box, and `min-height`
overriding a smaller explicit `height` — the case the review agent's live repro caught) pin both fixes.
Full net8.0 suite (9118 passing, zero regressions), 100% diff coverage on the changed lines, and a
zero-warning `dotnet build -t:Rebuild` all pass.
