# Line breaking inside a run of text follows the Unicode line breaking algorithm

Where a line may end inside one box's text used to come from a hand-written approximation: white space, a hyphen, the ideographs
U+4E00 to U+FA2D, some emoji, and a table of opening and closing punctuation. It now comes from the Unicode line breaking
algorithm (UAX #14, Unicode 18), with `word-break` applied.

What a document author can see:

- **Kana and CJK punctuation break.** Text in Hiragana or Katakana, and the CJK punctuation of U+3000 to U+303F, used to be one
  unbreakable word (only U+4E00 to U+FA2D, which includes the Hangul syllables and the ideographs, counted as Asian); it now breaks
  between characters as `normal` should, and `word-break: keep-all` keeps it together. Small kana, the prolonged sound mark,
  iteration marks and closing CJK punctuation stay off the start of a line.
- **`word-break: keep-all` works.** It used to behave as `normal`.
- **Breaks the old rules never offered.** After a dash or a hyphen the following letter starts a new word as before, but a break
  is now also allowed after an ellipsis, after other break-after characters, and around zero width spaces.
- **Breaks the old rules did offer, and the algorithm forbids.** A hyphen before a digit no longer breaks (`abc-123` is one
  word, as is `-5`), and no break is offered inside a number or between two letters of different scripts (`abcאבג`, previously
  breakable at the change of direction).
- **No break between `!`, `/` or `|` and a following Latin, Greek or Cyrillic letter** (not before an ideograph, kana or Hangul
  character), following the deviation CSS Text 3 suggests, so `!important` and `and/or` stay whole even though the bare algorithm
  would allow a break after them. A break after `?` is allowed (`a?b=c` can wrap after the question mark).
- **`word-break: break-all` no longer breaks before closing punctuation** (`. , ) ! ?`): the algorithm keeps it attached to the
  letter before it, where the old code offered a break before every character.

Not changed: text in separate inline elements is still not analysed as one run (`foo<b>bar</b>`), so a break at an element
boundary still needs white space, a hyphen or an ideograph beside it; `line-break` is not a CSS property here yet, so the
strictness is always the normal one.
