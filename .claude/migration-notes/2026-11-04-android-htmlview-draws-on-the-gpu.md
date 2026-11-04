# Android HtmlView draws on the GPU by default

**What changed.** `PeachBrackets.Android`'s `HtmlView` now draws on an OpenGL surface (`SKGLSurfaceView`), keeping the painted page as GPU
textures while it scrolls. It used to paint every frame into a CPU `SKCanvasView`. `new HtmlView(context, useGpu: false)` gives the previous
CPU view. The Android example app takes `--ez cpu true` to start in that mode.

**What a document author would notice.** Scrolling over a page that has been painted is a texture draw rather than a repaint. The first time
a scroll reaches an area it is painted as a 1024 by 1024 pixel tile, which can take longer than a CPU frame on a device with a slow GL driver.
A device whose OpenGL cannot create a surface should construct the view with `useGpu: false`.

**Verified against** `git show v0.9.26-preview1:src/engine/PeachBrackets.Android/HtmlView.cs`, which hosts an `SKCanvasView`.
