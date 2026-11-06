# A float with `position: relative` is drawn at its static position

The offset of a floated box that is itself `position: relative` is never applied, with or without other floats around it. The same box in normal flow, and a relatively positioned float inside a relatively positioned container, are offset as expected (tracking issue #2230).
