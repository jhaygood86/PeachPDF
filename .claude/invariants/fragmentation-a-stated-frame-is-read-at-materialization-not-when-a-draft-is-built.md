# A frame layout states for a fragmentainer is read at materialization, and the frame a box had is stated before its live geometry moves

`HtmlContainerInt.RecordInlineFrame` / `FragmentEmitter._inlineFrames` state a box's border box (absolute X and
width) for one fragmentainer, for a box whose engine sized it once but which draws differently on pages of
different measures (a flex container and its items, and the blocks inside a straddling item).

**Read it in `ExtentOf`, never in `BuildDraft`.** A pass that resumes into slot *n* states the frame of slots
before *n* — after those slots' drafts were frozen — so a value captured on the draft when it was built is the
value from before the statement existed. Measured symptom: a straddling flex row's first-page items came out at
the *last* page's width (170.7pt instead of 187.3pt) while the container, whose frame is stated by the fresh
pass before any draft exists, looked right.

**State the old frame before moving live geometry.** The fragment tree reads a box's live `Location`/`Size`
wherever nothing is stated, and a box has one of each for all its fragments. A pass that moves them to fit the
page it is on (`CssLayoutEngineFlex.MoveFrame`) must first state the frame the box had for every earlier slot it
was laid out in, or every earlier fragment silently takes the new frame. Statements for *later* slots are kept:
they were derived from the same CSS and hold for pages no pass visits.

A statement never creates a fragment: it resizes one the box already has in that slot.
