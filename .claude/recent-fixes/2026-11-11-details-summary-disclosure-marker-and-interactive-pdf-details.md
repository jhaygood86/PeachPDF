# details/summary: a disclosure marker, and a closed details a PDF reader can open

Issue: the first `<summary>` drew no triangle (HTML Standard 15.5.4). It also grew into making details work in PDF and on screen.

## What changed

- **Marker.** UA sheet (`CssDefaults.cs`): `details > summary:first-of-type { display: list-item; list-style: disclosure-closed inside }`, `details[open] > ...` with `disclosure-open`. `EnsureListItemMarkers` then makes the marker like any list item's.
  - The disclosure styles are drawn as **vector triangles** (`CssBoxMarker` shape `disclosure-open|closed`, `MarkerFragmentPainter.PaintDisclosureTriangle`), not as the U+25B8/U+25BE glyphs they were for a `li`. Found by running it: a PDF/A document whose font lacks the glyph throws `PdfAConformanceException` (no `.notdef`). The shape's box includes the gap after the triangle because a summary's inside marker has no `::marker` margin.
  - **Do not add a `summary::marker` UA rule.** Marker boxes are synthesized from the *subject compound* of a `::marker` rule at selector-match time, before the `details >` combinator is checked, so a loose `<summary>` (first of its type) got a disc.
- **Screen.** `HtmlDocument.GetDetailsToggledBy` / `ToggleDetailsAsync` (a click inside a summary, not on a link or control in it) are wired in the WinUI view (pointer release and the Invoke pattern) and in the Apple engine (`TapForm`/`ActivateControl`). The Apple change has not been built (needs a Mac). Android has no form handling; its host handles `Tapped` itself.
- **PDF.** `HtmlDocument.GetDisclosuresAsync`/`PaintDisclosureAsync` lay the source out a *second time* with every closed details forced open (`AttributeOverrides`, the same mechanism a host's `open` edit uses) and paint each details box from that twin onto a widget appearance; `PdfFormBuilder.AddDisclosureAsync` writes a hidden push-button widget (the content) plus a transparent push button over the summary. `PdfGenerateConfig.EnableInteractivePdfDetails` (default on; off for PDF/A, PDF/X, PDF/UA).

## Traps found by running it (PDFium via pypdfium2 form fill)

- PDFium resolves a `Hide` action's `/T` against **form field names**, so the content must be a form field (a widget), not a plain annotation. A link annotation's action is never run by PDFium; use a push button widget.
- `Hide` has no toggle: `/H true|false` only. Open = `Hide(content, false)` chained with `Hide(open button, true)` through `/Next`; the content widget carries the reverse chain. PDFium runs `/Next` chains.
- A second, stacked "close" button never worked: PDFium hit-tests widgets in array order, not z-order, so the visible open button (or the content) got the click. The content widget therefore closes itself, and there is no second button.
- A hidden widget needs only `/F` Hidden; adding NoView stops it showing when a `Hide` shows it.
- MuPDF runs no `Hide` actions: it shows the closed page, which is the correct static result.

## An open details can be collapsed too

`GetDisclosuresAsync` also offers each open details (one page, no nested details, at most 16): a twin of the document with just that details closed gives (a) the collapsed heading (the closed details box) and (b) the rest of the content area below it as the closed layout lays it out. Widgets: a visible transparent collapse button over the heading; a hidden collapsed-heading widget (clicking it re-opens) and a hidden inert body widget. The heading widget and the collapse button share a rect but are never visible together, so PDFium's array-order hit test does not matter; the body starts below the heading so it never overlaps them. Verified click-by-click in PDFium (open to collapsed to open); MuPDF shows the open page.

Limits: the shown collapsed page covers links and other widgets below the heading, content that the closed layout would pull up from the next page is not shown, and every open details costs a layout.

## Widget order matters (found in the showcase)

A closed details' opened content reaches over the details below it. With the closed ones first in the page's annotation list, the open one's collapsed page drew over the opened content, and PDFium let the closed one's *hidden* content widget, which covers the open one's heading, take the click on that heading (a collapsed heading could not be re-opened by clicking its marker). `GetDisclosuresAsync` therefore lists the open ones first and the closed ones after them (later is drawn on top). In a reader that hit-tests in array order the opened content of a closed details is still under the open one's buttons where they overlap; accepted.

## Not done

- No reflow when opened; the opened picture is clipped at the page bottom and its text and links are not live.
- Nested closed details are not offered.
