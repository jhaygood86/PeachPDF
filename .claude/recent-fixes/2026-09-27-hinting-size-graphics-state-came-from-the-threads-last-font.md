# A size's starting graphics state came from the thread's last font (flaky macOS "hinting returned unfitted")

**Symptom.** On macOS CI only (three-core arm64 runners, so a thread is reused far more often than on the Windows and Linux jobs), on
`push` runs of main from d05dacd0 to be003c94 and once on a PR: `HintingGaspTests` (all ten of its hinted cases, "glyph 1 at 9 ppem
(gasp 1) is not fitted"), `VariableCvarTests.TheHintedOutline_*` (`Assert.True(outline.IsGridFitted)`) and, probably the same cause and not reproduced, once
`TextHintingPdfTests.TextThatTheRasterBackendDrawsIsHintedButTheVectorTextAroundItIsNot` (hinted and unhinted PDFs equal). One of the
three test hosts (net8/net10/net11, run concurrently by one `dotnet test`) failed, never all of them, and a rerun passed.

**Cause.** `TtExecContext` is `[ThreadStatic]` and reused from font to font. `TtSize.InitBytecode`/`RunPrep` set `GraphicsState = Default`
and then `exec.SaveContext(ref GraphicsState)` copies `InstructControl`, `ScanControl` and the like *out of the context's own `GS`*. The
context's `GS` is only assigned by `RunContext`, which runs only if the font has an `fpgm`/`prep`. FreeType's `TT_Load_Context` starts
the context from the size's state (`exec->GS = size->GS`); the port did not. So for a font with neither program (`HintingGasp.ttf`,
`VariableCvarTest.ttf`, many real subset fonts) the size took whatever the thread's previous font left: after
`HintingEngineTests.AFontCanSwitchHintingOffInItsCvtProgram...` (a `prep` of INSTCTRL selector 1), `HintingDisabled` was true for the next
prep-less font on that thread, so its glyphs came out scaled and `IsGridFitted` false, and the engine cached that answer for the size.
Which test failed depended on which thread pool thread ran the INSTCTRL test before it.

**Fix.** `exec.GS = GraphicsState` after the default is set, in `InitBytecode` and `RunPrep` (`TtSize.cs`). Two tests in `HintingEngineTests` fail
without it, deterministically, on one thread (a font whose prep disables hinting, then a font with no programs).

**Not the cause (measured).** The Adler-32 + length font identity (replaced in the SHA-256 change) *could* collide: across the ~12,600
distinct fonts the engine tests create, three pairs of hostile CFF mutants shared an old key (five in the 131,128-byte SourceCodePro
family), and the CFF2 variant fixtures collide with each other (variants 1/4 and 2/3 of `HintingCff2Variants.golden.json.gz`). None of
the TrueType fixtures involved in the failures did, and the failures kept happening up to be003c94 (which is before the SHA-256 change
was merged), so it is a real but separate defect. The CFF2 robustness sweep failing at 120 s+ on macOS is the whole-sweep wall-clock
assertion that the per-case bound replaced.

**Why the flake stopped by itself.** The bug was never gone: the runs after 81a6057a simply ordered tests onto threads differently. It
is exactly the kind of failure that comes back with a new test that switches hinting off in a `prep`.

**Follow-up worth knowing.** Anything else a thread-static context or pool keeps between fonts is a candidate for the same class
(`Cf2Pool` states that `Cf2HintMap.Init` sets what a map reads; audited by reading only). A new size/loader path should reset the context fields it
depends on rather than rely on the previous font having been benign.
