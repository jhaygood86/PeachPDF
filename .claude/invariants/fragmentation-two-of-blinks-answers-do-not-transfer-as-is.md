# Two of Blink's LayoutNG answers do not transfer as-is

_CSS Fragmentation Level 3. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

Read this before concluding from a Chromium comparison that PeachPDF is simply doing it wrong. Both
of the answers below are tracked as work rather than treated as permanent divergences, and both have
been re-derived from scratch more than once.

- **Blink children never position themselves.** `Layout()` returns a sized fragment and the *parent* assigns the offset. That is why Chrome needs nothing like #332, why PeachPDF's flex/grid lines must be *translated* rather than laid out again, and why #374 exists: a retry has to *un-write* what it wrote onto shared boxes, where Blink would discard a fragment. **Adopting this contract is #390.**
- **Blink fragmentainers are real boxes and child offsets are fragmentainer-relative** — there is no global document Y. PeachPDF keeps one continuous document space, which is why a column needed a two-axis membership question, why §6.2's edges at a column break come from the break record rather than geometry (#368), why a forced column break is a decision rather than a placement (#312), and why which column a run member is in is an index question (#383). **Closing this was #400**; the last of it goes with #390's stage 2 flip.
- **Monolithic content in Blink produces exactly one fragment** that overflows and is clipped. PeachPDF deliberately departs — see #350. #406 is the narrower version that is *not* deliberate.

What *does* transfer, and already has: **relayout with a hint** (`EarlyBreak`) for the retroactive cases (#332, #371, #355); inline resumption keyed on item index plus text offset (#321); `InlineFlowBox::paintFillLayer` as §6.2's unbroken-box strip (PR #338); and break tokens naming the boxes that continue, which is where #331 and #368 take their fragment edges from.
