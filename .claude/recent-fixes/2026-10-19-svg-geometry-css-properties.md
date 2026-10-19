# SVG geometry attributes are CSS properties too

#1780. Third step toward CSS animation of inline-SVG elements (#1672); builds on [SVG-only properties from stylesheets](2026-10-19-svg-only-properties-from-stylesheets.md).

## What was done

- `r`, `cx`, `cy`, `rx`, `ry`, `x`, `y` are registered in the CSS-OM (`SvgOnlyProperty.GeometryLength`: length-percentage or `auto`); `d` takes `Converters.Any`. `width`/`height` were already kept (they are HTML properties).
- `BuildPath`, `BuildCircle`, `BuildRect`, `BuildEllipse`, `BuildUse`, `BuildNestedSvg`, `BuildImage` and `BuildForeignObject` read these through `ResolveStyledAttr` (inline style > rule > attribute). `d: path("...")` is unwrapped
  by `ResolveStyledPathData`; `d: none` draws nothing; a bare attribute string still goes through unchanged.

## What was decided

`x`/`y`/`width`/`height` are read from CSS only in those builders. The reads for `mask`, `pattern`, `filter` and `text` are unchanged: SVG 2 makes them properties only on the elements above, and `text` takes lists.
A rule such as `* { width: 10px }` now reaches shapes, as in a browser.

## Not done

`d: path(evenodd, "...")`'s fill-rule is ignored (the data is read, the rule is not). Animating `r` (the point of this step) needs the builder-side sampling tracked under #1672.
