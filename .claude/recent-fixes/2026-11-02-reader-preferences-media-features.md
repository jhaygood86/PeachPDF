# Reader preferences as media features, and WCAG text spacing

First step of the accessibility work: let a host tell style sheets about the reader's settings, and let it ask for more text spacing.

- `UserPreferences` (`HtmlDocumentOptions.Preferences`) feeds `MediaQueryContext.Preferences`; `MediaQueryMatcher` reads it for `prefers-reduced-motion`, `prefers-contrast`, `forced-colors`, `inverted-colors`, `prefers-reduced-transparency` and `prefers-reduced-data`. The default instance **is** the old hard-wired stance (reduced motion on, the rest at their resting values), which is what keeps every PDF byte-identical; do not change a default without a migration note.
- `inverted-colors` and `prefers-reduced-data` were not registered features at all (`FeatureNames`, `MediaFeatureFactory`, one class each under `CSS/MediaFeatures/`), so the whole query was invalid and the block skipped. A test that only checks a default for a feature that was never registered passes for the wrong reason: the cursor-per-query trick in `UserPreferencesTests` asserts both the matching and the non-matching branch, which is what caught it.
- `forced-colors: active` only **reports** the mode. Recolouring the page with a system palette (system colours at the cascade, `forced-color-adjust`) is a separate, larger change and is not done here.
- `TextSpacingOverrides` (`HtmlDocumentOptions.TextSpacing`) is generated CSS with `!important` appended to the user style sheet, so it needs no cascade change. It is ignored when a pre-parsed `StyleSheet` is set (that path replaces `UserStyleSheet`).
- Not done on purpose: a font scale. Authors size text in `px`, so scaling only the root would miss most text; a real text-size multiplier belongs in font-size resolution and is its own change.
- Evidence: `PeachBrackets.Tests` 249 passed (new `UserPreferencesTests`), `PeachBrackets.Engine.Tests` 17,236 passed, public API snapshot regenerated and reviewed.
