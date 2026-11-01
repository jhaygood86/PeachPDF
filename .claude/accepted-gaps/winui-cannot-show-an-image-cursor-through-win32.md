# WinUI 3 cannot be made to show a document's image cursor with Win32 alone

`cursor: url(...) x y, keyword` is shown by the Android view (`PointerIcon.Create`), but the WinUI `HtmlView` shows the keyword's
system pointer instead. Making an image pointer through Win32 was built and measured, and it does not reach the screen:

- `Microsoft.UI.Input.InputCustomCursor` cannot be constructed from managed code (its only constructor takes a CsWinRT
  `IObjectReference`), and the `InputDesktop*ResourceCursor` types only load a cursor stored in the exe's resources by id or name.
- An `HCURSOR` made with `CreateIconIndirect` from decoded pixels (premultiplied BGRA, hotspot scaled with the image) is valid and
  `SetCursor` on the UI thread accepts it (`GetCursor()` returns it), but `GetCursorInfo().hCursor` - what is on screen - never becomes
  it. While the pointer was over the page the framework's own pointer (the `ProtectedCursor` shape, or the arrow when it is null) was
  what Windows showed, with real mouse input (`mouse_event`; `SetCursorPos` alone sends the window no pointer events).
- `SetWindowSubclass` on the top-level window and on `Microsoft.UI.Content.DesktopChildSiteBridge`, `InputSiteWindowClass` and
  `InputNonClientPointerSource` succeeded (and a `SendMessage` through them reached the proc), yet no message at all, `WM_SETCURSOR`
  included, reached the proc for real mouse movement over the page; `WindowFromPoint` was the (hooked) bridge window on the UI thread.
- `SetClassLongPtr(GCLP_HCURSOR)` on those windows changed nothing either, and assigning `ProtectedCursor` fresh on every pointer move
  made the framework put its pointer back (assign only when the shape changes).

What would work: draw the image ourselves (an overlay element that follows the pointer, with the system pointer hidden over the view),
which cannot draw outside the view; or a Win32 hook below the island's input stack, which was not found. Until then the WinUI view
shows the keyword, which the docs say. Android is unaffected.
