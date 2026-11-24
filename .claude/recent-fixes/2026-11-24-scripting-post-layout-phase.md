# Scripts are called after layout (Prince post-layout phase)

Phase 1 of the Prince-parity plan. A script host can now be told the document was laid out, change it again, and have it laid out again.

**Load-bearing ideas:**
- The seam is additive: `IScriptHost` gained default methods `LayoutCompletedAsync` (returns true to ask for another pass), `LayoutFinishedAsync`
  (the final layout; a change is not laid out) and `DocumentClosed`. `ScriptDocument.KeepAlive` (set by the host from `RunAsync`) says the
  document must stay live, and `ScriptDocument.HostState` is where the Jint host keeps its runtime. No `IScriptSession` type was needed.
- The **script tree stays the live DOM**; the laid-out box tree is a separate parse of its serialization (as it already was). So JS object identity
  survives a relayout, and a pass is: host runs, `Version` moved, `AdoptScriptedSource(Serialize())`, the existing private `ReloadAsync`
  (reparse, restyle, `LayOutAsync`). `_scriptedSource` equal to the new source is what stops the scripts from re-running on that reparse.
- The loop is in `HtmlDocument.LayoutAsync` (`RunScriptLayoutPassesAsync`), so every caller of the first layout gets it, not only PeachPDF.
  `MaxLayoutPasses` (default 16, 0 = unlimited) bounds it; the last allowed pass's change is not laid out.
- A document is kept live **only** when scripts registered a post-layout function or a `complete` listener (`ScriptRuntime.WantsLayoutCallbacks`);
  everything else releases the parse tree and engine as before, so ordinary scripted pages pay nothing.
- Post-layout functions are one-shot per registration (Prince: a function that wants to repeat registers itself again); pending registrations are a
  reason for another pass even when nothing changed, which is what makes a self-registering function run to the limit.

**Trap found by running it:** a single tall *empty* block is not split across pages (800pt of empty div in a 200pt page is 2 pages), so a test that
wants more pages needs several in-flow blocks, not one tall one.

**Not done / measured:** `Prince.trackBoxes` and `getPrinceBoxes` (phase 2), `PDF` and `failStatus` (phase 4). Full scripted reftest run showed css-text 983 and
css-multicol 186 once (floors 984/187) but both modules run alone give 984/187 twice, so it was load flakiness of the whole-suite run, not this change.
