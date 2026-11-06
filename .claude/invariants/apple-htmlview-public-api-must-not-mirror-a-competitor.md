# PeachBrackets.Apple: name members for what they are, not after another view library

`HtmlView`'s members follow the names `PeachBrackets.WinUI` and `PeachBrackets.Android` already use (`Html`, `Document`, `Zoom`, `SelectedText`,
`CopySelection`, `SelectAll`, `ScrollToElement`, `ApplyUpdate`, `DocumentLoaded`/`DocumentFailed`, `ScrollChanged`/`ZoomChanged`), and the platform's own
words only where the gesture differs (`Clicked` on a Mac, `Tapped`/`LongPressed`/`TouchStarted` on iOS; `LinkActivated`, not `LinkClicked` or `LinkTapped`, because
both a click and a tap activate it). Do not copy member names from `WKWebView`, `WebView` or another HTML view: a consumer reading the three PeachBrackets
views side by side should see one vocabulary.

Two traps found by running it, not by reading it:

- **The two platforms' public surfaces differ**, so one `PublicApi.txt` cannot pin both. The test reads the most recent build of each platform against
  `PublicApi.macos.txt` and `PublicApi.ios.txt`; regenerate both (build both TFMs, then `UPDATE_PUBLIC_API=1`) after a change to either view.
- **Scripts that run the iOS example with `simctl launch --console-pty` never return** while the app lives: run them in the background with a timeout and read
  the log (as `.github/scripts/ios-smoke.sh` does), or a CI step hangs to its timeout.
