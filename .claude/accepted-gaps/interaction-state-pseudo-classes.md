# Interaction-state pseudo-classes never match

`:visited`/`:active` (and other interaction-state pseudo-classes) never match, by design — there is no browsing history or interaction/hover state in a static PDF renderer. `CssData.DoesSelectorMatch(PseudoClassSelector, ...)` intentionally only matches `:root` and `:link`.
