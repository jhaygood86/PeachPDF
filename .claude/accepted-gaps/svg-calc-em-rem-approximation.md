# em/rem-relative `calc()` in an SVG length uses a fixed 16px approximation

em/rem-relative `calc()` inside an SVG length uses a fixed 16px approximation for `em`/`rem` (the same approximation `SvgValueParsers.ParseLength` already uses for a plain `em`/`rem` SVG length), since arbitrary SVG geometry attributes have no live font-size context — only where the element's real font context is threaded would it differ. Pure-length and percentage `calc()` are exact. Filed as [issue #207](https://github.com/jhaygood86/PeachPDF/issues/207).
