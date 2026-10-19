# SVG-only properties are kept from a stylesheet and a keyframe

#1777. Second of the steps toward animating inline-SVG elements (#1672), after [the stop colour step](2026-10-16-svg-stylesheet-stop-colour-bare-numbers-and-css-transforms.md).

## What was done

- One `SvgOnlyProperty` class (`CSS/StyleProperties/Fill/`) registers `flood-color`, `flood-opacity`, `lighting-color`, `clip-rule`, `marker`, `marker-start/-mid/-end`, `mask`, `paint-order`, `color-interpolation-filters`,
  `transform-box`, `alignment-baseline`, `baseline-shift`, `dominant-baseline`, `inline-size`, `shape-inside`, `shape-subtract` in `PropertyFactory`. Colour, opacity, `clip-rule` and the marker references have a real converter; the rest
  use `Converters.Any`, because the SVG builder (`SvgTextShapes`, `ParseFillRule`, the paint-order reader) already validates them when it reads the value and a second grammar here would be the "two parsers" the project avoids.
- The builder's attribute-only readers (`clip-rule`, `color-interpolation-filters`, `flood-color`, `flood-opacity`, `lighting-color`) now go through `ResolveStyledAttr`, so inline style > rule > attribute. `marker-*`, `mask`,
  `paint-order` and the baseline properties already did; they only lacked the registration.
- `@supports`: `DeclarationCondition` asks the CSS-OM converter for these properties and for `stop-color`/`stop-opacity`. They are not in `css-properties.json` because nothing in `CssBox` or `SvgElement` stores them.

## Not done

- A `Converters.Any` property accepts any value in `@supports` (`paint-order: banana` is "supported"); the builder rejects it when reading.
- `transform-box` is kept but not applied (#1778). The uppercase `GRAD` angle unit and `-0px` serialisation from #1777 are not fixed here.
- `currentColor` on `flood-color`/`lighting-color` is still the host colour (#1779 follow-up).
