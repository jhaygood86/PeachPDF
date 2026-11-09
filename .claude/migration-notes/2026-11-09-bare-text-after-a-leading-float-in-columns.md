# Bare text after a float in a multi-column container is a block of its own after the float

In a `columns` container, a float that came before the first text of a run (`<div style="columns:2"><p>..</p><div style="float:left">..</div>text ..`)
used to share one anonymous block with that text. It no longer does: the float stays a child of the container and the text follows it as
a block. A float that continues into the next column now carries the text with it (before, the words after it were drawn clipped at the
page edge or on no page). A float wider than its column with bare text after it now sends the text to the top of the next column,
as a `<p>` after such a float already did, instead of below the float in its own column.
