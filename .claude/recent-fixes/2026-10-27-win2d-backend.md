# PeachDrawing.Win2D: a Win2D backend whose resources are all counted

A fourth `Canvas`/`RenderContext` backend (`src/PeachDrawing.Win2D/`), Windows-only (`net10.0-windows10.0.19041.0`, net11 under the .NET 11
SDK), over Win2D 1.4 for the Windows App SDK. Read `docs/peachdrawing-win2d.md`'s lifecycle table before touching it.

## The load-bearing idea

Win2D objects are native handles, so every one has exactly one owner and a deterministic disposer, and `Win2dResourceCounts` (internal)
counts the long-lived ones (clip layers, tile/layer canvases, command lists, sessions the backend opened) so `LifecycleTests` can assert
zero after any ending - normal, early dispose, exception mid-draw, `SuspendClipping`/`ResumeClipping`, a layer never finished, a hatch
brush's tile. A borrowed session (`Win2dCanvas.FromSession`) is never closed and gets its transform/blend/anti-aliasing back; a borrowed
device outlives its context.

## Found by running it, not by reading

- `new LayerOptions()` is an all-zero struct, i.e. **opacity 0**: a test drew a layer that vanished. Say `new LayerOptions(Opacity: 1)`.
- Conic gradients as 360 anti-aliased wedges left an alpha of ~0.7 at the centre (each wedge covers a fraction of the pixel and
  source-over compositing of independent fractions never adds up to 1). The wedges are drawn **aliased** inside the shape's clip.
- Win2D has no `CanvasBlend.Max` and no `CanvasDashStyle.Custom`; a custom dash is just `CustomDashStyle`, and dashes are in units of the
  **stroke width**, so lengths are divided by it. `Lighten` falls back to Normal.
- A DirectWrite font file added by a document is named to Win2D as `file:///path.ttf#<family declared inside the file>` - not the family
  name the document gave it; with the wrong name DirectWrite silently falls back to the default font (measured: identical widths).
- `Microsoft.Graphics.Win2D` alone is enough to compile and run headless on a software `CanvasDevice(true)`; it warns `WIN2D0001` for an
  AnyCPU library unless `Win2DWarnNoPlatform` is `false` (the default `true` is what turns the warning on).
- A command list can only be drawn after the session recording into it is closed; `Win2dTileImage.Content` closes the tile canvas first.

## Deliberately not done

Blend modes needing the backdrop, two-circle radial gradients, DirectWrite outlines: see `.claude/accepted-gaps/win2d-*.md`. No API
reference page and no Linux build: the package builds on Windows only, so `publish.yml` packs it in a separate `windows-latest` job and
`Build-ApiReference.ps1` (Linux, forces `net10.0`) leaves it out.

## Evidence

76 tests in `PeachDrawing.Win2D.Tests` (pixel read-back from a WARP device, lifecycle, links, both text engines, public-API snapshot) and
the example's eight scenes viewed as PNGs.
