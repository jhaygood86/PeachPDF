# `rem` in letter-spacing / word-spacing / text-indent (#1816)

`CssLength` took the unit from the last two characters of the text, so `1.5rem` read as the unit `em` with the number `1.5r`, which failed to parse. `NoEms` (the eager em-to-pt step for those three properties) then called `ConvertEmToPoints` on an errored length and threw `Invalid length`, aborting the document. The `UnitNames.Rem` case in the switch could never match a two-character slice.

Fix: `rem` is recognised as the one three-letter unit before the slice. `NoEms` leaves a rem length alone, and the typed value resolves it later against the root font size, the same as `margin`/`padding`.

Trap: any new unit longer than two letters needs the same treatment in that constructor.
