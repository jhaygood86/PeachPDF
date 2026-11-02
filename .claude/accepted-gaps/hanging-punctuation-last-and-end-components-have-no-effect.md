# `hanging-punctuation: last`, `allow-end` and `force-end` are accepted but do nothing

`first` is implemented (a negative first-line start offset equal to the opening character's advance, folded into `CssLayoutEngine.GetLineTextIndent`). The end-edge components parse (`HangingPunctuationGrammar`) but nothing reads their flags.

They are not the same shape as `first`: the hung character has to be left out of the fit test in `FlowBox` and out of the justification distribution in `ApplyJustifyAlignment`, and `allow-end` has to re-run the break decision with the character hung. Tracked in #2071; CSS Text 3 §8.
