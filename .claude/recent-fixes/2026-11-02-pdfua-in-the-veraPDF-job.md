# The veraPDF job now validates PDF/UA too

`Test-PdfConformance.ps1` renders `.github/conformance/pdfua-sample.html` as PDF/UA-1, UA-2, UA-1 + PDF/A-2a and UA-2 + PDF/A-4 and validates each against its profile(s). The richer sample found a real defect the first one did not: collapsed table borders (`PaintCollapsedTableBorders`) were drawn between marked-content sequences, seven untagged paths (UA-1 7.1-3, UA-2 8.2.2-1). They are an artifact now.

The job is the existing `conformance` leg of `test.yml`; its path filter already covers `.github/conformance/**`, so a change to the sample or script runs it. The job's artifact keeps the PDFs and XML reports of the UA cases beside the PDF/A ones.
