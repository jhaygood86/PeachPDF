# A float with `position: relative` is drawn at its static position

The horizontal offset of a floated box that is itself `position: relative` is never applied, and the vertical one only when a block box follows it (a float that is the last child of its container is drawn at its static position). The same box in normal flow, and a relatively positioned float inside a relatively positioned container, are offset as expected (tracking issue #2230).
