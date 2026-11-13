# PeachBrackets.Avalonia

An [Avalonia](https://avaloniaui.net/) 12 `HtmlView` control that shows an HTML document laid out by [PeachBrackets](https://peachpdf.net/peachbrackets.html), painted through the Skia surface Avalonia leases to a custom draw operation (via [PeachDrawing.Skia](https://peachpdf.net/peachdrawing-skia.html)).

```bash
dotnet add package PeachBrackets.Avalonia
```

```csharp
var view = new HtmlView { Html = "<h1>Hello</h1><p>A <a href='https://peachpdf.net/'>link</a>.</p>" };
window.Content = view;
```

It scrolls and zooms the page, follows the CSS cursor, `:hover`/`:active`, `pointer-events`, `user-select` and `touch-action` behavior the document asks for, opens links, selects and copies text, edits form fields, plays animations on request and exposes the page to assistive technology. It needs Avalonia 12 with the Skia renderer and targets `net10.0` and `net11.0`.

See the [guide](https://peachpdf.net/peachbrackets-avalonia.html) for the members, what it does with each interaction style and its limits.

Licensed under the BSD 3-Clause license.
