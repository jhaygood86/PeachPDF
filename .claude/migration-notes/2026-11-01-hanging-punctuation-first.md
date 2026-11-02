# `hanging-punctuation: first` now hangs the opening quote or bracket

Before, `hanging-punctuation` was dropped at parse time and an opening quote or bracket at the start of a block's first line stayed inside the text column. Now `hanging-punctuation: first` hangs it outside the start edge, so the text after it begins at the content edge and the first line gains the character's width (it can fit a word that would otherwise wrap). `last`, `allow-end` and `force-end` are accepted but still have no effect.
