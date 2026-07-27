# Margin collapsing ignores clearance in the adjoining set

Margin collapsing (`CssBox.FoldSelfCollapsingMargins`, added by the Acid2 mouth-gap fix) folds every in-flow descendant's margins of a self-collapsing box into one adjoining set without checking for clearance — CSS2.1 §8.3.1 excludes margins separated by clearance from the adjoining set. Hitting the difference requires a float plus a cleared empty child *inside* a collapse-through box; deemed too exotic to special-case.
