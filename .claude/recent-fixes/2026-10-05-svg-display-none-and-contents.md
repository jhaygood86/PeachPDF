# `display: none` and `display: contents` on SVG elements (#1295)

The SVG tree builder never read `display` at all: `none` was the larger gap the issue called out, `contents` the one it asked for. The gate is one check at the top of `SvgTreeBuilder.BuildElement` (`IsNotRendered`), so a hidden element is skipped wherever it is reached from: the document walk, a `<use>` instance (which builds its target through `BuildElement`), pattern, marker and mask content.

`contents` is not a second build path. `ApplyCommon` already runs for every element and returns the paint its children inherit, so for a `display: contents` node it just resets the five things a box would have done (opacity, transform, clip-path, mask, filter) after computing that paint, which is exactly what CSS Display 3 Appendix B describes: no box, still in the tree for inheritance. `use` drops its `x`/`y`/size, `tspan`/`textPath` ignore their position and rotation lists (and `textPath` its path); both are read in the builder before `ApplyCommon`. Elements that are not in `CanBeDisplayContents` compute to `none`.

Two traps:
- A `<switch>` picks its first candidate whatever its `display`, so a `display: none` first child must end the switch rather than fall through to the next candidate (`BuildSwitch`).
- A `<tspan>` with `display: none` is skipped before the whitespace-collapsing state is touched, so the spaces around it collapse as if it were absent.

Verification: `SvgDisplayTests` (the tree, the recorded draw calls and transform pushes), plus a rendered check through PDFium and MuPDF of the inline-SVG cascade path (a class rule `display: none`/`contents`), since the unit tests build the standalone XML path where a `<style>` rule does not reach.
