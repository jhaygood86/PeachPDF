# Invariants and traps

One file per invariant, named `<area>-<the invariant>.md`. Each records something this repo has
already paid for at least once: a rule the code depends on, or a trap that cost real debugging time.
**Read the files for an area before making a non-trivial change in it.**

These are not the same thing as the two sibling folders, and the difference is what they are *for*:

- [.claude/recent-fixes/](../recent-fixes/) is engineering history for one change, and **expires at
  30 days**.
- [.claude/accepted-gaps/](../accepted-gaps/) records behaviour we have decided **not** to
  implement.
- This folder records what a *future* change must not break or re-derive. It outlives both — an
  invariant stays true long after the issue that discovered it is closed and the fix that found it
  has aged out.

**The directory listing is the index.** Filenames are the entry titles, so `ls` gives you a readable
table of contents grouped by area, and grep over the folder finds an invariant by any term in its
body. There is deliberately **no index file listing the entries** — an index is a single shared file
every change has to append to, which is exactly the merge conflict that keeping these out of
`CLAUDE.md` is meant to remove. Don't add one back. For the same reason, refer to a sibling by
linking its file, never by position or by number.

## Areas

The prefix is the area, not a category system to be maintained — add one when an invariant does not
fit an existing prefix. Today: `fragmentation-` (CSS Fragmentation Level 3 and the paged-media
cluster, tracked by [#320](https://github.com/jhaygood86/PeachPDF/issues/320)), `testing-` (traps in
how this repo is verified, which apply to every area), and `csharp-` (language and compiler
behaviour that has misled a reader here).

An invariant discovered inside one feature's work but true of the whole repo belongs under the
general prefix, not the feature's — filing it under an area whose issues will all eventually close
is how it gets lost.

## Adding one

State the invariant as the title, in the imperative or as a fact, so `ls` reads as a list of rules.
In the body, say what breaks when it is violated and — where there is one — the concrete incident,
with the issue or PR number. An invariant with a measured symptom attached is worth several without
one, because the symptom is what a future reader recognizes.

Adding an entry should touch exactly one new file and nothing else, so two branches recording
invariants never conflict.

## Deleting one

**These do not expire**, but they are not permanent either: delete a file when the invariant stops
being true — when the trap is designed out rather than merely avoided, or the mechanism it describes
is gone. That is a deliberate act with a commit message explaining why, not a tidy-up.
