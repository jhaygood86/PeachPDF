# Variable fonts: CFF2 outlines are not supported

A variable font with CFF2 outlines (a `CFF2` table) has no outlines here: the table is not parsed and `Type2CharstringInterpreter` does not
know `vsindex` and `blend`. `Typeface.WithAxes` on such a font changes only what `HVAR` and `MVAR` change. Tracked in
[#1407](https://github.com/jhaygood86/PeachPDF/issues/1407).
