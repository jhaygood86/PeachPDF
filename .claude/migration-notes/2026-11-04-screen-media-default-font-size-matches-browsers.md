# A screen document starts from the browsers' type size

**Before.** Every document, paged or continuous, took `font-size: medium` as 11pt (about 14.7px), with the keywords a fixed 2-4pt apart. A page written for a
browser that sets no size, or sizes things in `rem`/`em`, laid out about 8% smaller than in Chrome (`1rem` was 14.7px, not 16px).

**Now.** When the document's media is `screen` (`HtmlDocumentOptions.MediaType = MediaType.Screen`, which is what `HtmlView` uses), `medium` is 16px (12pt) and
the absolute-size keywords follow the browsers' scale: `xx-small` 9px, `x-small` 10px, `small` 13px, `large` 18px, `x-large` 24px, `xx-large` 32px. `1rem` is
16px when the root sets nothing. A paged document (the PDF default) is unchanged: `medium` stays 11pt. `smaller`/`larger` still step 2pt from the parent.

Not verified against `v0.9.26-preview1`: the screen medium did not exist as a continuous surface in that tag's public API.
