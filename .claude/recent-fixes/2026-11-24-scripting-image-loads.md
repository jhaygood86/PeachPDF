# Image and HTMLImageElement load events for scripts

**Idea.** An `img` source set by a script (watched through `ScriptDocument.Mutated`, so the property, `setAttribute` and `new Image().src` all take the same path) starts
`ScriptDocument.LoadImageAsync` (new public, returns `ScriptImageInfo`). The task joins the pending-request mechanism of the network seam: `DeliverRequestsAsync` awaits images
before the clock moves and delivers each as a zero-delay timer that sets the state and fires `load`/`error`. A per-element `Token` drops the answer of a source that was replaced.

**Trap found by running it.** `ImageLoadHandler` returned nothing for a `data:` address outside a layout (the layout paths decode `data:` before the handler); `LoadImageAsync` decodes
`data:` itself with `DataUriUtils`. Initial page images start on the first read of an image property or the first timer turn, not at parse, so a script can attach `onload` first.

**Not done.** `srcset`/`<picture>` selection, `crossOrigin`, lazy loading, drawing the image anywhere (phase 6d). The `html/semantics/embedded-content/the-img-element` WPT directory is not fetched (it needs a larger checkout), so this is covered by `ImageTests` only.
