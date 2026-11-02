# Forced colors recolours the page, and the system colour keywords are colours

**Before:** a host that set `UserPreferences.ForcedColors` only made `@media (forced-colors: active)` match; the page kept its own colours. `Canvas`, `CanvasText`, `LinkText` and the other CSS system colour keywords were not colours, so a declaration using one was dropped.

**Now:** with `ForcedColors` on, text, backgrounds, borders, outlines and column rules are drawn in `UserPreferences.ForcedPalette` (a new `SystemColorPalette`, light by default) and shadows are not drawn; `forced-color-adjust: none` opts an element out. The system colour keywords are valid colours (the light palette's values). `HtmlDocumentOptions.FontScale` is new and defaults to 1. A document rendered without the preference, as every PDF is, is unchanged apart from a declaration that names a system colour now taking effect.

**Why:** a screen host (WinUI, Android) that follows the system's high contrast setting needs the page to be readable in it, not only to be told that the setting is on.
