# PeachBrackets.Scripting

`PeachBrackets.Scripting` runs the JavaScript of an HTML document with the [Jint](https://github.com/sebastienros/jint) interpreter, so a page that
builds itself with a script (a list filled from data, a chart drawn with the DOM, a layout fixed up by a few lines of code) renders as it would in a
browser. It is a pure .NET package: no browser, no external process, and it is trimmable and Native AOT compatible.

Scripting is **off by default**, and [PeachBrackets](peachbrackets.md) and [PeachPDF](getting-started.md) do not depend on this package. Without a
script host, `<script>` elements are ignored and `<noscript>` content is rendered. Add the package and set a host to turn scripting on.

```bash
dotnet add package PeachBrackets.Scripting
```

```csharp
using PeachBrackets.Scripting;

// PeachPDF
var config = new PdfGenerateConfig { ScriptHost = new JintScriptHost() };
var pdf = await new PdfGenerator().GeneratePdf(html, config);

// PeachBrackets, with no PDF involved
var options = new HtmlDocumentOptions { ScriptHost = new JintScriptHost() };
using var document = await HtmlDocument.LoadAsync(html, renderContext, options);
```

The command-line tool has the same switch: `peachpdf --enable-scripting page.html -o page.pdf`.

## How it runs

Scripts run **while the document loads**: after the HTML is parsed and before any style sheet is applied. They run in document order (`defer` and
`async` external scripts after the rest), then `DOMContentLoaded` and `load` are fired. What the scripts leave in the document is what is styled,
laid out and painted; a page whose scripts change nothing is loaded from its own source.

A document has no wall clock, so timers and animation frames run on a **virtual clock**: `setTimeout`, `setInterval` and `requestAnimationFrame`
callbacks are played in order of their time, as fast as they run, until none is left. A page that fills itself in from a `setTimeout`, or a
reftest-style page that removes a class when it is ready, is complete when it is laid out. The limits below keep a timer that sets itself again from
running forever.

Each document gets its own JavaScript engine, so one `JintScriptHost` may be shared by any number of documents, including ones loading at the same time.

## After layout

A document is laid out after its scripts have run, and a script can ask to be called then. `Prince.registerPostLayoutFunc(fn)` registers a function that runs once after the next layout; if it changes the document, the document is laid out again and the functions registered meanwhile run after that, until a pass changes nothing and registers nothing, or the pass limit is reached (`HtmlDocumentOptions.MaxLayoutPasses`, `PdfGenerateConfig.MaxLayoutPasses` and `peachpdf --max-passes`; default 16, 0 for no limit, and a change made on the last allowed pass is not laid out). A function that registers itself and changes the document on every pass therefore runs until the limit. `Prince.pageCount` is the number of pages of the last layout (0 before the first). When the layout is final, `complete` fires on `Prince` (`Prince.addEventListener('complete', fn)` or `Prince.oncomplete = fn`) once; a change made in it is not laid out. A document keeps its scripts' DOM and engine alive only when a script registered one of these; otherwise they are released when the scripts finish. `document.readyState` changes fire `readystatechange`.

A host built on PeachBrackets gets the same through `IScriptHost.LayoutCompletedAsync` (called after each layout; return true to be called again) and `LayoutFinishedAsync`, with `ScriptDocument.KeepAlive` set from `RunAsync` to ask for them.

## Text a script computes: `prince-script()`

A style sheet can ask a script for text. `Prince.addScriptFunc(name, fn)` registers a function, and the content item `prince-script(name, item, item...)` calls it while the document is laid out: each `item` is a content item (a string, `counter()`, `counters()`, `attr()`, `string()`...) resolved to text, those texts are the function's arguments, and what it returns is inserted as text. It works wherever a content list does: `content` of a box or pseudo-element, the margin boxes of `@page` rules (where `counter(page)` and `counter(pages)` are the page being made and the page count), and `bookmark-label`.

```css
li::marker { content: prince-script(words, counter(list-item)) "." }
@page { @top-center { content: prince-script(stamp, counter(page), counter(pages)) } }
```

```js
Prince.addScriptFunc('words', n => ['one', 'two', 'three'][n - 1] || n);
Prince.addScriptFunc('stamp', (page, pages) => 'page ' + page + ' of ' + pages);
```

The function runs synchronously and is called each time the text is made, so it is called again when the document is laid out again. A name with no function, or no script host, gives empty text; a function that throws is logged and gives empty text. A document that registered a function stays live, as for post-layout functions, so the function is there when layout asks. A `string-set` value and a PDF page label are not made from it yet.

## Reading the laid-out boxes

After layout a script can ask where things ended up. Set `Prince.trackBoxes = true`, then in a post-layout function or the `complete` handler call `element.getPrinceBoxes()`: one box for each piece of the element, which is one on each page it reaches (and one for each line an inline element wraps over). A box has `type` (`BODY` for a page's content area, `BOX`, `SPAN`, `TEXT`, `IMAGE`, `SVG`, `FOOTNOTES`), `pageNum` (from 1), `x`, `y`, `w` and `h` in points with the origin at the lower left of the page, the used `marginTop`/`marginRight`/`marginBottom`/`marginLeft`, `padding…` and `border…` widths, `pseudo` (`before`, `after`, `marker`... for generated content, else null), `text` and `src` (for a text run and an image), and `element`, `parent` and `children`, which are read when asked for. A host written in C# reads the same tree from `ScriptLayout.Pages` and `ScriptLayout.GetBoxes(element)` (a `BoxReport` has its position from the top left, as everything else in PeachBrackets does).

`LINE`, `COLUMN`, `FLEXLINE` and `FLOATS` boxes are not reported, and a box has no `baseline`: text sits in the box that owns it, one `TEXT` box for each line of it, and a float or a column is an ordinary box of its element. The text of a box comes before its child boxes whatever their order in the source.

## The PDF object

With PeachPDF and a script host, a script can set properties of the PDF it is turned into. `PDF` and `Prince.failStatus` are added by PeachPDF itself through the mechanism below, so they exist only when PeachPDF runs the document:

| Member | Effect |
|---|---|
| `PDF.title`, `subject`, `author`, `keywords`, `creator`, `lang` | The document information and language. They replace what the HTML says (`<title>`, `<meta>`, `<html lang>`) and give way to a value set in `PdfGenerateConfig.Metadata`. |
| `PDF.pageMode` | `auto`, `show-bookmarks`, `show-thumbnails`, `fullscreen` or `show-attachments`: what a viewer shows beside the pages. |
| `PDF.pageLayout` | `auto`, `single-page`, `one-column`, `two-column` (or `two-column-left`), `two-column-right`, `two-page` (or `two-page-left`), `two-page-right`. |
| `PDF.openAction` | `print`, `fit-page` or `fit-width` (also written `zoom(fit-page)`, `zoom(fit-width)`). `print` is refused for a PDF/A or PDF/X file when the file is configured. |
| `PDF.printScaling`, `duplex`, `displayDocTitle` | The viewer preferences: `auto` or `none`; `auto`, `simplex`, `duplex-flip-short-edge`, `duplex-flip-long-edge`; and a boolean. |
| `PDF.encrypt`, `userPassword`, `ownerPassword`, `allowPrint`, `allowModify`, `allowCopy`, `allowAnnotate` | Encrypts the file (AES-256) with those passwords and permissions. A script can ask for encryption only when `PdfGenerateConfig.Encryption` is null; it never changes or removes a configured one. `PDF.keyBits` reads `256`, and assigning any other value is an error. |
| `PDF.compress`, `PDF.xmpMetadata` | Whether content streams are compressed; whether the XMP packet is written. |
| `PDF.attachFile(url, description)` | Attaches a file, read when the layout is final through the document's loader (a relative address is resolved against the document's). |
| `PDF.pages` | The boxes of the final layout, one tree per page, as `getPrinceBoxes` describes them (`type`, `pageNum`, `x`, `y`, `w`, `h`, `text`, `src`, `pseudo`, `element` and `children`); null before the document is laid out, so read it in a `complete` handler. |
| `Prince.failStatus` | Set it to true (for example in a `complete` handler that finds content overflowing) and the conversion ends with `PdfFailSafeException` and no PDF; the CLI reports it as an error and exits 1. |

A property that takes one of a few names throws for any other, so the script finds out as it runs. The settings a script leaves unset fall back to `PdfGenerateConfig` and the document. `PDF.profile`, `outputIntent`, `embedFonts`, `subsetFonts`, `artificialFonts`, `objectStreams`, `colorConversion` and the other members other print engines have are not provided: the conformance level and color setup are decided before the scripts run, and fonts are always embedded and subset.

## Adding your own objects

A host adds JavaScript objects to the scripts without referencing Jint or this package. A `ScriptApi` (in `PeachBrackets`, namespace `PeachBrackets.ScriptHosting`) is a named global with properties, methods and events, built from plain C# delegates and `ScriptValue`s (undefined, null, boolean, number, text, arrays, objects, functions and nodes convert both ways). Put it in `HtmlDocumentOptions.ScriptApis` (PeachPDF: `PdfGenerateConfig.ScriptApis`) and the script host installs it before the scripts run:

```csharp
var settings = new ScriptApi("Settings")
    .AddProperty("title", () => title, value => title = value.AsString() ?? "")
    .AddMethod("greet", args => "hello " + args[0].AsString())
    .AddEvent("ready");

settings.LayoutFinished += layout => Console.WriteLine(layout.PageCount);
options.ScriptApis.Add(settings);
```

A script reads and assigns the properties, calls the methods, and listens to events with `Settings.addEventListener('ready', fn)` or `Settings.onready = fn`; `settings.Raise("ready", detail)` sends one. A method that throws reaches the script as an `Error` it can catch. The object is told when the scripts are about to run (`ScriptsStarting`, with the `ScriptDocument` it can read), when they have run (`ScriptsCompleted`), after each layout (`LayoutCompleted`) and when the layout is final (`LayoutFinished`), and when the document is closed (`Closed`). Subscribing to the layout events keeps the script document live, as a post-layout function does. Each object serves one document. The `Log` object is made this way, and so are PeachPDF's `PDF` object and `Prince.failStatus`. An object named like a global that already exists (`Prince`) adds its members to it instead of replacing it.

## Options

`JintScriptHost` takes a `JintScriptOptions`:

| Option | Default | Meaning |
|---|---|---|
| `ScriptTimeout` | 5 s | The longest one script (or timer or event callback) may run. |
| `MaxRecursionDepth` | 400 | How deep a script's calls may nest. |
| `MemoryLimitBytes` | 256 MiB | The most memory a document's scripts may allocate. |
| `MaxVirtualTime` | 10 s | How much virtual time the timers may take to settle; later timers never fire. |
| `MaxCallbacks` | 10,000 | The most timer and frame callbacks one document runs. |
| `LoadExternalScripts` | true | Fetch and run `<script src>` through the document's resource loader. |
| `RunModuleScripts` | false | Run `type="module"` scripts as ordinary scripts (there is no `import`). |
| `Logger` | none | An [`ILogger`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.logging.ilogger) (from `Microsoft.Extensions.Logging.Abstractions`) that receives everything the scripts say and everything that goes wrong; see below. |
| `RunAuthorScripts` | true | Run the document's own `<script>` elements. Set it to false to run only `UserScripts`. |
| `UserScripts` | empty | Scripts (`UserScript(name, code)`) the host supplies; they run in order before the document's own scripts, whether or not those run. |
| `PrinceCompatibility` | true | Also offer the `Log` object (below) that other print engines give their scripts. |

A script that fails is logged and skipped; the document still loads with whatever the scripts did before.

## Logging

All script output goes to `JintScriptOptions.Logger`; with none set it is discarded. `console.log` and `console.info` are logged at `Information`, `console.warn` at `Warning`, `console.error` and a failed `console.assert` at `Error`, and `console.debug` and `console.trace` at `Debug`. A script that throws, fails to parse, hits a limit or cannot be fetched is logged at `Error` under `ScriptLogEvents.ScriptFailed` or `ScriptLoadFailed`. The `Log` object offers `Log.debug`, `Log.info`, `Log.warning` and `Log.error` at the matching levels, and `Log.data(name, value)`, which passes a named value to the process that runs the document: it is an `Information` entry under `ScriptLogEvents.Data` whose state is a `ScriptDataEntry` (a list of the `name` and `value` pairs a structured logger records).

`peachpdf --script=FILE` (repeatable) runs a file as a user script; used alone it leaves the document's own scripts off, and with `--enable-scripting` both run. The CLI writes script output to its log: standard error with `--verbose` or `--debug` (warnings and errors always), and the `--log` file.

## The DOM scripts see

- **`window`**: `document`, `location`, `navigator`, `history`, `console`, `performance`, timers and animation frames, `getComputedStyle`, `matchMedia`,
  `atob`/`btoa`, `CSS.supports`, `innerWidth`/`innerHeight`, `MutationObserver`, `getSelection`, and inert `ResizeObserver`/`IntersectionObserver` constructors.
- **`document`**: `documentElement`, `head`, `body`, `title`, `readyState`, `currentScript`, `getElementById`, `getElementsByTagName`/`ClassName`/`Name`,
  `querySelector(All)`, `createElement`, `createTextNode`, `createDocumentFragment`, `importNode`, `write`, `fonts.ready`.
- **Nodes and elements**: navigation (`parentNode`, `children`, `firstChild`, `nextElementSibling`...), `appendChild`, `insertBefore`, `removeChild`,
  `replaceChild`, `append`, `prepend`, `before`, `after`, `remove`, `replaceWith`, `cloneNode`, `contains`, `textContent`, `innerHTML`, `outerHTML`,
  `insertAdjacentHTML`/`Element`/`Text`, attributes (`getAttribute`, `setAttribute`, `removeAttribute`, `toggleAttribute`, `dataset`, and the common
  reflected properties such as `id`, `className`, `href`, `src`, `value`, `hidden`, `disabled`), `classList`, `style` (any property, by camel-case or CSS
  name, with `setProperty` and `cssText`), `matches`, `closest`.
- **Layout reads**: `getBoundingClientRect`, `offsetWidth`/`Height`/`Left`/`Top`, `clientWidth`/`Height`, `scrollWidth`/`Height` and `getComputedStyle` read
  the document **as the script has left it so far**: the first read after a change lays the document out again (once per batch of changes, however many
  changes there were). PeachPDF supplies the canvas to measure on; a host using PeachBrackets directly sets `HtmlDocumentOptions.MeasuringCanvasFactory`,
  and without it layout reads answer zero and computed styles come from the element's own `style` attribute.
- **Node kinds**: comments, processing instructions, CDATA sections (XML documents) and document types are nodes (`createComment`, `createProcessingInstruction`, `createCDATASection`, `implementation.createDocumentType`, `document.doctype`); attributes are `Attr` nodes (`attributes`, `getAttributeNode`, `setAttributeNode`, `createAttribute`) with the namespaced methods (`setAttributeNS`, `getAttributeNS`, `lookupNamespaceURI`...).
- **Other documents**: `document.implementation.createHTMLDocument`/`createDocument`, `new Document()`, `DOMParser` (HTML and XML), an `<iframe>`'s `contentDocument` (loaded from its `src` through the document's network when a script first asks), and moving nodes between documents with `adoptNode`, `importNode` or any insertion. The documents share the page's resource loader and are never laid out.
- **Live lists**: `childNodes`, `children` and `getElementsByTagName`/`ClassName`/`Name`/`TagNameNS`, `scripts`, `forms`, `images` and `links` follow the tree.
- **`MutationObserver`**: `childList`, `attributes` (with `attributeFilter` and old values), `characterData` and `subtree`, delivered on a microtask, with the transient observers that keep a removed subtree watched until then.
- **`Range` and `Selection`**: live ranges that follow insertions, removals and text changes, `extractContents`, `cloneContents`, `deleteContents`, `insertNode`, `surroundContents`, the comparison methods, and one document selection.
- **Events**: `addEventListener`/`removeEventListener`/`dispatchEvent` with capture, bubble, `once` and `handleEvent`; the `on<type>` properties; handler
  attributes (`onclick="..."`, and the body's `onload`); `Event`, `CustomEvent` and the other event constructors; `DOMContentLoaded` and `load`.

## The network scripts see

`XMLHttpRequest`, `fetch`, `Request`, `Response`, `Headers`, `URL`, `URLSearchParams`, `Blob`, `File`, `FormData`, `FileReader`, `TextEncoder` and `TextDecoder` are provided. They are defined by a script bundle that is evaluated the first time a script names one of them, so a document that uses none pays nothing. Requests go to `HtmlDocumentOptions.ScriptNetwork` (an `IScriptNetwork`: method, URL, headers and body in, status, headers and body out); without one a script can only `GET` what the document's resource loader can fetch, and `data:` addresses are read in the package. Answers are delivered as timers: the virtual clock does not move past a request until its answer has arrived, so a script that waits for a response with a timer sees it. A synchronous `XMLHttpRequest` blocks the calling thread on the answer. `URL` parses host names with UTS #46 (non-transitional, from a table generated from the Unicode data), and `a` and `area` elements have the URL parts of their `href` (`protocol`, `host`, `pathname`, `search`, `hash`, `origin`...), resolved against the document's `<base href>`. `fetch` follows the Fetch standard's CORS rules for a request to another origin: `mode: "same-origin"` rejects it, `no-cors` answers an opaque response (type `opaque`, status 0, no body), and `cors` (the default) sends the page's `Origin`, makes a preflight `OPTIONS` request first when the method or a header is not CORS-safelisted (`Access-Control-Request-Method`/`-Headers`, `Accept: */*`), requires `Access-Control-Allow-Origin` (and `Access-Control-Allow-Credentials` with `credentials: "include"`) on the answer, and shows the script only the CORS-safelisted headers and those named by `Access-Control-Expose-Headers`. Redirects are followed by the script bundle hop by hop (at most 20): `manual` answers an `opaqueredirect` response, `error` rejects, `301`/`302` of a `POST` and `303` become a `GET` without the body, and each cross-origin hop is checked and preflighted again. Credentials are not modeled by the package (cookies are the network's business), and the HTTP cache modes are ignored. `ReadableStream` (with `tee`, `pipeTo`, `pipeThrough`, `values()`/async iteration and `from`), `WritableStream`, `TransformStream`, `TextEncoderStream`, `TextDecoderStream` and the queuing strategies are provided, with queues, `highWaterMark`/`size` and backpressure; a `type: "bytes"` source is read through a default reader (there are no BYOB readers). `Blob.stream()`, `response.body` and `request.body` are streams, a `ReadableStream` is accepted as a `Response`/`Request` body (an upload stream is read to its end before it is sent), and `formData()` reads `application/x-www-form-urlencoded` and `multipart/form-data` bodies, files included.

## Images scripts load

HTML elements have the properties of their interface that reflect content attributes, with the standard's rules for each type: strings, URLs (resolved against the document's base), booleans, integers (`long`, `unsigned long`, limited and clamped ones, with the `IndexSizeError` a limited setter throws), doubles, enumerated attributes (keywords matched without regard to ASCII case, with their missing and invalid defaults and nullable ones), `DOMTokenList` properties (`classList`, `relList`, `sandbox`, `htmlFor`...) and the nullable ARIA reflection (`role`, `ariaLabel`, `ariaBusy`...). The properties of an interface exist only on elements in the HTML namespace; `HTMLElement` and the other interfaces are provided for every HTML element name, and one made with `createElementNS` in another namespace is a plain `Element`. `innerText` and `outerText` are the text content, not the rendered text.

`XMLSerializer` serializes a node as XML the way the DOM Parsing standard describes (namespace declarations and prefixes worked out, `xmlns` attributes reconciled, void XHTML elements written `<br />`). `DOMParser` makes a `Document` (not an `XMLDocument`) whose `location` is `null`; an XML document that is not well formed is a document with one `parsererror` element, and `createElement` in a non-XHTML XML document makes an element with no namespace. `outerHTML` can be set, and `setHTMLUnsafe`, `appendHTMLUnsafe`, `prependHTMLUnsafe`, `beforeHTMLUnsafe`, `afterHTMLUnsafe`, `replaceWithHTMLUnsafe` and `Document.parseHTMLUnsafe` put markup in without sanitizing. `streamHTMLUnsafe` and the other `stream*HTMLUnsafe` methods return a `WritableStream` whose written text is parsed into the same place after each write (the whole text is parsed again each time, so a tag cut across two writes is seen only once complete). The sanitizing methods (`setHTML`, `appendHTML`...), shadow roots and script execution from inserted markup are not provided.

`new Image(width, height)` makes an `img` element, and setting an `img` element's `src` or `srcset` (the property or `setAttribute`) loads it, as does an `img` already in the page. The image is fetched and decoded through the document's resource loader (data: addresses included, SVG too); the result is delivered the way a network answer is, before the virtual clock moves on, as a `load` or `error` event on the element. `complete`, `naturalWidth`, `naturalHeight`, `currentSrc`, `width`, `height` and `decode()` report it. The address is chosen as the HTML standard says: `srcset` is parsed with its error rules, `sizes` is read as CSS component values (comments, escapes, every length unit, `calc()`/`min()`/`max()`/`clamp()`, media conditions with the unknown-is-false logic), `<picture>` sources are tried in order by `type` and `media`, a `src` joins as a 1x candidate, and the smallest sufficient density wins, for a screen of pixel ratio 1 and a viewport 800 by 600 px (an `iframe`'s document uses the frame's size). Choosing happens as "update the image data": a relevant mutation (`src`, `srcset`, `sizes`, a `crossorigin` or `referrerpolicy` change of state while there is a source, a source before the image changing, the image entering or leaving a picture) queues it for after the current script, and the images a page starts with are loaded, with their events delivered, before the window's `load`. `crossOrigin`, `loading`, `decoding`, `referrerPolicy`, `fetchPriority`, `useMap` and `isMap` reflect; `crossOrigin`, `loading` and `fetchpriority` have no effect on the load, and an image a script loads is decoded only to learn its size: it is not drawn into anything (there is no canvas yet).

## Canvas 2D

A `<canvas>` is a replaced element as large as its `width` and `height` attributes (300 by 150 CSS pixels by default), and `getContext('2d')` gives scripts a `CanvasRenderingContext2D`: `save`/`restore`/`reset`, the transform methods, `fillStyle` and `strokeStyle` (colors and linear, radial and conic gradients), line settings with dashes, `globalAlpha`, `globalCompositeOperation` (the blend modes and `lighter`), shadows, paths (`arc`, `arcTo`, `ellipse`, `rect`, `roundRect`, Bezier curves, `Path2D`), `fill`, `stroke`, `clip`, the rectangle methods, `drawImage` (a loaded `img` or another canvas), `fillText`/`strokeText`/`measureText`, and `imageSmoothingEnabled`.

Every call is recorded and replayed onto the page's canvas, so a PDF or SVG gets the shapes and text as **vector content**, scaled from the bitmap's size to the box's. What a script draws before layout is what the box shows; a canvas drawn on by a timer or a post-layout function after the document has been laid out is not repainted.

Pixels can be read and written: `getImageData`, `putImageData`, `createImageData`, `ImageData`, `toDataURL` (PNG), `toBlob`, `isPointInPath`/`isPointInStroke`, `createImageBitmap` (a blob, image, canvas or `ImageData`) and `OffscreenCanvas` (no workers). Reading a canvas replays its drawing onto a bitmap, so text drawn with a font from another backend may not show in it. Elements with an `id` are properties of `window`.

Not provided yet: `putImageData` does not replace pixels (it draws them over), patterns as a fill, color spaces (`display-p3`), the Porter-Duff compositing modes (`source-in`, `destination-out`, `xor`, `copy`...; they draw as `source-over`), `clearRect` of part of the canvas (clearing all of it works), `filter`, `letterSpacing`/`fontKerning` and the other text properties beyond `font`, `textAlign`, `textBaseline` and `direction`.

## Limits

- A page's scripts run once at load. Nothing replays input into the document afterwards, so there is no pointer or keyboard event unless a script
  dispatches one itself.
- A document with scripts that changed it is written back out as HTML and loaded again; constructs the HTML parser cannot round-trip exactly (some
  foreign content in `<svg>` and `<math>`) may differ. A page the scripts did not change keeps its own source.
- `<script type="module">` has no `import`; Web APIs outside the DOM (`Worker`, Web Animations, custom elements,
  shadow DOM, streams) are not provided; `document.cookie` and `location` are inert.
- Shadow DOM, custom elements, `Observable`, `StaticRange`, `moveBefore` and `ProcessingInstruction` pseudo-attributes are not provided. Namespaces are tracked per element and attribute on the script side, so a script's namespaced markup keeps its names but an XML document is written back as HTML when it is the page's own. A document made by a script is never styled or laid out (`getComputedStyle` and `getBoundingClientRect` read the page only), has no `window` unless it is an iframe's, and fires no load events.

## Testing

Besides its unit tests, the package is checked against **real React 18**: a smoke test renders a component tree and runs a component with state, an
effect timer and a click through React's delegated event system, in the Jint host. The two React UMD builds are fetched once into
`~/.peachbrackets-react` (never committed), and the test skips when they cannot be fetched.

## Testing against web-platform-tests

The WPT runner's `--scripting` option runs the reftests that need script with this package, and its `--testharness` option runs the testharness.js tests of the DOM standard (`dom/`) through it, counting passing subtests against a floor; see [Testing](testing.md). The DOM interfaces that exist because those tests asked for them are `DOMException` with its legacy codes, `NodeList`, `HTMLCollection`, character data (`substringData`, `splitText`...), `compareDocumentPosition`, `createElementNS`, `DOMTokenList` to the standard's rules, `AbortController`/`AbortSignal`, `NodeFilter`, `TreeWalker`, `NodeIterator`, and `document.createEvent`/`initEvent`.
