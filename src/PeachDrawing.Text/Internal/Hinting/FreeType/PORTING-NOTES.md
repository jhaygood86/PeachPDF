# Porting notes: FreeType code in PeachDrawing.Text

Everything in this directory is derived from the FreeType Project and is used under the **FreeType Project License** (`FTL.TXT`, in this directory, byte-identical to upstream). FreeType is dual-licensed (FTL or GPLv2); PeachDrawing.Text takes the FTL. Nothing FreeType-derived lives outside this directory. The rest of the package is BSD 3-Clause; this directory is BSD 3-Clause *and* FTL, and the FTL's credit requirement is met by the notices listed at the end of this file.

Two families of files are here: the TrueType bytecode interpreter and glyph loader (`Tt*.cs`, with the arithmetic helpers `Ft*.cs`), which are FreeType's own; and the CFF engine (`Cff*.cs` and `Ps*.cs`), which is FreeType's loader for CFF fonts (`Cff*.cs`) around **Adobe's CFF engine** (`Ps*.cs`, the `psaux` module of FreeType, which Adobe Systems Incorporated contributed). The Adobe files carry Adobe's own header on top of the FTL: it says the work is made available under the FreeType Project License and adds a patent licence grant from each contributor, with a termination clause if the licensee starts patent litigation over the work. That header is reproduced unchanged in every C# file derived from an Adobe file, and the notice is listed separately in the package's `THIRD-PARTY-LICENSES.md`.

The FTL asks that source redistributions keep `FTL.TXT` unaltered, keep the copyright notices of the original files, and indicate additions, deletions and changes "clearly ... in accompanying documentation". This file is that documentation.

## What was ported, from where

| | |
|---|---|
| Upstream | FreeType, <https://freetype.org> (mirror <https://github.com/freetype/freetype>) |
| Release | **VER-2-14-3** (commit `0a0221a1347e2f1e07c395263540026e9a0aa7c7`) |
| `FTL.TXT` | `docs/FTL.TXT` of that release; git blob `b9e8cbd66ffff526113350953f4a2f4252a92f49`, SHA-256 `5a5ee54c5001bbad1cdc1a57cc3dd4c42199b2da09d39c7ee41fab002d02967f`. `.gitattributes` in this directory switches line-ending conversion off for it. |
| Copyright years in the headers | 1996-2026 (David Turner, Robert Wilhelm and Werner Lemberg; 2001-2026 for `fttrigon.c`) |
| Verified against | FreeType 2.14.3 built from that tag (see `assets/fonts/generate_hinting_golden.py`) |

Only files under the FTL were read for this port. The auto-hinter (`src/autofit`), the rasterizers, the bundled zlib/bzip2 and every GPL-only file were not used.

## The C# files

Every C# file in this directory begins with the header of the FreeType file it derives from, verbatim, followed by a line stating that it was ported to C# for PeachDrawing.Text and modified. The table lists, per C# file, the FreeType file or files it derives from and what changed.

| C# file | Derives from (FreeType 2.14.3) | What changed, beyond "Deliberate differences" below |
|---|---|---|
| `FtCalc.cs` | `src/base/ftcalc.c` | `FT_MulFix`, `FT_MulDiv`, `FT_MulDiv_No_Round`, `FT_DivFix`, `FT_MSB` (with `BitOperations.LeadingZeroCount`), `FT_Vector_NormLen` and the pixel-rounding macros of `ftobjs.h` (`FT_PIX_FLOOR`, `FT_PIX_ROUND`, `FT_PIX_CEIL`, `FT_PAD_ROUND`); only the 64-bit-integer code path is ported |
| `FtTrigon.cs` | `src/base/fttrigon.c` | Only `FT_Hypot` (which is what `FT_Vector_Length` calls) and the CORDIC helpers it needs (`ft_trig_prenorm`, `ft_trig_pseudo_polarize`, `ft_trig_downscale`); nothing else of the file |
| `TtTypes.cs` | `src/truetype/ttobjs.h`, `ttinterp.h` | The structures the interpreter reads (`TT_GraphicsState`, `TT_CodeRange`, `TT_DefRecord`, zones, the error codes as `TtError`, the version and render-mode enumerations), as C# classes and structs instead of C structs |
| `TtFace.cs` | `src/truetype/ttobjs.c` (`tt_face_init`, `tt_check_trickyness*`), `ttpload.c` (`tt_face_load_cvt`, `tt_face_load_fpgm`, `tt_face_load_prep`, `tt_face_load_hdmx`, `tt_face_get_device_metrics`), `ttload.c` (`tt_face_load_maxp`, `tt_face_load_loca`), `ttmtx.c` | Tables are read from the font's bytes with span readers instead of a stream, and every offset and length is checked against the table; only the tables the hinter needs are read; the tricky-font list is kept as in `ttobjs.c`; `gasp`, `LTSH` and `VDMX` are not read |
| `TtSize.cs` | `src/truetype/ttobjs.c` (`tt_size_init`, `tt_size_run_fpgm`, `tt_size_run_prep`, `tt_size_reset`, `tt_size_ready_bytecode`), `ttobjs.h`, `src/base/ftobjs.c` (the ppem and scale computation of `FT_Request_Size`/`FT_Select_Metrics`) | A size is immutable once `prep` has run (see "No shared mutable state"); fpgm and prep are run when the size is created; sizes are rejected outside 1/64 to 65535 pixels |
| `TtInterp.cs` | `src/truetype/ttinterp.c`, `ttinterp.h` | The execution context, the run loop (`TT_RunIns`), the instruction-length and stack-effect tables, the projection/rounding/move primitives and the `Ins_*` helpers they use, plus the work limit; the function-pointer dispatch became `switch` |
| `TtInterpInstructions.cs` | `src/truetype/ttinterp.c` | The `Ins_*` instruction implementations, one method per opcode family, in FreeType's order and with its comments; the two deliberate differences in it are the copy-on-write cvt of `WCVTP`/`WCVTF` and the zone clamp of `SHZ` |
| `TtGload.cs` | `src/truetype/ttgload.c` (`TT_Load_Glyph`, `load_truetype_glyph`, `TT_Process_Simple_Glyph`, `TT_Process_Composite_Glyph`, `TT_Hint_Glyph`, `tt_get_metrics`, `compute_glyph_metrics`, `TT_Get_HMetrics`/`VMetrics`, the phantom-point code), `src/base/ftgloadr.c`, `src/base/ftobjs.c` (`ft_glyphslot_grid_fit_metrics`, the rounding of the metrics of a hinted glyph) | Glyph data is read from the font's bytes with bounds checks; embedded bitmaps, incremental loading and the `FT_LOAD_*` flags other than the ones in "Not ported" are not ported; a variable font's deltas come from this package's `gvar` reader (see "Variable fonts"); the loader returns a hinted point set, tags, contour ends and an advance, not a glyph slot |

### The CFF files

The C files of Adobe's engine were in `src/cff` (as `cf2*.c`) until FreeType 2.9 and are in `src/psaux` (as `ps*.c`) in 2.14.3; the C# files use the names of the 2.14.3 files. The headers in the Adobe rows are those of the C files they derive from (the `ps*` and `cffdecode`/`psobjs` files are Adobe's; the `cff*` files are FreeType's alone).

| C# file | Derives from (FreeType 2.14.3) | What changed, beyond "Deliberate differences" below |
|---|---|---|
| `CffParse.cs` | `src/cff/cffparse.c`, `cfftoken.h` | The DICT reader of `cff_parser_run` for the operators hinting needs (the Top or Font DICT's `charset`, `CharStrings`, `Private`, `CharstringType`, `FontMatrix`, `ROS`, `FDArray`, `FDSelect`; the Private DICT's blue values, `BlueScale`, `BlueShift`, `BlueFuzz`, `StdHW`, `StdVW`, `LanguageGroup`, `Subrs`, `defaultWidthX`, `nominalWidthX`, `initialRandomSeed`), with the number readers (`cff_parse_integer`, `cff_parse_real`, `cff_parse_num`, `do_fixed`, `cff_parse_fixed_dynamic`) and `cff_parse_font_matrix`, in the 32-bit `FT_Long` arithmetic; the other fields FreeType reads (names, `FontBBox`, the multiple-master operator, ...) are skipped |
| `CffLoad.cs` | `src/cff/cffload.c` (`cff_index_init`, `cff_index_access_element`, `cff_index_get_pointers`, `cff_subfont_load`, `cff_load_private_dict`, `cff_font_load`, the FDSelect and charset loaders, `cff_compute_bias`), `src/cff/cffobjs.c` (the font-matrix normalisation of `cff_face_init`) and `src/psaux/cffdecode.c` (`cff_lookup_glyph_by_stdcharcode`) | Everything is read from the font's bytes with bounds checks instead of a stream; a font in an SFNT wrapper only (its units per em come from `head`, and the charset is not inverted); the encoding is not read, and neither are the strings and names |
| `CffTables.cs` | `src/cff/cffload.c` | The three predefined charsets and the Adobe standard encoding, as C# arrays |
| `CffGload.cs` | `src/cff/cffgload.c` (`cff_slot_load`, for a hinted glyph), `src/cff/cffobjs.c` (`cff_size_request`), `src/psaux/cffdecode.c` (`cff_decoder_init`, `cff_decoder_prepare`), `src/base/ftobjs.c` (`FT_Request_Metrics`, `ft_glyphslot_grid_fit_metrics`) | The loader returns a point set, tags, contour ends and an advance, not a glyph slot; the checks of `FT_Outline_Check` are made on the result |
| `PsFixed.cs` | `src/psaux/psfixed.h` | The macros as static methods |
| `PsGlue.cs` | `src/psaux/psglue.h`, `pserror.h`, `psread.h`, `psarrst.c` and `psarrst.h` | The structures, the shared first-error rule, the charstring buffer (a read past the end gives zero) and the array stack as C# classes |
| `PsStack.cs` | `src/psaux/psstack.c`, `psstack.h` | The operand stack, with its errors (only the first is recorded) |
| `PsBlues.cs` | `src/psaux/psblues.c`, `psblues.h` | The blue zones, captured hints and the em box heuristic |
| `PsHints.cs` | `src/psaux/pshints.c`, `pshints.h` | The hint maps and the glyph path (with its stem darkening offsets and joins) |
| `PsFont.cs` | `src/psaux/psfont.c`, `psfont.h` | The per-size state (blue zones, darkening amounts) and the outline loop that runs the charstring again with the darkening reversed when the winding is clockwise |
| `PsIntrp.cs` | `src/psaux/psintrp.c`, `psintrp.h` | The Type 2 charstring interpreter for CFF fonts, and the methods of the hint mask |
| `PsFt.cs` | `src/psaux/psft.c`, `psft.h`, `psobjs.c` | The glue that gives the engine the font, the size and the subroutines (`PS_Decoder`), and the outline builder (`ps_builder_*`, `cf2_builder_*`) |

The port was made from the C sources of those files only. The files in `Internal/Hinting/` outside this directory (`HintingEngine.cs`, `HintingException.cs`) are PeachDrawing.Text's own code that calls the port and contain nothing of FreeType's.

See "Deliberate differences" below for what applies to all of them.

## Deliberate differences, listed individually

These are the places where the port does something other than FreeType on purpose; everything else was ported as written, and the reference tests (below) show that the result is the same.

* **`SHZ` is clamped to the zone.** FreeType's `Ins_SHZ` bounds its loop by the end of the last contour of `zp2`; in a composite glyph that bound can exceed the number of points of the zone, and FreeType then writes past the end of the array (into allocation slack). The port clamps the loop to the zone's point count, which changes nothing for a glyph FreeType handles inside its arrays.
* **`WCVTP`/`WCVTF` write a private copy of the cvt.** The size's cvt is shared by every glyph of the size and never changes after `prep`; a glyph program that writes it gets a copy made at the first write.
* **The twilight zone is copied per glyph** (each glyph starts from what `prep` left) instead of being shared and mutated across glyphs.
* **A second work limit** on top of the instruction limit (see "Work limit").
* **CFF: Type 1, CFF2 and multiple-master code is not ported.** Adobe's engine also draws Type 1 and CFF2 (variable CFF) glyphs; here it is only used for CFF fonts, so the branches for `isT1` and `isCFF2` (the `seac` and `callothersubr` operators of Type 1, `vsindex` and `blend`, the blend vectors) are left out. A CFF2 font is not hinted. The `MultipleMaster` operator of a Top DICT is ignored.
* **CFF: a glyph load starts from a fresh engine state.** FreeType keeps one `CF2_Font` for a size and recomputes what depends on the transform or the font dictionary when it changes; the port makes one for each glyph, which gives the same values (nothing else goes into them). The state of the `random` operator, which FreeType keeps in the subfont (so that a glyph's numbers depend on the glyphs loaded before it) and seeds from the address of an object, starts each glyph from the font's own `initialRandomSeed`, which is what FreeType does when it is given the random seed 0. The reference data is made with that seed and with a face for each glyph.
* **CFF: sizes over 2000 ppem are not hinted.** FreeType retries a glyph that its engine calls too big (a scale over 2000 ppem) unhinted and scales the integer-unit outline; here the load fails, and the caller gets the unhinted outline of this package.
* **CFF: stem darkening is off**, as it is in FreeType by default; the engine has it (the reference tests turn it on with FreeType's `no-stem-darkening` property) and no public setting reaches it.
* **CFF: a load that makes more than 65,535 points or contours fails** (`FT_OUTLINE_POINTS_MAX`), as it does in FreeType, and a Font DICT array of more than 256 entries makes the CID-keyed font be read as if it had none, as FreeType does.
* **CFF hinting has no modes.** Adobe's engine has one behaviour, so `GridFitting.Monochrome` gives what `Standard` does, and the caches share the entries. The advance of a hinted glyph is the `hmtx` advance scaled and rounded to a whole pixel.
* **Interpreter errors are values, not exceptions**, until a load fails as a whole; the numeric codes are the port's own.
* **Variable fonts** are approximated (see "Variable fonts" in the list below).

## Tests, and what they derive from

The reference for every claim of exactness is FreeType itself, run over the same fonts, not values worked out by hand or copied from a document:

| Test (in `PeachDrawing.Text.Tests/Hinting/`) | Reference | Derivation |
|---|---|---|
| `HintingGoldenTests` | `HintingGolden.json.gz`: 155 (font, mode, size) runs, every glyph, compared point by point in 26.6 | Made by `assets/fonts/generate_hinting_golden.py` with FreeType 2.14.3 (built from the VER-2-14-3 tag as a 32-bit-`long` Windows DLL and driven through `freetype-py`, whose own bundled FreeType is 2.13.2 and is refused by the script). Modes `standard`, `monochrome`, and the two mixed combinations |
| `HintingOpcodeFixtureTests` | `HintingOpcodes.golden.json.gz` | `HintingOpcodes.ttf` is a synthetic font whose 390 glyphs run random programs that use every opcode, invalid arguments included, and composites of every component-argument form; `generate_hinting_opcode_fixtures.py` writes it and what FreeType makes of each glyph. It is the per-opcode test: it is not derived from a FreeType test file (FreeType has none of this kind) and contains no FreeType code |
| `FtCalcTests` | `HintingArithmetic.golden.json.gz` | `FT_MulFix`, `FT_DivFix`, `FT_MulDiv` and `FT_Hypot` (`FT_Vector_Length`) called in FreeType with the corner cases of a 32-bit `FT_Long` and random values, by `generate_hinting_arithmetic_golden.py`. The arithmetic edge cases FreeType tests only indirectly |
| `FreeTypeRegressionTests` | FreeType's `tests/issue-1063/main.c` | Ported: loads glyphs of character codes 59 to 170 and fails on any interpreter error. The font FreeType's test uses (a downloaded third-party font) is not used; the same loop runs over the bundled hinted fonts at four sizes in both interpreter versions. The file keeps the FreeType attribution and FTL notice |
| `HintingCffGoldenTests` | `HintingCff.golden.json.gz` | 43 (font, mode, size) runs over two bundled CFF fonts (SourceCodePro, gsubtest-lookup3) and five synthetic ones, compared point by point in 26.6, refusals included. Made by `assets/fonts/generate_hinting_cff_fixtures.py` with FreeType 2.14.3 (a fresh library and face for each glyph). The synthetic fonts (CC0) have glyphs that are random Type 2 charstrings: hints near blue zones, hint masks and substitution, every drawing operator, flex, subroutines, the arithmetic operators, accent composition with `endchar`, family blues, `LanguageGroup 1`, a sheared font matrix, a CID-keyed font with three font dictionaries (private dictionaries and matrices of their own), and hostile charstrings (a call tree of 30^12 calls, recursion, 100 hints, 60 operands, 40,000 points); the modes are FreeType's normal and monochrome targets, and normal with stem darkening on |
| `HintingRobustnessTests`, `HintingCffRobustnessTests`, `HostileFonts` | none (own tests) | Hostile fonts built here: scrambled tables, truncated glyphs, infinite loops, stack over- and underflow, deep composites, 30,000-point glyphs, and a seeded random fuzz. FreeType's fuzz corpora were not used, see below |

FreeType's other test material was looked at and deliberately not used:

* **FreeType's `tests/` directory** holds only `issue-1063` in 2.14.3 (ported, above).
* **`freetype2-testing`** (the fuzzing corpora and harnesses) is distributed under the GNU GPL version 2 only, not the FTL, so nothing of it (harness code or corpus files) was copied into this package. The classes of input its TrueType corpora exercise (broken tables, programs that loop or overflow the stack) are covered by the hostile-font tests above.
* **`freetype2-demos`** was not available to inspect and is GPL/FTL-mixed; its `ttdebug` step-through tool has no test suite to port.

## Deliberate differences from FreeType, all files

* **Language.** C to C#. Pointer arithmetic became indexes, `FT_Vector*` arrays became parallel `int[]` x/y arrays, function pointers (`func_round`, `func_project`, ...) became `switch` dispatch on the same state, `goto`-based error exits became `return`/exceptions where the exit did nothing else.
* **`FT_Long` is 32 bits.** FreeType's `long`-typed values (`FT_Long`, `FT_Pos`, `FT_Fixed`, `FT_F26Dot6`) are C# `int`, with wrap-around on overflow (`unchecked`), matching FreeType built for a 32-bit `long` (Windows, which is what the reference build is). Intermediate products are 64-bit as in FreeType's `FT_INT64` code path. No path of the C code depends on the width of `long` for well-formed input.
* **Square pixels only.** A `PixelsPerEm` is one number, so `x_ppem == y_ppem` always: the "stretched" cvt/ppem routines (`Current_Ratio`, `Read_CVT_Stretched`, ...) and the non-square branches of `MD`/`MDRP`/`IP` are not ported, and `FT_Vector_Length` is ported only for the composite-offset case that still needs it.
* **No shared mutable state.** In FreeType a size object owns an execution context whose cvt/storage/twilight zone glyph programs can modify for the *next* glyph. Here the state left by `prep` is immutable and every glyph starts from a copy of it, so a glyph's outline never depends on which glyphs were loaded before it. (The reference data is generated by re-running `prep` before each glyph, which gives FreeType the same guarantee.)
* **Work limits.** FreeType stops a program after `TT_CONFIG_OPTION_MAX_RUNNABLE_OPCODES` (1,000,000) instructions; that limit is kept, but it is one budget for the font program, one for the CVT program and one for each glyph loaded with all of its components (FreeType restarts the count for each program run, so a composite glyph could multiply it). On top of it a second limit bounds the *total loop work* (`SLOOP`-driven and `LOOPCALL` iterations, `IUP`, the flips, `ROLL`, and code skipped over) of a glyph, which FreeType leaves unbounded, so a hostile font cannot spend minutes in one glyph. Exceeding either is the same `Execution_Too_Long` error FreeType raises. A third limit allows one glyph load to read 1,024 glyphs, components included: FreeType refuses a glyph that contains itself and nothing more, so a composite of composites (each listing the next many times) takes exponential time there.
* **Caches are bounded.** A face keeps 16 sizes and 4,096 hinted glyphs (by weight as well, one million contours-plus-segments), evicting the least recently used.
* **Errors.** FreeType error codes became `TtError` values inside the interpreter (`exec->error` is kept, because FreeType's non-pedantic mode continues after most of them). Errors that make FreeType refuse to load a glyph become a `HintingException`, which the public API turns into "return the unhinted outline".
* **Variable fonts.** Deltas come from this package's own `gvar` reader (double precision) rather than `ttgxvar.c`'s 16.16 arithmetic; hinting of variable-font instances is therefore not bit-exact with FreeType. `cvar` deltas are not applied.
* **Not ported:** `TT_CONFIG_OPTION_BYTECODE_INTERPRETER` debugging hooks and `FT_TRACE` calls, the incremental-loading interface, embedded bitmap strikes, `FT_LOAD_*` flags other than hinted/unhinted and the target mode, `FT_Render_Mode` values other than normal and mono.
