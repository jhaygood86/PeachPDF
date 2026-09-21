# ZUGFeRD / Factur-X hybrid e-invoices (`PdfGenerateConfig.FacturX`)

A Factur-X / ZUGFeRD invoice is a PDF/A-3 file carrying one embedded Cross Industry Invoice XML plus an XMP
block that identifies it as such. Built on [the attachments API](2026-09-21-pdf-a3-attachments-api.md); targets
**Factur-X 1.09.2 / ZUGFeRD 2.5.2**. PeachPDF embeds the XML the caller supplies - it does not generate or
validate it (see the accepted gaps under `.claude/accepted-gaps/facturx-*`).

## The load-bearing ideas

**Every fact came from the specification package, not from secondary sources.** The first round of research
(blog posts, library READMEs) disagreed with itself on three points that mattered; the 1.09.2 spec, its sample
XMP file, its HYBRID code lists and its shipped Schematron settled them: the embedded name is always
`factur-x.xml` (`xrechnung.xml` for XRECHNUNG) - `zugferd-invoice.xml` is legacy 2.0; `fx:ConformanceLevel` is
`BASIC WL` with a space (not `BASIC_WL`), `COMFORT` is Order-X only; and `/AFRelationship` is `Data` for
MINIMUM/BASIC WL and `Alternative`/`Source`/`Data` for the rest in France but **`Alternative` only in Germany**.
Default: `Alternative` (legal in both) except `Data` for the two small profiles.

**The profile is derived from the XML, then checked, never trusted.** No validator enforces that
`fx:ConformanceLevel` matches the XML's own guideline (BT-24), so `FacturXInvoice` reads
`GuidelineSpecifiedDocumentContextParameter/ID`, maps it to a profile (exact strings from the Schematron; XRechnung
by the `...#compliant#urn:xeinkauf.de:kosit:xrechnung_` prefix), and rejects an explicit `Profile` that
disagrees. An unrecognised guideline needs an explicit `Profile`. The XRechnung prefix was an assumption until
the showcase ran ZUGFeRD-csharp's real output through it: `...xrechnung_3.0`, matched.

**The XMP is several `rdf:Description`s, exactly like the spec's sample:** the extension schema (`pdfaExtension`
-> `pdfaSchema` -> four `pdfaProperty` entries, in the spec's order, trailing `#` on the namespace URI) and the
`fx:` values are separate blocks. PDF/A only allows a property outside the predefined schemas if the schema is
declared in the packet, which is why an `fx:` block alone fails validators. `xmlns:rdf` is now declared
explicitly on `rdf:RDF` for every XMP packet (LINQ to XML had been generating `p1`); same meaning, conventional
spelling.

**The mismatch guard has to cover "dropped".** The XMP stream is rebuilt on every render call from the
established plan, so a later call without `FacturX` would silently rewrite the document's claim about itself.
`PdfEmbeddingPlan.SameAs` therefore compares the conformance level too, and a call that drops or changes the
invoice throws.

**Exactly one invoice XML, and it is first.** The invoice is added to `Files` before the caller's attachments,
so it heads `/AF`; `factur-x.xml`/`xrechnung.xml` are reserved names in `Attachments` (a XRECHNUNG document must
not also contain `factur-x.xml`).

## CLI

`--pdfa=LEVEL`, `--pdf-creation-date`, repeatable `--attach=FILE[;mime=..][;rel=..]`, `--facturx-xml`,
`--facturx-profile`. The CLI had no PDF/A option at all. **No creation date is invented**: PDF/A's XMP stream
needs one, and the library throws when it has none, so `--pdfa` uses `--pdf-creation-date` if given, else the
document's own `<meta name="date">`, else fails - with a hint appended, since the library's message names an API a
command-line user cannot set. (A first version defaulted to "now", which silently discarded a source's own date and
made every run's bytes differ; a `FallbackCreationDate` property to fix that was rejected in favour of just
throwing.) A date-only value is UTC so the same command line gives the same file everywhere.

## Showcases

`zugferd_factur_x_invoice` (EN 16931) and `zugferd_xrechnung_invoice` (XRECHNUNG), both built with the
declarative API so the site shows C#: `Showcases/ZugferdInvoiceShowcase.cs` is compiled into the harness *and*
copied next to it, and its text is the published source (displayed code == executed code; the harness'
hand-copied `const string` convention drifts). The invoice XML comes from the open-source **ZUGFeRD-csharp
18.0.0** (Apache-2.0, showcase-only `PackageReference`, same precedent as ScottPlot). One invoice model drives
both the XML and the layout. The docs-site link for these now reads "C# source" (`ShowcaseEntry.SourceKind`).

## What running it found (that reading would not have)

- **Mustang 2.26.0** (which embeds veraPDF) on the first render: PDF/A-3a `isCompliant=true` (6055 assertions),
  XMP and extension schema accepted - but the *XML* failed twice, both my invoice data, not PeachPDF: the
  generator omits `ApplicableHeaderTradeDelivery` unless a delivery date is set (the EN 16931 schema requires
  the element), and `Math.Round` is half-to-even (46.305 -> 46.30) where the validators recompute half-up. The
  XRechnung variant also needed BT-23 (business process). After the fixes both showcases are `valid` end to end
  (EN 16931 profile, and XRechnung 3.0 rules), PDF `flavour=3a`.
- Both PyMuPDF and PDFium extract the attachments from all three showcases.
- ZUGFeRD-csharp 18.0.0 documents ZUGFeRD 2.3 while the target is 2.5.2 - same `1p0` URNs and CII D22B, and its
  output passes the 1.09.2 artefacts as shipped in Mustang.
- **Declarative table cells could not right-align text** - `Alignment(...)` had been a silent no-op everywhere
  (it set the `text-align` shorthand on a per-longhand registry). Fixed in
  [2026-09-21-declarative-text-alignment-and-table-cells.md](2026-09-21-declarative-text-alignment-and-table-cells.md);
  the invoice amounts are now right-aligned.
- **A post-change review** (a read-only agent over the diff) turned up, and this change fixed: embedded files with
  PDF/X (now rejected - PDF/X does not allow them); attachment names with path separators or control characters
  (rejected); a plain attachment leaving the header at PDF 1.4 while using 1.7 features (now bumped to 1.7);
  `PdfDate` formatting with the current culture (a Buddhist/Hijri-calendar culture wrote a different year into every
  PDF date, `/ModDate` included - now invariant); the CLI's `--facturx-profile` rejecting the specification's own
  spellings (`EN 16931`, `BASIC WL`). Recorded as an accepted gap instead: Latin-1-only attachment names
  ([accepted-gaps/attachment-file-names-are-latin-1-only.md](../accepted-gaps/attachment-file-names-are-latin-1-only.md)).

## Evidence

- `FacturXIntegrationTests` (profile derivation for every URN incl. the ZUGFeRD 2p0 spellings and the XRechnung
  extension suffix; XMP shape parsed as XML against the spec sample; relationship rules; requirements; reserved
  names; multi-call and declarative two-page idempotence) and `CliPdfAAndEmbeddingTests`.
- Validators run on `-c Release` output only: Mustang 2.26.0 `<summary status="valid"/>` for both invoices.
