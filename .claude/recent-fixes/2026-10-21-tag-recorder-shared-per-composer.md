# One tag recorder per composer

A declarative document with several `Page` handlers calls `RenderPagesCore` once per handler, and each call made its own `StructureTagBuilder`
around a fresh `PdfTagRecorder` (`PdfComposer.CreateTagRecorder` returned a new one every time). Two recorders on one document broke the
tagged structure in two ways, found while carrying a stamped file's tags across (a layer on both pages showed one set of elements):

- the second recorder's constructor called `StructureTreeRoot.SetRootKid` with its own `Document` element, replacing the first, so the first
  handler's whole structure disappeared from the tree;
- each recorder numbered its pages' `/StructParents` keys from 0, so the second handler's pages reused the first's parent-tree keys.

Fix: `CreateTagRecorder` returns the one recorder of the composer. The recorder takes page keys from the structure tree root's running
`ParentTreeNextKey` (the counter annotation keys already shared) instead of `Count`, and `Finish` writes only the pages recorded since the last
`Finish` and then clears them, so it can be called after each handler. For a single call this is the same numbering as before (pages 0..n-1,
annotation keys from n).

Trap: `Finish` is also where annotation keys begin, so a link made between two handlers' `Finish` calls takes a key that the next handler's
pages must not reuse - which the shared counter now guarantees.

Evidence: `TaggedPdfStructureTreeTests.DeclarativeDocument_WithSeveralPageHandlers_SharesOneStructureTree` fails without the change (verified by
reverting it) and passes with it; the existing tagged-PDF suites are unchanged.
