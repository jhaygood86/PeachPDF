# `text-indent`'s `hanging`/`each-line` keywords, and a latent RTL indent bug found along the way

Issue [#607](https://github.com/jhaygood86/PeachPDF/issues/607). `text-indent`'s full grammar
(`<length-percentage> && hanging? && each-line?`, CSS Text 3 §3) was only ever the bare
length-percentage. `hanging` inverts which lines get the indent (every line except the first,
instead of only the first); `each-line` additionally indents the line after every forced break.

**Grammar shared across layers, not duplicated.** `TextIndentGrammar.TryParse` (new,
`CSS/TextIndentGrammar.cs`) tokenizes and classifies once; both the CSS-OM converter
(`TextIndentValueConverter`) and the layout-time resolver (`DerivedStyle.EnsureTextIndentResolved`)
call it. This mirrors `AspectRatioGrammar`, not the generic `WithAnyOrderIndependent` combinator
used for shorthands like `columns`/`list-style` — a hand-rolled token scan was necessary here because
`calc(...)` already tokenizes as a single opaque `FunctionToken` (confirmed via
`FunctionValueConverter`), and a naive whitespace-split (the pattern `ParseTransformOrigin` uses)
would incorrectly break `calc(1em + 2px) hanging` apart at its internal space. The length token
itself is validated by delegating to the existing `Converters.LengthOrPercentConverter` rather than
re-deriving what counts as a length/percentage/calc().

**`css-properties.json`'s `cssDataType` had to move from `"length"` to `"cssom"`.** The generated
HTML-binding validator for `"length"` calls `CssValueParser.IsValidLength`, which would reject the
compound string (`"40pt hanging"`) outright before it ever reached `CssBox.TextIndent` — independent
of whatever the CSS-OM converter itself now accepts. `"cssom"` re-runs the value through the real
`TextIndentProperty` converter instead (same pattern as `transform-origin`/`aspect-ratio`).

**A real, pre-existing bug the compound grammar exposed: `NoEms` silently stopped converting em
units.** `CssBox.StyleProperties.cs`'s `TextIndent` setter eagerly converts `em` to `pt` at cascade
time (so inheritance resolves against the *declaring* element's font-size, not a descendant's) via
`NoEms`, whose `CssLength` constructor reads only the **last 2 characters** of the whole string as
the unit. For `"3em hanging"` that reads `"ng"`, matches nothing, and `NoEms` quietly no-ops instead
of converting — meaning `text-indent: 3em hanging` on a box, inherited by a child with a different
font-size, would have resolved the em against the *wrong* element. Fixed with a `NoEmsTextIndent`
helper that isolates the length token via `TextIndentGrammar` before calling the existing `NoEms`,
then reattaches the keywords.

**Where the indent gets applied, in one formula.** `CssLayoutEngine.GetLineTextIndent(blockBox,
isFirstLine, followsForcedBreak)`: `selected = isFirstLine || (eachLine && followsForcedBreak);
indented = hanging ? !selected : selected`. Verified by hand against all four keyword combinations
and against MDN's description of each. `followsForcedBreak` needed a new signal that didn't exist
anywhere: `CssLineBox.FollowsForcedBreak`, set `true` only at the *one* other `new CssLineBox(...)`
call site in `CssLayoutEngine.FlowBox` (the wrap loop), from `word.IsLineBreak` — the synthetic
`"\n"` marker word that represents both `<br>` and a preserved `white-space: pre`/`pre-wrap`/
`pre-line` newline. A natural/no-wrap-box wrap never sets it, so `each-line` correctly excludes
soft-wrapped continuation lines.

**A latent, direction-independent-in-cause but RTL-visible bug: indent was always physical-left,
never direction-aware.** Not new to this change — the base (no-keyword) case had it too — but working
through `each-line`'s mechanics surfaced it. The fix went through two shapes before landing:

*First attempt (superseded): inset only the post-flow alignment target.* `ApplyRightAlignment` runs
for *every* plain RTL paragraph (RTL's default `text-align: start` resolves to `right`), and it runs
**before** `ApplyBidiReordering` — alignment operates on words in logical/DOM order; mirroring only
reflects positions within the span alignment already established, and never moves that span's own
edges. The old code always flushed the *logically-last* word to `ActualRight - padding - border`,
leaving any indent-reserved space on the logically-first word's side (physical left, pre-mirror);
mirroring then relocates the reading-order-first character to the physical right — flush, no gap —
stranding the reserved space next to the reading-order-*last* character instead. Insetting
`ApplyRightAlignment`'s/`ApplyJustifyAlignment`'s own flush target by the indent while leaving the
flow-time `CurrentX` offset untouched fixed the common case, but broke down whenever a line's natural
slack was *smaller* than the indent: `diff` went negative, and `ApplyRightAlignment`'s `if (!(diff >
0)) return;` guard left the line exactly where flow put it — un-aligned, indent invisible.

*What actually shipped: reserve the indent on the wrap boundary itself, for RTL.* `FlowBox` now
narrows `actualLimitRight` by the line's indent when the block is RTL (`isRtl` handling alongside the
existing per-word wrap-overflow check), instead of leaving the flow-time offset unconditionally
direction-independent. `CreateLineBoxes`'/`FlowBox`'s `CurrentX` addition is now gated `!isRtl` to
avoid double-reserving. This makes the indent a property of *how much content wrapping ever allows on
the line*, not something a later alignment step has to reproduce or fail to — so `ApplyRightAlignment`
and `ApplyJustifyAlignment` only need their flush target inset to land the reservation on the correct
(already-narrower) side; they no longer have to manufacture the reservation themselves. Verified this
generalizes correctly to a combination that was expected to still need `.claude/accepted-gaps/`
tracking: `direction: rtl; text-align: left` (explicitly forced against the writing direction, so it
bypasses both fixed alignment functions entirely) already indents correctly from the physical right,
because the wrap-boundary narrowing happens during flow, before any alignment step runs at all.

**A second bug the first review pass caught: `each-line` lost the indent across a fragmentation
resume.** The line-in-progress when a fragmentainer break is taken is discarded (a line box is
monolithic, css-break-3 §4.1 — the *whole* line moves to the next fragmentainer) and rebuilt from
scratch as a fresh seed line on the resumed pass. That rebuild hard-coded `followsForcedBreak: false`
for the new seed line, so a `<br>`-preceded line that happened to also fall right at a page boundary
silently stopped being indented under `each-line` — verse/poetry content, exactly what `each-line`
exists for, is exactly the content likely to span pages. Fixed by carrying
`CssLineBox.FollowsForcedBreak` of the discarded line forward on `InlineBreakToken` (a new
`FollowsForcedBreak` field, read back when `CreateLineBoxes` builds the resumed seed line) rather than
assuming it's always false. The sibling `hanging` case didn't have this bug — `isFirstLine: resume is
null` already correctly marks a resumed seed line as "not the first line" (so `hanging` still indents
it), it was specifically the forced-break signal that got dropped.

**Deliberately not fixed, tracked instead:** `ApplyCenterAlignment` splits any indent-driven slack
evenly across both sides rather than reserving it start-side (a different bug shape from the RTL one
above — "how much", not "which side"). See
`.claude/accepted-gaps/text-indent-center-alignment-splits-indent-slack.md` and issue
[#623](https://github.com/jhaygood86/PeachPDF/issues/623) (originally filed covering both this and the
`text-align: left` RTL combination above; narrowed once the latter turned out already fixed).

**Verified:** rendered a showcase (`text_indent` in `PeachPDF.TestHarness/Program.cs`) covering plain/
`hanging`/`each-line`/`hanging each-line`, plus an RTL default-alignment and RTL-justify case, and
rasterized with both MuPDF and PDFium — both agree, and the indent lands on the visually-correct side
in every case. A width sweep (100–200pt) confirmed the RTL line's right edge lands exactly on
`ClientRight - indent` at every width, including the narrow ones where the superseded flush-target-only
approach broke down. `dotnet test PeachPDF.Tests --framework net8.0`: full suite green (see the PR for
the final pass/fail count and diff-coverage percentage — both were re-checked after the fragmentation
fix above landed). `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
