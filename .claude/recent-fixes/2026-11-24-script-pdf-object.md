# The PDF script object, viewer options and Prince.failStatus

Phase 4 of the Prince-parity plan. PeachPDF registers `PDF` and a `Prince` extension through `ScriptApi` (phase 1b) when a script host is set, so
PeachPDF still does not reference Jint.

**Load-bearing ideas:**
- The missing PDF options came first, as real options: `PdfComposerOptions.PageMode/PageLayout/OpenAction/Viewer` (PeachDrawing.PDF), mirrored by
  `PeachPDF.PdfPageMode/PdfPageLayout/PdfOpenAction/PdfViewerOptions` and mapped in `EngineOptions`; `PdfGenerateConfig.PageMode/PageLayout/
  OpenAction/ViewerOptions`. The engine already had `Catalog.PageMode/PageLayout/ViewerPreferences`; nothing in the composer set them. The mirrors
  are pinned in `EngineOptionsParityTests`. The open action is written in `PrepareToSave` (it points at page 1), not in `Configure`.
- `Print` as an open action is `<< /S /Named /N /Print >>`, an action that runs, so `Configure` refuses it with PDF/A or PDF/X.
- Precedence is in `PdfGenerator.BuildComposerOptions`: for information a value in `PdfGenerateConfig.Metadata` beats the script, which beats
  the HTML; for page mode, layout and open action a configured value beats the script; for encryption the script can add it only when none is
  configured; `compress` and `xmpMetadata` take the script's value because the config's bools have no "unset".
- `Prince.failStatus` is a `ScriptApi("Prince")` whose members are added to the **existing** Prince global (a new rule in `InstallApi`: an API named
  like an existing global extends it). It throws `PdfFailSafeException` (an `InvalidOperationException`, so the CLI exits 1) from `RenderPagesCore`
  before anything is configured or written.
- `PDF.pages` is read lazily from the layout the PDF object saw on its last `LayoutCompleted` (not `LayoutFinished`, which comes after the `complete`
  event a script reads it in).
- `attachFile` is resolved in `RenderPagesCore` (async), not when the script calls it, through `NetworkLoader.GetResourceStream` against the loader's
  `BaseUri`.

**Found by running it:** a long UTF-16 hex string is wrapped over lines in the written file, so tests join the digits before comparing.

**Not done:** see [the accepted gap](../accepted-gaps/script-pdf-object-members-that-need-the-writer-or-the-profile-first.md). The CLI has no flags for
the new options.
