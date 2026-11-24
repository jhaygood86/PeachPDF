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

## Reading the laid-out boxes

After layout a script can ask where things ended up. Set `Prince.trackBoxes = true`, then in a post-layout function or the `complete` handler call `element.getPrinceBoxes()`: one box for each piece of the element, which is one on each page it reaches (and one for each line an inline element wraps over). A box has `type` (`BODY` for a page's content area, `BOX`, `SPAN`, `TEXT`, `IMAGE`, `SVG`, `FOOTNOTES`), `pageNum` (from 1), `x`, `y`, `w` and `h` in points with the origin at the lower left of the page, the used `marginTop`/`marginRight`/`marginBottom`/`marginLeft`, `padding…` and `border…` widths, `pseudo` (`before`, `after`, `marker`... for generated content, else null), `text` and `src` (for a text run and an image), and `element`, `parent` and `children`, which are read when asked for. A host written in C# reads the same tree from `ScriptLayout.Pages` and `ScriptLayout.GetBoxes(element)` (a `BoxReport` has its position from the top left, as everything else in PeachBrackets does).

`LINE`, `COLUMN`, `FLEXLINE` and `FLOATS` boxes are not reported, and a box has no `baseline`: text sits in the box that owns it, one `TEXT` box for each line of it, and a float or a column is an ordinary box of its element. The text of a box comes before its child boxes whatever their order in the source.

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

A script reads and assigns the properties, calls the methods, and listens to events with `Settings.addEventListener('ready', fn)` or `Settings.onready = fn`; `settings.Raise("ready", detail)` sends one. A method that throws reaches the script as an `Error` it can catch. The object is told when the scripts are about to run (`ScriptsStarting`, with the `ScriptDocument` it can read), when they have run (`ScriptsCompleted`), after each layout (`LayoutCompleted`) and when the layout is final (`LayoutFinished`), and when the document is closed (`Closed`). Subscribing to the layout events keeps the script document live, as a post-layout function does. Each object serves one document. The `Log` object is made this way.

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
  `atob`/`btoa`, `CSS.supports`, `innerWidth`/`innerHeight`, and inert `MutationObserver`/`ResizeObserver`/`IntersectionObserver` constructors.
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
- **Events**: `addEventListener`/`removeEventListener`/`dispatchEvent` with capture, bubble, `once` and `handleEvent`; the `on<type>` properties; handler
  attributes (`onclick="..."`, and the body's `onload`); `Event`, `CustomEvent` and the other event constructors; `DOMContentLoaded` and `load`.

## Limits

- A page's scripts run once at load. Nothing replays input into the document afterwards, so there is no pointer or keyboard event unless a script
  dispatches one itself.
- A document with scripts that changed it is written back out as HTML and loaded again; constructs the HTML parser cannot round-trip exactly (some
  foreign content in `<svg>` and `<math>`) may differ. A page the scripts did not change keeps its own source.
- `<script type="module">` has no `import`; Web APIs outside the DOM (`fetch`, `XMLHttpRequest`, `Worker`, canvas, Web Animations, custom elements,
  shadow DOM) are not provided; `document.cookie` and `location` are inert.
- The document is the HTML tree PeachBrackets lays out, so the parts of the DOM standard that have no box in it are missing: comments, processing instructions and doctypes as nodes, `Attr` nodes and the namespaced attribute methods, `Range` and `Selection`, `MutationObserver` (the constructor exists but never reports), `DOMParser` and `document.implementation.createHTMLDocument`/`createDocument` (a script cannot make another document), shadow DOM and custom elements. Lists such as `childNodes` and `getElementsByTagName` are `NodeList`/`HTMLCollection` snapshots, not live.

## Testing

Besides its unit tests, the package is checked against **real React 18**: a smoke test renders a component tree and runs a component with state, an
effect timer and a click through React's delegated event system, in the Jint host. The two React UMD builds are fetched once into
`~/.peachbrackets-react` (never committed), and the test skips when they cannot be fetched.

## Testing against web-platform-tests

The WPT runner's `--scripting` option runs the reftests that need script with this package, and its `--testharness` option runs the testharness.js tests of the DOM standard (`dom/`) through it, counting passing subtests against a floor; see [Testing](testing.md). The DOM interfaces that exist because those tests asked for them are `DOMException` with its legacy codes, `NodeList`, `HTMLCollection`, character data (`substringData`, `splitText`...), `compareDocumentPosition`, `createElementNS`, `DOMTokenList` to the standard's rules, `AbortController`/`AbortSignal`, `NodeFilter`, `TreeWalker`, `NodeIterator`, and `document.createEvent`/`initEvent`.
