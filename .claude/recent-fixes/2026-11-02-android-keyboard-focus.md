# Android keyboard focus

Tab, Shift+Tab and Enter on the Android view, over the engine's `MoveFocusAsync`/`FocusAsync` (`HtmlView.Focus.cs`), the same shape as the WinUI view.

- **Decide at once, apply later**, as on WinUI: `StepFocus` asks the tree for the next stop and answers `OnKeyDown` from that; the move is queued in order and the destination remembered by node id, so two quick Tabs step on from the right place. Past the last stop `OnKeyDown` returns false and Android moves the focus on.
- **Coming in with Tab.** `OnFocusChanged` with `FocusSearchDirection.Forward`/`Backward` lands on the first/last stop; losing the focus clears the page's.
- **The host's tree goes stale after a restyle.** A focus change that restyles the document makes the engine build a new tree, so the nodes the view stored at its last refresh are not the ones `FocusedNode` returns. Compare nodes by `Id`, never by reference, and take the tree again (`RefreshAccessibility`) after an applied change. `RefreshAccessibility` now keeps TalkBack's accessibility focus on the same part by id (it used to drop it to the host).
- **Validated on an emulator, not only in unit tests.** `android-smoke.sh` presses Tab twice (`input keyevent KEYCODE_TAB`) and expects the example to log `focus=a link` and then `focus=none`. Run locally on the Pixel 8 AVD: the swipe scrolls, the first Tab focuses the link (the view scrolls it into view once more), the second gives the page's focus up.
- **Binding names:** `FocusChanged`/`OnFocusChanged` are now in the reviewed `PublicApi.txt`; the snapshot test reads the built DLL, which needs the android workload (SDK 10.0.403 here, not the RC SDK).
