# Color-glyph artwork is drawn once into a Form XObject and referenced thereafter

_Landed 2026-09-10._

Every occurrence of a COLR/CPAL glyph re-emitted its whole decoded paint graph into the page content
stream. Measured on `main` with Segoe UI Emoji, 400 occurrences on one page: a plain color glyph cost
~16 KB per occurrence, a variation-selector sequence ~42 KB, and a ZWJ family sequence ~133 KB — a
**53.3 MB** single-page PDF that took Chrome minutes to display, because the viewer re-parses those
paths per occurrence too. The artwork is byte-identical every time; only the placement differs.

Each distinct glyph's artwork now goes into one Form XObject and each occurrence emits
`q s 0 0 s tx ty cm /Fm0 Do Q`. The same fixture: **53.3 MB → 237 KB** (225×) and 7.3 s → 1.8 s of
wall time. A realistic two-page report (tables, cards, emoji in running text) went 1,081,412 →
448,460 bytes, with page 0's content stream falling 541,869 → 20,756 bytes.

## What the design turns on

**The artwork is size-independent, so size must not be in the key.** It is drawn at a canonical em
size (`ColorGlyphFormCache.EmSize`, 100 world units) and the placement `cm` scales it by
`fontSize / EmSize`. This is the one place the color-glyph cache differs from `PdfImageTable`, whose
embedded pixels genuinely do depend on display size. A verified consequence: 🚀 at 22/16/11/7pt on
one page is four invocations of one form.

**The key is everything that changes the artwork, and nothing else**: descriptor identity, glyph id,
CPAL palette index, the `font-palette` entry overrides, and *the text color*. The last one is easy to
miss — the COLR `0xFFFF` sentinel means "use the foreground", and a glyph with no color record at all
is filled outright in the text color, so two runs of the same emoji in different colors are different
artwork. Overrides are compared **by value**: a resolved palette reaches the backend as a freshly
allocated dictionary per `DrawString` (`GraphicsAdapter.ToGlyphPalette`), so identity comparison would
have deduped nothing. `Selector.Equals` deliberately does *not* short-circuit on the override hash —
a dictionary only asks two keys whether they are equal once their hashes already matched, so a hash
compare there would have made the value compare unreachable (and untestable).

**The form is sized from a measure pass, not from a guessed box.** `MeasureGlyphBounds` re-runs the
same paint through `_measuring`, accumulating the bounds of every fill it *would* make. This is exact
rather than conservative because of a property of the v1 interpreter: `FillClip` refuses to paint
without an enclosing glyph clip, so **all** v1 ink is inside the union of the `PaintGlyph` clip
rectangles, and v0 layers (and the plain-outline fallback) are bounded by their own outlines. The
alternative considered and rejected was a fixed generous box (say ±2em around the em square) — it
needs an arbitrary constant, and a COLR v1 `PaintTransform` can put ink outside any constant chosen.

**No `/Group` on the form.** CLAUDE.md notes that a soft-masked or constant-alpha form needs its own
transparency group; this one is invoked by a bare `Do`, and adding an isolated group would *change*
compositing — COLR v1 `PaintComposite` blend modes blend against the page backdrop today, and
isolation would give them a transparent one instead. No group is what preserves current behavior
exactly.

**The invisible text stays on the page.** Only the ink moved. See
[.claude/invariants/color-glyph-invisible-text-belongs-to-the-page-not-the-artwork.md](../invariants/color-glyph-invisible-text-belongs-to-the-page-not-the-artwork.md).

## The bug this surfaced

The showcase comparison came back 110 of 113 byte-identical, and the three that moved were the
color-font ones — but `color_emoji` also *rendered* differently (max 69/255 in both engines), which a
pure representation change must not do. Chasing it found a real defect that had nothing to do with
Form XObjects: `ResolveColor` scaled a COLR paint's alpha as though `XColor.A` were a 0..255 byte when
it is a 0..1 double, so alpha 0.2 rounded to a fully transparent `0`. Every non-opaque COLR paint was
effectively invisible.

What made it hard to see is that the *symptom* depended on graphics-state history rather than on the
glyph: rendering Noto's 🇯🇵 flag on its own produced an opaque near-black border in both old and new
builds (the transparent state was never realized), while the same glyph on the busy showcase page
emitted `/ca 0` and vanished. Moving the artwork into a form changed which way that fell, which is why
a representation-only change appeared to alter output. Fixed (see
[../migration-notes/2026-09-10-colr-paint-alpha-is-now-applied.md](../migration-notes/2026-09-10-colr-paint-alpha-is-now-applied.md));
the flag's border now renders as the light grey of Noto's own reference in both builds.

Two lessons worth keeping: a rasterization diff that a byte diff called "only the expected files" is
still worth reading, and `ResolveColor` is the only place in the codebase with this byte-versus-double
confusion — every other `FromArgb` site works on `RColor`, whose `A` really is a byte.

## Cost

A distinct color glyph costs about **440 bytes** of form dictionary, object header and xref entry that
an inlined copy did not. Measured on the `color_emoji` showcase, which is the worst case by
construction — a per-paint-feature breakdown where nearly every glyph appears once: 198,964 → 210,769
bytes (+5.9%) for 26 new forms. That overhead is small against one copy of real emoji artwork
(16–133 KB), so promoting only on a second occurrence was considered and rejected as complexity that
buys back a rounding error. The two showcases with repeats went the other way: `emoji` 322,322 →
212,797 and `font_palette` 217,104 → 161,729.

## Evidence

Rasterized through **both** PDFium and MuPDF, per this repo's paint-verification convention: on the
two-page report, PDFium's maximum per-channel difference against the pre-change output was **1/255**
over 24 pixels of 1.17M, MuPDF's **4–7/255** — antialiasing moving under the new coordinate
quantization, not geometry. (Quantization actually improves: a form written to four decimal places at
a 100-unit em resolves finer than the same four places at a 12pt em.) Cross-page reuse was confirmed
structurally — one form object, `/XObject << /Fm0 6 0 R >>` in two page resource dictionaries — and
emoji still come back from `get_text()` in both engines.

Suite: 10,699 passed, 0 failed. Diff coverage 97.3%; the four uncovered lines are two defensive paths
(an upwards page, which `XGraphics.PageDirection` will not produce, and a color glyph that measures as
painting no ink at all, which no bundled fixture has — `' '` is never reached, since layout does not
paint whitespace as a glyph).

## Not done here, deliberately

The same treatment for SVG (a logo in a running header re-emits its scene graph per page;
`CssImagePainter.PaintSvgLayer` even builds a *fresh* Form XObject per paint). Left for its own change
because it touches the paint path for all SVG content, not just repeated content. One thing already
checked for it: SVG text uses the real selectable `DrawString` path, and text inside a Form XObject
still extracts correctly in both MuPDF and PDFium, so the tile route would not cost selectability.
Keying that cache on the `SvgDocument` *instance* restricts it to the two safe tiers by construction
(one instance per element, with `currentColor` already baked in at build time); the resource URL is
what would additionally share one logo across separate `<img>` elements.

## Trap found on the way

`.gitignore` carried a bare `*.pdf` (twice) and `*.html`. Git matches ignore patterns
case-insensitively on Windows and macOS, so `*.pdf` also matched the **directory**
`src/PeachPDF/PdfSharpCore/Drawing.Pdf/` — git refused to recurse into it, and both new files added
there were invisible to `git status`, with no way to re-include them from inside an excluded
directory. Fixed by adding `!*.pdf/` / `!*.html/` (a trailing slash matches directories only), which
re-includes the directories while leaving the file ignores intact.
