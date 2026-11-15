# Avalonia: the caret blinks on a fixed 530 ms

Avalonia 12.1 exposes no system caret blink time (WinUI reads `GetCaretBlinkTime`), so the text caret in an `<input>`/`<textarea>` blinks every 530 ms regardless of the user's setting, and a user who
disabled blinking at the OS level still sees it blink. Revisit if `IPlatformSettings` gains a blink-time or caret-blink-enabled property.
