# Accessibility reaches other formats as flat semantic regions

`Canvas.AddSemanticRegion(bounds, SemanticInfo)` follows the `AddLink`/`AddCursor` pattern (a virtual no-op, called after painting by `HtmlDocument.PaintAccessibility`), so no backend is forced to change. Only the SVG canvas writes it.

- **Flat on purpose.** The tree has parents, but a region is one rectangle: nesting would need begin/end calls interleaved with painting, which the link and cursor passes (made after painting, from geometry) avoid. SVG's accessibility tree is read from `role` on elements, so flat is readable; a reader loses landmark nesting.
- **Unpainted and unreachable.** `fill="none" pointer-events="none"`, so a region neither draws nor takes clicks from the link or cursor rectangles under it.
- **Role names come from `RoleResolver`'s own table** (reversed), so the role a screen reader hears in SVG is the one the engine's tree and the host peers use. `image` and `none` are the second spellings of `img` and `presentation`.
- **Text is not a region**: outlined SVG text has no accessible text yet; selectable-text mode is still the way to get readable text (see the docs).
- **Not done:** Skia and Win2D keep ignoring the calls; a semantics map beside `SkiaLinkMap`/`Win2dLinkMap` is the follow-up. `aria-labelledby` and the other relations are not written.
