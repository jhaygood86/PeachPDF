# Date a behavior change against the release tag, never against the notes folders

Deciding whether a change needs a migration note means deciding whether the old behavior ever
shipped. The contents of `.claude/migration-notes/`, `.claude/recent-fixes/` and
`.claude/accepted-gaps/` cannot answer that, and reasoning from them gives the wrong answer.

**The trap.** A still-present file in `.claude/migration-notes/` dated well before the feature you are
touching looks like evidence that no release has happened since — so the feature "cannot have
shipped", so no note is needed. It is not evidence of anything. These folders are drained by hand,
and a note outliving its release means only that nobody folded it into the release notes and deleted
it. This reasoning was used once to skip a migration note for `outline-style: auto`; the oldest
pending note was dated 2026-07-20 and outline support landed 2026-08-10, but v0.9.10 through v0.9.18
had shipped in between and the behavior had been public for a month.

**What to run instead.** Three commands, against the tag:

```
gh release list --repo jhaygood86/PeachPDF --limit 5   # what is the previous release?
git merge-base --is-ancestor <commit> <tag>            # did the behavior ship in it?
git show <tag>:<path>                                  # what did it actually do there?
```

The last one is the one that settles it, and it is what CLAUDE.md's migration-note convention already
asks for. It also cuts the other way and saves work: run against a feature that has *not* shipped, it
proves a second note would describe a baseline no user ever had, and that the right move is to amend
the pending note instead of adding one beside it.

A local clone may have no tags at all (`git tag` silent) when it was cloned from a fork — fetch them
from the upstream remote first (`git fetch upstream --tags`) rather than concluding no releases exist.
