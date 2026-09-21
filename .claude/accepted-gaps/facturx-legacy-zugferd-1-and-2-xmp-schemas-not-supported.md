# Factur-X: only the current `fx` XMP schema - not ZUGFeRD 1.0 / 2.0

`FacturXOptions` writes the Factur-X 1.0x / ZUGFeRD 2.1-and-later XMP schema
(`urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#`, prefix `fx`) and always names the embedded file
`factur-x.xml` (`xrechnung.xml` for XRECHNUNG). The legacy schema for ZUGFeRD 2.0
(`urn:zugferd:pdfa:CrossIndustryDocument:invoice:1p0#`, prefix `zf`, `zf:Version` `2p0`, file
`zugferd-invoice.xml`) and ZUGFeRD 1.0 are not produced.

## Why

The specification (1.09.2 §6.3.2) itself marks the ZUGFeRD 2.0 settings "legacy" and "deprecated", and its
HYBRID code list allows the old `fx:Version` values (`1p0`, `2p0`, `2p1`, `2p2`) only "with a warning" for a
document in the current validity period. The German mandate (2025 onwards) and the French one (September 2026)
both target the current format; nobody generating new invoices needs the old container, and each legacy variant
would add a schema block, a file name and a version rule to keep tested.

`fx:Version` is always `1.0`, the code list's default for the current specification.
