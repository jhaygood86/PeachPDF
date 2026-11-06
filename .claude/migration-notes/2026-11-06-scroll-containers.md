# Scroll containers on a screen

**Before.** On a screen (`MediaType.Screen`) an element with `overflow: auto` or `scroll` was not clipped: its content ran on past its box, as it does in a printed page. Only `hidden` and `clip` clipped.

**Now.** On a screen `auto`, `scroll` and `hidden` boxes are scroll containers: they clip to their padding box, scroll (with `SetScrollPosition`, `ScrollBy`, `ScrollIntoView`, or the wheel in the WinUI view), and `auto` and `scroll`
draw overlay scrollbars. A paged document is unchanged. A page that relied on a screen showing the overflow of an `overflow: auto` box needs `overflow: visible` there.
