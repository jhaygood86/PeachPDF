# Undoing a run's proxies means dropping the instances they recorded

A `CssProxyBox` is a layout-time placeholder, not what paint reads. Every proxy that lays out also
calls `HtmlContainerInt.RecordRepeatingGroupInstance`, and it is that record — a `CapturedInstance`
held by `FragmentEmitter`, keyed `(tableBox, slot)` — that becomes a fragment and gets painted.

So a pass that abandons a run's output has **two** things to undo, and removing the proxies from
`CssBox.Boxes` is only the first. The second is `ClearCapturedInstances(root)`, which
`CssLayoutEngineColumns` has always called and `CssLayoutEngineTable` did not.

**The measured symptom.** A table carrying `break-inside: avoid` that does not fit in what is left
of a page is laid out again on the next one. `RestoreStructureFromAnyPreviousRun` dropped the
abandoned run's proxies, and a test counting `DisplayMode.TableHeaderGroup` boxes in the tree
therefore passed — while the PDF drew the `<thead>` **three** times: once stranded on the page the
table had left, at the position the abandoned run gave it, and twice on top of itself at the top of
the page it moved to, where the abandoned run's page break had been.

The census that sees it counts words per fragmentainer in `FragmentTree`, not boxes in the tree.
Any assertion about how often a repeating group is drawn has to be taken from the fragment tree for
the same reason.
