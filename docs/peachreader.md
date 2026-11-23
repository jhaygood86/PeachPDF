---
title: PeachReader
layout: default
---

# PeachReader

PeachReader is a PDF reader built on PeachPDF's own libraries. It opens a PDF with `PeachDrawing.PDF`, reads its pages, table of contents,
links and text, and draws each page with the native, hardware-accelerated drawing backend of the platform it runs on: **Win2D** on Windows,
**Skia** on Android and **CoreGraphics** on iOS and Mac Catalyst (the app runs on Windows, Android, iOS and Mac Catalyst). It is a
.NET MAUI app, and the window is laid out the way GNOME Papers lays out its own: a sidebar on the left and the pages on the right.

## What it does

| | |
|---|---|
| **Pages** | Every page at its own size and orientation, as a sheet of paper (white, with a 1 px border and a soft shadow) on a desk, scrolled continuously. Fit width, fit page, or a zoom from 10% to 800%. |
| **Contents** | The document's own outline as an expandable tree. The section you are reading is shown in bold. |
| **Pages tab** | A thumbnail of each page; a click goes there. While a search is shown it says how many matches each page has. |
| **Bookmarks** | Your own bookmarks (Ctrl+D), kept per document in the app's data folder and found again by a hash of the file, so they follow it when it is renamed or moved. They are not written into the PDF. |
| **Links** | A link to another place in the document scrolls there and can be undone with Back; a link to a web, mail or telephone address asks first and then opens it. Any other kind of address is refused. |
| **Selection and copy** | Drag to select across lines and pages, double-click for a word, triple-click for a line, Ctrl+A for everything and Ctrl+C to copy. |
| **Find** | Ctrl+F finds text in the whole document, with a match count, next and previous (F3, Shift+F3), match case and whole word. Every match is highlighted and the current one is a different color. |
| **Protected files** | A file with a password asks for it. |

### Touch (Android, iOS and Mac Catalyst)

| Gesture | |
|---|---|
| Drag, fling | Scroll |
| Pinch | Zoom around the fingers |
| Double tap | Zoom in, or back to the width of the screen |
| Tap | Follow a link; clear a selection |
| Long press | Select the word; keep the finger down and move it to extend the selection; lifting it shows Copy, Select all and Clear |
| Menu button, or a link in another app | The sidebar covers the pages on a phone and goes away when something in it is chosen; a PDF opened from a file manager, a mail attachment or a browser opens here |

### Keyboard

| Keys | |
|---|---|
| Ctrl+O | Open a file |
| Ctrl+F, F3, Shift+F3 | Find, next, previous |
| Ctrl+D | Add or remove a bookmark at the place you are reading |
| F9 | Show or hide the sidebar |
| Ctrl++ / Ctrl+- / Ctrl+0 / Ctrl+9 | Zoom in, zoom out, fit width, fit page; Ctrl and the mouse wheel zooms around the pointer |
| Alt+Left / Alt+Right | Back and forward after following a link |
| Page Up, Page Down, Space, Home, End, arrows | Scroll |

## How it is built

`src/reader/` holds two projects:

- **`PeachReader.Core`** is everything the reader decides that is neither drawing nor windowing. It has no UI reference and reads PDFs only
  through the public API of `PeachDrawing.PDF`: `ReaderDocument` (a `PdfPageSource` with its links and text kept), `OutlineTree`,
  `PageLayoutModel` (where each page sits in the scrolled content at a given zoom), `LinkResolver`, `NavigationHistory`, `BookmarkCollection` and
  `BookmarkStorage`, `TextSelectionModel`, `DocumentSearch`, and `ViewportController`, which turns the size of the view, its scroll position and
  the pointer into the cursor to show, a selection, a followed link, a highlight and a request to scroll. Its tests need no window.
- **`PeachReader`** is the MAUI app: the window, the sidebar and the find bar, and one platform view per backend. The Windows one is a Win2D
  `CanvasVirtualControl` as large as all the pages stacked, inside a scroll viewer, that paints only the regions the system asks for, plus a second
  control above it for the selection and search highlights, which move with the pointer and so must not repaint the pages. The Android one is a Skia
  GL surface (`SkiaSharp.Views`' `SKGLSurfaceView`, not the MAUI wrapper) that keeps each page as 1024 px tiles in graphics memory, painting a tile
  with `PdfPageSource.Paint` through `SkiaCanvas` the first time it comes into view (two a frame at most) and drawing the paper, the highlights and
  the tiles on every frame; scrolling, fling and pinch are its own, over the same `ViewportController`. The iOS and Mac Catalyst one is a single UIKit
  view (`UIScrollView`) over a content view that holds one `CALayer` sheet per visible page and, on it, 1024 px tile layers painted through
  `CoreGraphicsCanvas` the first time they come into view (three a pass at most), so Core Animation composites scrolling and nothing repaints while
  a finger moves; tap, double tap, long press and pinch are its own, and on a Mac the pointer drags to select, hovers and shows the text cursor.
  PDFs the system opens (Files, Finder, Open In) arrive through the document type the app registers for PDF.

A page is painted by `PdfPageSource.Paint` onto a `PeachDrawing.Core` canvas under the zoom, so the same call serves every backend.

## Building and running

The app needs the MAUI workloads and is not in `PeachPDF.slnx`; `PeachReader.Core` and its tests are.

```powershell
dotnet build src/reader/PeachReader/PeachReader.csproj -f net11.0-windows10.0.19041.0
src/reader/PeachReader/bin/Debug/net11.0-windows10.0.19041.0/win-x64/PeachReader.exe report.pdf
```

A file named on the command line, or in the `PEACHREADER_OPEN` environment variable, is opened at start.

For Android, `net11.0-android` needs the Android SDK platform for API 37 (`dotnet build -t:InstallAndroidDependencies -f net11.0-android` installs
it). To run on an emulator or device:

```powershell
dotnet build src/reader/PeachReader/PeachReader.csproj -f net11.0-android -p:RuntimeIdentifier=android-x64 -p:EmbedAssembliesIntoApk=true -p:AndroidPackageFormat=apk -t:Install
```

For iOS and Mac Catalyst a Mac with the `ios`, `maccatalyst` and MAUI workloads is needed (`-p:ValidateXcodeVersion=false` when Xcode is newer than the workload). The Android head is left out of the target list on a Mac unless `-p:ReaderIncludeAndroid=true` is given:

```bash
dotnet build src/reader/PeachReader/PeachReader.csproj -f net11.0-maccatalyst -p:ValidateXcodeVersion=false
PEACHREADER_OPEN=report.pdf src/reader/PeachReader/bin/Debug/net11.0-maccatalyst/maccatalyst-arm64/PeachReader.app/Contents/MacOS/PeachReader
```

With `PEACHREADER_SMOKE` set, the Apple view logs `PEACH_SMOKE` lines (the first tile painted and its size), which the CI check reads.

## Limits

- Text can be selected and searched only where the file says what a glyph stands for (a `ToUnicode` map or a standard encoding). The reading
  order is the order the file draws the text in, which is reading order for most producers but not for every multi-column layout. There is no
  recognition of text in scanned pages.
- A font that is not embedded is drawn with an installed one chosen by its name; the layout follows the file's own widths, but the shapes differ.
  CJK text in the predefined character sets is drawn with an installed CJK family when its font is not embedded, so a machine with no CJK font shows gaps. What a page could not show, or had to stand in for, is
  listed when you click the message in the status bar.
- Files encrypted with RC4 are not opened.
- Form fields are not interactive. Layers the file turns off are hidden, and pages show their labels ("iv", "A-3") where the file defines them; the page box accepts a label or a number.
- Only links to a place in the document and to web, mail and telephone addresses are followed.
- Pinch zoom on iOS and Mac Catalyst applies when the fingers lift (no live preview), the Mac pointer shows the text cursor but not a hand over links, and the macOS menu bar and keyboard shortcuts are not wired up yet. A page tile is painted on the graphics thread, so the first look at a page at a new zoom can pause a
  frame.
