# Thai, Lao, Khmer and Burmese wrap at words

**Before:** a run of Thai, Lao, Khmer or Burmese text had no place where a line could end (the Unicode line breaking algorithm leaves the
`SA` class to a dictionary, and PeachPDF had none). A paragraph with no spaces between its words, which is how these scripts are written,
was one unbreakable word: it overflowed its box, made a table column as wide as the whole text, and was only ever cut by
`overflow-wrap: break-word` or `anywhere`, anywhere in a word. A word's min-content width was the width of the whole run.

**Now:** the text is broken between the words of a word list the library carries (ICU's break-iterator dictionaries), as browsers do,
and never inside a syllable. The script decides, not `lang`. Lines wrap at the words, the min-content width of a run is its longest word,
and `overflow-wrap` only has to break a single word that is wider than the line. `word-break: keep-all`, `line-break` and
`overflow-wrap` apply on top as they do for other text (`keep-all` still leaves the word boundaries of these scripts alone, as in a
browser). A compound the list has as one word stays whole, and a stretch of text with no match stays whole, so a document with unusual
vocabulary wraps less often than one with common words.

`PeachDrawing.Text` callers get the same from `LineBreaker.FindOpportunities`; set `LineBreakOptions.ComplexContext` to
`ComplexContextBreaking.GeneralCategory` for the previous behaviour, which is also rule LB1 of the algorithm.
