# Accessibility

PeachBrackets describes a document to an assistive technology - a screen reader, a switch, a braille display - as an **accessibility tree**: the
parts of the page that mean something to a reader, each with the role it has, the name it is called by, and what state it is in. A host such as the
[WinUI](peachbrackets-winui.md) or [Android](peachbrackets-android.md) view builds the tree its platform wants (UI Automation, an accessibility node
provider) from this one.

```csharp
var document = await HtmlDocument.LoadAsync(html, renderContext);
await document.LayoutAsync(canvas);

AccessibilityTree tree = document.GetAccessibilityTree();
foreach (var node in tree.Nodes)
    Console.WriteLine($"{node.Role} \"{node.Name}\"");

AccessibilityNode? underTheFinger = document.GetAccessibilityNodeAt(pageIndex: 0, x: 120, y: 340);
```

The tree is built when it is first asked for and kept until the document is laid out again. It needs no more than layout: text, bounds and
links come from the laid-out page.

## The tree

`AccessibilityTree.Root` is the document (named by its `<title>`); the nodes below it follow the document. Each `AccessibilityNode` has:

| Member | What it is |
|---|---|
| `Role` | What it is: one of the roles of [WAI-ARIA](https://www.w3.org/TR/wai-aria-1.2/#role_definitions) (`Heading`, `Link`, `List`, `Table`, `Button`, `Navigation`...) or `Text` for text itself |
| `Name`, `Description` | The name it is called by, and more about it; see [Names](#names) |
| `Text` | The text it holds, white space collapsed |
| `ReadingText`, `ReadingChildren` | What a reader says for it, and the children it steps to - an element's own text is read as part of the element, so its text nodes are left out |
| `Level`, `Uri`, `Language`, `ElementId`, `TagName` | The heading level; where a link leads (an address, or `#name` for a place in the document); the language of its content; the element's `id` and name |
| `Checked`, `Pressed`, `Expanded`, `Selected`, `Disabled`, `Required`, `ReadOnly`, `Invalid`, `Busy`, `Modal`, `Current` | State, from the element's attributes and the elements around it |
| `Live`, `Atomic` | How changes are announced (`aria-live`, or what the role implies) |
| `IsFocusable`, `TabIndex`, `AccessKey`, `KeyShortcuts` | What a keyboard can do with it |
| `RowIndex`, `ColumnIndex`, `RowSpan`, `ColumnSpan`, `RowCount`, `ColumnCount`, `ColumnHeaders`, `RowHeaders` | Where a cell or row is in its table or grid (counted from 0), how many rows and columns it covers, how big the table is, and the cells that head a cell; see [Tables](#tables) |
| `PositionInSet`, `SetSize` | The place of a list item, option, tab, menu item or tree item among its siblings (counted from 1), and how many there are |
| `Controls`, `Owns`, `FlowTo`, `Details`, `ErrorMessages`, `LabelledBy`, `DescribedBy`, `ActiveDescendant` | The nodes that `aria-controls`, `aria-owns`, `aria-flowto`, `aria-details`, `aria-errormessage`, `aria-labelledby`, `aria-describedby` and `aria-activedescendant` point at |
| `Bounds` | One rectangle for each page it is on, in points from the top-left of the sheet |
| `Id` | A name that is the same each time the document is laid out, so a reader that is on a node stays there |

Elements that mean nothing to a reader (`div`, `span`, a `section` that has no name) are not in the tree; their content is. Content that is not
shown is not in it either: `display: none`, `visibility: hidden`, `hidden`, `inert`, and anything inside an `aria-hidden="true"` element. An element
with `role="presentation"` or `role="none"` is left out too, unless it can be focused, and so is an image whose `alt` is empty.

## Roles

An element has the role HTML gives it ([HTML-AAM](https://www.w3.org/TR/html-aam-1.0/#html-element-role-mappings)): `h1`-`h6` are headings,
`a[href]` links, `ul` and `ol` lists, `table` a table with `th` column or row headers and `td` cells, `nav`, `main`, `aside` and `search` landmarks,
`header` and `footer` banner and content information landmarks unless they are inside an `article`, `aside`, `main`, `nav` or `section`, `section`
and `form` landmarks only when they have a name, and the form controls are what their type says (`input type=checkbox` a check box, `type=range` a
slider, `type=number` a spin button, `select` a combo box or, with `multiple`, a list box...).

A `role` attribute replaces that. When it names several roles the first one that exists is used, as WAI-ARIA lets an author give a fallback.

## Names

The name of a node is the first of these that gives one (the [Accessible Name and Description Computation](https://www.w3.org/TR/accname-1.2/)):

1. the text of the elements `aria-labelledby` points at;
2. `aria-label`;
3. what HTML labels the element with: an image's `alt`, a control's `<label>` (with `for`, or around it), a button's `value`, a `fieldset`'s `<legend>`,
   a `table`'s `<caption>`, a `figure`'s `<figcaption>`, an `input`'s `placeholder`;
4. its content, for the roles that are named by it (links, buttons, headings, cells, tabs, menu items...);
5. its `title`.

The description is `aria-describedby`, then `aria-description`, then `title` if the name did not already come from it.

## SVG and MathML

An inline `<svg>` is a `GraphicsDocument` node (or `Image` when it says `role="img"`). Its name is the first of `aria-labelledby`, `aria-label` and the
text of its own [`<title>`](https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/title) child, and its description is `aria-describedby`,
`aria-description` or its [`<desc>`](https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/desc) child. An SVG with `aria-hidden="true"` is left out.

A [`<math>`](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/math) element is a `Math` node. Its name is the first of
`aria-labelledby`, `aria-label`, `alttext` and a `text/plain` `<annotation>` inside `<semantics>`; with none of them the formula is said in English from
its structure: `x² + 1` is "x squared plus 1", `a/b` is "a over b", a sum with limits is "sum from i equals 1 to n ...", a matrix is "table with 2 rows and
2 columns; row 1: a, b; row 2: c, d". Fractions, roots, scripts, limits, fences, tables, the usual operators and Greek letters are covered; anything
else is read in the order it is written.

## Tables

Cells are placed the way [HTML lays a table out](https://html.spec.whatwg.org/multipage/tables.html#forming-a-table): left to right in their row,
skipping the places a cell from a row above reaches down into, each taking as many places as its `colspan` and `rowspan` say. A header or footer
row group goes first or last whatever order the source has it in. `aria-rowindex`, `aria-colindex`, `aria-rowcount` and `aria-colcount` replace the
computed numbers for a grid that shows only part of its data.

A data cell's `ColumnHeaders` are the column headers above it that overlap its columns, and its `RowHeaders` the `th scope=row` cells before it that
overlap its rows; a cell with a `headers` attribute is headed by exactly the cells it names.

## Sets and references

The items of a list are numbered among the siblings with the same role; `aria-posinset` and `aria-setsize` replace that, and an item without a
position follows the one before it. A reference such as `aria-controls="panel"` is resolved to the node `id="panel"` is when there is one: an
element that is not in the tree (a `div` or `span` with no role, or one that is not shown) cannot be pointed at. `aria-owns` is reported but the
tree is not rearranged to match it.

## State

`aria-checked`, `aria-pressed`, `aria-expanded`, `aria-selected`, `aria-disabled`, `aria-required`, `aria-readonly`, `aria-invalid`, `aria-busy`,
`aria-modal` and `aria-current` are read, and so are their native equivalents: `checked`, `disabled` (also through a disabled `fieldset`), `required`,
`readonly`, `selected`, and a `details` element's `open`.

## PDF/UA

A tagged PDF built from a document carries the same names, languages and table details (see
[Tagged PDF (PDF/UA) Support](html-css-support.md#tagged-pdf-pdfua-support)), and `PdfGenerateConfig.PdfUAConformance` makes the file claim
[PDF/UA](https://pdfa.org/resource/iso-14289-pdfua/): it identifies it in the XMP metadata, requires a title, a language and a creation date, gives every
figure alternative text, and shows the title in the viewer. See [Generating PDF/UA (accessible) output](usage-examples.md#generating-pdfua-accessible-output).

## Other formats

`HtmlDocument.PaintAccessibility(pageIndex, canvas)` hands the tree to any `Canvas` as `AddSemanticRegion` calls: one unpainted region per part of a
page that has a role (landmarks, headings, links, buttons, lists, table parts...), in reading order, with its name, description, language, heading
level and states. [PeachDrawing.Svg](peachdrawing-svg.md) writes them as `<rect role="..." aria-label="...">`, so an SVG made from a document is
readable by a screen reader; the text itself is still drawn as outlines unless the SVG is made with selectable text. The regions are flat (no
nesting). The Skia and Win2D canvases record them in their link map (`SkiaLinkMap.Semantics` / `Win2dLinkMap.Semantics`) beside the links, and
`SemanticAt` finds the innermost one under a point, which is what a host's touch exploration or UI Automation hit test needs.

## Form controls

A host that shows the page sets `HtmlDocumentOptions.PaintFormControls`, and the engine draws every `<input>` and `<select>` itself from the
control's own CSS - no native widget is placed over the page - so a control is styled like any other box. The look covers check boxes, radio
buttons, text-like fields (value or placeholder), buttons, drop-downs and ranges; `progress`, `meter`, the date and colour inputs, a `textarea` and
typing into a field are not drawn or handled yet. The accessibility tree already reports each control's role, name and state.

## Preferences and text spacing

A host reports what the reader has asked of the system with `HtmlDocumentOptions.Preferences`, so style sheets can answer with
[`prefers-reduced-motion`](https://developer.mozilla.org/en-US/docs/Web/CSS/@media/prefers-reduced-motion),
[`prefers-contrast`](https://developer.mozilla.org/en-US/docs/Web/CSS/@media/prefers-contrast),
[`forced-colors`](https://developer.mozilla.org/en-US/docs/Web/CSS/@media/forced-colors),
[`inverted-colors`](https://developer.mozilla.org/en-US/docs/Web/CSS/@media/inverted-colors),
[`prefers-reduced-transparency`](https://developer.mozilla.org/en-US/docs/Web/CSS/@media/prefers-reduced-transparency) and
`prefers-reduced-data`. The defaults are those of a static document. `HtmlDocumentOptions.TextSpacing` applies the spacing
[WCAG 1.4.12](https://www.w3.org/TR/WCAG22/#text-spacing) asks a page to survive (`TextSpacingOverrides.Wcag`) over the document's own.

### Forced colors

With `UserPreferences.ForcedColors` on, the document is also drawn in the colours of `UserPreferences.ForcedPalette` (a
`SystemColorPalette`: `Canvas`, `CanvasText`, `LinkText`, `ButtonFace`... filled from the platform, or `SystemColorPalette.Light` / `.Dark`), as a
browser does under a Windows high contrast theme: text takes `CanvasText` (`LinkText` in a link, `ButtonText` in a button, `FieldText` in a field),
backgrounds take `Canvas` (`ButtonFace`, `Field`, `Mark`), borders, outlines and column rules take `CanvasText` (`ButtonBorder` on a control),
and box shadows and text shadows are not drawn. A fully transparent colour stays transparent. An element opts out with
[`forced-color-adjust`](https://developer.mozilla.org/en-US/docs/Web/CSS/forced-color-adjust) `none`, or keeps just its text colour with
`preserve-parent-color`. The CSS [system colour keywords](https://developer.mozilla.org/en-US/docs/Web/CSS/system-color) (`CanvasText`, `LinkText`...)
work in any style sheet; they always have the colours of `SystemColorPalette.Light`, whatever palette the host passes. Gradient backgrounds are
not dropped yet.

### Text size

`HtmlDocumentOptions.FontScale` scales the root font size (1.5 is 150% of the default), so text the document sizes in `em`, `rem`, a
percentage or a keyword follows the reader's text size setting, and text sized in `px` or `pt` does not, as in a browser.

## Limits of this version

- A name that comes from an element that is not shown (`display: none`) is not read, because it is never laid out; `aria-labelledby` pointing at one
  gives nothing, and no relation can point at one.
- Content moved into the tree by `display: contents` is not in it yet.
- A table header or footer that repeats on every page is in the tree once, with the rectangle it has on the first page.
- An inline SVG is one node: the shapes and text inside it are not separate nodes, and a `<title>` or `<desc>` of an element inside it is not read.
  A `math` element is one node too, and its spoken form is plain English for common formulas, not a full speech-rule system (no language but English,
  no ClearSpeak or MathSpeak style settings).
- A `th` with neither `scope` nor a `headers` link is taken to head its column, and `scope=colgroup` and `scope=rowgroup` are not read.
- Radio buttons are not counted as a set by their `name`.
