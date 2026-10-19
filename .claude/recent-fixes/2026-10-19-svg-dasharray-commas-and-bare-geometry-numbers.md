# Comma-separated stroke-dasharray and bare-number geometry in a stylesheet

#1800. `StrokeDasharrayConverter` is now `(<length-percentage>|<number>)+` per comma item (`.Many().FromList()`), matching CSS Fill and Stroke 3's `<length-percentage>+#`; before, `2px, 4px` and `2,4` were dropped and computed to an empty array. `SvgOnlyProperty.GeometryLength` also takes a bare `<number>` (`r: 10`).

## Decided
Bare numbers are not `<length-percentage>` in the specs; they are accepted as the attribute's user-unit form because Chrome does and a keyframe `r: 10` otherwise falls back to the attribute.

## Found while checking
#1780 and #1777 were already implemented by #1789/#1790 (SvgOnlyProperty registration, styled lookup); only the bare-number grammar remained. SVG 2 makes width/height CSS properties only on rect/image/foreignObject/outermost svg; `BuildNestedSvg`/`BuildUse` still read them from CSS (not changed here).

An upper-case angle unit (`rotate(100GRAD)`) is also accepted now, as CSS Values 4 makes unit names ASCII case-insensitive.
