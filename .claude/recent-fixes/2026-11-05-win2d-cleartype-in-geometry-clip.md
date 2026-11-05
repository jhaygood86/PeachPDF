# Win2D: ClearType text inside a rounded or path clip

Fixes #2206.

**Idea.** Direct2D keeps a geometry clip's content in a layer until the pop, and the GPU ClearType blend samples the real target, so text in such a clip was forced to grayscale. On a canvas that has an opaque `_lcdTarget` (`FromRenderTarget`) a geometry clip now draws into a `CanvasRenderTarget` of its own, copied from the current target, and `_session`/`_lcdTarget` are redirected to it (`BeginSurfaceClip`). At the pop (`EndSurfaceClip`) the parent session is put back and the surface is drawn at identity under `CreateLayer(geometry)` with `Copy` blend. The surface is a copy of the backdrop, so everything outside the shape is unchanged.

**Traps.**
- The layer is created on the *parent* session, whose transform is still the push-time one; the clip session's transform is the one that changes while the clip is open.
- `InsideGeometryLayer` needed no change: a surface clip has no `Layer`, so it no longer counts.
- Nested clips copy from the enclosing clip's surface (the current `_lcdTarget`), not from the original target.
- The surface costs a target-sized allocation and copy per clip, so it is only made when text could be ClearType (`Lcd`, or `Auto` with ClearType on). Host sessions and composers keep the layer and grayscale text.
- Counted by `Win2dResourceCounts.ClipSurfaces` (and the session by `Sessions`), part of `Total`.

**Evidence.** `GpuTextTests` compares text in a rounded clip against the same text with no clip (fails with the redirect disabled), checks pixels outside the shape are untouched, and checks nested/exclude clips left open are released. Full Win2D suite passes.
