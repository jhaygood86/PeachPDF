# Everything defined over the *whole* box is resolved at materialization, not when a slot is frozen

_CSS Fragmentation Level 3. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

A box continuing into a later fragmentainer has not had its height applied on the pass that freezes this slot. Read at pass time, **every intermediate fragment of a multi-page block lost its background and borders**.
