# An inline-block with a `transform` or `opacity` inside a footnote body is not painted

An `inline-block` (or `<img>`) inside a `float: footnote` body that is a stacking context, because of a `transform`
or an `opacity` below 1, is not painted at all; its space is reserved in the footnote line. Plain text and a plain
inline-block in the same body are painted, and so is the same box in a running element or the page body.

```html
<style>@page { size: a6; margin: 12mm; } .fn { float: footnote; }</style>
<p>Body<span class="fn">note A <span style="display:inline-block;width:12pt;height:12pt;background:#c33;transform:rotate(30deg)"></span> end</span></p>
```

Rasterized in PDFium and MuPDF: "note A  end" with a gap. `opacity: .99` alone does the same, so it is not about the
transform pivot, and it is identical on `main` before the whole-box rectangle fix for inline-flowed boxes (which
only repaired running elements, whose fragments come from the same `MarginBoxContentFragmentBuilder`). The
footnote area is painted through `PdfGenerator.PaintFootnoteArea` / `PaintDetached`; where the stacking-context
inline child is dropped on the way has not been traced.

Filed as [issue #1690](https://github.com/jhaygood86/PeachPDF/issues/1690).
