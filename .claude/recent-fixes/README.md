# Recent fixes

One file per fix, named `YYYY-MM-DD-<slug>.md` for the date it landed on `main`. These are
engineering history — the *why* behind a change, the traps found on the way in, and the evidence
each conclusion rests on — not user-facing documentation.

**The directory listing is the index.** Filenames are the entry titles, so `ls` gives you a
chronological, readable table of contents, and grep over the folder finds an entry by any term in
its body rather than only by title. There is deliberately **no index file listing the entries** — an
index is a single shared file every change has to append to, which is exactly the merge conflict
that splitting this folder out of CLAUDE.md was meant to remove. Don't add one back.

That is also why nothing here cross-references entries by position. Say what you mean and link the
sibling file by name; never "the entry above" or "the previous fix", which stop meaning anything the
moment the neighbouring file ages out.

## Adding one

Add a file rather than editing an existing one. Say what the load-bearing idea was, what was found
by running it rather than by reading it, what was deliberately not done and why, and what evidence
the conclusion rests on (suite/showcase/diff-coverage results). A defect a future change could
plausibly reintroduce is worth more words than the diff itself.

Adding an entry should touch exactly one new file and nothing else, so two branches recording fixes
never conflict over it.

## Deleting one

**A fix is not recent once it is more than 30 days old — delete the file.** By then anything a
reader still needs should already live somewhere durable: user-facing behaviour in `docs/**` /
`README.md`, and a deviation we have decided to live with in [.claude/accepted-gaps/](../accepted-gaps/),
which does not expire. If some of it does not, migrating it *is part of deleting the file*, not a
follow-up — the deletion is the last moment that knowledge is guaranteed to be written down
anywhere. A stale entry is worse than no entry: it describes code that has since moved on, and every
future change pays to read past it.
