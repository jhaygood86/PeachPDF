# Factur-X: the invoice XML is embedded as given - never generated, never validated

`PdfGenerateConfig.FacturX` takes the invoice as Cross Industry Invoice XML bytes. PeachPDF does not generate
that XML and does not validate it against the format's XSD and Schematron. What it does check: the XML is
well-formed, is a `CrossIndustryInvoice`, declares a guideline (BT-24) it can map to a profile (or the caller
names the profile), and that an explicit profile agrees with it.

## Why

- **Generating** it is a business-domain model (parties, tax categories, code lists, rounding, payment means)
  with several existing .NET libraries; a PDF renderer should not own it. The showcases use ZUGFeRD-csharp.
- **Validating** it means Schematron. The specification ships its rules as compiled XSLT 2.0, which needs a
  Saxon-class engine; nothing in the .NET base class library runs it, and bundling one (or a Schematron port)
  into a rendering library is a large dependency for a check the caller's pipeline already needs to run with the
  format's own validator (Mustang, ZUV, KoSIT). Structural validity of the *PDF* side is what PeachPDF owns and
  is checked against veraPDF.

## What is therefore not enforced

- Any EN 16931 / Factur-X business rule on the XML, including that the PDF and XML carry the same information
  (specification Principle 4) - a caller who renders the pages and the XML from different sources can break it.
- BR-HYBRID-DE-01/02 (MINIMUM and BASIC WL must not be used when seller and buyer are both in Germany): the
  parties are inside the XML and PeachPDF does not read them. Documented in `docs/usage-examples.md` instead.
- The `AFRelationship` German restriction beyond the default: the permitted set is the union of France and
  Germany, so a French-legal `Source`/`Data` on a German invoice is accepted; the default is `Alternative`.

Don't relitigate without new information - e.g. a Schematron engine available in the base class library.
