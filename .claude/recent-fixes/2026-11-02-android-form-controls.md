# Android: controls are painted by the engine and a tap toggles them

Same shape as the WinUI change (see `2026-11-02-winui-form-controls.md`): `PaintFormControls` on, `TapAt` asks `ActivateControlAt` before it looks for a link, `Action.Click` on a checkable node calls `ActivateControl`, and Space/Enter on a focused check box or radio button toggles it. After a change `RefreshAccessibility` takes the new tree and the surface repaints.

- **Why a whole-surface `Invalidate`.** The Android view paints its visible region every frame from the document; the dirty regions the document returns matter to WinUI's virtual canvas, not here.
- **Not run on a device in this change:** the project builds (SDK 10.0.4xx, the RC SDK has no android workload), but the emulator job is what exercises the tap path.
- **Not done:** typing, the drop-down list and range dragging (the `InputConnection` bridge).
