# Form nesting limit (16 -> 40) and a corpus trap: git autocrlf

`bug_972999` nests 35 forms. The interpreter capped forms, patterns, glyph procedures and soft masks at 16 deep (scattered literals) and
reported `Do (form nesting)`. PDFium's own limit is 40, so `MaxFormDepth` is now 40, one constant for all four places; a cycle still ends
at the limit. Tests: 35 deep paints, 60 deep is reported and not drawn (both fail with the old limit).

**Trap: measure the corpus from the runner's `fetch`, never from a `git clone`.** A clone on Windows with `core.autocrlf=true` rewrote the
checked-in `.pdf` files to CRLF, which moves every byte offset: five tests (bug_2152, bug_332462378 and the three page-tree tests bug_2123,
bug_335309995, bug_1484283) looked like importer defects (blank page, wrong page size) and were not. With the exact bytes the corpus
passes 145 of 207 (139 before this fix on the CRLF copy). `fetch` downloads the GitHub tarball, which is byte-exact, and CI uses it.

The floor is 138: below the 145 measured on Windows, to leave room for Linux fonts. Raise it after the first CI run.
