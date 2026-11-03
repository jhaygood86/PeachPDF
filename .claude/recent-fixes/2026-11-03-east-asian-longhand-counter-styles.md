# East Asian longhand counter styles (cjk-ideographic, japanese-*, korean-*, *-chinese-*)

The ten CSS Counter Styles 3 section 7.1 styles used to fall to `decimal`. They are now `EastAsianCounterStyles` (digit row + unit row per style) wired into `CssCounterEngine.FormatCounterValue`, with the per-style marker suffix in `CssBoxMarker`.

The spec states each style as one additive table for 1-9999, but a purely additive reading is wrong for Chinese (no 零, wrong for 110 and 10001). The reference used was Chromium: a page of `counter(n, style)` spans, read off a screenshot of the in-app browser (the pseudo-element text is not readable by script), for 0..2147483647 and negatives. What it showed, and what the tests (`EastAsianCounterStylesTests`) pin:
- Chinese styles are unbounded and group by 4 digits with 万/亿 (萬/億 traditional). 零 stands for a zero run between digits, and between groups when the lower group is below 1000 or a group was skipped; never trailing. Informal drops 一 only when 十 opens a group (十, 十一, 一万零十) but not inside a longer one (一百一十, 一千零一十).
- Japanese styles are additive with no zero and stop at 9999 (10000 is `一〇〇〇〇`); Korean ones are additive with no zero digit too but continue with 만/萬 and 억/億 groups (일만, 萬).
- Out-of-range falls back to `cjk-decimal` as the spec says (Chromium even shows plain `-10000` for a negative there; not copied).

Not done: no showcase - the harness has no bundled CJK font (the existing Noto Sans JP subset holds 81 glyphs), so a page would be tofu on CI. Chinese groups above 亿 (10^12 and up) are unreachable through an `int` counter.
