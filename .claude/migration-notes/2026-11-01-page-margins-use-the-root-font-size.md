# `@page` margins in `em`/`rem` now use the `<html>` element's font size

Before, a base `@page` margin in `rem` (or in `em` with no `@page { font-size }`) resolved against the engine's default font size whatever `html { font-size }` said: `html { font-size: 32px } @page { margin-top: 1rem }` gave an 11pt margin. Now it resolves against the real root element's font (24pt here), as CSS Values 4 §6.1.1 requires. Documents with no root font-size declaration are unchanged.
