# Screen media takes the browsers' default type size

**Why.** Comparing arstechnica.com in the WinUI demo with Chrome, text and every `rem`-sized icon were about 8% smaller: the engine's `medium` is 11pt (a paper
default), Chrome's is 16px. Print/PDF must keep 11pt (existing output, tests), so the switch is the container's media, not a global change.

**How.** `DefaultFontResolver.Medium(screen)` / `Keyword(step, screen)` hold the two scales (screen keywords are not a fixed step: 9/10/13/16/18/24/32px).
`FontSizeResolver.Resolve` takes `screen`; `CssBox.IsScreenMedia` (container is `Continuous`) feeds it from `DerivedStyle.ActualFont` and
`ResolveFontSizeValueComputation`. The page-rule/margin-box/footnote fallbacks (`PageLengthContext?.RemPt ?? FontSize`) are deliberately untouched: they only exist
on paged media. `SvgTreeBuilder`'s root-font default likewise still reads the paper size when no HTML root font is given.

**Trap.** A test that lays out with a default-constructed `HtmlContainerInt` is Print; a rem/em expectation in browser pixels needs `MediaType = MediaType.Screen`
(`ReplacedElementConstraintTableTests` did). Tests: `ScreenDefaultFontSizeTests`. Evidence: Engine 18,027 and PeachBrackets 624 pass.
