# CFF DICT refusals match FreeType, dead Adobe code removed, and a font with only a format-12 cmap loads

Housekeeping after the hinting ports; three unrelated pieces.

## What was done

- **The stale boundary in `per-character-font-matching-boundaries.md` was half right.** The file said a font with only a cmap format-12 subtable "isn't
  usable" because `CMapTable.Read` needed format 4 for the WinAnsi `Widths` loop. The Widths loop is gone (it moved to `PdfSimpleFontWidths`) but the
  refusal was not: `CMapTable.Read` still threw "no usable platform or encoding ID" unless a format-4 subtable was found, and `CharCodeToGlyphIndexCore`
  read `cmap4` unconditionally. Found by building the font (Source Sans with its cmap replaced by one platform 3 / encoding 10 format-12 subtable,
  `Format12OnlyFontTests`), not by reading. Fixed rather than deleted from the docs: `cmap4` is now nullable, the format-12 groups answer for the BMP too,
  and only a font with neither subtable is refused. Everything else the file said about colour formats was stale in the other direction (bitmap and
  SVG glyphs render now); the file was rewritten to what is still true (COLR over CFF outlines, `PaintVar*`) and the docs note was dropped.
  `variable-fonts-font-face-ranges-and-percentage-font-stretch.md` is confirmed gone on `origin/main`.
- **The Private DICT operand stack holds 97 entries, not 96.** FreeType's `cff_load_private_dict` adds one "for the operator" (an operator is refused when the
  stack is full); the Top and Font DICT parsers have 96. A Private DICT of 96 operands and an operator loaded in FreeType and was refused here.
- **Operators the port used to skip are checked like FreeType.** The port ignored the Top DICT fields it does not use, so a Top DICT with `Notice` and no
  operand (FreeType: `Stack_Underflow`, the face is refused), a `FontBBox` of three numbers, a `MultipleMaster` of fewer than five operands or a design count
  outside 2 to 16, or a Private DICT `ForceBold`/`ForceBoldThreshold`/`lenIV`/`ExpansionFactor` without an operand still got hinted. `StemSnapH`/`StemSnapV` are
  delta arrays and may be empty, in both.
- **Dead code left in the Adobe files was removed** (`Cf2Stack.SetReal`/`Pop`, the `Frac` number type, `Cf2Fixed.FracToFixed`), each recorded in
  `PORTING-NOTES.md`. FreeType 2.14.3 calls `cf2_stack_setReal`/`cf2_stack_pop` only from Type 1 (`hstem3`, `callothersubr`) and CFF2 (`blend`) code and
  never pushes a `Frac` number at all.

## What was found by running it

- **The evidence is a new reference, not a hand-reading of `cffparse.c`.** `HintingCffDicts.golden.json.gz` (`generate_hinting_cff_fixtures.py`) has 72 fonts whose
  CFF table is written by hand (fontTools cannot write a malformed DICT; its head table is kept and the `CFF ` table replaced) and whether FreeType 2.14.3
  opens each: 36 are refused (FreeType's errors are not all the same: 6 for a missing operand or a full stack, 3 for a bad design count, 161 for the short `FontBBox` and `MultipleMaster`; the port only has to refuse). Without the parser change 33 of them
  fail `HintingCffDictGoldenTests`. My first list of the refused operators came from reading `cfftoken.h` and was right, but the boundary of the stack (96 for
  Top, 97 for Private, and that an *operator* needs a free slot where a number only needs to fit) was only certain once FreeType said so.
- **The regenerated main golden is byte-identical** to the committed one (the seeded fixtures are deterministic); the `.otf` files differ from run to run only in
  the `head` table's timestamps, so they are not regenerated and committed.

## Deliberately not done

`MultipleMaster` designs are still not kept or used (FreeType keeps them only for the Private DICT's blend, which is Type 1/CFF2 and not ported); CFF2
operators of the DICTs (`vsindex`, `blend`, `maxstack`) stay out with the rest of CFF2 hinting.
