# Command-Line Tool (`peachpdf`)

`peachpdf` is a standalone command-line tool that renders HTML to PDF using the PeachPDF engine — no
.NET runtime, browser, or other external dependency required. It is compiled ahead of time
([Native AOT](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)) into a single
self-contained native executable per platform.

It uses a conventional HTML-to-PDF command-line grammar. It recognizes only the options documented
below; any other flag is reported as an unknown option and the tool exits with a non-zero status.

## Installing

Download the archive for your platform from the project's
[GitHub Releases](https://github.com/jhaygood86/PeachPDF/releases) and extract the `peachpdf`
executable (`peachpdf.exe` on Windows). Prebuilt binaries are published for:

| Platform | Architecture | Asset |
|---|---|---|
| Windows | x64 | `peachpdf-<version>-win-x64.zip` |
| Windows | ARM64 | `peachpdf-<version>-win-arm64.zip` |
| Linux | x64 | `peachpdf-<version>-linux-x64.tar.gz` |
| Linux | ARM64 | `peachpdf-<version>-linux-arm64.tar.gz` |
| macOS | Apple Silicon (ARM64) | `peachpdf-<version>-osx-arm64.tar.gz` |

## Usage

```
peachpdf [OPTIONS] FILES... [-o OUTPUT.pdf]
```

`FILES` are one or more inputs — local HTML files, HTTP(S) URLs, MHTML (`.mht`/`.mhtml`) archives, or
`-` to read HTML from standard input. Multiple inputs are combined into a single PDF, in order. If
`-o` is omitted, the output is written next to the first input with a `.pdf` extension; `-o -` writes
the PDF to standard output. An explicit `-o` is required when the first input is a URL or standard
input.

### Examples

```bash
# Convert a file (writes report.pdf)
peachpdf report.html

# Convert to a named output
peachpdf doc.html -o out.pdf

# Fetch and render a web page (output is required for URL input)
peachpdf https://example.com -o example.pdf

# Read HTML from stdin, write the PDF to stdout
cat page.html | peachpdf - -o - > page.pdf

# Combine several documents into one PDF
peachpdf cover.html chapter1.html chapter2.html -o book.pdf

# Apply user style sheets, set a page size and margins, and set document metadata
peachpdf doc.html -s print.css --page-size "A4" --page-margin 20mm \
  --pdf-title "Quarterly Report" --pdf-author "Jane Doe" -o report.pdf
```

## Supported options

The default page size is **US Letter** (a document's own `@page { size: … }` still overrides it), and
the default media type is **print**.

### General

| Option | Description |
|---|---|
| `-h`, `--help` | Show usage and exit. |
| `--version` | Show version information and exit. |
| `--show-license` | Print the license and exit. |
| `--credits` | Print the license and third-party acknowledgements and exit. |
| `-v`, `--verbose` | Log informative messages to standard error. |
| `--debug` | Log debug messages to standard error. |
| `--log=FILE` | Append log messages to `FILE`. |

### Input

| Option | Description |
|---|---|
| `-l`, `--input-list=FILE` | Read a newline-separated list of inputs from `FILE`. |
| `--baseurl=URL` | Base URL for resolving relative resource references. |
| `--no-network` | Disable network (HTTP) resource access. |
| `--no-local-files` | Disable local-file resource access. |

### CSS

| Option | Description |
|---|---|
| `-s`, `--style=FILE` | Apply a user style sheet. Repeatable; later sheets win. |
| `--media=MEDIA` | CSS media type to render as (default `print`). |
| `--no-default-style` | Ignore the default (user-agent) style sheet. |
| `--no-author-style` | Ignore the document's own `<style>`/`<link>` style sheets. |
| `--page-size=SIZE` | Page size: a keyword (`A4`, `letter`, …), one or two lengths (`"210mm 297mm"`), optionally with `portrait`/`landscape`. |
| `--page-margin=MARGIN` | Page margin as 1–4 CSS lengths, in CSS shorthand order (e.g. `20mm`, `1cm 2cm`). |

### PDF output

| Option | Description |
|---|---|
| `-o`, `--output=FILE` | Output PDF file (`-` for standard output). |
| `--pdf-title=TITLE` | Set the PDF title (overrides the HTML `<title>`). |
| `--pdf-author=AUTHOR` | Set the PDF author. |
| `--pdf-subject=SUBJECT` | Set the PDF subject. |
| `--pdf-keywords=KEYWORDS` | Set the PDF keywords. |
| `--pdf-creator=CREATOR` | Set the PDF creator. |
| `--pdf-lang=LANG` | Set the PDF document language (the catalog `/Lang` entry), used when the document declares no language of its own (a document's own `<html lang>` takes priority). |
| `--tagged-pdf` | Emit a tagged (PDF/UA) structure tree. |
| `--no-compress` | Do not compress PDF content streams. |

Length units accepted by `--page-size` and `--page-margin` are `mm`, `cm`, `in`, `pt`, `pc`, and `px`
(`1px = 1/96in`); a bare number is treated as points.

### Network (HTTP inputs and resources)

| Option | Description |
|---|---|
| `--http-timeout=SEC` | HTTP request timeout in seconds. |
| `--http-header="Name: value"` | Add a request header. Repeatable. |
| `--user-agent=UA` | Set the `User-Agent` header. |
| `--auth-user=USER` | HTTP basic-auth user name. |
| `--auth-password=PASS` | HTTP basic-auth password. |
| `--http-proxy=PROXY` | HTTP proxy server. |
| `--insecure` | Disable TLS certificate verification. |

## Notes

- The tool is not thread-safe internally, but each invocation is a separate process, so running
  several `peachpdf` processes concurrently is fine.
- For programmatic use inside a .NET application, use the [PeachPDF library API](usage-examples.md)
  directly instead of shelling out to the CLI.
