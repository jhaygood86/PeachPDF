# Floats whose widths add up to the container only by rounding can be split onto two rows

70% and 30% of a 333px container sum to it up to about 1e-14, and the float conflict tests (`DomUtils.IsFloatIntersecting`, the fit checks in `FloatBoxLeft`/`FloatBoxRight`) compare strictly, so the second float goes below the first where Chrome keeps them side by side. A left float after a right one has a tolerance (`FloatFitTolerance`); the other comparisons do not (tracking issue #2223).
