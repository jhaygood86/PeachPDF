# Auto horizontal margins resolve against the page a box lands on (#1518)

`CssLayoutEngine.FreeInlineSpace` (and the table `boxWidth` centring) used `ContainingBlock.AvailableWidth`, a single live value from the containing block's start page, so `width:300pt; margin:0 auto` under `@page :first { margin-left:0 }` kept page 0's X on later pages.

Now `GetActualMarginLeft/Right` take an optional `blockTop` and resolve against `PageAwareWidthBasis(containingBlock, blockTop)` (equal to `AvailableWidth` wherever per-page measure is off). `CssBox.ResolveBlockInlineStart` passes the real landing Y via `ActualMarginLeftAt/RightAt`, because `Location` is not yet written at that point; parameterless readers default to `Location.Y`.

Trap found by running it: a fixture whose first page holds only an empty div is "content-empty" and skipped, shifting `:first` geometry onto the next slot — give test blocks text.

Not done: a single box that *spans* pages keeps its start page's X (same as non-auto boxes); flex/grid items, floats, abs-pos unchanged (see per-page-horizontal-reflow-scope accepted gap).
Tests: `AutoMarginPerPageMeasureIntegrationTests` (fail without the fix).
