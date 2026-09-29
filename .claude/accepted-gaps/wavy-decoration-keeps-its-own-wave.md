# `text-decoration-style: wavy` does not use `PathShapes.AddWave`

`WavyDecorationRenderer` builds one cubic per full wavelength with both control points at the period's midpoint (the
construction Blink and WebKit use, tuned against Chrome), whose shape is not the smooth half-period curve `AddWave` produces.
Swapping them would visibly change every wavy underline, so `AddWave` stays a general-purpose shape and the decoration keeps its
own geometry.
