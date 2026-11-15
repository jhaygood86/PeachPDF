# Avalonia accessibility: no text, grid or table patterns, no live-region notification, no focus event

Avalonia 12.1's automation peers have no equivalent of UI Automation's Text pattern, Grid/Table patterns, `RaiseNotificationEvent` or a focus-changed event, and no position-in-set / size-in-set
overrides. `HtmlViewAutomation.cs` therefore exposes tables as table/header/cell peers only, announces a live-region change as a `NameProperty` change on the region's peer, and does not port
`HtmlTextModel`. A reader's text navigation inside a document is by its child peers (paragraphs, headings), not by character/word/line.

Why it stays: the missing pieces are in the framework's peer API, not in PeachBrackets. Revisit when Avalonia adds them (check `Avalonia.Automation.Provider` and `AutomationPeer` on each release);
until then the WinUI view is the reference for a platform that has them.
