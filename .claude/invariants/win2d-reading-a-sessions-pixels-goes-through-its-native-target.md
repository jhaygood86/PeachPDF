# Win2D: reading a session's own pixels is possible, but the details are exact

`Win2dNativeTarget` reads back what a `CanvasDrawingSession` has drawn, so LCD text can blend against it over a control's surface that Win2D
itself offers no way to read. It was found by trying it, and each step has a trap that cost real debugging time.

- **Win2D answers `ICanvasResourceWrapperNative` on a session** (`GetNativeResource` is slot 3, IID `5F10688D-EA55-4D55-A3B0-4DDB55C0C20A`); ask it
  for `ID2D1DeviceContext`. The target is `GetTarget` (slot 75), an `ID2D1Bitmap1`; a command-list session has none, and that is how it is told.
  `ICanvasFactoryNative::GetOrCreate` on that bitmap returns S_OK with a **null** wrapper, so the bitmap is copied with Direct2D, not wrapped.
  `RoGetActivationFactory` does not find the Windows App SDK build of Win2D either; not needed, so do not go that way again.
- **Flush first.** A read before `session.Flush()` returns zeros. Drawing inside a pushed clip, or a path clip, is visible once flushed, so clips do
  not stop a read (measured on a software and a hardware device).
- **`CanvasVirtualControl` has no `AlphaMode`; its surface is alpha-less only while `ClearColor` is opaque.** `HtmlView` showed no LCD fringes until it
  set `ClearColor`: the reader opened fine but the target's alpha mode was premultiplied, so blending was refused.
- **The target is a shared atlas.** The region's origin is the translation of the context's transform with the session transform set aside, in DIPs:
  pixels = offset x dpi / 96 (verified at 100, 150 and 200 percent). Reads must stay inside the atlas; drawing outside the region is clipped by Win2D.
- **Slots, not names.** The calls are fixed Direct2D 1.1 vtable slots (`CreateBitmap` 57, `GetTransform` 31, bitmap `CopyFromBitmap` 8, `Map` 14,
  `Unmap` 15, `GetPixelFormat` 6, `GetPixelSize` 5). `GetPixelSize`/`GetPixelFormat` return their struct through a hidden pointer; `GetTarget` returns
  void. A wrong signature crashed the process with 0xC0000409 rather than failing.
- **Cost.** About 0.03 to 0.04 ms per small rectangle with drawing pending, on a GPU. Read the rectangle behind one run, not the surface.
- **Ownership.** One reference each to the context, target and staging bitmap, released by the canvas before its session ends and counted in
  `Win2dResourceCounts.NativeTargets`.
