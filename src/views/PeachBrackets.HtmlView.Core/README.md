# PeachBrackets.HtmlView.Core

The platform-free logic the `PeachBrackets.WinUI`, `.Android`, `.Apple` and `.Avalonia` views share: the tile grid and cache, caret and word moves, IME edit math, picker value parsing, animation timing and image scale. It is not a package and has no project file; a view (and its test project) imports `PeachBrackets.HtmlView.Core.props`, which compiles the sources in as `internal` types.
