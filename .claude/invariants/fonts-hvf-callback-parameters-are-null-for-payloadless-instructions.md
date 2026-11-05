# The HVF render callback's parameters pointer is null for instructions with no payload

`HVF_render_current_part` calls back with a `const HVFPartRenderParameters*` that is **null** for the instructions that
carry no payload (observed for BeginPath, ClosePath and EndPath). `HvfRenderer.OnInstruction` reads the union only for the instruction kinds that
carry one, as far as that kind carries it: AddPoint/AddLine 2 doubles, AddQuad 4, AddCubic 6.

**Symptom if it is broken:** every composite glyph (all CJK) returns no outline, `HVF_render_current_part` returns `-1`
right after the first nested `BeginPart`, and nothing is thrown (the callback's catch returns `Stop`, which the library
reports as `-1`). Simple glyphs and spaces still "work", which hides it. A plain C program that makes the same calls is the
quickest way to tell the library from the interop.

Also: the callback must never let an exception unwind into native frames, and a renderer is single-threaded
(`HvfRenderer.TryRender` locks); the `hvgl` bytes are copied into 8-byte-aligned native memory because a table sits at a
4-byte offset in a font file.
