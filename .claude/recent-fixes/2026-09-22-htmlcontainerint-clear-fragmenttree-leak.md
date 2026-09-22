# HtmlContainerInt.Clear() left three CssBox-keyed fields un-reset

`Clear()` disposes `Root` and resets a list of per-document tracking fields so a container can be
reused across `SetHtml` calls (the pattern `PerformLayout`'s own container-query refinement loop
relies on) without retaining the previous document's box tree. Three fields referencing that same
tree were missing from the reset list, each independently keeping the whole disposed tree reachable
until the next document happened to overwrite it:

- `FragmentTree` — built once at the end of layout; its `BoxFragment`s reference the same `CssBox`
  tree `Root` does.
- `_idIndex`/`_idIndexRoot` — the lazily-built id-lookup index `GetBoxById` maintains. Plausibly the
  largest of the three leaks, since any document with ids anywhere builds one.
- `CanvasBackgroundBox` — resolved by `ResolveCanvasBackground()` during paint.

Found by a WeakReference-based test: lay out a document, `Clear()`, force a GC, assert the root box
is collected. The test could not pass on `FragmentTree` alone — fixing only the field that prompted
this investigation still leaves `_idIndex`/`CanvasBackgroundBox` holding the tree, so all three had
to be found and fixed together. Found the other two by grepping `HtmlContainerInt.cs` for `CssBox`-
typed field declarations near `Clear()`'s existing reset list, the same technique that would catch a
fourth if one gets added later without updating `Clear()`.

All three are read-only-after-fresh-layout (never read in the window between `Clear()` and the next
completed `PerformLayout`), so nulling them in `Clear()` carries no NRE risk — confirmed by tracing
every read site of each field.

**Trap for a WeakReference leak test in this codebase**: in a Debug build, the JIT keeps a local
variable rooted for the rest of its enclosing method's stack frame regardless of reassigning it to
null, so a `WeakReference` test that lays out, clears, and asserts collection all in one test method
can report a false leak. Isolate the layout+clear logic into a separate, non-inlined private static
method that returns only the `WeakReference`, matching the existing pattern in
`FontFactoryCrossInstanceIsolationTests.GeneratorInstance_IsCollectible_AfterUse`.

No other `CssBox`-keyed `HtmlContainerInt` field was found missing from `Clear()` —
`_widowsRewind`/`_runPullRewind`/`_passesRewoundFor` are self-healing transient state scoped to a
single `LayoutDocument` pass and don't need document-lifetime reset.
