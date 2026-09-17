# tools

Maintenance and diagnostic scripts. Nothing here ships in the library or its package.

| script | what it is for |
| --- | --- |
| `Update-HyphenationPatterns.ps1` | regenerates the bundled hyphenation patterns |
| `pdf-text-state.py` | reads a PDF's text state from the content stream — `Tf` sizes and `cm` scales, separately |
| `reduce-repro.py` | bisects a document that reproduces a rendering bug down to the markup or CSS that causes it |
| `compare-with-chrome.py` | renders one HTML file with Chrome and with PeachPDF and diffs the word geometry |

The three Python scripts need [PyMuPDF](https://pymupdf.readthedocs.io/), which
`CONTRIBUTING.md` already expects for two-renderer rasterization checks:

```bash
python3 -m pip install pymupdf
```

Each takes `--help`, and each is standalone — no shared module, nothing to install.

## `pdf-text-state.py`

A PDF reader reports an **effective** font size: whatever `Tf` set, after the CTM and
the text matrix. When a document comes out the wrong size, only one of those is
usually to blame, and they want telling apart:

- a wrong `Tf` size — a font-size resolution problem;
- an identity `Tf` under a scaling `cm` — a shrink-to-fit or page-fit path;
- a form XObject with its own `/Matrix` — neither, and invisible in the page stream.

`PyMuPDF`'s `span["size"]` collapses all three into one number, so a scaled page and a
mis-sized font look identical in it.

```bash
python3 tools/pdf-text-state.py out.pdf --pages 1-3
```

## `reduce-repro.py`

A real document that renders wrong is usually tens of kilobytes of markup plus a
stylesheet nobody wrote for this purpose, and the cause is a handful of elements or one
rule. This keeps subsets — of the children at a chosen nesting level, or of the
top-level CSS rules — always emitting well-formed HTML, and asks a command of your
choosing whether the result still reproduces.

```bash
# what is at this level?
python3 tools/reduce-repro.py page.html --list
python3 tools/reduce-repro.py page.html --css --list

# bisect: the test command exits 0 while the reduction still reproduces
python3 tools/reduce-repro.py page.html --css \
    --test 'peachpdf {} -o /tmp/o.pdf && ! pdftotext /tmp/o.pdf - | grep -q "ONE LINE"' \
    --out reduced.html
```

`{}` is replaced with the candidate's path; without it the path is appended. The
reduction is delta-debugging rather than a plain halving, so it still finds a trigger
that needs two elements together.

## `compare-with-chrome.py`

"What does a browser do with this?" is where most layout questions end, and the
differences that matter most — a word two points off, a line that wrapped where the
other engine kept it whole — are invisible to a text comparison and easy to miss in a
side-by-side render. This pairs the words and reports the drift.

```bash
python3 tools/compare-with-chrome.py page.html \
    --peachpdf 'dotnet run --project src/PeachPDF.Cli -c Release --'
```

A wrap shows up as a large negative `dx` with a positive `dy`; a sizing difference shows
up as drift that accumulates along the line or down the page.
