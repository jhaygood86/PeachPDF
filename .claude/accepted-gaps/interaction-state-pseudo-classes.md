# Interaction-state pseudo-classes never match

`:visited`/`:active` (and other interaction-state pseudo-classes) never match, by design — there is no browsing history or interaction/hover state in a static PDF renderer. `CssData.DoesSelectorMatch(PseudoClassSelector, ...)` intentionally answers only the pseudo-classes a document tree alone can decide: `:root`, `:scope`, `:empty`, `:link` and `:any-link`. (`:any-link` is the union of `:link` and `:visited`, and `:visited` never matches, so it selects exactly what `:link` does.)
