# Out of scope / accepted gaps

One file per gap, named for the gap rather than for a date. **Don't relitigate one of these without
new information** — each records a limitation already argued through once, and several record an
approach that was tried, measured and rejected. Read the relevant file before "fixing" behaviour it
describes; what looks like a defect may be a decision with a reason attached.

**The directory listing is the index.** Filenames are the entry titles, so `ls` gives you a readable
table of contents and grep over the folder finds a gap by any term in its body rather than only by
title. There is deliberately **no index file listing the entries** — an index is a single shared
file every change has to append to, which is exactly the merge conflict that splitting this folder
out of CLAUDE.md was meant to remove. Don't add one back. For the same reason, refer to a sibling
gap by linking its file, never by position ("the entry above").

## Adding one

Add a file when a change deliberately leaves a gap behind. State what the gap is, the specific spec
rule it deviates from, and why it was out of scope. If it is a genuine spec deviation it must also
be tracked as a GitHub issue — file one and reference it here; see
[CLAUDE.md](../../CLAUDE.md#post-change-review-pass).

Adding an entry should touch exactly one new file and nothing else, so two branches recording gaps
never conflict over it.

## Deleting one

**These do not expire.** Unlike [.claude/recent-fixes/](../recent-fixes/), a gap file is deleted only
when the gap itself is closed — and closing one means deleting the file *and* removing the matching
limitation note from the user-facing page in `docs/**` that describes it.
