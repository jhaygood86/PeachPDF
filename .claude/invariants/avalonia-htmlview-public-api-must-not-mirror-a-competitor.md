# PeachBrackets.Avalonia: one vocabulary across the views, and two traps in its tests

`HtmlView`'s members follow the names `PeachBrackets.WinUI`, `.Android` and `.Apple` already use (`Html`, `Source`, `Document`, `Zoom`, `SelectedText`,
`CopySelection`, `SelectAll`, `ScrollToElement`, `ApplyUpdate`, `DocumentLoaded`/`DocumentFailed`, `FocusedNode`/`FocusChanged`, `ScrollChanged`/`ZoomChanged`,
`LinkActivated`, `FormSubmitting`/`FormInvalid`, `AnimationEvents`), and Avalonia's own words only where Avalonia differs (styled properties named
`<Name>Property`, `Cursor`, `TextInputMethodClient`). Do not copy member names from Avalonia's `NativeWebView`, the community HtmlRenderer controls or another
HTML view: a consumer reading the views side by side should see one vocabulary. `PublicApi.txt` is the reviewed snapshot (events included).

The platform-free logic every view shares lives in `src/views/PeachBrackets.HtmlView.Core/`: internal sources a view and its test project compile in through
`PeachBrackets.HtmlView.Core.props`, not a package. Keep it free of any UI framework type. WinUI still carries its own copies of the parts that were
extracted from Android and Apple.

Two traps found by running it, not by reading it:

- **A headless test whose body is never awaited passes vacuously.** `HeadlessUnitTestSession.Dispatch(Func<Task>, ...)` binds to the `Dispatch<T>(Func<T>)`
  overload and returns the body's task unawaited, so every assertion in it was ignored (a deliberate `Assert.Fail` passed). Use the
  `Dispatch<T>(Func<Task<T>>)` overload, as `TestSupport/HeadlessSession.Run` does, and put a failing probe in any new harness before trusting it.
- **A headless bitmap's channel order is the bitmap's own** (RGBA or BGRA by platform): read `bitmap.Format` before decoding a pixel.
